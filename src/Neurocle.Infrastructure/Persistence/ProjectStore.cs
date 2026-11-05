using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO.Compression;
using System.IO;
using System.Linq;
using Clipper2Lib;
using Microsoft.Data.Sqlite;
using Neurocle.Application.Projects;
using Neurocle.Domain.Segmentation;

namespace Neurocle.Infrastructure.Persistence
{
    // 將 SQLite 資料庫與受管理圖片目錄封裝成可交易、可復原的專案工作階段。
    public sealed class ProjectStore : IProjectSession
    {
        // 記錄目前資料庫結構版本，供開啟與遷移流程檢查。
        private const int CurrentSchemaVersion = 7;
        // 限制每個專案保留的備份數量。
        private const int MaximumBackupCount = 5;
        // 提供自動分割資料集時使用的隨機數產生器。
        private static readonly Random random = new Random();
        // 保持專案檔案鎖定期間的檔案控制代碼。
        private readonly FileStream lockStream;
        // 保存此工作階段使用的 SQLite 連線。
        private readonly SqliteConnection connection;
        // 依標註集分開保存尚未寫入資料庫的 Undo/Redo 動作。
        private readonly Dictionary<string, AnnotationHistory> histories = new Dictionary<string, AnnotationHistory>();
        // 指出目前是否正在重播歷程，以免 Undo/Redo 再次建立歷程項目。
        private bool replayingHistory;
        // 暫存 Undo/Redo 正在使用的交易，供歷程委派共用。
        private SqliteTransaction historyTransaction;
        // 防止工作階段處置後再建立背景匯入連線。
        private bool disposed;

        // 提供可替換的時間來源，供備份流程測試使用。
        internal static Func<DateTime> UtcNow = () => DateTime.UtcNow;
        // 提供可替換的安全檔案替換操作，供還原流程測試使用。
        internal static Action<string, string> ReplaceFile = ReplaceFileSafely;
        // 提供可替換的原生檔案替換操作，供復原流程測試使用。
        internal static Action<string, string> NativeReplaceFile = (source, destination) => File.Replace(source, destination, null);
        // 提供可替換的檔案移動操作，供中斷復原流程測試使用。
        internal static Action<string, string> MoveFile = File.Move;
        // 提供可替換的隨機數來源，供資料集切分測試使用。
        internal static Func<int, int> NextRandom = maximum => random.Next(maximum);

        /// <summary>
        /// 建立專案工作階段並保存資料庫連線與獨佔鎖。
        /// </summary>
        /// <param name="path">要讀取、檢查或轉換的路徑。</param>
        /// <param name="lockStream">持有專案獨佔鎖的檔案資料流。</param>
        /// <param name="connection">供資料庫操作使用的 SQLite 連線。</param>
        private ProjectStore(string path, FileStream lockStream, SqliteConnection connection)
        {
            ProjectPath = path;
            ImagesDirectory = Path.Combine(Path.GetDirectoryName(path), Path.GetFileNameWithoutExtension(path) + ".images");
            this.lockStream = lockStream;
            this.connection = connection;
        }

        // 取得目前工作階段開啟的專案資料庫路徑。
        public string ProjectPath { get; }
        // 取得此專案受管理圖片的儲存目錄。
        public string ImagesDirectory { get; }

        /// <summary>
        /// 建立 SQLite 專案資料庫、受管理圖片目錄及初始備份。
        /// </summary>
        /// <param name="projectPath">專案資料庫檔案路徑。</param>
        /// <returns>回傳已建立並完成初始化的專案工作階段。</returns>
        public static ProjectStore Create(string projectPath)
        {
            string fullPath = PreparePath(projectPath, false);
            FileStream lockHandle = AcquireLock(fullPath);
            SqliteConnection connection = null;
            try
            {
                connection = OpenConnection(fullPath);
                using (var transaction = connection.BeginTransaction())
                {
                    Execute(connection, transaction, "CREATE TABLE SchemaInfo (Version INTEGER NOT NULL);");
                    Execute(connection, transaction, "CREATE TABLE ProjectInfo (ProjectId TEXT NOT NULL, CreatedUtc TEXT NOT NULL);");
                    CreateImageTable(connection, transaction);
                    CreateAnnotationTables(connection, transaction);
                    Execute(connection, transaction, "INSERT INTO SchemaInfo (Version) VALUES ($version);", new SqliteParameter("$version", CurrentSchemaVersion));
                    Execute(connection, transaction, "INSERT INTO ProjectInfo (ProjectId, CreatedUtc) VALUES ($id, $created);", new SqliteParameter("$id", Guid.NewGuid().ToString("D")), new SqliteParameter("$created", UtcNow().ToString("O")));
                    transaction.Commit();
                }

                var store = new ProjectStore(fullPath, lockHandle, connection);
                Directory.CreateDirectory(store.ImagesDirectory);
                store.RecoverStagedImages();
                store.CreateDailyBackup();
                return store;
            }
            catch
            {
                connection?.Dispose();
                lockHandle.Dispose();
                DeleteNewProject(fullPath);
                throw;
            }
        }

        /// <summary>
        /// 開啟專案資料庫，完成結構檢查、必要遷移及暫存圖片復原。
        /// </summary>
        /// <param name="projectPath">專案資料庫檔案路徑。</param>
        /// <returns>回傳已開啟並完成結構檢查的專案工作階段。</returns>
        /// <exception cref="ProjectOpenException">專案資料庫無法開啟或結構檢查失敗。</exception>
        public static ProjectStore Open(string projectPath)
        {
            string fullPath = PreparePath(projectPath, true);
            FileStream lockHandle = AcquireLock(fullPath);
            SqliteConnection connection = null;
            try
            {
                try
                {
                    connection = OpenConnection(fullPath);
                    EnsureCurrentSchema(connection, fullPath);
                }
                catch (SqliteException exception)
                {
                    connection?.Dispose();
                    connection = null;
                    lockHandle.Dispose();
                    throw new ProjectOpenException(fullPath, GetBackups(fullPath), exception);
                }

                var store = new ProjectStore(fullPath, lockHandle, connection);
                Directory.CreateDirectory(store.ImagesDirectory);
                store.RecoverStagedImages();
                store.CreateDailyBackup();
                return store;
            }
            catch
            {
                connection?.Dispose();
                lockHandle.Dispose();
                throw;
            }
        }

        /// <summary>
        /// 驗證指定備份後，以備份建立目標專案並開啟工作階段。
        /// </summary>
        /// <param name="projectPath">專案資料庫檔案路徑。</param>
        /// <param name="backupPath">屬於此專案的備份檔案路徑。</param>
        /// <returns>回傳從指定備份還原並開啟的專案工作階段。</returns>
        /// <exception cref="ArgumentNullException">必要參數為 null。</exception>
        /// <exception cref="ArgumentException">輸入資料不符合操作契約。</exception>
        /// <exception cref="FileNotFoundException">指定的檔案不存在。</exception>
        public static ProjectStore Restore(string projectPath, string backupPath)
        {
            string fullProjectPath = PreparePath(projectPath, true);
            string fullBackupPath = Path.GetFullPath(backupPath ?? throw new ArgumentNullException(nameof(backupPath)));
            string backupDirectory = GetBackupDirectory(fullProjectPath) + Path.DirectorySeparatorChar;
            if (!fullBackupPath.StartsWith(backupDirectory, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("The backup does not belong to this project.", nameof(backupPath));
            FileStream lockHandle = AcquireLock(fullProjectPath);
            SqliteConnection connection = null;
            string temporary = fullProjectPath + ".restore";
            try
            {
                if (!File.Exists(fullBackupPath)) throw new FileNotFoundException("The backup file does not exist.", fullBackupPath);
                File.Copy(fullBackupPath, temporary, true);
                ValidateDatabase(temporary);
                CreateBackupFile(fullProjectPath, "before-restore-" + UtcNow().ToString("yyyyMMdd-HHmmssfff"));
                try
                {
                    ReplaceFile(temporary, fullProjectPath);
                }
                finally
                {
                    if (File.Exists(temporary)) File.Delete(temporary);
                }
                connection = OpenConnection(fullProjectPath);
                EnsureCurrentSchema(connection, fullProjectPath);
                var store = new ProjectStore(fullProjectPath, lockHandle, connection);
                Directory.CreateDirectory(store.ImagesDirectory);
                store.RecoverStagedImages();
                store.CreateDailyBackup();
                return store;
            }
            catch
            {
                connection?.Dispose();
                lockHandle.Dispose();
                throw;
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        /// <summary>
        /// 以單一 SQLite 交易執行並提交一次專案資料變更。
        /// </summary>
        /// <param name="operation">要在資料庫交易內執行的操作委派。</param>
        /// <exception cref="ArgumentNullException">必要參數為 null。</exception>
        public void CompleteOperation(Action<SqliteConnection, SqliteTransaction> operation)
        {
            if (operation == null) throw new ArgumentNullException(nameof(operation));
            // 將整組資料變更放在同一交易；委派失敗時交易會回滾。
            using (SqliteTransaction transaction = connection.BeginTransaction())
            {
                operation(connection, transaction);
                transaction.Commit();
            }
        }

        /// <inheritdoc />
        public void Save()
        {
            Execute(connection, null, "PRAGMA wal_checkpoint(FULL);");
        }

        /// <inheritdoc />
        public IReadOnlyList<ProjectImage> ImportFiles(IEnumerable<string> filePaths)
        {
            if (filePaths == null) throw new ArgumentNullException(nameof(filePaths));
            var sources = OrderSources(filePaths);
            if (sources.Count == 0) return new List<ProjectImage>();
            long existing = Convert.ToInt64(ExecuteScalar(connection, "SELECT COUNT(*) FROM ImageRecord;"));
            if (existing + sources.Count > 10000) throw new ImageImportException(ImageImportFailure.ProjectImageLimit);

            var imported = new List<ProjectImage>();
            foreach (string source in sources)
            {
                imported.Add(ImportFile(source, checked((int)existing + imported.Count + 1)));
            }
            return imported;
        }

        /// <inheritdoc />
        public ImageImportReport ImportFilesWithReport(IEnumerable<string> filePaths, Func<bool> cancellationRequested)
        {
            return ImportFilesWithReport(filePaths, cancellationRequested, null);
        }

        /// <inheritdoc />
        public ImageImportReport ImportFilesWithReport(IEnumerable<string> filePaths, Func<bool> cancellationRequested, Action<ImageImportProgress> progress)
        {
            if (filePaths == null) throw new ArgumentNullException(nameof(filePaths));
            if (disposed) throw new ObjectDisposedException(nameof(ProjectStore));
            using (var importSession = new ProjectStore(ProjectPath, null, OpenConnection(ProjectPath)))
            {
                return importSession.ImportSourcesWithReport(OrderSources(filePaths), cancellationRequested, null, progress);
            }
        }

        /// <inheritdoc />
        public ImageImportReport ImportDirectory(string directoryPath, bool includeSubdirectories, Func<bool> cancellationRequested)
        {
            return ImportDirectory(directoryPath, includeSubdirectories, cancellationRequested, null);
        }

        /// <inheritdoc />
        public ImageImportReport ImportDirectory(string directoryPath, bool includeSubdirectories, Func<bool> cancellationRequested, Action<ImageImportProgress> progress)
        {
            if (directoryPath == null) throw new ArgumentNullException(nameof(directoryPath));
            if (disposed) throw new ObjectDisposedException(nameof(ProjectStore));
            using (var importSession = new ProjectStore(ProjectPath, null, OpenConnection(ProjectPath)))
            {
                return importSession.ImportDirectoryCore(directoryPath, includeSubdirectories, cancellationRequested, progress);
            }
        }

        /// <summary>
        /// 匯入指定目錄中的圖片，並回報逐項結果、進度與取消狀態。
        /// </summary>
        /// <param name="directoryPath">要掃描或匯入的目錄路徑。</param>
        /// <param name="includeSubdirectories">是否遞迴掃描子目錄。</param>
        /// <param name="cancellationRequested">查詢目前是否要求取消的委派。</param>
        /// <param name="progress">回報匯入進度的委派。</param>
        /// <returns>回傳資料夾匯入的成功、失敗、未處理及取消結果。</returns>
        private ImageImportReport ImportDirectoryCore(string directoryPath, bool includeSubdirectories, Func<bool> cancellationRequested, Action<ImageImportProgress> progress)
        {
            var sources = new List<string>();
            var failures = new List<ImageImportFailureDetail>();
            try
            {
                foreach (string source in Directory.EnumerateFiles(directoryPath, "*", includeSubdirectories ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly))
                {
                    if (cancellationRequested != null && cancellationRequested()) return new ImageImportReport(new List<ProjectImage>(), failures, 0, true);
                    sources.Add(source);
                }
            }
            catch (DirectoryNotFoundException)
            {
                failures.Add(new ImageImportFailureDetail(directoryPath, ImageImportFailure.DirectoryNotFound));
            }
            catch (UnauthorizedAccessException)
            {
                failures.Add(new ImageImportFailureDetail(directoryPath, ImageImportFailure.DirectoryAccess));
            }
            catch (IOException)
            {
                failures.Add(new ImageImportFailureDetail(directoryPath, ImageImportFailure.Unknown));
            }
            return ImportSourcesWithReport(OrderSources(sources), cancellationRequested, failures, progress);
        }

        /// <inheritdoc />
        public IReadOnlyList<ProjectImage> GetImages()
        {
            var images = new List<ProjectImage>();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT Id, OriginalFileName, ManagedFileName, Sha256, Width, Height, Sequence FROM ImageRecord ORDER BY Sequence;";
                using (SqliteDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string managedFileName = reader.GetString(2);
                        images.Add(new ProjectImage(reader.GetString(0), reader.GetString(1), managedFileName, reader.GetString(3), reader.GetInt32(4), reader.GetInt32(5), reader.GetInt32(6), !CanReadImage(Path.Combine(ImagesDirectory, managedFileName))));
                    }
                }
            }
            return images;
        }

        /// <inheritdoc />
        public string ExportImagesZip(string outputPath, IEnumerable<string> imageIds)
        {
            if (String.IsNullOrWhiteSpace(outputPath)) throw new ArgumentException("The output path is required.", nameof(outputPath));
            string requestedPath = Path.GetFullPath(outputPath);
            string directory = Path.GetDirectoryName(requestedPath);
            if (!Directory.Exists(directory)) throw new DirectoryNotFoundException(directory);
            string extension = Path.GetExtension(requestedPath);
            if (!String.Equals(extension, ".zip", StringComparison.OrdinalIgnoreCase)) requestedPath += ".zip";
            string output = CreateUniqueExportPath(requestedPath);
            ISet<string> selected = imageIds == null ? null : new HashSet<string>(imageIds, StringComparer.Ordinal);
            ProjectImage[] images = GetImages().Where(image => selected == null || selected.Contains(image.Id)).Where(image => !image.IsMissing).ToArray();
            bool created = false;
            try
            {
                using (var file = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var archive = new ZipArchive(file, ZipArchiveMode.Create))
                {
                    created = true;
                    foreach (ProjectImage image in images)
                    {
                        ZipArchiveEntry entry = archive.CreateEntry(image.ManagedFileName, CompressionLevel.Optimal);
                        using (Stream input = File.OpenRead(Path.Combine(ImagesDirectory, image.ManagedFileName)))
                        using (Stream destination = entry.Open()) input.CopyTo(destination);
                    }
                }
                return output;
            }
            catch
            {
                if (created && File.Exists(output)) File.Delete(output);
                throw;
            }
        }

        /// <summary>
        /// 產生不會覆寫既有輸出檔案的匯出路徑。
        /// </summary>
        /// <param name="requestedPath">使用者要求的輸出路徑。</param>
        /// <returns>回傳不會覆寫既有檔案的唯一輸出路徑。</returns>
        private static string CreateUniqueExportPath(string requestedPath)
        {
            if (!File.Exists(requestedPath) && !Directory.Exists(requestedPath)) return requestedPath;
            string directory = Path.GetDirectoryName(requestedPath);
            string name = Path.GetFileNameWithoutExtension(requestedPath);
            string extension = Path.GetExtension(requestedPath);
            for (int index = 1; ; index++)
            {
                string candidate = Path.Combine(directory, name + " (" + index + ")" + extension);
                if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;
            }
        }

        /// <inheritdoc />
        public AnnotationSet CreateAnnotationSet(string name, AnnotationSetType type)
        {
            string normalized = NormalizeName(name);
            var set = new AnnotationSet(Guid.NewGuid().ToString("D"), normalized, type);
            CompleteOperation((db, transaction) =>
            {
                Execute(db, transaction, "INSERT INTO AnnotationSet (Id, Name, Type) VALUES ($id, $name, $type);", new SqliteParameter("$id", set.Id), new SqliteParameter("$name", set.Name), new SqliteParameter("$type", (int)set.Type));
                Execute(db, transaction, "INSERT INTO AnnotationClass (Id, AnnotationSetId, Name, Color) VALUES ($id, $set, 'class1', $color);", new SqliteParameter("$id", Guid.NewGuid().ToString("D")), new SqliteParameter("$set", set.Id), new SqliteParameter("$color", NextClassColor(new string[0])));
            });
            return set;
        }

        /// <inheritdoc />
        public AnnotationSet ImportAnnotationJson(string json, string annotationSetName, bool allowUnknownFields)
        {
            ImportedAnnotationSet imported = JsonAnnotationImport.Parse(json);
            if (!allowUnknownFields && imported.UnknownFields.Count > 0) throw new JsonAnnotationImportException("JSON contains unknown fields.");
            ProjectImage[] images = GetImages().ToArray();
            var imageByName = images.GroupBy(image => image.OriginalFileName, StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
            foreach (ImportedImage item in imported.Data)
            {
                ProjectImage[] matches;
                if (!imageByName.TryGetValue(item.FileName, out matches) || matches.Length != 1 || matches[0].Width != item.Width || matches[0].Height != item.Height) throw new JsonAnnotationImportException("JSON image does not map to exactly one project image.");
                foreach (ImportedRegion region in item.Regions)
                {
                    if (imported.Type == AnnotationSetType.Obd) ValidateObdGeometry(new Size(item.Width, item.Height), region.X, region.Y, region.Width, region.Height);
                    else ValidateSegGeometry(new Size(item.Width, item.Height), new SegRegion("import", "import", "import", "import", region.Type, region.X, region.Y, region.Type == SegRegionType.Ellipse ? region.RadiusX : region.Width, region.Type == SegRegionType.Ellipse ? region.RadiusY : region.Height, 0, region.Points, region.StrokeWidth));
                }
            }
            var set = new AnnotationSet(Guid.NewGuid().ToString("D"), NormalizeName(annotationSetName), imported.Type);
            AnnotationClass[] classes;
            try { classes = imported.Classes.Select(value => new AnnotationClass(Guid.NewGuid().ToString("D"), set.Id, NormalizeName(value.Name), AnnotationColorFormat.Normalize(value.Color))).ToArray(); }
            catch (ArgumentException exception) { throw new JsonAnnotationImportException(exception.Message); }
            if (classes.Select(value => value.Name).Distinct(StringComparer.Ordinal).Count() != classes.Length) throw new JsonAnnotationImportException("classes contains duplicates.");
            var classByName = classes.ToDictionary(value => value.Name, StringComparer.Ordinal);
            // 解析與跨圖片驗證先完成，再以單一交易新增整個標註集。
            CompleteOperation((db, transaction) =>
            {
                Execute(db, transaction, "INSERT INTO AnnotationSet (Id, Name, Type) VALUES ($id, $name, $type);", new SqliteParameter("$id", set.Id), new SqliteParameter("$name", set.Name), new SqliteParameter("$type", (int)set.Type));
                foreach (AnnotationClass value in classes) Execute(db, transaction, "INSERT INTO AnnotationClass (Id, AnnotationSetId, Name, Color) VALUES ($id, $set, $name, $color);", new SqliteParameter("$id", value.Id), new SqliteParameter("$set", set.Id), new SqliteParameter("$name", value.Name), new SqliteParameter("$color", value.Color));
                foreach (ImportedImage item in imported.Data)
                {
                    string imageId = imageByName[item.FileName][0].Id;
                    DatasetAssignmentStatus status = item.Set == "train" ? DatasetAssignmentStatus.Train : item.Set == "test" ? DatasetAssignmentStatus.Test : item.Set == "not_used" ? DatasetAssignmentStatus.NotUsed : DatasetAssignmentStatus.Unassigned;
                    SetDatasetAssignmentRaw(db, transaction, new DatasetAssignment(set.Id, imageId, status, item.Retest));
                    if (set.Type == AnnotationSetType.Cla && item.ClassLabel != "") Execute(db, transaction, "INSERT INTO ClaAnnotation (AnnotationSetId, ImageId, ClassId) VALUES ($set, $image, $class);", new SqliteParameter("$set", set.Id), new SqliteParameter("$image", imageId), new SqliteParameter("$class", classByName[item.ClassLabel].Id));
                    int zOrder = 0;
                    foreach (ImportedRegion region in item.Regions)
                    {
                        string classId = classByName[region.ClassName].Id;
                        if (set.Type == AnnotationSetType.Obd) Execute(db, transaction, "INSERT INTO ObdAnnotation (Id, AnnotationSetId, ImageId, ClassId, X, Y, Width, Height, ZOrder) VALUES ($id, $set, $image, $class, $x, $y, $width, $height, $order);", new SqliteParameter("$id", Guid.NewGuid().ToString("D")), new SqliteParameter("$set", set.Id), new SqliteParameter("$image", imageId), new SqliteParameter("$class", classId), new SqliteParameter("$x", region.X), new SqliteParameter("$y", region.Y), new SqliteParameter("$width", region.Width), new SqliteParameter("$height", region.Height), new SqliteParameter("$order", zOrder++));
                        else InsertImportedSegRegion(db, transaction, set.Id, imageId, classId, region, zOrder++);
                    }
                }
            });
            return set;
        }

        /// <summary>
        /// 將 CLA JSON 的標註與資料集分配加入指定標註集目前沒有標註的圖片；此操作不建立 Undo 歷程。
        /// </summary>
        public void ImportAnnotationJsonIntoSet(string json, string annotationSetId, bool importAnnotations, bool importClasses, bool importDataset, bool allowUnknownFields)
        {
            ImportedAnnotationSet imported = JsonAnnotationImport.Parse(json);
            if (!allowUnknownFields && imported.UnknownFields.Count > 0) throw new JsonAnnotationImportException("JSON contains unknown fields.");
            AnnotationSet target = GetAnnotationSets().SingleOrDefault(value => value.Id == annotationSetId);
            if (target == null) throw new ArgumentException("The annotation set does not exist.", nameof(annotationSetId));
            if (target.Type != AnnotationSetType.Cla || imported.Type != AnnotationSetType.Cla) throw new JsonAnnotationImportException("JSON label type does not match the target annotation set.");
            ProjectImage[] images = GetImages().ToArray();
            var imageByName = images.GroupBy(image => image.OriginalFileName, StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
            foreach (ImportedImage item in imported.Data)
            {
                ProjectImage[] matches;
                if (!imageByName.TryGetValue(item.FileName, out matches) || matches.Length != 1 || matches[0].Width != item.Width || matches[0].Height != item.Height) throw new JsonAnnotationImportException("JSON image does not map to exactly one project image.");
            }
            AnnotationClass[] existing = GetClasses(target.Id).ToArray();
            var classByName = existing.ToDictionary(value => value.Name, StringComparer.Ordinal);
            var newClasses = new List<AnnotationClass>();
            foreach (ImportedClass value in imported.Classes)
            {
                if (classByName.ContainsKey(value.Name)) continue;
                if (!importClasses && importAnnotations) throw new JsonAnnotationImportException("JSON contains a class that is not in the target annotation set.");
                var created = new AnnotationClass(Guid.NewGuid().ToString("D"), target.Id, NormalizeName(value.Name), AnnotationColorFormat.Normalize(value.Color));
                classByName.Add(created.Name, created);
                newClasses.Add(created);
            }
            var assignments = GetDatasetAssignments(target.Id).ToDictionary(value => value.ImageId, StringComparer.Ordinal);
            var importable = imported.Data
                .Select(item => new { Item = item, ImageId = imageByName[item.FileName][0].Id })
                .Where(value => Convert.ToInt32(ExecuteScalar(connection, "SELECT COUNT(*) FROM ClaAnnotation WHERE AnnotationSetId = $set AND ImageId = $image;", new SqliteParameter("$set", target.Id), new SqliteParameter("$image", value.ImageId))) == 0)
                .ToArray();
            CompleteOperation((db, transaction) =>
            {
                if (importClasses) foreach (AnnotationClass value in newClasses) Execute(db, transaction, "INSERT INTO AnnotationClass (Id, AnnotationSetId, Name, Color) VALUES ($id, $set, $name, $color);", new SqliteParameter("$id", value.Id), new SqliteParameter("$set", value.AnnotationSetId), new SqliteParameter("$name", value.Name), new SqliteParameter("$color", value.Color));
                foreach (var value in importable)
                {
                    ImportedImage item = value.Item;
                    string imageId = value.ImageId;
                    if (!importAnnotations && !importDataset) continue;
                    DatasetAssignmentStatus status = item.Set == "train" ? DatasetAssignmentStatus.Train : item.Set == "test" ? DatasetAssignmentStatus.Test : item.Set == "not_used" ? DatasetAssignmentStatus.NotUsed : DatasetAssignmentStatus.Unassigned;
                    bool retest = assignments.ContainsKey(imageId) && assignments[imageId].Retest;
                    if (importDataset) SetDatasetAssignmentRaw(db, transaction, new DatasetAssignment(target.Id, imageId, status, retest));
                    if (importAnnotations && item.ClassLabel != "") Execute(db, transaction, "INSERT INTO ClaAnnotation (AnnotationSetId, ImageId, ClassId) VALUES ($set, $image, $class);", new SqliteParameter("$set", target.Id), new SqliteParameter("$image", imageId), new SqliteParameter("$class", classByName[item.ClassLabel].Id));
                }
            });
        }

        /// <inheritdoc />
        public string ExportAnnotationJson(string annotationSetId, string outputPath)
        {
            AnnotationSet set = GetAnnotationSets().Single(value => value.Id == annotationSetId);
            return JsonAnnotationExport.Write(this, set, outputPath);
        }

        /// <summary>
        /// 將已驗證的 SEG 區域及其點位寫入指定資料庫交易。
        /// </summary>
        /// <param name="db">供目前操作使用的 SQLite 連線。</param>
        /// <param name="transaction">目前 SQLite 連線上的資料庫交易。</param>
        /// <param name="setId">標註集識別碼。</param>
        /// <param name="imageId">圖片識別碼。</param>
        /// <param name="classId">類別識別碼。</param>
        /// <param name="region">已驗證的標註區域資料。</param>
        /// <param name="zOrder">標註在圖片中的繪製順序。</param>
        private static void InsertImportedSegRegion(SqliteConnection db, SqliteTransaction transaction, string setId, string imageId, string classId, ImportedRegion region, int zOrder)
        {
            string id = Guid.NewGuid().ToString("D");
            int x = region.X;
            int y = region.Y;
            int width = region.Type == SegRegionType.Ellipse ? region.RadiusX : region.Width;
            int height = region.Type == SegRegionType.Ellipse ? region.RadiusY : region.Height;
            Execute(db, transaction, "INSERT INTO SegRegion (Id, AnnotationSetId, ImageId, ClassId, Type, X, Y, Width, Height, ZOrder, StrokeWidth) VALUES ($id, $set, $image, $class, $type, $x, $y, $width, $height, $order, $stroke);", new SqliteParameter("$id", id), new SqliteParameter("$set", setId), new SqliteParameter("$image", imageId), new SqliteParameter("$class", classId), new SqliteParameter("$type", (int)region.Type), new SqliteParameter("$x", x), new SqliteParameter("$y", y), new SqliteParameter("$width", width), new SqliteParameter("$height", height), new SqliteParameter("$order", zOrder), new SqliteParameter("$stroke", region.StrokeWidth == 0 ? (object)DBNull.Value : region.StrokeWidth));
            for (int index = 0; index < region.Points.Count; index++) Execute(db, transaction, "INSERT INTO SegRegionPoint (RegionId, Ordinal, X, Y) VALUES ($id, $ordinal, $x, $y);", new SqliteParameter("$id", id), new SqliteParameter("$ordinal", index), new SqliteParameter("$x", region.Points[index].X), new SqliteParameter("$y", region.Points[index].Y));
        }

        /// <inheritdoc />
        public IReadOnlyList<AnnotationSet> GetAnnotationSets()
        {
            var result = new List<AnnotationSet>();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT Id, Name, Type FROM AnnotationSet ORDER BY rowid;";
                using (SqliteDataReader reader = command.ExecuteReader()) while (reader.Read()) result.Add(new AnnotationSet(reader.GetString(0), reader.GetString(1), (AnnotationSetType)reader.GetInt32(2)));
            }
            return result;
        }

        /// <inheritdoc />
        public void RenameAnnotationSet(string annotationSetId, string name)
        {
            Execute(connection, null, "UPDATE AnnotationSet SET Name = $name WHERE Id = $id;", new SqliteParameter("$name", NormalizeName(name)), new SqliteParameter("$id", annotationSetId));
        }

        /// <inheritdoc />
        public bool DeleteAnnotationSet(string annotationSetId, string confirmationName)
        {
            AnnotationSet set = GetAnnotationSets().SingleOrDefault(item => item.Id == annotationSetId);
            if (set == null || !string.Equals(set.Name, confirmationName, StringComparison.Ordinal)) return false;
            CompleteOperation((db, transaction) => Execute(db, transaction, "DELETE FROM AnnotationSet WHERE Id = $id;", new SqliteParameter("$id", annotationSetId)));
            histories.Remove(annotationSetId);
            return true;
        }

        /// <inheritdoc />
        public IReadOnlyList<AnnotationClass> GetClasses(string annotationSetId)
        {
            var result = new List<AnnotationClass>();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT Id, AnnotationSetId, Name, Color FROM AnnotationClass WHERE AnnotationSetId = $set ORDER BY rowid;";
                command.Parameters.AddWithValue("$set", annotationSetId);
                using (SqliteDataReader reader = command.ExecuteReader()) while (reader.Read()) result.Add(new AnnotationClass(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
            }
            return result;
        }

        /// <inheritdoc />
        public AnnotationClass AddClass(string annotationSetId, string name, string color)
        {
            string nextColor = color == null ? NextClassColor(GetClasses(annotationSetId).Select(item => item.Color)) : AnnotationColorFormat.Normalize(color);
            var value = new AnnotationClass(Guid.NewGuid().ToString("D"), annotationSetId, NormalizeName(name), nextColor);
            Execute(connection, null, "INSERT INTO AnnotationClass (Id, AnnotationSetId, Name, Color) VALUES ($id, $set, $name, $color);", new SqliteParameter("$id", value.Id), new SqliteParameter("$set", value.AnnotationSetId), new SqliteParameter("$name", value.Name), new SqliteParameter("$color", value.Color));
            Record(annotationSetId, () => DeleteClassRaw(value.Id), () => InsertClassRaw(value), EstimateHistoryBytes(value.Id, value.AnnotationSetId, value.Name, value.Color));
            return value;
        }

        /// <inheritdoc />
        public void RenameClass(string classId, string name)
        {
            AnnotationClass previous = GetClass(classId); string next = NormalizeName(name);
            Execute(connection, null, "UPDATE AnnotationClass SET Name = $name WHERE Id = $id;", new SqliteParameter("$name", next), new SqliteParameter("$id", classId));
            Record(previous.AnnotationSetId, () => RenameClassRaw(classId, previous.Name), () => RenameClassRaw(classId, next), EstimateHistoryBytes(classId, previous.Name, next));
        }
        /// <inheritdoc />
        public void ChangeClassColor(string classId, string color)
        {
            AnnotationClass previous = GetClass(classId); string next = AnnotationColorFormat.Normalize(color);
            Execute(connection, null, "UPDATE AnnotationClass SET Color = $color WHERE Id = $id;", new SqliteParameter("$color", next), new SqliteParameter("$id", classId));
            Record(previous.AnnotationSetId, () => ChangeClassColorRaw(classId, previous.Color), () => ChangeClassColorRaw(classId, next), EstimateHistoryBytes(classId, previous.Color, next));
        }
        /// <inheritdoc />
        public void DeleteClass(string classId)
        {
            object setValue = ExecuteScalar(connection, "SELECT AnnotationSetId FROM AnnotationClass WHERE Id = $id;", new SqliteParameter("$id", classId));
            if (setValue == null || setValue == DBNull.Value) return;
            string setId = Convert.ToString(setValue);
            if (Convert.ToInt32(ExecuteScalar(connection, "SELECT COUNT(*) FROM AnnotationClass WHERE AnnotationSetId = $set;", new SqliteParameter("$set", setId))) <= 1) throw new InvalidOperationException("An annotation set must retain one class.");
            AnnotationClass deleted = GetClass(classId);
            var assignments = GetClaAssignments(classId);
            var obdAnnotations = GetObdAnnotationsForClass(classId);
            var segRegions = GetSegRegionsForClass(classId);
            Execute(connection, null, "DELETE FROM AnnotationClass WHERE Id = $id;", new SqliteParameter("$id", classId));
            Record(setId, () => { InsertClassRaw(deleted); foreach (var assignment in assignments) SetClaRaw(setId, assignment, classId); foreach (var annotation in obdAnnotations) InsertObdRaw(annotation); foreach (var region in segRegions) InsertSegRegionRaw(region); }, () => DeleteClassRaw(classId), EstimateHistoryBytes(deleted.Id, deleted.AnnotationSetId, deleted.Name, deleted.Color) + EstimateHistoryBytes(assignments) + EstimateHistoryBytes(obdAnnotations) + EstimateHistoryBytes(segRegions));
        }

        /// <inheritdoc />
        public void SetClaClass(string annotationSetId, string imageId, string classId)
        {
            object setType = ExecuteScalar(connection, "SELECT Type FROM AnnotationSet WHERE Id = $set;", new SqliteParameter("$set", annotationSetId));
            if (setType == null || Convert.ToInt32(setType) != (int)AnnotationSetType.Cla) throw new ArgumentException("The annotation set is not CLA.", nameof(annotationSetId));
            if (classId != null && Convert.ToInt32(ExecuteScalar(connection, "SELECT COUNT(*) FROM AnnotationClass WHERE Id = $class AND AnnotationSetId = $set;", new SqliteParameter("$class", classId), new SqliteParameter("$set", annotationSetId))) != 1) throw new ArgumentException("The class does not belong to the annotation set.", nameof(classId));
            string previous = GetClaClassId(annotationSetId, imageId);
            SetClaRaw(annotationSetId, imageId, classId);
            Record(annotationSetId, () => SetClaRaw(annotationSetId, imageId, previous), () => SetClaRaw(annotationSetId, imageId, classId), EstimateHistoryBytes(annotationSetId, imageId, previous, classId));
        }

        /// <inheritdoc />
        public IReadOnlyList<DatasetAssignment> GetDatasetAssignments(string annotationSetId)
        {
            EnsureAnnotationSetExists(annotationSetId);
            var result = new List<DatasetAssignment>();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT image.Id, assignment.Status, assignment.Retest FROM ImageRecord image LEFT JOIN DatasetAssignment assignment ON assignment.ImageId = image.Id AND assignment.AnnotationSetId = $set ORDER BY image.Sequence;";
                command.Parameters.AddWithValue("$set", annotationSetId);
                using (SqliteDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read()) result.Add(new DatasetAssignment(annotationSetId, reader.GetString(0), reader.IsDBNull(1) ? DatasetAssignmentStatus.Unassigned : (DatasetAssignmentStatus)reader.GetInt32(1), !reader.IsDBNull(2) && reader.GetInt32(2) != 0));
                }
            }
            return result;
        }

        /// <inheritdoc />
        public IReadOnlyList<ImageAnnotationSummary> GetImageAnnotationSummaries(string annotationSetId)
        {
            AnnotationSet set = GetAnnotationSets().SingleOrDefault(value => value.Id == annotationSetId);
            if (set == null) throw new ArgumentException("The annotation set does not exist.", nameof(annotationSetId));
            string table = set.Type == AnnotationSetType.Cla ? "ClaAnnotation" : set.Type == AnnotationSetType.Obd ? "ObdAnnotation" : "SegRegion";
            var grouped = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT ImageId, ClassId FROM " + table + " WHERE AnnotationSetId = $set ORDER BY ImageId;";
                command.Parameters.AddWithValue("$set", annotationSetId);
                using (SqliteDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string imageId = reader.GetString(0);
                        List<string> classIds;
                        if (!grouped.TryGetValue(imageId, out classIds)) { classIds = new List<string>(); grouped.Add(imageId, classIds); }
                        counts[imageId] = counts.ContainsKey(imageId) ? counts[imageId] + 1 : 1;
                        string classId = reader.GetString(1);
                        if (!classIds.Contains(classId)) classIds.Add(classId);
                    }
                }
            }
            return grouped.Select(value => new ImageAnnotationSummary(value.Key, value.Value, counts[value.Key])).ToArray();
        }

        /// <inheritdoc />
        public void SetDatasetAssignments(string annotationSetId, IEnumerable<string> imageIds, DatasetAssignmentStatus status)
        {
            if (!Enum.IsDefined(typeof(DatasetAssignmentStatus), status)) throw new ArgumentOutOfRangeException(nameof(status));
            UpdateDatasetAssignments(annotationSetId, imageIds, assignment => new DatasetAssignment(annotationSetId, assignment.ImageId, status, assignment.Retest));
        }

        /// <inheritdoc />
        public void SetRetest(string annotationSetId, IEnumerable<string> imageIds, bool retest)
        {
            UpdateDatasetAssignments(annotationSetId, imageIds, assignment => new DatasetAssignment(annotationSetId, assignment.ImageId, assignment.Status, retest));
        }

        /// <inheritdoc />
        public void ClearAnnotations(string annotationSetId, IEnumerable<string> imageIds)
        {
            string[] ids = ValidateDatasetImageIds(annotationSetId, imageIds);
            if (ids.Length == 0) return;
            AnnotationSet set = GetAnnotationSets().Single(value => value.Id == annotationSetId);
            if (set.Type == AnnotationSetType.Cla)
            {
                string[] previous = ids.Select(imageId => GetClaClassId(annotationSetId, imageId)).ToArray();
                if (previous.All(value => value == null)) return;
                CompleteOperation((db, transaction) => { foreach (string imageId in ids) Execute(db, transaction, "DELETE FROM ClaAnnotation WHERE AnnotationSetId = $set AND ImageId = $image;", new SqliteParameter("$set", annotationSetId), new SqliteParameter("$image", imageId)); });
                Record(annotationSetId, () => { for (int index = 0; index < ids.Length; index++) SetClaRaw(annotationSetId, ids[index], previous[index]); }, () => { foreach (string imageId in ids) SetClaRaw(annotationSetId, imageId, null); }, EstimateHistoryBytes(ids) + EstimateHistoryBytes(previous));
                return;
            }
            if (set.Type == AnnotationSetType.Obd)
            {
                ObdAnnotation[] previous = ids.SelectMany(imageId => GetObdAnnotations(annotationSetId, imageId)).ToArray();
                if (previous.Length == 0) return;
                CompleteOperation((db, transaction) => { foreach (string imageId in ids) Execute(db, transaction, "DELETE FROM ObdAnnotation WHERE AnnotationSetId = $set AND ImageId = $image;", new SqliteParameter("$set", annotationSetId), new SqliteParameter("$image", imageId)); });
                Record(annotationSetId, () => { foreach (ObdAnnotation value in previous) InsertObdRaw(value); }, () => { foreach (string imageId in ids) Execute(connection, historyTransaction, "DELETE FROM ObdAnnotation WHERE AnnotationSetId = $set AND ImageId = $image;", new SqliteParameter("$set", annotationSetId), new SqliteParameter("$image", imageId)); }, EstimateHistoryBytes(previous));
                return;
            }
            SegRegion[] regions = ids.SelectMany(imageId => GetSegRegions(annotationSetId, imageId)).ToArray();
            if (regions.Length == 0) return;
            CompleteOperation((db, transaction) => { foreach (string imageId in ids) Execute(db, transaction, "DELETE FROM SegRegion WHERE AnnotationSetId = $set AND ImageId = $image;", new SqliteParameter("$set", annotationSetId), new SqliteParameter("$image", imageId)); });
            Record(annotationSetId, () => { foreach (SegRegion value in regions) InsertSegRegionRaw(value); }, () => { foreach (string imageId in ids) Execute(connection, historyTransaction, "DELETE FROM SegRegion WHERE AnnotationSetId = $set AND ImageId = $image;", new SqliteParameter("$set", annotationSetId), new SqliteParameter("$image", imageId)); }, EstimateHistoryBytes(regions));
        }

        /// <inheritdoc />
        public DatasetSplitPlan AutoSplitDataset(string annotationSetId, int trainPercent)
        {
            return AutoSplitDataset(annotationSetId, trainPercent, null);
        }

        /// <inheritdoc />
        public DatasetSplitPlan AutoSplitDataset(string annotationSetId, int trainPercent, IReadOnlyCollection<string> imageIds)
        {
            IReadOnlyList<ImageAnnotationSummary> summaries = GetImageAnnotationSummaries(annotationSetId);
            var classes = summaries.ToDictionary(value => value.ImageId, value => value.ClassIds, StringComparer.Ordinal);
            ProjectImage[] images = GetImages().ToArray();
            HashSet<string> selected = imageIds == null ? null : new HashSet<string>(imageIds, StringComparer.Ordinal);
            DatasetSplitPlan plan = DatasetSplitPlanner.Create(images.Where(image => selected == null || selected.Contains(image.Id)).Select(image => new DatasetSplitCandidate(image.Id, classes.ContainsKey(image.Id) ? classes[image.Id] : new string[0], !image.IsMissing)), trainPercent, NextRandom);
            var train = new HashSet<string>(plan.TrainImageIds, StringComparer.Ordinal);
            var test = new HashSet<string>(plan.TestImageIds, StringComparer.Ordinal);
            var availableImageIds = new HashSet<string>(images.Where(image => !image.IsMissing && (selected == null || selected.Contains(image.Id))).Select(image => image.Id), StringComparer.Ordinal);
            DatasetAssignment[] previous = GetDatasetAssignments(annotationSetId).Where(value => availableImageIds.Contains(value.ImageId)).ToArray();
            DatasetAssignment[] next = previous.Select(value => new DatasetAssignment(annotationSetId, value.ImageId, train.Contains(value.ImageId) ? DatasetAssignmentStatus.Train : test.Contains(value.ImageId) ? DatasetAssignmentStatus.Test : DatasetAssignmentStatus.Unassigned, value.Retest)).ToArray();
            if (previous.Where((value, index) => value.Status != next[index].Status).Any())
            {
                CompleteOperation((db, transaction) => { foreach (DatasetAssignment value in next) SetDatasetAssignmentRaw(db, transaction, value); });
                Record(annotationSetId, () => { foreach (DatasetAssignment value in previous) SetDatasetAssignmentRaw(connection, historyTransaction, value); }, () => { foreach (DatasetAssignment value in next) SetDatasetAssignmentRaw(connection, historyTransaction, value); }, EstimateHistoryBytes(next.Select(value => value.ImageId).ToArray()));
            }
            return plan;
        }

        /// <summary>
        /// 計算並交易更新圖片的資料集狀態或 Retest 旗標，再記錄復原動作。
        /// </summary>
        /// <param name="annotationSetId">標註集識別碼。</param>
        /// <param name="imageIds">圖片識別碼集合。</param>
        /// <param name="transform">用於產生更新後資料集分配的轉換委派。</param>
        private void UpdateDatasetAssignments(string annotationSetId, IEnumerable<string> imageIds, Func<DatasetAssignment, DatasetAssignment> transform)
        {
            string[] ids = ValidateDatasetImageIds(annotationSetId, imageIds);
            if (ids.Length == 0) return;
            DatasetAssignment[] requested = GetDatasetAssignments(annotationSetId).Where(value => ids.Contains(value.ImageId)).ToArray();
            var changes = requested.Select(value => new { Previous = value, Next = transform(value) }).Where(value => value.Previous.Status != value.Next.Status || value.Previous.Retest != value.Next.Retest).ToArray();
            if (changes.Length == 0) return;
            DatasetAssignment[] previous = changes.Select(value => value.Previous).ToArray();
            DatasetAssignment[] next = changes.Select(value => value.Next).ToArray();
            CompleteOperation((db, transaction) => { foreach (DatasetAssignment value in next) SetDatasetAssignmentRaw(db, transaction, value); });
            Record(annotationSetId, () => { foreach (DatasetAssignment value in previous) SetDatasetAssignmentRaw(connection, historyTransaction, value); }, () => { foreach (DatasetAssignment value in next) SetDatasetAssignmentRaw(connection, historyTransaction, value); }, EstimateHistoryBytes(ids));
        }

        /// <summary>
        /// 確認圖片識別碼屬於指定標註集，並移除重複識別碼。
        /// </summary>
        /// <param name="annotationSetId">標註集識別碼。</param>
        /// <param name="imageIds">圖片識別碼集合。</param>
        /// <returns>回傳屬於指定標註集且已去重的圖片識別碼。</returns>
        /// <exception cref="ArgumentNullException">必要參數為 null。</exception>
        /// <exception cref="ArgumentException">輸入資料不符合操作契約。</exception>
        private string[] ValidateDatasetImageIds(string annotationSetId, IEnumerable<string> imageIds)
        {
            if (imageIds == null) throw new ArgumentNullException(nameof(imageIds));
            EnsureAnnotationSetExists(annotationSetId);
            string[] ids = imageIds.ToArray();
            if (ids.Distinct(StringComparer.Ordinal).Count() != ids.Length) throw new ArgumentException("An image can only be assigned once.", nameof(imageIds));
            foreach (string imageId in ids) GetImageSize(imageId);
            return ids;
        }

        /// <summary>
        /// 確認標註集識別碼仍對應專案中的標註集。
        /// </summary>
        /// <param name="annotationSetId">標註集識別碼。</param>
        /// <exception cref="ArgumentException">輸入資料不符合操作契約。</exception>
        private void EnsureAnnotationSetExists(string annotationSetId)
        {
            if (ExecuteScalar(connection, "SELECT Id FROM AnnotationSet WHERE Id = $id;", new SqliteParameter("$id", annotationSetId)) == null) throw new ArgumentException("The annotation set does not exist.", nameof(annotationSetId));
        }

        /// <summary>
        /// 在目前 SQLite 交易中新增或更新資料集分配資料。
        /// </summary>
        /// <param name="db">供目前操作使用的 SQLite 連線。</param>
        /// <param name="transaction">目前 SQLite 連線上的資料庫交易。</param>
        /// <param name="value">要建立、更新或保存的資料值。</param>
        private static void SetDatasetAssignmentRaw(SqliteConnection db, SqliteTransaction transaction, DatasetAssignment value)
        {
            Execute(db, transaction, "INSERT INTO DatasetAssignment (AnnotationSetId, ImageId, Status, Retest) VALUES ($set, $image, $status, $retest) ON CONFLICT(AnnotationSetId, ImageId) DO UPDATE SET Status = excluded.Status, Retest = excluded.Retest;", new SqliteParameter("$set", value.AnnotationSetId), new SqliteParameter("$image", value.ImageId), new SqliteParameter("$status", (int)value.Status), new SqliteParameter("$retest", value.Retest ? 1 : 0));
        }

        /// <inheritdoc />
        public bool Undo(string annotationSetId) { return RunHistory(annotationSetId, false); }
        /// <inheritdoc />
        public bool Redo(string annotationSetId) { return RunHistory(annotationSetId, true); }
        /// <inheritdoc />
        public bool CanUndo(string annotationSetId) { return GetHistory(annotationSetId).CanUndo; }
        /// <inheritdoc />
        public bool CanRedo(string annotationSetId) { return GetHistory(annotationSetId).CanRedo; }

        /// <summary>
        /// 將已提交操作的復原與重做動作加入對應標註集的歷程。
        /// </summary>
        /// <param name="annotationSetId">標註集識別碼。</param>
        /// <param name="undo">執行 Undo 時還原資料狀態的委派。</param>
        /// <param name="redo">執行 Redo 時重做資料狀態的委派。</param>
        /// <param name="sizeBytes">歷程項目估計占用的位元組數。</param>
        private void Record(string annotationSetId, Action undo, Action redo, long sizeBytes)
        {
            if (replayingHistory) return;
            GetHistory(annotationSetId).Push(new HistoryEntry(undo, redo, sizeBytes));
        }

        /// <summary>
        /// 估算歷程資料所佔用的記憶體容量。
        /// </summary>
        /// <param name="values">要估算、驗證或處理的值集合。</param>
        /// <returns>回傳歷程資料的估計記憶體用量。</returns>
        private static long EstimateHistoryBytes(params string[] values)
        {
            long size = 256;
            foreach (string value in values) if (value != null) size += 64 + (long)value.Length * sizeof(char);
            return size;
        }

        /// <summary>
        /// 估算歷程資料所佔用的記憶體容量。
        /// </summary>
        /// <param name="values">要估算、驗證或處理的值集合。</param>
        /// <returns>回傳歷程資料的估計記憶體用量。</returns>
        private static long EstimateHistoryBytes(IReadOnlyList<string> values)
        {
            long size = 64 + (long)IntPtr.Size * values.Count;
            foreach (string value in values) size += 64 + (long)value.Length * sizeof(char);
            return size;
        }

        /// <summary>
        /// 估算歷程資料所佔用的記憶體容量。
        /// </summary>
        /// <param name="values">要估算、驗證或處理的值集合。</param>
        /// <returns>回傳歷程資料的估計記憶體用量。</returns>
        private static long EstimateHistoryBytes(IReadOnlyList<ObdAnnotation> values)
        {
            long size = 64 + (long)IntPtr.Size * values.Count;
            foreach (ObdAnnotation value in values) size += EstimateHistoryBytes(value.Id, value.AnnotationSetId, value.ImageId, value.ClassId) + sizeof(int) * 5;
            return size;
        }

        /// <summary>
        /// 估算歷程資料所佔用的記憶體容量。
        /// </summary>
        /// <param name="values">要估算、驗證或處理的值集合。</param>
        /// <returns>回傳歷程資料的估計記憶體用量。</returns>
        private static long EstimateHistoryBytes(IReadOnlyList<SegRegion> values)
        {
            long size = 64 + (long)IntPtr.Size * values.Count;
            foreach (SegRegion value in values) size += EstimateHistoryBytes(value.Id, value.AnnotationSetId, value.ImageId, value.ClassId) + sizeof(int) * (6 + value.Points.Count * 2);
            return size;
        }

        /// <summary>
        /// 取得標註集的歷程狀態；尚未建立時建立空白歷程。
        /// </summary>
        /// <param name="annotationSetId">標註集識別碼。</param>
        /// <returns>回傳指定標註集的 Undo/Redo 歷程。</returns>
        /// <exception cref="ArgumentNullException">必要參數為 null。</exception>
        private AnnotationHistory GetHistory(string annotationSetId)
        {
            if (annotationSetId == null) throw new ArgumentNullException(nameof(annotationSetId));
            AnnotationHistory history;
            if (!histories.TryGetValue(annotationSetId, out history))
            {
                history = new AnnotationHistory();
                histories.Add(annotationSetId, history);
            }
            return history;
        }

        /// <summary>
        /// 在 SQLite 交易中執行 Undo 或 Redo，提交成功後才移動歷程項目。
        /// </summary>
        /// <param name="annotationSetId">標註集識別碼。</param>
        /// <param name="redo">執行 Redo 時重做資料狀態的委派。</param>
        /// <returns>回傳是否成功完成指定的 Undo 或 Redo 操作。</returns>
        private bool RunHistory(string annotationSetId, bool redo)
        {
            AnnotationHistory history = GetHistory(annotationSetId);
            // 暫時取出待執行項目；只有資料庫交易提交後才移到另一個堆疊。
            HistoryEntry entry = redo ? history.TakeRedo() : history.TakeUndo();
            if (entry == null) return false;
            bool moved = false;
            try
            {
                replayingHistory = true;
                using (historyTransaction = connection.BeginTransaction())
                {
                    if (redo) entry.Redo(); else entry.Undo();
                    historyTransaction.Commit();
                }
                historyTransaction = null;
                if (redo) history.MoveRedoToUndo(entry); else history.MoveUndoToRedo(entry);
                moved = true;
                Save();
            }
            catch
            {
                // 交易失敗時，將未完成的歷程項目放回原堆疊。
                if (!moved)
                {
                    if (redo) history.ReturnRedo(entry); else history.ReturnUndo(entry);
                }
                throw;
            }
            finally
            {
                historyTransaction = null;
                replayingHistory = false;
            }
            return true;
        }

        /// <summary>
        /// 依類別識別碼讀取類別資料。
        /// </summary>
        /// <param name="classId">類別識別碼。</param>
        /// <returns>回傳與類別識別碼相符的類別資料。</returns>
        /// <exception cref="ArgumentException">輸入資料不符合操作契約。</exception>
        private AnnotationClass GetClass(string classId)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT Id, AnnotationSetId, Name, Color FROM AnnotationClass WHERE Id = $id;";
                command.Parameters.AddWithValue("$id", classId);
                using (SqliteDataReader reader = command.ExecuteReader())
                {
                    if (!reader.Read()) throw new ArgumentException("The annotation class does not exist.", nameof(classId));
                    return new AnnotationClass(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3));
                }
            }
        }

        /// <summary>
        /// 取得指定類別目前使用的 CLA 圖片指派。
        /// </summary>
        /// <param name="classId">類別識別碼。</param>
        /// <returns>回傳指定 CLA 類別已指派的圖片識別碼。</returns>
        private IReadOnlyList<string> GetClaAssignments(string classId)
        {
            var imageIds = new List<string>();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT ImageId FROM ClaAnnotation WHERE ClassId = $class;";
                command.Parameters.AddWithValue("$class", classId);
                using (SqliteDataReader reader = command.ExecuteReader()) while (reader.Read()) imageIds.Add(reader.GetString(0));
            }
            return imageIds;
        }

        /// <summary>
        /// 取得指定類別目前使用的 OBD 標註。
        /// </summary>
        /// <param name="classId">類別識別碼。</param>
        /// <returns>回傳指定類別的 OBD 標註。</returns>
        private IReadOnlyList<ObdAnnotation> GetObdAnnotationsForClass(string classId)
        {
            var result = new List<ObdAnnotation>();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT Id, AnnotationSetId, ImageId, ClassId, X, Y, Width, Height, ZOrder FROM ObdAnnotation WHERE ClassId = $class;";
                command.Parameters.AddWithValue("$class", classId);
                using (SqliteDataReader reader = command.ExecuteReader()) while (reader.Read()) result.Add(ReadObdAnnotation(reader));
            }
            return result;
        }

        /// <summary>
        /// 取得指定類別目前使用的 SEG 區域。
        /// </summary>
        /// <param name="classId">類別識別碼。</param>
        /// <returns>回傳指定類別的 SEG 區域。</returns>
        private IReadOnlyList<SegRegion> GetSegRegionsForClass(string classId)
        {
            var result = new List<SegRegion>();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT Id, AnnotationSetId, ImageId, ClassId, Type, X, Y, Width, Height, ZOrder, StrokeWidth FROM SegRegion WHERE ClassId = $class;";
                command.Parameters.AddWithValue("$class", classId);
                using (SqliteDataReader reader = command.ExecuteReader()) while (reader.Read()) result.Add(ReadSegRegion(reader));
            }
            return result;
        }

        /// <summary>
        /// 在目前工作階段中新增類別資料列。
        /// </summary>
        /// <param name="value">要建立、更新或保存的資料值。</param>
        private void InsertClassRaw(AnnotationClass value)
        {
            Execute(connection, historyTransaction, "INSERT INTO AnnotationClass (Id, AnnotationSetId, Name, Color) VALUES ($id, $set, $name, $color);", new SqliteParameter("$id", value.Id), new SqliteParameter("$set", value.AnnotationSetId), new SqliteParameter("$name", value.Name), new SqliteParameter("$color", value.Color));
        }

        /// <summary>
        /// 在目前工作階段中刪除類別資料列。
        /// </summary>
        /// <param name="classId">類別識別碼。</param>
        private void DeleteClassRaw(string classId)
        {
            Execute(connection, historyTransaction, "DELETE FROM AnnotationClass WHERE Id = $id;", new SqliteParameter("$id", classId));
        }

        /// <summary>
        /// 在目前工作階段中更新類別名稱。
        /// </summary>
        /// <param name="classId">類別識別碼。</param>
        /// <param name="name">要建立、查詢或更新的名稱。</param>
        private void RenameClassRaw(string classId, string name)
        {
            Execute(connection, historyTransaction, "UPDATE AnnotationClass SET Name = $name WHERE Id = $id;", new SqliteParameter("$name", name), new SqliteParameter("$id", classId));
        }

        /// <summary>
        /// 在目前工作階段中更新類別色彩。
        /// </summary>
        /// <param name="classId">類別識別碼。</param>
        /// <param name="color">類別色彩字串。</param>
        private void ChangeClassColorRaw(string classId, string color)
        {
            Execute(connection, historyTransaction, "UPDATE AnnotationClass SET Color = $color WHERE Id = $id;", new SqliteParameter("$color", color), new SqliteParameter("$id", classId));
        }

        /// <summary>
        /// 在目前工作階段中設定圖片的 CLA 類別指派。
        /// </summary>
        /// <param name="annotationSetId">標註集識別碼。</param>
        /// <param name="imageId">圖片識別碼。</param>
        /// <param name="classId">類別識別碼。</param>
        private void SetClaRaw(string annotationSetId, string imageId, string classId)
        {
            Execute(connection, historyTransaction, "DELETE FROM ClaAnnotation WHERE AnnotationSetId = $set AND ImageId = $image;", new SqliteParameter("$set", annotationSetId), new SqliteParameter("$image", imageId));
            if (classId != null) Execute(connection, historyTransaction, "INSERT INTO ClaAnnotation (AnnotationSetId, ImageId, ClassId) VALUES ($set, $image, $class);", new SqliteParameter("$set", annotationSetId), new SqliteParameter("$image", imageId), new SqliteParameter("$class", classId));
        }

        /// <summary>
        /// 將目前資料列轉換為 OBD 標註物件。
        /// </summary>
        /// <param name="reader">目前資料列的 SQLite 資料讀取器。</param>
        /// <returns>回傳資料列所表示的 OBD 標註。</returns>
        private static ObdAnnotation ReadObdAnnotation(SqliteDataReader reader)
        {
            return new ObdAnnotation(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetInt32(4), reader.GetInt32(5), reader.GetInt32(6), reader.GetInt32(7), reader.GetInt32(8));
        }

        /// <summary>
        /// 將目前資料列與點位轉換為 SEG 區域物件。
        /// </summary>
        /// <param name="reader">目前資料列的 SQLite 資料讀取器。</param>
        /// <returns>回傳資料列所表示的 SEG 區域。</returns>
        private SegRegion ReadSegRegion(SqliteDataReader reader)
        {
            string id = reader.GetString(0);
            return new SegRegion(id, reader.GetString(1), reader.GetString(2), reader.GetString(3), (SegRegionType)reader.GetInt32(4), reader.GetInt32(5), reader.GetInt32(6), reader.GetInt32(7), reader.GetInt32(8), reader.GetInt32(9), GetSegPoints(id), reader.IsDBNull(10) ? 0 : reader.GetInt32(10));
        }

        /// <summary>
        /// 依標註識別碼讀取 OBD 標註。
        /// </summary>
        /// <param name="annotationId">標註識別碼。</param>
        /// <returns>回傳與標註識別碼相符的 OBD 標註。</returns>
        /// <exception cref="ArgumentException">輸入資料不符合操作契約。</exception>
        private ObdAnnotation GetObdAnnotation(string annotationId)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT Id, AnnotationSetId, ImageId, ClassId, X, Y, Width, Height, ZOrder FROM ObdAnnotation WHERE Id = $id;";
                command.Parameters.AddWithValue("$id", annotationId);
                using (SqliteDataReader reader = command.ExecuteReader())
                {
                    if (!reader.Read()) throw new ArgumentException("The OBD annotation does not exist.", nameof(annotationId));
                    return ReadObdAnnotation(reader);
                }
            }
        }

        /// <summary>
        /// 確認 OBD 標註所屬標註集、圖片、類別及矩形尺寸有效。
        /// </summary>
        /// <param name="annotationSetId">標註集識別碼。</param>
        /// <param name="imageId">圖片識別碼。</param>
        /// <param name="classId">類別識別碼。</param>
        /// <param name="x">原圖或幾何的 X 座標。</param>
        /// <param name="y">原圖或幾何的 Y 座標。</param>
        /// <param name="width">圖片或形狀的寬度。</param>
        /// <param name="height">圖片或形狀的高度。</param>
        /// <exception cref="ArgumentException">輸入資料不符合操作契約。</exception>
        private void ValidateObd(string annotationSetId, string imageId, string classId, int x, int y, int width, int height)
        {
            object type = ExecuteScalar(connection, "SELECT Type FROM AnnotationSet WHERE Id = $set;", new SqliteParameter("$set", annotationSetId));
            if (type == null || Convert.ToInt32(type) != (int)AnnotationSetType.Obd) throw new ArgumentException("The annotation set is not OBD.", nameof(annotationSetId));
            if (Convert.ToInt32(ExecuteScalar(connection, "SELECT COUNT(*) FROM AnnotationClass WHERE Id = $class AND AnnotationSetId = $set;", new SqliteParameter("$class", classId), new SqliteParameter("$set", annotationSetId))) != 1) throw new ArgumentException("The class does not belong to the annotation set.", nameof(classId));
            ValidateObdGeometry(GetImageSize(imageId), x, y, width, height);
        }

        /// <summary>
        /// 確認 OBD 矩形尺寸為正值且位於圖片範圍內。
        /// </summary>
        /// <param name="imageSize">原始圖片尺寸。</param>
        /// <param name="x">原圖或幾何的 X 座標。</param>
        /// <param name="y">原圖或幾何的 Y 座標。</param>
        /// <param name="width">圖片或形狀的寬度。</param>
        /// <param name="height">圖片或形狀的高度。</param>
        /// <exception cref="ArgumentOutOfRangeException">參數值超出支援範圍。</exception>
        private static void ValidateObdGeometry(Size imageSize, int x, int y, int width, int height)
        {
            if (!IsObdGeometryWithinImage(imageSize, x, y, width, height)) throw new ArgumentOutOfRangeException(nameof(width));
        }

        /// <summary>
        /// 判斷 OBD 矩形是否完整位於圖片尺寸範圍內。
        /// </summary>
        /// <param name="imageSize">原始圖片尺寸。</param>
        /// <param name="x">原圖或幾何的 X 座標。</param>
        /// <param name="y">原圖或幾何的 Y 座標。</param>
        /// <param name="width">圖片或形狀的寬度。</param>
        /// <param name="height">圖片或形狀的高度。</param>
        /// <returns>回傳矩形是否完全位於圖片邊界內。</returns>
        private static bool IsObdGeometryWithinImage(Size imageSize, int x, int y, int width, int height)
        {
            return width >= 1 && height >= 1 && x >= 0 && y >= 0 && (long)x + width <= imageSize.Width && (long)y + height <= imageSize.Height;
        }

        /// <summary>
        /// 讀取指定圖片的原始尺寸。
        /// </summary>
        /// <param name="imageId">圖片識別碼。</param>
        /// <returns>回傳指定圖片的原始寬度與高度。</returns>
        /// <exception cref="ArgumentException">輸入資料不符合操作契約。</exception>
        private Size GetImageSize(string imageId)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT Width, Height FROM ImageRecord WHERE Id = $id;";
                command.Parameters.AddWithValue("$id", imageId);
                using (SqliteDataReader reader = command.ExecuteReader())
                {
                    if (!reader.Read()) throw new ArgumentException("The image does not exist.", nameof(imageId));
                    return new Size(reader.GetInt32(0), reader.GetInt32(1));
                }
            }
        }

        /// <summary>
        /// 將 OBD 標註資料列寫入目前資料庫或交易。
        /// </summary>
        /// <param name="value">要建立、更新或保存的資料值。</param>
        private void InsertObdRaw(ObdAnnotation value)
        {
            InsertObdRaw(connection, historyTransaction, value);
        }

        /// <summary>
        /// 將 OBD 標註資料列寫入目前資料庫或交易。
        /// </summary>
        /// <param name="db">供目前操作使用的 SQLite 連線。</param>
        /// <param name="transaction">目前 SQLite 連線上的資料庫交易。</param>
        /// <param name="value">要建立、更新或保存的資料值。</param>
        private static void InsertObdRaw(SqliteConnection db, SqliteTransaction transaction, ObdAnnotation value)
        {
            Execute(db, transaction, "INSERT INTO ObdAnnotation (Id, AnnotationSetId, ImageId, ClassId, X, Y, Width, Height, ZOrder) VALUES ($id, $set, $image, $class, $x, $y, $width, $height, $order);", new SqliteParameter("$id", value.Id), new SqliteParameter("$set", value.AnnotationSetId), new SqliteParameter("$image", value.ImageId), new SqliteParameter("$class", value.ClassId), new SqliteParameter("$x", value.X), new SqliteParameter("$y", value.Y), new SqliteParameter("$width", value.Width), new SqliteParameter("$height", value.Height), new SqliteParameter("$order", value.ZOrder));
        }

        /// <summary>
        /// 將 OBD 標註資料列更新至目前資料庫或交易。
        /// </summary>
        /// <param name="value">要建立、更新或保存的資料值。</param>
        private void UpdateObdRaw(ObdAnnotation value)
        {
            UpdateObdRaw(value, historyTransaction);
        }

        /// <summary>
        /// 將 OBD 標註資料列更新至目前資料庫或交易。
        /// </summary>
        /// <param name="db">供目前操作使用的 SQLite 連線。</param>
        /// <param name="transaction">目前 SQLite 連線上的資料庫交易。</param>
        /// <param name="value">要建立、更新或保存的資料值。</param>
        private static void UpdateObdRaw(SqliteConnection db, SqliteTransaction transaction, ObdAnnotation value)
        {
            Execute(db, transaction, "UPDATE ObdAnnotation SET ClassId = $class, X = $x, Y = $y, Width = $width, Height = $height, ZOrder = $order WHERE Id = $id;", new SqliteParameter("$class", value.ClassId), new SqliteParameter("$x", value.X), new SqliteParameter("$y", value.Y), new SqliteParameter("$width", value.Width), new SqliteParameter("$height", value.Height), new SqliteParameter("$order", value.ZOrder), new SqliteParameter("$id", value.Id));
        }

        /// <summary>
        /// 將 OBD 標註資料列更新至目前資料庫或交易。
        /// </summary>
        /// <param name="value">要建立、更新或保存的資料值。</param>
        /// <param name="transaction">目前 SQLite 連線上的資料庫交易。</param>
        private void UpdateObdRaw(ObdAnnotation value, SqliteTransaction transaction)
        {
            UpdateObdRaw(connection, transaction, value);
        }

        /// <summary>
        /// 從資料庫移除指定 OBD 標註資料列。
        /// </summary>
        /// <param name="annotationId">標註識別碼。</param>
        private void DeleteObdRaw(string annotationId)
        {
            Execute(connection, historyTransaction, "DELETE FROM ObdAnnotation WHERE Id = $id;", new SqliteParameter("$id", annotationId));
        }

        /// <summary>
        /// 將 SEG 區域資料列及其點位寫入目前資料庫或交易。
        /// </summary>
        /// <param name="value">要建立、更新或保存的資料值。</param>
        private void InsertSegRegionRaw(SegRegion value)
        {
            if (historyTransaction != null) InsertSegRegionRaw(connection, historyTransaction, value);
            else CompleteOperation((db, transaction) => InsertSegRegionRaw(db, transaction, value));
        }

        /// <summary>
        /// 將 SEG 區域資料列及其點位寫入目前資料庫或交易。
        /// </summary>
        /// <param name="db">供目前操作使用的 SQLite 連線。</param>
        /// <param name="transaction">目前 SQLite 連線上的資料庫交易。</param>
        /// <param name="value">要建立、更新或保存的資料值。</param>
        private static void InsertSegRegionRaw(SqliteConnection db, SqliteTransaction transaction, SegRegion value)
        {
            Execute(db, transaction, "INSERT INTO SegRegion (Id, AnnotationSetId, ImageId, ClassId, Type, X, Y, Width, Height, ZOrder, StrokeWidth) VALUES ($id, $set, $image, $class, $type, $x, $y, $width, $height, $order, $stroke);", new SqliteParameter("$id", value.Id), new SqliteParameter("$set", value.AnnotationSetId), new SqliteParameter("$image", value.ImageId), new SqliteParameter("$class", value.ClassId), new SqliteParameter("$type", (int)value.Type), new SqliteParameter("$x", value.X), new SqliteParameter("$y", value.Y), new SqliteParameter("$width", value.Width), new SqliteParameter("$height", value.Height), new SqliteParameter("$order", value.ZOrder), new SqliteParameter("$stroke", value.StrokeWidth == 0 ? (object)DBNull.Value : value.StrokeWidth));
            InsertSegPointsRaw(db, transaction, value.Id, value.Points);
        }

        /// <summary>
        /// 在指定交易中依序寫入 SEG 區域點位。
        /// </summary>
        /// <param name="db">供目前操作使用的 SQLite 連線。</param>
        /// <param name="transaction">目前 SQLite 連線上的資料庫交易。</param>
        /// <param name="regionId">區域識別碼。</param>
        /// <param name="points">要插入、檢查或轉換的幾何點集合。</param>
        private static void InsertSegPointsRaw(SqliteConnection db, SqliteTransaction transaction, string regionId, IReadOnlyList<SegPoint> points)
        {
            using (var command = db.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "INSERT INTO SegRegionPoint (RegionId, Ordinal, X, Y) VALUES ($region, $ordinal, $x, $y);";
                command.Parameters.AddWithValue("$region", regionId);
                SqliteParameter ordinal = command.Parameters.Add("$ordinal", SqliteType.Integer);
                SqliteParameter x = command.Parameters.Add("$x", SqliteType.Integer);
                SqliteParameter y = command.Parameters.Add("$y", SqliteType.Integer);
                command.Prepare();
                for (int index = 0; index < points.Count; index++)
                {
                    SegPoint point = points[index];
                    ordinal.Value = index;
                    x.Value = point.X;
                    y.Value = point.Y;
                    command.ExecuteNonQuery();
                }
            }
        }

        /// <summary>
        /// 依區域識別碼讀取排序後的 SEG 點位。
        /// </summary>
        /// <param name="regionId">區域識別碼。</param>
        /// <returns>回傳區域依序保存的幾何點。</returns>
        private IReadOnlyList<SegPoint> GetSegPoints(string regionId)
        {
            var result = new List<SegPoint>();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT X, Y FROM SegRegionPoint WHERE RegionId = $region ORDER BY Ordinal;";
                command.Parameters.AddWithValue("$region", regionId);
                using (SqliteDataReader reader = command.ExecuteReader()) while (reader.Read()) result.Add(new SegPoint(reader.GetInt32(0), reader.GetInt32(1)));
            }
            return result;
        }

        /// <summary>
        /// 依區域識別碼讀取 SEG 區域及其點位。
        /// </summary>
        /// <param name="regionId">區域識別碼。</param>
        /// <returns>回傳與區域識別碼相符的 SEG 區域。</returns>
        /// <exception cref="ArgumentException">輸入資料不符合操作契約。</exception>
        private SegRegion GetSegRegion(string regionId)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT Id, AnnotationSetId, ImageId, ClassId, Type, X, Y, Width, Height, ZOrder, StrokeWidth FROM SegRegion WHERE Id = $id;";
                command.Parameters.AddWithValue("$id", regionId);
                using (SqliteDataReader reader = command.ExecuteReader())
                {
                    if (!reader.Read()) throw new ArgumentException("The SEG region does not exist.", nameof(regionId));
                    return ReadSegRegion(reader);
                }
            }
        }

        /// <summary>
        /// 在目前資料庫或交易中更新 SEG 區域及其點位。
        /// </summary>
        /// <param name="value">要建立、更新或保存的資料值。</param>
        private void UpdateSegRegionRaw(SegRegion value)
        {
            UpdateSegRegionRaw(connection, historyTransaction, value);
        }

        /// <summary>
        /// 在目前資料庫或交易中更新 SEG 區域及其點位。
        /// </summary>
        /// <param name="db">供目前操作使用的 SQLite 連線。</param>
        /// <param name="transaction">目前 SQLite 連線上的資料庫交易。</param>
        /// <param name="value">要建立、更新或保存的資料值。</param>
        private static void UpdateSegRegionRaw(SqliteConnection db, SqliteTransaction transaction, SegRegion value)
        {
            Execute(db, transaction, "DELETE FROM SegRegionPoint WHERE RegionId = $id;", new SqliteParameter("$id", value.Id));
            Execute(db, transaction, "UPDATE SegRegion SET ClassId = $class, Type = $type, X = $x, Y = $y, Width = $width, Height = $height, ZOrder = $order, StrokeWidth = $stroke WHERE Id = $id;", new SqliteParameter("$class", value.ClassId), new SqliteParameter("$type", (int)value.Type), new SqliteParameter("$x", value.X), new SqliteParameter("$y", value.Y), new SqliteParameter("$width", value.Width), new SqliteParameter("$height", value.Height), new SqliteParameter("$order", value.ZOrder), new SqliteParameter("$stroke", value.StrokeWidth == 0 ? (object)DBNull.Value : value.StrokeWidth), new SqliteParameter("$id", value.Id));
            InsertSegPointsRaw(db, transaction, value.Id, value.Points);
        }

        /// <inheritdoc />
        public string GetClaClassId(string annotationSetId, string imageId)
        {
            object value = ExecuteScalar(connection, "SELECT ClassId FROM ClaAnnotation WHERE AnnotationSetId = $set AND ImageId = $image;", new SqliteParameter("$set", annotationSetId), new SqliteParameter("$image", imageId));
            return value == null || value == DBNull.Value ? null : Convert.ToString(value);
        }

        /// <summary>
        /// 取得指定標註集內各圖片所使用的 CLA 類別識別碼。
        /// </summary>
        /// <param name="annotationSetId">標註集識別碼。</param>
        /// <returns>以圖片識別碼為鍵、CLA 類別識別碼為值的對應集合。</returns>
        internal IReadOnlyDictionary<string, string> GetClaClassIds(string annotationSetId)
        {
            var result = new Dictionary<string, string>();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT ImageId, ClassId FROM ClaAnnotation WHERE AnnotationSetId = $set;";
                command.Parameters.AddWithValue("$set", annotationSetId);
                using (SqliteDataReader reader = command.ExecuteReader()) while (reader.Read()) result.Add(reader.GetString(0), reader.GetString(1));
            }
            return result;
        }

        /// <inheritdoc />
        public IReadOnlyList<ObdAnnotation> GetObdAnnotations(string annotationSetId, string imageId)
        {
            var result = new List<ObdAnnotation>();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT Id, AnnotationSetId, ImageId, ClassId, X, Y, Width, Height, ZOrder FROM ObdAnnotation WHERE AnnotationSetId = $set AND ImageId = $image ORDER BY ZOrder;";
                command.Parameters.AddWithValue("$set", annotationSetId);
                command.Parameters.AddWithValue("$image", imageId);
                using (SqliteDataReader reader = command.ExecuteReader()) while (reader.Read()) result.Add(ReadObdAnnotation(reader));
            }
            return result;
        }

        /// <summary>
        /// 讀取指定標註集所有圖片的 OBD 標註。
        /// </summary>
        /// <param name="annotationSetId">標註集識別碼。</param>
        /// <returns>回傳指定標註集或圖片的 OBD 標註集合。</returns>
        internal IReadOnlyList<ObdAnnotation> GetObdAnnotations(string annotationSetId)
        {
            var result = new List<ObdAnnotation>();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT Id, AnnotationSetId, ImageId, ClassId, X, Y, Width, Height, ZOrder FROM ObdAnnotation WHERE AnnotationSetId = $set ORDER BY ImageId, ZOrder;";
                command.Parameters.AddWithValue("$set", annotationSetId);
                using (SqliteDataReader reader = command.ExecuteReader()) while (reader.Read()) result.Add(ReadObdAnnotation(reader));
            }
            return result;
        }

        /// <inheritdoc />
        public ObdAnnotation AddObdAnnotation(string annotationSetId, string imageId, string classId, int x, int y, int width, int height)
        {
            ValidateObd(annotationSetId, imageId, classId, x, y, width, height);
            int zOrder = Convert.ToInt32(ExecuteScalar(connection, "SELECT COALESCE(MAX(ZOrder), -1) + 1 FROM ObdAnnotation WHERE AnnotationSetId = $set AND ImageId = $image;", new SqliteParameter("$set", annotationSetId), new SqliteParameter("$image", imageId)));
            var value = new ObdAnnotation(Guid.NewGuid().ToString("D"), annotationSetId, imageId, classId, x, y, width, height, zOrder);
            InsertObdRaw(value);
            Record(annotationSetId, () => DeleteObdRaw(value.Id), () => InsertObdRaw(value), EstimateHistoryBytes(value.Id, value.AnnotationSetId, value.ImageId, value.ClassId));
            return value;
        }

        /// <inheritdoc />
        public IReadOnlyList<ObdAnnotation> AddObdAnnotationCopies(string annotationSetId, IEnumerable<string> imageIds, IEnumerable<ObdAnnotation> annotations)
        {
            if (imageIds == null) throw new ArgumentNullException(nameof(imageIds));
            if (annotations == null) throw new ArgumentNullException(nameof(annotations));
            string[] targets = imageIds.Distinct(StringComparer.Ordinal).ToArray();
            ObdAnnotation[] source = annotations.ToArray();
            if (source.Length == 0 || targets.Length == 0) return new List<ObdAnnotation>();
            object type = ExecuteScalar(connection, "SELECT Type FROM AnnotationSet WHERE Id = $set;", new SqliteParameter("$set", annotationSetId));
            if (type == null || Convert.ToInt32(type) != (int)AnnotationSetType.Obd) throw new ArgumentException("The annotation set is not OBD.", nameof(annotationSetId));
            HashSet<string> classIds = new HashSet<string>(GetClasses(annotationSetId).Select(value => value.Id), StringComparer.Ordinal);
            if (source.Any(value => value.AnnotationSetId != annotationSetId || !classIds.Contains(value.ClassId))) throw new ArgumentException("The source annotations must use classes in the target annotation set.", nameof(annotations));
            var copies = new List<ObdAnnotation>();
            foreach (string imageId in targets)
            {
                Size imageSize = GetImageSize(imageId);
                int zOrder = Convert.ToInt32(ExecuteScalar(connection, "SELECT COALESCE(MAX(ZOrder), -1) + 1 FROM ObdAnnotation WHERE AnnotationSetId = $set AND ImageId = $image;", new SqliteParameter("$set", annotationSetId), new SqliteParameter("$image", imageId)));
                foreach (ObdAnnotation value in source)
                {
                    if (!IsObdGeometryWithinImage(imageSize, value.X, value.Y, value.Width, value.Height)) continue;
                    copies.Add(new ObdAnnotation(Guid.NewGuid().ToString("D"), annotationSetId, imageId, value.ClassId, value.X, value.Y, value.Width, value.Height, zOrder++));
                }
            }
            if (copies.Count == 0) return copies;
            CompleteOperation((db, transaction) => { foreach (ObdAnnotation value in copies) InsertObdRaw(db, transaction, value); });
            Record(annotationSetId, () => { foreach (ObdAnnotation value in copies) DeleteObdRaw(value.Id); }, () => { foreach (ObdAnnotation value in copies) InsertObdRaw(value); }, EstimateHistoryBytes(copies));
            return copies;
        }

        /// <inheritdoc />
        public IReadOnlyList<SegRegion> GetSegRegions(string annotationSetId, string imageId)
        {
            var result = new List<SegRegion>();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT Id, AnnotationSetId, ImageId, ClassId, Type, X, Y, Width, Height, ZOrder, StrokeWidth FROM SegRegion WHERE AnnotationSetId = $set AND ImageId = $image ORDER BY ZOrder;";
                command.Parameters.AddWithValue("$set", annotationSetId);
                command.Parameters.AddWithValue("$image", imageId);
                using (SqliteDataReader reader = command.ExecuteReader()) while (reader.Read()) result.Add(ReadSegRegion(reader));
            }
            return result;
        }

        /// <summary>
        /// 讀取指定標註集所有圖片的 SEG 區域。
        /// </summary>
        /// <param name="annotationSetId">標註集識別碼。</param>
        /// <returns>回傳指定標註集或圖片的 SEG 區域集合。</returns>
        internal IReadOnlyList<SegRegion> GetSegRegions(string annotationSetId)
        {
            var points = new Dictionary<string, List<SegPoint>>();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT point.RegionId, point.X, point.Y FROM SegRegionPoint point INNER JOIN SegRegion region ON region.Id = point.RegionId WHERE region.AnnotationSetId = $set ORDER BY point.RegionId, point.Ordinal;";
                command.Parameters.AddWithValue("$set", annotationSetId);
                using (SqliteDataReader reader = command.ExecuteReader()) while (reader.Read()) { string id = reader.GetString(0); if (!points.ContainsKey(id)) points.Add(id, new List<SegPoint>()); points[id].Add(new SegPoint(reader.GetInt32(1), reader.GetInt32(2))); }
            }
            var result = new List<SegRegion>();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT Id, AnnotationSetId, ImageId, ClassId, Type, X, Y, Width, Height, ZOrder, StrokeWidth FROM SegRegion WHERE AnnotationSetId = $set ORDER BY ImageId, ZOrder;";
                command.Parameters.AddWithValue("$set", annotationSetId);
                using (SqliteDataReader reader = command.ExecuteReader()) while (reader.Read()) { string id = reader.GetString(0); result.Add(new SegRegion(id, reader.GetString(1), reader.GetString(2), reader.GetString(3), (SegRegionType)reader.GetInt32(4), reader.GetInt32(5), reader.GetInt32(6), reader.GetInt32(7), reader.GetInt32(8), reader.GetInt32(9), points.ContainsKey(id) ? points[id] : new SegPoint[0], reader.IsDBNull(10) ? 0 : reader.GetInt32(10))); }
            }
            return result;
        }

        /// <inheritdoc />
        public IReadOnlyList<SegRegion> AddSegRegionCopies(string annotationSetId, IEnumerable<string> imageIds, IEnumerable<SegRegion> regions)
        {
            if (imageIds == null) throw new ArgumentNullException(nameof(imageIds));
            if (regions == null) throw new ArgumentNullException(nameof(regions));
            string[] targets = imageIds.Distinct(StringComparer.Ordinal).ToArray();
            SegRegion[] source = regions.ToArray();
            if (source.Length == 0 || targets.Length == 0) return new List<SegRegion>();
            object type = ExecuteScalar(connection, "SELECT Type FROM AnnotationSet WHERE Id = $set;", new SqliteParameter("$set", annotationSetId));
            if (type == null || Convert.ToInt32(type) != (int)AnnotationSetType.Seg) throw new ArgumentException("The annotation set is not SEG.", nameof(annotationSetId));
            HashSet<string> classIds = new HashSet<string>(GetClasses(annotationSetId).Select(value => value.Id), StringComparer.Ordinal);
            if (source.Any(value => value.AnnotationSetId != annotationSetId || !classIds.Contains(value.ClassId))) throw new ArgumentException("The source regions must use classes in the target annotation set.", nameof(regions));
            var copies = new List<SegRegion>();
            foreach (string imageId in targets)
            {
                Size imageSize = GetImageSize(imageId);
                int zOrder = NextSegZOrder(annotationSetId, imageId);
                foreach (SegRegion value in source)
                {
                    var candidate = new SegRegion(Guid.NewGuid().ToString("D"), annotationSetId, imageId, value.ClassId, value.Type, value.X, value.Y, value.Width, value.Height, zOrder, value.Points.ToArray(), value.StrokeWidth);
                    try { ValidateSegGeometry(imageSize, candidate); }
                    catch (ArgumentException) { continue; }
                    copies.Add(candidate);
                    zOrder++;
                }
            }
            if (copies.Count == 0) return copies;
            CompleteOperation((db, transaction) => { foreach (SegRegion value in copies) InsertSegRegionRaw(db, transaction, value); });
            Record(annotationSetId,
                () => { foreach (SegRegion value in copies) Execute(connection, historyTransaction, "DELETE FROM SegRegion WHERE Id = $id;", new SqliteParameter("$id", value.Id)); },
                () => { foreach (SegRegion value in copies) InsertSegRegionRaw(value); },
                EstimateHistoryBytes(copies));
            return copies;
        }

        /// <inheritdoc />
        public SegRegion AddSegRegion(string annotationSetId, string imageId, string classId, SegRegionType type, int x, int y, int width, int height)
        {
            object setType = ExecuteScalar(connection, "SELECT Type FROM AnnotationSet WHERE Id = $set;", new SqliteParameter("$set", annotationSetId));
            if (setType == null || Convert.ToInt32(setType) != (int)AnnotationSetType.Seg) throw new ArgumentException("The annotation set is not SEG.", nameof(annotationSetId));
            if (type != SegRegionType.Rect && type != SegRegionType.Ellipse) throw new ArgumentOutOfRangeException(nameof(type));
            if (Convert.ToInt32(ExecuteScalar(connection, "SELECT COUNT(*) FROM AnnotationClass WHERE Id = $class AND AnnotationSetId = $set;", new SqliteParameter("$class", classId), new SqliteParameter("$set", annotationSetId))) != 1) throw new ArgumentException("The class does not belong to the annotation set.", nameof(classId));
            Size imageSize = GetImageSize(imageId);
            if (type == SegRegionType.Rect) ValidateObdGeometry(imageSize, x, y, width, height);
            else if (width < 1 || height < 1 || x - width < 0 || y - height < 0 || (long)x + width > imageSize.Width || (long)y + height > imageSize.Height) throw new ArgumentOutOfRangeException(nameof(width));
            int zOrder = NextSegZOrder(annotationSetId, imageId);
            var value = new SegRegion(Guid.NewGuid().ToString("D"), annotationSetId, imageId, classId, type, x, y, width, height, zOrder);
            return AddNewSegRegion(value, imageSize);
        }

        /// <inheritdoc />
        public SegRegion AddSegPath(string annotationSetId, string imageId, string classId, SegRegionType type, IReadOnlyList<SegPoint> points, int strokeWidth)
        {
            if (points == null) throw new ArgumentNullException(nameof(points));
            if (type != SegRegionType.PolyLine && type != SegRegionType.PolyGon) throw new ArgumentOutOfRangeException(nameof(type));
            object setType = ExecuteScalar(connection, "SELECT Type FROM AnnotationSet WHERE Id = $set;", new SqliteParameter("$set", annotationSetId));
            if (setType == null || Convert.ToInt32(setType) != (int)AnnotationSetType.Seg) throw new ArgumentException("The annotation set is not SEG.", nameof(annotationSetId));
            if (Convert.ToInt32(ExecuteScalar(connection, "SELECT COUNT(*) FROM AnnotationClass WHERE Id = $class AND AnnotationSetId = $set;", new SqliteParameter("$class", classId), new SqliteParameter("$set", annotationSetId))) != 1) throw new ArgumentException("The class does not belong to the annotation set.", nameof(classId));
            SegPoint[] values = points.ToArray();
            if (values.Any(point => point == null)) throw new ArgumentException("The path contains a null point.", nameof(points));
            int distinctPointCount = values.Distinct(new SegPointComparer()).Count();
            if (values.Length < (type == SegRegionType.PolyGon ? 3 : 1) ||
                (type == SegRegionType.PolyGon && distinctPointCount < 3) ||
                (type == SegRegionType.PolyLine && values.Length > 1 && distinctPointCount < 2)) throw new ArgumentException("The path has too few distinct points.", nameof(points));
            if (type == SegRegionType.PolyGon && AreCollinear(values)) throw new ArgumentException("The polygon points are collinear.", nameof(points));
            if (type == SegRegionType.PolyLine && (strokeWidth < 1 || strokeWidth > 100)) throw new ArgumentOutOfRangeException(nameof(strokeWidth));
            Size imageSize = GetImageSize(imageId);
            if (values.Any(point => point.X < 0 || point.Y < 0 || point.X > imageSize.Width || point.Y > imageSize.Height)) throw new ArgumentOutOfRangeException(nameof(points));
            if (type == SegRegionType.PolyLine)
            {
                int inset = (strokeWidth + 1) / 2;
                if (values.Any(point => point.X < inset || point.Y < inset || point.X > imageSize.Width - inset || point.Y > imageSize.Height - inset)) throw new ArgumentOutOfRangeException(nameof(points));
            }
            int zOrder = NextSegZOrder(annotationSetId, imageId);
            var value = new SegRegion(Guid.NewGuid().ToString("D"), annotationSetId, imageId, classId, type, 0, 0, 0, 0, zOrder, values, type == SegRegionType.PolyLine ? strokeWidth : 0);
            return AddNewSegRegion(value, imageSize);
        }

        /// <inheritdoc />
        public void EraseSegPath(string annotationSetId, string imageId, string classId, IReadOnlyList<SegPoint> points, int strokeWidth)
        {
            EraseSegGeometry(annotationSetId, imageId, classId, SegRegionType.PolyLine, 0, 0, 0, 0, points, strokeWidth);
        }

        /// <inheritdoc />
        public void EraseSegGeometry(string annotationSetId, string imageId, string classId, SegRegionType type, int x, int y, int width, int height, IReadOnlyList<SegPoint> points, int strokeWidth)
        {
            if (points == null) points = new SegPoint[0];
            object setType = ExecuteScalar(connection, "SELECT Type FROM AnnotationSet WHERE Id = $set;", new SqliteParameter("$set", annotationSetId));
            if (setType == null || Convert.ToInt32(setType) != (int)AnnotationSetType.Seg) throw new ArgumentException("The annotation set is not SEG.", nameof(annotationSetId));
            if (Convert.ToInt32(ExecuteScalar(connection, "SELECT COUNT(*) FROM AnnotationClass WHERE Id = $class AND AnnotationSetId = $set;", new SqliteParameter("$class", classId), new SqliteParameter("$set", annotationSetId))) != 1) throw new ArgumentException("The class does not belong to the annotation set.", nameof(classId));
            Size imageSize = GetImageSize(imageId);
            SegRegion geometry = new SegRegion("erase", annotationSetId, imageId, classId, type, x, y, width, height, 0, points, strokeWidth);
            ValidateSegGeometry(imageSize, geometry);
            Paths64 eraser = ToPaths(geometry);
            SegRegion[] affected = GetSegRegions(annotationSetId, imageId).Where(value => value.ClassId == classId && HasAreaOverlap(ToPaths(value), eraser)).ToArray();
            if (affected.Length == 0) return;
            var next = new List<SegRegion>();
            int zOrder = NextSegZOrder(annotationSetId, imageId);
            foreach (SegRegion region in affected)
            {
                Paths64 difference = SegGeometryPrototype.Difference(ToPaths(region), eraser);
                next.AddRange(ToPolygonRegions(region.AnnotationSetId, region.ImageId, region.ClassId, difference, imageSize, ref zOrder, new[] { region }));
            }
            CompleteOperation((db, transaction) =>
            {
                foreach (SegRegion region in affected) Execute(db, transaction, "DELETE FROM SegRegion WHERE Id = $id;", new SqliteParameter("$id", region.Id));
                foreach (SegRegion region in next) InsertSegRegionRaw(db, transaction, region);
            });
            Record(annotationSetId,
                () => { foreach (SegRegion region in next) Execute(connection, historyTransaction, "DELETE FROM SegRegion WHERE Id = $id;", new SqliteParameter("$id", region.Id)); foreach (SegRegion region in affected) InsertSegRegionRaw(region); },
                () => { foreach (SegRegion region in affected) Execute(connection, historyTransaction, "DELETE FROM SegRegion WHERE Id = $id;", new SqliteParameter("$id", region.Id)); foreach (SegRegion region in next) InsertSegRegionRaw(region); },
                EstimateHistoryBytes(affected) + EstimateHistoryBytes(next));
        }

        /// <summary>
        /// 驗證新 SEG 區域，並合併同類別中具有正面積重疊的區域。
        /// </summary>
        /// <param name="value">要建立、更新或保存的資料值。</param>
        /// <param name="imageSize">原始圖片尺寸。</param>
        /// <returns>回傳完成幾何驗證、聯集或差集後的 SEG 區域集合。</returns>
        private SegRegion AddNewSegRegion(SegRegion value, Size imageSize)
        {
            Paths64 merged = ToPaths(value);
            var removed = new List<SegRegion>();
            bool found;
            do
            {
                found = false;
                foreach (SegRegion existing in GetSegRegions(value.AnnotationSetId, value.ImageId).Where(item => item.ClassId == value.ClassId && !removed.Any(removedItem => removedItem.Id == item.Id)))
                {
                    Paths64 existingPaths = ToPaths(existing);
                    if (!HasAreaOverlap(merged, existingPaths)) continue;
                    merged = SegGeometryPrototype.Union(merged, existingPaths);
                    removed.Add(existing);
                    found = true;
                }
            } while (found);
            if (removed.Count == 0)
            {
                InsertSegRegionRaw(value);
                Record(value.AnnotationSetId, () => Execute(connection, historyTransaction, "DELETE FROM SegRegion WHERE Id = $id;", new SqliteParameter("$id", value.Id)), () => InsertSegRegionRaw(value), EstimateHistoryBytes(new[] { value }));
                return value;
            }
            int zOrder = NextSegZOrder(value.AnnotationSetId, value.ImageId);
            List<SegRegion> next = ToPolygonRegions(value.AnnotationSetId, value.ImageId, value.ClassId, merged, imageSize, ref zOrder, removed.Concat(new[] { value }));
            CompleteOperation((db, transaction) =>
            {
                foreach (SegRegion region in removed) Execute(db, transaction, "DELETE FROM SegRegion WHERE Id = $id;", new SqliteParameter("$id", region.Id));
                foreach (SegRegion region in next) InsertSegRegionRaw(db, transaction, region);
            });
            Record(value.AnnotationSetId,
                () => { foreach (SegRegion region in next) Execute(connection, historyTransaction, "DELETE FROM SegRegion WHERE Id = $id;", new SqliteParameter("$id", region.Id)); foreach (SegRegion region in removed) InsertSegRegionRaw(region); },
                () => { foreach (SegRegion region in removed) Execute(connection, historyTransaction, "DELETE FROM SegRegion WHERE Id = $id;", new SqliteParameter("$id", region.Id)); foreach (SegRegion region in next) InsertSegRegionRaw(region); },
                EstimateHistoryBytes(removed) + EstimateHistoryBytes(next));
            return next[0];
        }

        /// <summary>
        /// 取得圖片內下一個 SEG 區域顯示順序值。
        /// </summary>
        /// <param name="annotationSetId">標註集識別碼。</param>
        /// <param name="imageId">圖片識別碼。</param>
        /// <returns>回傳指定圖片下一個可用的標註繪製順序。</returns>
        private int NextSegZOrder(string annotationSetId, string imageId)
        {
            return Convert.ToInt32(ExecuteScalar(connection, "SELECT COALESCE(MAX(ZOrder), -1) + 1 FROM SegRegion WHERE AnnotationSetId = $set AND ImageId = $image;", new SqliteParameter("$set", annotationSetId), new SqliteParameter("$image", imageId)));
        }

        /// <summary>
        /// 判斷兩個 Clipper2 路徑是否有正面積重疊。
        /// </summary>
        /// <param name="first">第一個比較或運算的值。</param>
        /// <param name="second">第二個比較或運算的值。</param>
        /// <returns>回傳兩組路徑是否有大於零的面積重疊。</returns>
        private static bool HasAreaOverlap(Paths64 first, Paths64 second)
        {
            return SegGeometryPrototype.Area(Clipper.Intersect(first, second, FillRule.NonZero)) > 0;
        }

        /// <summary>
        /// 將 SEG 點位轉換為 Clipper2 整數路徑。
        /// </summary>
        /// <param name="points">要插入、檢查或轉換的幾何點集合。</param>
        /// <returns>回傳由標註點轉換而成的整數幾何路徑。</returns>
        private static Path64 ToPath(IEnumerable<SegPoint> points)
        {
            return new Path64(points.Select(point => new Point64(point.X, point.Y)).ToList());
        }

        /// <summary>
        /// 將 SEG 區域轉換為包含外框與洞的 Clipper2 路徑集合。
        /// </summary>
        /// <param name="value">要建立、更新或保存的資料值。</param>
        /// <returns>回傳與 SEG 區域型別相符的幾何路徑集合。</returns>
        private static Paths64 ToPaths(SegRegion value)
        {
            if (value.Type == SegRegionType.Rect) return new Paths64 { SegGeometryPrototype.Rectangle(value.X, value.Y, value.X + value.Width, value.Y + value.Height) };
            if (value.Type == SegRegionType.Ellipse) return new Paths64 { SegGeometryPrototype.Ellipse(value.X, value.Y, value.Width, value.Height, 32) };
            if (value.Type == SegRegionType.PolyLine) return SegGeometryPrototype.Stroke(ToPath(value.Points), value.StrokeWidth);
            Path64 polygon = ToPath(value.Points);
            if (Clipper.Area(polygon) < 0) polygon.Reverse();
            return new Paths64 { polygon };
        }

        /// <summary>
        /// 將布林運算路徑轉回有效的 SEG 多邊形區域。
        /// </summary>
        /// <param name="annotationSetId">標註集識別碼。</param>
        /// <param name="imageId">圖片識別碼。</param>
        /// <param name="classId">類別識別碼。</param>
        /// <param name="paths">要合併或運算的路徑集合。</param>
        /// <param name="imageSize">原始圖片尺寸。</param>
        /// <param name="zOrder">標註在圖片中的繪製順序。</param>
        /// <param name="previousRegions">更新前已存在的區域集合。</param>
        /// <returns>回傳布林運算結果轉換而成的多邊形區域。</returns>
        private static List<SegRegion> ToPolygonRegions(string annotationSetId, string imageId, string classId, Paths64 paths, Size imageSize, ref int zOrder, IEnumerable<SegRegion> previousRegions = null)
        {
            var remaining = paths.Where(path => path.Count >= 3 && Math.Abs(Clipper.Area(path)) > 0).OrderByDescending(path => Math.Abs(Clipper.Area(path))).Select(path => new Path64(path)).ToList();
            var result = new List<SegRegion>();
            while (remaining.Count > 0)
            {
                Path64 outer = remaining[0];
                remaining.RemoveAt(0);
                int orientation = Math.Sign(Clipper.Area(outer));
                var holes = new List<Path64>();
                for (int index = remaining.Count - 1; index >= 0; index--)
                {
                    Path64 possibleHole = remaining[index];
                    if (Math.Sign(Clipper.Area(possibleHole)) != orientation && SegGeometryPrototype.IsPointInside(outer, possibleHole[0]))
                    {
                        holes.Add(possibleHole);
                        remaining.RemoveAt(index);
                    }
                }
                Path64 polygon;
                if (holes.Count == 0) polygon = new Path64(outer);
                else
                {
                    Path64[] contours = new[] { outer }.Concat(holes).ToArray();
                    SegGeometryPrototype.BridgeConnection[] existing = (previousRegions ?? Enumerable.Empty<SegRegion>())
                        .Where(region => region.Type == SegRegionType.PolyGon && region.Points.Count > 0)
                        .SelectMany(region => SegGeometryPrototype.FindExistingBridgeConnections(ToPath(region.Points), outer, holes))
                        .ToArray();
                    polygon = SegGeometryPrototype.BridgeHoles(contours[0], contours.Skip(1), existing);
                }
                SegPoint[] points = polygon.Select(point => new SegPoint((int)Math.Max(0, Math.Min(imageSize.Width, point.X)), (int)Math.Max(0, Math.Min(imageSize.Height, point.Y)))).ToArray();
                if (points.Distinct(new SegPointComparer()).Count() >= 3) result.Add(new SegRegion(Guid.NewGuid().ToString("D"), annotationSetId, imageId, classId, SegRegionType.PolyGon, 0, 0, 0, 0, zOrder++, points));
            }
            return result;
        }

        /// <inheritdoc />
        public void UpdateSegRegions(IEnumerable<SegRegion> regions)
        {
            if (regions == null) throw new ArgumentNullException(nameof(regions));
            SegRegion[] requested = regions.ToArray();
            if (requested.Length == 0) return;
            if (requested.Select(value => value.Id).Distinct(StringComparer.Ordinal).Count() != requested.Length) throw new ArgumentException("A SEG region can only be updated once.", nameof(regions));
            SegRegion[] previous = requested.Select(value => GetSegRegion(value.Id)).OrderBy(value => value.ZOrder).ToArray();
            string setId = previous[0].AnnotationSetId;
            string imageId = previous[0].ImageId;
            if (previous.Any(value => value.AnnotationSetId != setId || value.ImageId != imageId)) throw new ArgumentException("SEG regions must belong to the same image and annotation set.", nameof(regions));
            var classes = new HashSet<string>(GetClasses(setId).Select(value => value.Id), StringComparer.Ordinal);
            Size imageSize = GetImageSize(imageId);
            foreach (SegRegion value in requested)
            {
                if (!classes.Contains(value.ClassId)) throw new ArgumentException("The class does not belong to the annotation set.", nameof(regions));
                ValidateSegGeometry(imageSize, value);
            }
            var byId = requested.ToDictionary(value => value.Id, StringComparer.Ordinal);
            int firstZOrder = Convert.ToInt32(ExecuteScalar(connection, "SELECT COALESCE(MAX(ZOrder), -1) + 1 FROM SegRegion WHERE AnnotationSetId = $set AND ImageId = $image;", new SqliteParameter("$set", setId), new SqliteParameter("$image", imageId)));
            SegRegion[] next = previous.Select((value, index) => WithZOrder(RebridgePolygon(byId[value.Id]), firstZOrder + index)).ToArray();
            CompleteOperation((db, transaction) => { foreach (SegRegion value in next) UpdateSegRegionRaw(db, transaction, value); });
            Record(setId, () => { foreach (SegRegion value in previous) UpdateSegRegionRaw(value); }, () => { foreach (SegRegion value in next) UpdateSegRegionRaw(value); }, EstimateHistoryBytes(previous) + EstimateHistoryBytes(next));
        }

        /// <summary>
        /// 重新輸出帶洞多邊形的橋接點位與繞行方向。
        /// </summary>
        /// <param name="value">要建立、更新或保存的資料值。</param>
        /// <returns>回傳重新建立空洞橋接後的多邊形區域。</returns>
        private static SegRegion RebridgePolygon(SegRegion value)
        {
            if (value.Type != SegRegionType.PolyGon) return value;
            Path64 polygon = SegGeometryPrototype.Rebridge(ToPath(value.Points));
            SegPoint[] points = polygon.Select(point => new SegPoint((int)point.X, (int)point.Y)).ToArray();
            return new SegRegion(value.Id, value.AnnotationSetId, value.ImageId, value.ClassId, value.Type, value.X, value.Y, value.Width, value.Height, value.ZOrder, points, value.StrokeWidth);
        }

        /// <inheritdoc />
        public void DeleteSegRegions(IEnumerable<string> regionIds)
        {
            if (regionIds == null) throw new ArgumentNullException(nameof(regionIds));
            string[] ids = regionIds.ToArray();
            if (ids.Length == 0) return;
            if (ids.Distinct(StringComparer.Ordinal).Count() != ids.Length) throw new ArgumentException("A SEG region can only be deleted once.", nameof(regionIds));
            SegRegion[] previous = ids.Select(GetSegRegion).OrderBy(value => value.ZOrder).ToArray();
            string setId = previous[0].AnnotationSetId;
            if (previous.Any(value => value.AnnotationSetId != setId || value.ImageId != previous[0].ImageId)) throw new ArgumentException("SEG regions must belong to the same image and annotation set.", nameof(regionIds));
            CompleteOperation((db, transaction) => { foreach (string id in ids) Execute(db, transaction, "DELETE FROM SegRegion WHERE Id = $id;", new SqliteParameter("$id", id)); });
            Record(setId, () => { foreach (SegRegion value in previous) InsertSegRegionRaw(value); }, () => { foreach (string id in ids) Execute(connection, historyTransaction, "DELETE FROM SegRegion WHERE Id = $id;", new SqliteParameter("$id", id)); }, EstimateHistoryBytes(previous));
        }

        /// <summary>
        /// 建立套用指定顯示順序的 SEG 區域副本。
        /// </summary>
        /// <param name="value">要建立、更新或保存的資料值。</param>
        /// <param name="zOrder">標註在圖片中的繪製順序。</param>
        /// <returns>回傳繪製順序更新後的區域副本。</returns>
        private static SegRegion WithZOrder(SegRegion value, int zOrder)
        {
            return new SegRegion(value.Id, value.AnnotationSetId, value.ImageId, value.ClassId, value.Type, value.X, value.Y, value.Width, value.Height, zOrder, value.Points, value.StrokeWidth);
        }

        /// <summary>
        /// 確認 SEG 區域座標、尺寸與點位符合圖片邊界及形狀要求。
        /// </summary>
        /// <param name="imageSize">原始圖片尺寸。</param>
        /// <param name="value">要建立、更新或保存的資料值。</param>
        /// <exception cref="ArgumentOutOfRangeException">參數值超出支援範圍。</exception>
        /// <exception cref="ArgumentException">輸入資料不符合操作契約。</exception>
        private static void ValidateSegGeometry(Size imageSize, SegRegion value)
        {
            if (value.Type == SegRegionType.Rect) { ValidateObdGeometry(imageSize, value.X, value.Y, value.Width, value.Height); return; }
            if (value.Type == SegRegionType.Ellipse)
            {
                if (value.Width < 1 || value.Height < 1 || value.X - value.Width < 0 || value.Y - value.Height < 0 || (long)value.X + value.Width > imageSize.Width || (long)value.Y + value.Height > imageSize.Height) throw new ArgumentOutOfRangeException(nameof(value));
                return;
            }
            if (value.Points == null || value.Points.Any(point => point == null) || value.Points.Any(point => point.X < 0 || point.Y < 0 || point.X > imageSize.Width || point.Y > imageSize.Height)) throw new ArgumentOutOfRangeException(nameof(value));
            if (value.Type == SegRegionType.PolyGon && (value.Points.Distinct(new SegPointComparer()).Count() < 3 || AreCollinear(value.Points))) throw new ArgumentException("The polygon is invalid.", nameof(value));
            if (value.Type == SegRegionType.PolyLine)
            {
                if (value.Points.Count < 1 || (value.Points.Count > 1 && value.Points.Distinct(new SegPointComparer()).Count() < 2) || value.StrokeWidth < 1 || value.StrokeWidth > 100) throw new ArgumentException("The line is invalid.", nameof(value));
                int inset = (value.StrokeWidth + 1) / 2;
                if (value.Points.Any(point => point.X < inset || point.Y < inset || point.X > imageSize.Width - inset || point.Y > imageSize.Height - inset)) throw new ArgumentOutOfRangeException(nameof(value));
            }
            if (value.Type != SegRegionType.PolyGon && value.Type != SegRegionType.PolyLine) throw new ArgumentOutOfRangeException(nameof(value));
        }

        /// <summary>
        /// 判斷點位是否全部共線。
        /// </summary>
        /// <param name="points">要插入、檢查或轉換的幾何點集合。</param>
        /// <returns>回傳指定點列是否共線。</returns>
        private static bool AreCollinear(IReadOnlyList<SegPoint> points)
        {
            SegPoint first = points[0];
            SegPoint second = points.First(point => point.X != first.X || point.Y != first.Y);
            return points.All(point => (long)(second.X - first.X) * (point.Y - first.Y) == (long)(second.Y - first.Y) * (point.X - first.X));
        }

        private sealed class SegPointComparer : IEqualityComparer<SegPoint>
        {
            /// <summary>
            /// 依 X 與 Y 座標比較兩個 SEG 點位。
            /// </summary>
            /// <param name="first">比較中的第一個 SEG 點位。</param>
            /// <param name="second">比較中的第二個 SEG 點位。</param>
            /// <returns>兩點座標相同時為 <see langword="true"/>，否則為 <see langword="false"/>。</returns>
            public bool Equals(SegPoint first, SegPoint second) { return first.X == second.X && first.Y == second.Y; }
            /// <summary>
            /// 依 X 與 Y 座標產生 SEG 點位雜湊碼。
            /// </summary>
            /// <param name="value">要計算雜湊碼的 SEG 點位。</param>
            /// <returns>由 X、Y 座標組合而成的整數雜湊碼。</returns>
            public int GetHashCode(SegPoint value) { return value.X.GetHashCode() ^ value.Y.GetHashCode(); }
        }

        /// <inheritdoc />
        public void UpdateObdAnnotation(string annotationId, string classId, int x, int y, int width, int height)
        {
            ObdAnnotation previous = GetObdAnnotation(annotationId);
            ValidateObd(previous.AnnotationSetId, previous.ImageId, classId, x, y, width, height);
            int zOrder = Convert.ToInt32(ExecuteScalar(connection, "SELECT COALESCE(MAX(ZOrder), -1) + 1 FROM ObdAnnotation WHERE AnnotationSetId = $set AND ImageId = $image;", new SqliteParameter("$set", previous.AnnotationSetId), new SqliteParameter("$image", previous.ImageId)));
            var next = new ObdAnnotation(previous.Id, previous.AnnotationSetId, previous.ImageId, classId, x, y, width, height, zOrder);
            UpdateObdRaw(next);
            Record(previous.AnnotationSetId, () => UpdateObdRaw(previous), () => UpdateObdRaw(next), EstimateHistoryBytes(previous.Id, previous.ClassId, next.ClassId));
        }

        /// <inheritdoc />
        public void UpdateObdAnnotations(IEnumerable<ObdAnnotation> annotations)
        {
            if (annotations == null) throw new ArgumentNullException(nameof(annotations));
            ObdAnnotation[] requested = annotations.ToArray();
            if (requested.Length == 0) return;
            if (requested.Select(value => value.Id).Distinct(StringComparer.Ordinal).Count() != requested.Length) throw new ArgumentException("An OBD annotation can only be updated once.", nameof(annotations));

            ObdAnnotation first = GetObdAnnotation(requested[0].Id);
            string annotationSetId = first.AnnotationSetId;
            string imageId = first.ImageId;
            var allAnnotations = GetObdAnnotations(annotationSetId, imageId).ToDictionary(value => value.Id, StringComparer.Ordinal);
            ObdAnnotation[] previous = requested.Select(value =>
            {
                ObdAnnotation existing;
                if (!allAnnotations.TryGetValue(value.Id, out existing)) throw new ArgumentException("OBD annotations must belong to the same image and annotation set.", nameof(annotations));
                return existing;
            }).ToArray();
            object type = ExecuteScalar(connection, "SELECT Type FROM AnnotationSet WHERE Id = $set;", new SqliteParameter("$set", annotationSetId));
            if (type == null || Convert.ToInt32(type) != (int)AnnotationSetType.Obd) throw new ArgumentException("The annotation set is not OBD.", nameof(annotations));
            var classIds = new HashSet<string>(GetClasses(annotationSetId).Select(value => value.Id), StringComparer.Ordinal);
            Size imageSize = GetImageSize(imageId);
            foreach (ObdAnnotation value in requested)
            {
                if (!classIds.Contains(value.ClassId)) throw new ArgumentException("The class does not belong to the annotation set.", nameof(annotations));
                ValidateObdGeometry(imageSize, value.X, value.Y, value.Width, value.Height);
            }
            var requestedById = requested.ToDictionary(value => value.Id, StringComparer.Ordinal);
            if (previous.All(value =>
            {
                ObdAnnotation requestedValue = requestedById[value.Id];
                return value.ClassId == requestedValue.ClassId && value.X == requestedValue.X && value.Y == requestedValue.Y && value.Width == requestedValue.Width && value.Height == requestedValue.Height;
            })) return;

            int firstZOrder = Convert.ToInt32(ExecuteScalar(connection, "SELECT COALESCE(MAX(ZOrder), -1) + 1 FROM ObdAnnotation WHERE AnnotationSetId = $set AND ImageId = $image;", new SqliteParameter("$set", annotationSetId), new SqliteParameter("$image", imageId)));
            ObdAnnotation[] next = previous.OrderBy(value => value.ZOrder).Select((value, index) =>
            {
                ObdAnnotation requestedValue = requestedById[value.Id];
                return new ObdAnnotation(value.Id, annotationSetId, imageId, requestedValue.ClassId, requestedValue.X, requestedValue.Y, requestedValue.Width, requestedValue.Height, firstZOrder + index);
            }).ToArray();
            CompleteOperation((db, transaction) => { foreach (ObdAnnotation value in next) UpdateObdRaw(db, transaction, value); });
            Record(annotationSetId, () => { foreach (ObdAnnotation value in previous) UpdateObdRaw(value); }, () => { foreach (ObdAnnotation value in next) UpdateObdRaw(value); }, EstimateHistoryBytes(previous) + EstimateHistoryBytes(next));
        }

        /// <inheritdoc />
        public void DeleteObdAnnotation(string annotationId)
        {
            ObdAnnotation previous = GetObdAnnotation(annotationId);
            DeleteObdRaw(annotationId);
            Record(previous.AnnotationSetId, () => InsertObdRaw(previous), () => DeleteObdRaw(annotationId), EstimateHistoryBytes(previous.Id, previous.AnnotationSetId, previous.ImageId, previous.ClassId));
        }

        /// <inheritdoc />
        public void DeleteObdAnnotations(IEnumerable<string> annotationIds)
        {
            if (annotationIds == null) throw new ArgumentNullException(nameof(annotationIds));
            string[] ids = annotationIds.ToArray();
            if (ids.Length == 0) return;
            if (ids.Distinct(StringComparer.Ordinal).Count() != ids.Length) throw new ArgumentException("An OBD annotation can only be deleted once.", nameof(annotationIds));
            ObdAnnotation[] previous = ids.Select(GetObdAnnotation).OrderBy(value => value.ZOrder).ToArray();
            string annotationSetId = previous[0].AnnotationSetId;
            string imageId = previous[0].ImageId;
            if (previous.Any(value => value.AnnotationSetId != annotationSetId || value.ImageId != imageId)) throw new ArgumentException("OBD annotations must belong to the same image and annotation set.", nameof(annotationIds));
            CompleteOperation((db, transaction) => { foreach (string id in ids) Execute(db, transaction, "DELETE FROM ObdAnnotation WHERE Id = $id;", new SqliteParameter("$id", id)); });
            Record(annotationSetId, () => { foreach (ObdAnnotation value in previous) InsertObdRaw(value); }, () => { foreach (string id in ids) DeleteObdRaw(id); }, EstimateHistoryBytes(previous));
        }

        /// <inheritdoc />
        public IReadOnlyList<ProjectImage> GetMissingImages()
        {
            return GetImages().Where(image => image.IsMissing).ToList();
        }

        /// <inheritdoc />
        public void RelinkImage(string imageId, string sourcePath)
        {
            if (imageId == null) throw new ArgumentNullException(nameof(imageId));
            if (sourcePath == null) throw new ArgumentNullException(nameof(sourcePath));
            ProjectImage image = GetImages().SingleOrDefault(candidate => candidate.Id == imageId);
            if (image == null) throw new ImageRelinkException(ImageRelinkFailure.ImageNotFound);
            if (!image.IsMissing) throw new ImageRelinkException(ImageRelinkFailure.ImageIsAvailable);
            if (!File.Exists(sourcePath)) throw new ImageRelinkException(ImageRelinkFailure.SourceNotFound);
            if (!string.Equals(image.Sha256, ProjectImageImporter.Hash(sourcePath), StringComparison.OrdinalIgnoreCase)) throw new ImageRelinkException(ImageRelinkFailure.ContentMismatch);

            string destination = Path.Combine(ImagesDirectory, image.ManagedFileName);
            string temporary = Path.Combine(ImagesDirectory, Guid.NewGuid().ToString("N") + ".relinking");
            try
            {
                ProjectImageImporter.CopyToManagedStorage(sourcePath, temporary);
                File.Move(temporary, destination);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        /// <inheritdoc />
        public void DeleteImages(IEnumerable<string> imageIds)
        {
            if (imageIds == null) throw new ArgumentNullException(nameof(imageIds));
            var ids = new HashSet<string>(imageIds);
            if (ids.Count == 0) return;

            var images = GetImages();
            var deleted = images.Where(image => ids.Contains(image.Id)).ToList();
            if (deleted.Count == 0) return;
            var remaining = images.Where(image => !ids.Contains(image.Id)).ToList();

            var stagedPaths = new Dictionary<string, string>();
            try
            {
                // 先移至暫存目錄，讓資料庫刪除失敗時仍可將圖片復原。
                string operationDirectory = Path.Combine(DeletingImagesDirectory, Guid.NewGuid().ToString("N"));
                foreach (ProjectImage image in deleted)
                {
                    string originalPath = Path.Combine(ImagesDirectory, image.ManagedFileName);
                    if (!File.Exists(originalPath)) continue;
                    Directory.CreateDirectory(operationDirectory);
                    string stagedPath = Path.Combine(operationDirectory, image.ManagedFileName);
                    File.Move(originalPath, stagedPath);
                    stagedPaths.Add(originalPath, stagedPath);
                }
            }
            catch
            {
                RestoreStagedImages(stagedPaths);
                throw;
            }

            try
            {
                CompleteOperation((db, transaction) =>
                {
                    foreach (ProjectImage image in deleted)
                    {
                        Execute(db, transaction, "DELETE FROM ImageRecord WHERE Id = $id;", new SqliteParameter("$id", image.Id));
                    }
                    for (int index = 0; index < remaining.Count; index++)
                    {
                        Execute(db, transaction, "UPDATE ImageRecord SET Sequence = $sequence WHERE Id = $id;", new SqliteParameter("$sequence", index + 1), new SqliteParameter("$id", remaining[index].Id));
                    }
                });
            }
            catch
            {
                RestoreStagedImages(stagedPaths);
                throw;
            }
            RecoverStagedImages();
            histories.Clear();
        }

        /// <inheritdoc />
        public IReadOnlyList<ProjectBackup> GetBackups()
        {
            return GetBackups(ProjectPath);
        }

        /// <inheritdoc />
        public IProjectSession RestoreBackup(string backupPath)
        {
            if (backupPath == null) throw new ArgumentNullException(nameof(backupPath));
            string fullBackupPath = Path.GetFullPath(backupPath);
            if (!fullBackupPath.StartsWith(BackupDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("The backup does not belong to this project.", nameof(backupPath));
            string temporary = ProjectPath + ".restore";
            if (!File.Exists(fullBackupPath)) throw new FileNotFoundException("The backup file does not exist.", fullBackupPath);
            File.Copy(fullBackupPath, temporary, true);
            try
            {
                // 在替換目前資料庫前先驗證備份副本，避免無效檔案取代可用專案。
                ValidateDatabase(temporary);
            }
            catch
            {
                File.Delete(temporary);
                throw;
            }
            // 還原失敗時保留可用備份，原工作階段可由呼叫端重新開啟。
            CreateBackup("before-restore-" + UtcNow().ToString("yyyyMMdd-HHmmssfff"));
            connection.Dispose();
            lockStream.Dispose();
            try
            {
                ReplaceFile(temporary, ProjectPath);
            }
            catch (Exception exception)
            {
                throw new ProjectRestoreException(Open(ProjectPath), exception);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
            return Open(ProjectPath);
        }

        /// <inheritdoc />
        public IProjectSession SaveAs(string targetPath)
        {
            string fullTargetPath = PreparePath(targetPath, false);
            try
            {
                Save();
                File.Copy(ProjectPath, fullTargetPath);
                if (Directory.Exists(ImagesDirectory))
                {
                    CopyDirectory(ImagesDirectory, Path.Combine(Path.GetDirectoryName(fullTargetPath), Path.GetFileNameWithoutExtension(fullTargetPath) + ".images"));
                }

                return Open(fullTargetPath);
            }
            catch
            {
                DeleteNewProject(fullTargetPath);
                throw;
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            connection.Dispose();
            lockStream?.Dispose();
        }

        /// <summary>
        /// 驗證並正規化專案路徑，同時復原未完成的檔案替換。
        /// </summary>
        /// <param name="projectPath">專案資料庫檔案路徑。</param>
        /// <param name="mustExist">指定專案路徑是否必須已存在。</param>
        /// <returns>回傳正規化後的完整專案路徑。</returns>
        /// <exception cref="FileNotFoundException">指定的檔案不存在。</exception>
        /// <exception cref="IOException">檔案或目錄作業失敗。</exception>
        private static string PreparePath(string projectPath, bool mustExist)
        {
            ProjectLocationValidator.EnsureSupported(projectPath);
            string fullPath = Path.GetFullPath(projectPath);
            RecoverPendingReplacement(fullPath);
            if (mustExist && !File.Exists(fullPath)) throw new FileNotFoundException("The project file does not exist.", fullPath);
            if (!mustExist && File.Exists(fullPath)) throw new IOException("The project file already exists.");
            string imagesDirectory = Path.Combine(Path.GetDirectoryName(fullPath), Path.GetFileNameWithoutExtension(fullPath) + ".images");
            if (!mustExist && Directory.Exists(imagesDirectory)) throw new IOException("The managed images directory already exists.");
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            return fullPath;
        }

        /// <summary>
        /// 取得專案檔案獨佔鎖，避免多個工作階段同時操作。
        /// </summary>
        /// <param name="path">要讀取、檢查或轉換的路徑。</param>
        /// <returns>回傳成功取得的專案獨佔檔案鎖。</returns>
        /// <exception cref="InvalidOperationException">目前物件或專案狀態不允許此操作。</exception>
        private static FileStream AcquireLock(string path)
        {
            try
            {
                return new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException exception)
            {
                throw new InvalidOperationException("This project is already open in another application instance.", exception);
            }
        }

        /// <summary>
        /// 以暫存檔與回復副本替換專案資料庫，支援中斷後復原。
        /// </summary>
        /// <param name="source">要讀取、匯入或複製的來源。</param>
        /// <param name="destination">複製或移動作業的目的地路徑。</param>
        /// <exception cref="IOException">檔案或目錄作業失敗。</exception>
        private static void ReplaceFileSafely(string source, string destination)
        {
            try { NativeReplaceFile(source, destination); }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is NotSupportedException)
            {
                string original = PendingReplacementPath(destination);
                if (File.Exists(original)) throw new IOException("A previous database replacement has not been recovered.");
                // 備存原資料庫後才替換；下次開啟時可依暫存檔判斷如何復原。
                MoveFile(destination, original);
                try
                {
                    MoveFile(source, destination);
                    File.Delete(original);
                }
                catch
                {
                    if (!File.Exists(destination) && File.Exists(original)) MoveFile(original, destination);
                    throw;
                }
            }
        }

        /// <summary>
        /// 在開啟專案前，依原檔與暫存回復檔狀態完成替換復原。
        /// </summary>
        /// <param name="projectPath">專案資料庫檔案路徑。</param>
        private static void RecoverPendingReplacement(string projectPath)
        {
            string original = PendingReplacementPath(projectPath);
            if (!File.Exists(original)) return;
            if (!File.Exists(projectPath))
            {
                MoveFile(original, projectPath);
                return;
            }
            File.Delete(original);
        }

        /// <summary>
        /// 取得安全替換流程使用的原資料庫暫存路徑。
        /// </summary>
        /// <param name="projectPath">專案資料庫檔案路徑。</param>
        /// <returns>回傳專案資料庫替換作業使用的暫存路徑。</returns>
        private static string PendingReplacementPath(string projectPath)
        {
            return projectPath + ".restore-original";
        }

        /// <summary>
        /// 開啟 SQLite 連線並啟用外鍵檢查。
        /// </summary>
        /// <param name="path">要讀取、檢查或轉換的路徑。</param>
        /// <returns>回傳已開啟並設定完成的 SQLite 連線。</returns>
        private static SqliteConnection OpenConnection(string path)
        {
            var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false }.ToString());
            connection.Open();
            Execute(connection, null, "PRAGMA foreign_keys = ON;");
            Execute(connection, null, "PRAGMA busy_timeout = 5000;");
            return connection;
        }

        /// <summary>
        /// 在指定連線及交易中執行 SQL 非查詢命令。
        /// </summary>
        /// <param name="connection">供資料庫操作使用的 SQLite 連線。</param>
        /// <param name="transaction">目前 SQLite 連線上的資料庫交易。</param>
        /// <param name="commandText">要執行的 SQL 指令文字。</param>
        /// <param name="parameters">要驗證或套用的參數集合。</param>
        private static void Execute(SqliteConnection connection, SqliteTransaction transaction, string commandText, params SqliteParameter[] parameters)
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = commandText;
                command.Parameters.AddRange(parameters);
                command.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// 執行 SQL 命令並回傳第一個資料列的第一個欄位。
        /// </summary>
        /// <param name="connection">供資料庫操作使用的 SQLite 連線。</param>
        /// <param name="commandText">要執行的 SQL 指令文字。</param>
        /// <returns>回傳 SQL 純量查詢結果；沒有資料時回傳 null。</returns>
        private static object ExecuteScalar(SqliteConnection connection, string commandText)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = commandText;
                return command.ExecuteScalar();
            }
        }

        /// <summary>
        /// 執行 SQL 命令並回傳第一個資料列的第一個欄位。
        /// </summary>
        /// <param name="connection">供資料庫操作使用的 SQLite 連線。</param>
        /// <param name="commandText">要執行的 SQL 指令文字。</param>
        /// <param name="parameters">要驗證或套用的參數集合。</param>
        /// <returns>回傳 SQL 純量查詢結果；沒有資料時回傳 null。</returns>
        private static object ExecuteScalar(SqliteConnection connection, string commandText, params SqliteParameter[] parameters)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = commandText;
                command.Parameters.AddRange(parameters);
                return command.ExecuteScalar();
            }
        }

        /// <summary>
        /// 嘗試以唯讀方式開啟圖片，判斷受管理檔案是否可用。
        /// </summary>
        /// <param name="path">要讀取、檢查或轉換的路徑。</param>
        /// <returns>回傳指定檔案是否為可讀取的圖片。</returns>
        private static bool CanReadImage(string path)
        {
            try
            {
                using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read)) return true;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        /// <summary>
        /// 逐張驗證及匯入圖片，回報成功、失敗、取消與處理進度。
        /// </summary>
        /// <param name="sources">要匯入的來源路徑集合。</param>
        /// <param name="cancellationRequested">查詢目前是否要求取消的委派。</param>
        /// <param name="initialFailures">掃描前已收集的失敗項目。</param>
        /// <param name="progress">回報匯入進度的委派。</param>
        /// <returns>回傳來源圖片匯入的成功、失敗、未處理及取消結果。</returns>
        /// <exception cref="ImageImportException">圖片格式、大小、尺寸或專案數量違反匯入限制。</exception>
        private ImageImportReport ImportSourcesWithReport(IReadOnlyList<string> sources, Func<bool> cancellationRequested, IReadOnlyList<ImageImportFailureDetail> initialFailures = null, Action<ImageImportProgress> progress = null)
        {
            var imported = new List<ProjectImage>();
            var failures = initialFailures == null ? new List<ImageImportFailureDetail>() : new List<ImageImportFailureDetail>(initialFailures);
            long existing = Convert.ToInt64(ExecuteScalar(connection, "SELECT COUNT(*) FROM ImageRecord;"));
            progress?.Invoke(new ImageImportProgress(0, sources.Count, 0, failures.Count));
            for (int index = 0; index < sources.Count; index++)
            {
                if (cancellationRequested != null && cancellationRequested()) return new ImageImportReport(imported, failures, sources.Count - index);
                try
                {
                    if (existing + imported.Count >= 10000) throw new ImageImportException(ImageImportFailure.ProjectImageLimit);
                    imported.Add(ImportFile(sources[index], checked((int)existing + imported.Count + 1)));
                }
                catch (ImageImportException exception)
                {
                    failures.Add(new ImageImportFailureDetail(sources[index], exception.Failure));
                }
                catch (Exception)
                {
                    failures.Add(new ImageImportFailureDetail(sources[index], ImageImportFailure.Unknown));
                }
                progress?.Invoke(new ImageImportProgress(index + 1, sources.Count, imported.Count, failures.Count));
            }
            return new ImageImportReport(imported, failures, 0);
        }

        /// <summary>
        /// 將來源圖片複製至受管理目錄並新增資料列，失敗時移除新複製檔。
        /// </summary>
        /// <param name="source">要讀取、匯入或複製的來源。</param>
        /// <param name="sequence">圖片或標註的排序序號。</param>
        /// <returns>回傳成功複製至專案目錄並建立資料記錄的圖片。</returns>
        private ProjectImage ImportFile(string source, int sequence)
        {
            string managedName = NextManagedName(ProjectImageImporter.RequiresPngConversion(source)
                ? Path.GetFileNameWithoutExtension(source) + ".png"
                : Path.GetFileName(source));
            ProjectImage image = ProjectImageImporter.Read(source, managedName, sequence);
            string destination = Path.Combine(ImagesDirectory, managedName);
            // 先寫入圖片檔，再提交資料庫記錄；資料庫寫入失敗時刪除新檔。
            ProjectImageImporter.CopyToManagedStorage(source, destination);
            try
            {
                CompleteOperation((db, transaction) => Execute(db, transaction,
                    "INSERT INTO ImageRecord (Id, OriginalFileName, ManagedFileName, Sha256, Width, Height, Sequence) VALUES ($id, $original, $managed, $sha, $width, $height, $sequence);",
                    new SqliteParameter("$id", image.Id), new SqliteParameter("$original", image.OriginalFileName), new SqliteParameter("$managed", image.ManagedFileName), new SqliteParameter("$sha", image.Sha256), new SqliteParameter("$width", image.Width), new SqliteParameter("$height", image.Height), new SqliteParameter("$sequence", image.Sequence)));
                return image;
            }
            catch
            {
                File.Delete(destination);
                throw;
            }
        }

        /// <summary>
        /// 依自然檔名排序來源路徑，讓匯入順序保持穩定。
        /// </summary>
        /// <param name="filePaths">待匯入的檔案路徑集合。</param>
        /// <returns>依檔名自然排序後的來源檔案路徑清單。</returns>
        private static List<string> OrderSources(IEnumerable<string> filePaths)
        {
            return filePaths.OrderBy(Path.GetFileName, NaturalFileNameComparer.Instance).ToList();
        }

        /// <summary>
        /// 依目前類別色彩選擇下一個未使用的色彩。
        /// </summary>
        /// <param name="existing">目前已存在且符合該操作條件的資料。</param>
        /// <returns>下一個尚未由現有類別使用的色彩識別碼。</returns>
        /// <exception cref="InvalidOperationException">目前物件或專案狀態不允許此操作。</exception>
        private static string NextClassColor(IEnumerable<string> existing)
        {
            var colors = new HashSet<string>(existing.Select(value => AnnotationColorFormat.Normalize(value).Replace("-custom", "")), StringComparer.Ordinal);
            for (int attempt = 0; attempt < 100; attempt++)
            {
                string candidate = string.Format("rgba({0}, {1}, {2}, 1)", NextRandom(256), NextRandom(256), NextRandom(256));
                if (colors.Add(candidate)) return candidate;
            }
            for (int value = 0; value <= 0xFFFFFF; value++)
            {
                string candidate = string.Format("rgba({0}, {1}, {2}, 1)", (value >> 16) & 255, (value >> 8) & 255, value & 255);
                if (colors.Add(candidate)) return candidate;
            }
            throw new InvalidOperationException("No available RGB colors remain.");
        }

        /// <summary>
        /// 檢查資料庫版本，必要時先備份再遷移至目前結構。
        /// </summary>
        /// <param name="db">供目前操作使用的 SQLite 連線。</param>
        /// <param name="projectPath">專案資料庫檔案路徑。</param>
        /// <exception cref="NotSupportedException">操作遇到 NotSupportedException 所代表的錯誤狀態。</exception>
        private static void EnsureCurrentSchema(SqliteConnection db, string projectPath)
        {
            int version = Convert.ToInt32(ExecuteScalar(db, "SELECT Version FROM SchemaInfo LIMIT 1;"));
            if (version == CurrentSchemaVersion) return;
            if (version != 1 && version != 2 && version != 3 && version != 4 && version != 5 && version != 6) throw new NotSupportedException("The project schema version is not supported.");

            CreateMigrationBackup(db, projectPath, version);
            using (SqliteTransaction transaction = db.BeginTransaction())
            {
                CreateImageTable(db, transaction);
                CreateAnnotationTables(db, transaction);
                if (version == 5) Execute(db, transaction, "ALTER TABLE SegRegion ADD COLUMN StrokeWidth INTEGER NULL;");
                Execute(db, transaction, "UPDATE SchemaInfo SET Version = $version;", new SqliteParameter("$version", CurrentSchemaVersion));
                transaction.Commit();
            }
        }

        /// <summary>
        /// 在結構遷移前建立資料庫備份。
        /// </summary>
        /// <param name="db">供目前操作使用的 SQLite 連線。</param>
        /// <param name="projectPath">專案資料庫檔案路徑。</param>
        /// <param name="version">目前資料庫結構版本。</param>
        private static void CreateMigrationBackup(SqliteConnection db, string projectPath, int version)
        {
            Execute(db, null, "PRAGMA wal_checkpoint(FULL);");
            string directory = Path.Combine(Path.GetDirectoryName(projectPath), Path.GetFileNameWithoutExtension(projectPath) + ".backups");
            Directory.CreateDirectory(directory);
            string name = "before-migration-v" + version + ".neurocle";
            string backupPath = Path.Combine(directory, name);
            int suffix = 1;
            while (File.Exists(backupPath)) backupPath = Path.Combine(directory, "before-migration-v" + version + " (" + suffix++ + ").neurocle");
            File.Copy(projectPath, backupPath);
            File.SetLastWriteTimeUtc(backupPath, UtcNow());
            TrimBackups(directory);
        }

        // 取得此專案的備份目錄。
        private string BackupDirectory
        {
            get { return GetBackupDirectory(ProjectPath); }
        }

        /// <summary>
        /// 建立當日備份，並維持專案備份數量上限。
        /// </summary>
        private void CreateDailyBackup()
        {
            string name = "daily-" + UtcNow().ToString("yyyy-MM-dd") + ".neurocle";
            if (File.Exists(Path.Combine(BackupDirectory, name))) return;
            Save();
            Directory.CreateDirectory(BackupDirectory);
            File.Copy(ProjectPath, Path.Combine(BackupDirectory, name));
            File.SetLastWriteTimeUtc(Path.Combine(BackupDirectory, name), UtcNow());
            TrimBackups(BackupDirectory);
        }

        /// <summary>
        /// 儲存目前資料庫後建立指定名稱的備份。
        /// </summary>
        /// <param name="name">要建立、查詢或更新的名稱。</param>
        private void CreateBackup(string name)
        {
            Save();
            CreateBackupFile(ProjectPath, name);
        }

        /// <summary>
        /// 建立具唯一檔名的資料庫備份並更新保留清單。
        /// </summary>
        /// <param name="projectPath">專案資料庫檔案路徑。</param>
        /// <param name="name">要建立、查詢或更新的名稱。</param>
        private static void CreateBackupFile(string projectPath, string name)
        {
            string directory = GetBackupDirectory(projectPath);
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, name + ".neurocle");
            int suffix = 1;
            while (File.Exists(path)) path = Path.Combine(directory, name + " (" + suffix++ + ").neurocle");
            File.Copy(projectPath, path);
            File.SetLastWriteTimeUtc(path, UtcNow());
            TrimBackups(directory);
        }

        /// <summary>
        /// 列出指定專案備份目錄中的備份檔，並依建立時間由新到舊排序。
        /// </summary>
        /// <param name="projectPath">專案資料庫檔案路徑。</param>
        /// <returns>依建立時間由新到舊排列的備份清單；時間相同時依路徑由後到前排列。</returns>
        private static IReadOnlyList<ProjectBackup> GetBackups(string projectPath)
        {
            string directory = GetBackupDirectory(projectPath);
            if (!Directory.Exists(directory)) return new List<ProjectBackup>();
            return Directory.GetFiles(directory, "*.neurocle")
                .Select(path => new ProjectBackup(path, File.GetLastWriteTimeUtc(path)))
                .OrderByDescending(backup => backup.CreatedUtc).ThenByDescending(backup => backup.Path, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>
        /// 組合指定專案的備份目錄路徑。
        /// </summary>
        /// <param name="projectPath">專案資料庫檔案路徑。</param>
        /// <returns>專案備份目錄的完整路徑。</returns>
        private static string GetBackupDirectory(string projectPath)
        {
            return Path.Combine(Path.GetDirectoryName(projectPath), Path.GetFileNameWithoutExtension(projectPath) + ".backups");
        }

        /// <summary>
        /// 刪除超出保留數量上限的舊備份檔。
        /// </summary>
        /// <param name="directory">要掃描的目錄路徑。</param>
        private static void TrimBackups(string directory)
        {
            foreach (string oldBackup in Directory.GetFiles(directory, "*.neurocle").OrderByDescending(File.GetLastWriteTimeUtc).ThenByDescending(file => file, StringComparer.OrdinalIgnoreCase).Skip(MaximumBackupCount)) File.Delete(oldBackup);
        }

        /// <summary>
        /// 開啟並檢查資料庫必要資料表、欄位及索引結構。
        /// </summary>
        /// <param name="path">要讀取、檢查或轉換的路徑。</param>
        /// <exception cref="NotSupportedException">操作遇到 NotSupportedException 所代表的錯誤狀態。</exception>
        private static void ValidateDatabase(string path)
        {
            using (SqliteConnection db = OpenConnection(path))
            {
                int version = Convert.ToInt32(ExecuteScalar(db, "SELECT Version FROM SchemaInfo LIMIT 1;"));
                if (version != 1 && version != 2 && version != 3 && version != 4 && version != 5 && version != 6 && version != CurrentSchemaVersion) throw new NotSupportedException("The backup schema version is not supported.");
                ValidateTableColumns(db, "SchemaInfo", new[] { "Version" }, null, null);
                ValidateTableColumns(db, "ProjectInfo", new[] { "ProjectId", "CreatedUtc" }, null, null);
                if (version >= 2) ValidateTableColumns(db, "ImageRecord", new[] { "Id", "OriginalFileName", "ManagedFileName", "Sha256", "Width", "Height", "Sequence" }, "Id", new[] { "ManagedFileName", "Sequence" });
                if (version == CurrentSchemaVersion)
                {
                    ValidateTableColumns(db, "AnnotationSet", new[] { "Id", "Name", "Type" }, "Id", null);
                    ValidateTableColumns(db, "AnnotationClass", new[] { "Id", "AnnotationSetId", "Name", "Color" }, "Id", null);
                    ValidateTableColumns(db, "ClaAnnotation", new[] { "AnnotationSetId", "ImageId", "ClassId" }, null, null);
                    ValidateTableColumns(db, "ObdAnnotation", new[] { "Id", "AnnotationSetId", "ImageId", "ClassId", "X", "Y", "Width", "Height", "ZOrder" }, "Id", null);
                    ValidateTableColumns(db, "SegRegion", new[] { "Id", "AnnotationSetId", "ImageId", "ClassId", "Type", "X", "Y", "Width", "Height", "ZOrder", "StrokeWidth" }, "Id", null);
                    ValidateTableColumns(db, "SegRegionPoint", new[] { "RegionId", "Ordinal", "X", "Y" }, null, null);
                    ValidateTableColumns(db, "DatasetAssignment", new[] { "AnnotationSetId", "ImageId", "Status", "Retest" }, null, null);
                }
            }
        }

        /// <summary>
        /// 確認資料表包含必要欄位、主鍵欄位與唯一欄位。
        /// </summary>
        /// <param name="db">供目前操作使用的 SQLite 連線。</param>
        /// <param name="table">要驗證或操作的資料表名稱。</param>
        /// <param name="requiredColumns">必須存在的資料表欄位名稱集合。</param>
        /// <param name="primaryKeyColumn">資料表的主鍵欄位名稱。</param>
        /// <param name="uniqueColumns">必須具有唯一性索引的欄位名稱集合。</param>
        /// <exception cref="InvalidDataException">操作遇到 InvalidDataException 所代表的錯誤狀態。</exception>
        private static void ValidateTableColumns(SqliteConnection db, string table, IReadOnlyList<string> requiredColumns, string primaryKeyColumn, IReadOnlyList<string> uniqueColumns)
        {
            var columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            using (var command = db.CreateCommand())
            {
                command.CommandText = "PRAGMA table_info('" + table + "');";
                using (SqliteDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read()) columns.Add(reader.GetString(1), reader.GetInt32(5));
                }
            }
            if (requiredColumns.Any(column => !columns.ContainsKey(column))) throw new InvalidDataException("The backup database schema is incomplete.");
            if (primaryKeyColumn != null && columns[primaryKeyColumn] == 0) throw new InvalidDataException("The backup database schema is incomplete.");
            if (uniqueColumns != null && uniqueColumns.Any(column => !HasUniqueIndex(db, table, column))) throw new InvalidDataException("The backup database schema is incomplete.");
        }

        /// <summary>
        /// 檢查資料表是否有涵蓋指定欄位的唯一索引。
        /// </summary>
        /// <param name="db">供目前操作使用的 SQLite 連線。</param>
        /// <param name="table">要驗證或操作的資料表名稱。</param>
        /// <param name="requiredColumn">必須存在的資料表欄位名稱。</param>
        /// <returns>存在涵蓋指定欄位的唯一索引時為 <see langword="true"/>，否則為 <see langword="false"/>。</returns>
        private static bool HasUniqueIndex(SqliteConnection db, string table, string requiredColumn)
        {
            using (var indexList = db.CreateCommand())
            {
                indexList.CommandText = "PRAGMA index_list('" + table + "');";
                using (SqliteDataReader indices = indexList.ExecuteReader())
                {
                    while (indices.Read())
                    {
                        if (indices.GetInt32(2) == 0) continue;
                        string indexName = indices.GetString(1);
                        using (var indexInfo = db.CreateCommand())
                        {
                            indexInfo.CommandText = "PRAGMA index_info('" + indexName + "');";
                            using (SqliteDataReader columns = indexInfo.ExecuteReader())
                            {
                                if (columns.Read() && string.Equals(columns.GetString(2), requiredColumn, StringComparison.OrdinalIgnoreCase) && !columns.Read()) return true;
                            }
                        }
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// 遞迴複製來源目錄中的檔案與子目錄。
        /// </summary>
        /// <param name="source">要讀取、匯入或複製的來源。</param>
        /// <param name="target">要複製、替換或比較的目標資料。</param>
        private static void CopyDirectory(string source, string target)
        {
            Directory.CreateDirectory(target);
            foreach (string file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
            foreach (string directory in Directory.GetDirectories(source)) CopyDirectory(directory, Path.Combine(target, Path.GetFileName(directory)));
        }

        /// <summary>
        /// 產生不與現有受管理圖片檔名衝突的名稱。
        /// </summary>
        /// <param name="originalName">來源檔案的原始名稱。</param>
        /// <returns>回傳不會與現有受管理檔名衝突的檔名。</returns>
        private string NextManagedName(string originalName)
        {
            string stem = Path.GetFileNameWithoutExtension(originalName);
            string extension = Path.GetExtension(originalName);
            string candidate = originalName;
            int suffix = 1;
            while (File.Exists(Path.Combine(ImagesDirectory, candidate))) candidate = stem + " (" + suffix++ + ")" + extension;
            return candidate;
        }

        /// <summary>
        /// 在指定交易中建立圖片資料表及相關索引。
        /// </summary>
        /// <param name="db">供目前操作使用的 SQLite 連線。</param>
        /// <param name="transaction">目前 SQLite 連線上的資料庫交易。</param>
        private static void CreateImageTable(SqliteConnection db, SqliteTransaction transaction)
        {
            Execute(db, transaction, "CREATE TABLE IF NOT EXISTS ImageRecord (Id TEXT PRIMARY KEY NOT NULL, OriginalFileName TEXT NOT NULL, ManagedFileName TEXT NOT NULL UNIQUE, Sha256 TEXT NOT NULL, Width INTEGER NOT NULL, Height INTEGER NOT NULL, Sequence INTEGER NOT NULL UNIQUE);");
        }

        /// <summary>
        /// 在指定交易中建立標註及資料集相關資料表。
        /// </summary>
        /// <param name="db">供目前操作使用的 SQLite 連線。</param>
        /// <param name="transaction">目前 SQLite 連線上的資料庫交易。</param>
        private static void CreateAnnotationTables(SqliteConnection db, SqliteTransaction transaction)
        {
            Execute(db, transaction, "CREATE TABLE IF NOT EXISTS AnnotationSet (Id TEXT PRIMARY KEY NOT NULL, Name TEXT NOT NULL, Type INTEGER NOT NULL);");
            Execute(db, transaction, "CREATE TABLE IF NOT EXISTS AnnotationClass (Id TEXT PRIMARY KEY NOT NULL, AnnotationSetId TEXT NOT NULL REFERENCES AnnotationSet(Id) ON DELETE CASCADE, Name TEXT NOT NULL, Color TEXT NOT NULL, UNIQUE (AnnotationSetId, Name));");
            Execute(db, transaction, "CREATE TABLE IF NOT EXISTS ClaAnnotation (AnnotationSetId TEXT NOT NULL REFERENCES AnnotationSet(Id) ON DELETE CASCADE, ImageId TEXT NOT NULL REFERENCES ImageRecord(Id) ON DELETE CASCADE, ClassId TEXT NOT NULL REFERENCES AnnotationClass(Id) ON DELETE CASCADE, PRIMARY KEY (AnnotationSetId, ImageId));");
            Execute(db, transaction, "CREATE TABLE IF NOT EXISTS ObdAnnotation (Id TEXT PRIMARY KEY NOT NULL, AnnotationSetId TEXT NOT NULL REFERENCES AnnotationSet(Id) ON DELETE CASCADE, ImageId TEXT NOT NULL REFERENCES ImageRecord(Id) ON DELETE CASCADE, ClassId TEXT NOT NULL REFERENCES AnnotationClass(Id) ON DELETE CASCADE, X INTEGER NOT NULL, Y INTEGER NOT NULL, Width INTEGER NOT NULL, Height INTEGER NOT NULL, ZOrder INTEGER NOT NULL);");
            Execute(db, transaction, "CREATE TABLE IF NOT EXISTS SegRegion (Id TEXT PRIMARY KEY NOT NULL, AnnotationSetId TEXT NOT NULL REFERENCES AnnotationSet(Id) ON DELETE CASCADE, ImageId TEXT NOT NULL REFERENCES ImageRecord(Id) ON DELETE CASCADE, ClassId TEXT NOT NULL REFERENCES AnnotationClass(Id) ON DELETE CASCADE, Type INTEGER NOT NULL, X INTEGER NOT NULL, Y INTEGER NOT NULL, Width INTEGER NOT NULL, Height INTEGER NOT NULL, ZOrder INTEGER NOT NULL, StrokeWidth INTEGER NULL);");
            Execute(db, transaction, "CREATE TABLE IF NOT EXISTS SegRegionPoint (RegionId TEXT NOT NULL REFERENCES SegRegion(Id) ON DELETE CASCADE, Ordinal INTEGER NOT NULL, X INTEGER NOT NULL, Y INTEGER NOT NULL, PRIMARY KEY (RegionId, Ordinal));");
            Execute(db, transaction, "CREATE TABLE IF NOT EXISTS DatasetAssignment (AnnotationSetId TEXT NOT NULL REFERENCES AnnotationSet(Id) ON DELETE CASCADE, ImageId TEXT NOT NULL REFERENCES ImageRecord(Id) ON DELETE CASCADE, Status INTEGER NOT NULL, Retest INTEGER NOT NULL, PRIMARY KEY (AnnotationSetId, ImageId));");
        }

        /// <summary>
        /// 修剪名稱前後空白並拒絕空白名稱。
        /// </summary>
        /// <param name="name">要建立、查詢或更新的名稱。</param>
        /// <returns>移除前後空白後的名稱。</returns>
        /// <exception cref="ArgumentNullException">必要參數為 null。</exception>
        /// <exception cref="ArgumentException">輸入資料不符合操作契約。</exception>
        private static string NormalizeName(string name)
        {
            string normalized = (name ?? throw new ArgumentNullException(nameof(name))).Trim();
            if (normalized.Length == 0) throw new ArgumentException("The name cannot be empty.", nameof(name));
            return normalized;
        }

        // 取得刪除圖片時暫存檔案的目錄。
        private string DeletingImagesDirectory
        {
            get { return ImagesDirectory + ".deleting"; }
        }

        /// <summary>
        /// 依資料庫記錄復原或清除刪除作業留下的暫存圖片。
        /// </summary>
        private void RecoverStagedImages()
        {
            if (!Directory.Exists(DeletingImagesDirectory)) return;
            foreach (string path in Directory.GetFiles(DeletingImagesDirectory, "*", SearchOption.AllDirectories))
            {
                string managedFileName = Path.GetFileName(path);
                string destination = Path.Combine(ImagesDirectory, managedFileName);
                try
                {
                    if (ImageRecordExists(managedFileName))
                    {
                        if (!File.Exists(destination)) File.Move(path, destination);
                        else File.Delete(path);
                    }
                    else File.Delete(path);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            foreach (string directory in Directory.GetDirectories(DeletingImagesDirectory, "*", SearchOption.AllDirectories).OrderByDescending(directory => directory.Length))
            {
                if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
            }
            if (Directory.Exists(DeletingImagesDirectory) && !Directory.EnumerateFileSystemEntries(DeletingImagesDirectory).Any()) Directory.Delete(DeletingImagesDirectory);
        }

        /// <summary>
        /// 確認受管理圖片檔名是否仍有資料庫記錄。
        /// </summary>
        /// <param name="managedFileName">專案內受管理的圖片檔名。</param>
        /// <returns>資料庫中存在該受管理檔名的圖片記錄時為 <see langword="true"/>，否則為 <see langword="false"/>。</returns>
        private bool ImageRecordExists(string managedFileName)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT COUNT(*) FROM ImageRecord WHERE ManagedFileName = $name;";
                command.Parameters.AddWithValue("$name", managedFileName);
                return Convert.ToInt64(command.ExecuteScalar()) != 0;
            }
        }

        /// <summary>
        /// 在檔案刪除流程失敗時將暫存圖片移回原位置。
        /// </summary>
        /// <param name="stagedPaths">暫存檔案與其最終路徑的對應集合。</param>
        private static void RestoreStagedImages(IDictionary<string, string> stagedPaths)
        {
            foreach (KeyValuePair<string, string> stagedPath in stagedPaths)
            {
                if (File.Exists(stagedPath.Value) && !File.Exists(stagedPath.Key)) File.Move(stagedPath.Value, stagedPath.Key);
            }
        }

        /// <summary>
        /// 清除建立或另存失敗而留下的新專案檔及圖片目錄。
        /// </summary>
        /// <param name="projectPath">專案資料庫檔案路徑。</param>
        private static void DeleteNewProject(string projectPath)
        {
            string imagesDirectory = Path.Combine(Path.GetDirectoryName(projectPath), Path.GetFileNameWithoutExtension(projectPath) + ".images");
            if (Directory.Exists(imagesDirectory)) Directory.Delete(imagesDirectory, true);
            if (File.Exists(projectPath)) File.Delete(projectPath);
            if (File.Exists(projectPath + ".lock")) File.Delete(projectPath + ".lock");
        }

        // 保存一個可重播的操作及其記憶體用量估算。
        private sealed class HistoryEntry
        {
            /// <summary>
            /// 建立一筆 Undo、Redo 委派及其容量估算資料。
            /// </summary>
            /// <param name="undo">執行 Undo 時還原資料狀態的委派。</param>
            /// <param name="redo">執行 Redo 時重做資料狀態的委派。</param>
            /// <param name="sizeBytes">歷程項目估計占用的位元組數。</param>
            /// <exception cref="ArgumentNullException">必要參數為 null。</exception>
            public HistoryEntry(Action undo, Action redo, long sizeBytes)
            {
                Undo = undo ?? throw new ArgumentNullException(nameof(undo));
                Redo = redo ?? throw new ArgumentNullException(nameof(redo));
                SizeBytes = sizeBytes;
            }

            // 執行操作反向還原的委派。
            public Action Undo { get; }
            // 重新套用操作的委派。
            public Action Redo { get; }
            // 此歷程項目的容量估算值。
            public long SizeBytes { get; }
        }

        // 管理單一標註集的 Undo/Redo 堆疊與容量上限。
        private sealed class AnnotationHistory
        {
            // 限制單一標註集歷程保留的操作數量。
            private const int MaximumActionCount = 100;
            // 限制單一標註集歷程保留的最大記憶體估算值。
            private const long MaximumSizeBytes = 512L * 1024L * 1024L;
            // 依新舊順序保存可復原的操作。
            private readonly List<HistoryEntry> undo = new List<HistoryEntry>();
            // 依新舊順序保存可重做的操作。
            private readonly List<HistoryEntry> redo = new List<HistoryEntry>();
            // 記錄 Undo 堆疊的估算容量。
            private long undoSizeBytes;
            // 記錄 Redo 堆疊的估算容量。
            private long redoSizeBytes;

            // 表示 Undo 堆疊是否有可復原項目。
            public bool CanUndo { get { return undo.Count != 0; } }
            // 表示 Redo 堆疊是否有可重做項目。
            public bool CanRedo { get { return redo.Count != 0; } }

            /// <summary>
            /// 將新操作放入 Undo 歷程並清空 Redo 歷程。
            /// </summary>
            /// <param name="entry">要處理的單一項目。</param>
            public void Push(HistoryEntry entry)
            {
                redo.Clear();
                redoSizeBytes = 0;
                undo.Add(entry);
                undoSizeBytes += entry.SizeBytes;
                Trim(undo, ref undoSizeBytes);
            }

            /// <summary>
            /// 取出最新的 Undo 歷程項目。
            /// </summary>
            /// <returns>最新的 Undo 歷程項目；堆疊為空時回傳 <see langword="null"/>。</returns>
            public HistoryEntry TakeUndo()
            {
                return Take(undo, ref undoSizeBytes);
            }

            /// <summary>
            /// 取出最新的 Redo 歷程項目。
            /// </summary>
            /// <returns>最新的 Redo 歷程項目；堆疊為空時回傳 <see langword="null"/>。</returns>
            public HistoryEntry TakeRedo()
            {
                return Take(redo, ref redoSizeBytes);
            }

            /// <summary>
            /// Undo 執行失敗時將歷程項目放回原堆疊。
            /// </summary>
            /// <param name="entry">要處理的單一項目。</param>
            public void ReturnUndo(HistoryEntry entry)
            {
                undo.Add(entry);
                undoSizeBytes += entry.SizeBytes;
            }

            /// <summary>
            /// Redo 執行失敗時將歷程項目放回原堆疊。
            /// </summary>
            /// <param name="entry">要處理的單一項目。</param>
            public void ReturnRedo(HistoryEntry entry)
            {
                redo.Add(entry);
                redoSizeBytes += entry.SizeBytes;
            }

            /// <summary>
            /// Undo 成功後將歷程項目移入 Redo 堆疊。
            /// </summary>
            /// <param name="entry">要處理的單一項目。</param>
            public void MoveUndoToRedo(HistoryEntry entry)
            {
                redo.Add(entry);
                redoSizeBytes += entry.SizeBytes;
                Trim(redo, ref redoSizeBytes);
            }

            /// <summary>
            /// Redo 成功後將歷程項目移入 Undo 堆疊。
            /// </summary>
            /// <param name="entry">要處理的單一項目。</param>
            public void MoveRedoToUndo(HistoryEntry entry)
            {
                undo.Add(entry);
                undoSizeBytes += entry.SizeBytes;
                Trim(undo, ref undoSizeBytes);
            }

            /// <summary>
            /// 移除指定歷程堆疊最新項目並更新容量計數。
            /// </summary>
            /// <param name="entries">要檢查或處理的項目集合。</param>
            /// <param name="sizeBytes">歷程項目估計占用的位元組數。</param>
            /// <returns>移除的最新歷程項目；集合為空時回傳 <see langword="null"/>。</returns>
            private static HistoryEntry Take(List<HistoryEntry> entries, ref long sizeBytes)
            {
                if (entries.Count == 0) return null;
                int index = entries.Count - 1;
                HistoryEntry entry = entries[index];
                entries.RemoveAt(index);
                sizeBytes -= entry.SizeBytes;
                return entry;
            }

            /// <summary>
            /// 移除超過項目數或記憶體容量上限的最舊歷程項目。
            /// </summary>
            /// <param name="entries">要檢查或處理的項目集合。</param>
            /// <param name="sizeBytes">歷程項目估計占用的位元組數。</param>
            private static void Trim(List<HistoryEntry> entries, ref long sizeBytes)
            {
                while (entries.Count > MaximumActionCount || sizeBytes > MaximumSizeBytes)
                {
                    sizeBytes -= entries[0].SizeBytes;
                    entries.RemoveAt(0);
                }
            }
        }

        // 以數字片段的數值順序比較檔名，例如 image2 排在 image10 前。
        private sealed class NaturalFileNameComparer : IComparer<string>
        {
            // 共用自然檔名比較器執行個體。
            public static readonly NaturalFileNameComparer Instance = new NaturalFileNameComparer();

            /// <summary>
            /// 以不區分大小寫的自然數字順序比較兩個檔名。
            /// </summary>
            /// <param name="left">排序比較中的第一個檔名。</param>
            /// <param name="right">排序比較中的第二個檔名。</param>
            /// <returns>依排序規則回傳負數、零或正數。</returns>
            public int Compare(string left, string right)
            {
                if (ReferenceEquals(left, right)) return 0;
                if (left == null) return -1;
                if (right == null) return 1;

                int leftIndex = 0;
                int rightIndex = 0;
                while (leftIndex < left.Length && rightIndex < right.Length)
                {
                    bool leftDigit = char.IsDigit(left[leftIndex]);
                    bool rightDigit = char.IsDigit(right[rightIndex]);
                    if (leftDigit && rightDigit)
                    {
                        int leftEnd = FindDigitsEnd(left, leftIndex);
                        int rightEnd = FindDigitsEnd(right, rightIndex);
                        int result = CompareDigits(left, leftIndex, leftEnd, right, rightIndex, rightEnd);
                        if (result != 0) return result;
                        leftIndex = leftEnd;
                        rightIndex = rightEnd;
                        continue;
                    }

                    int characterResult = char.ToUpperInvariant(left[leftIndex]).CompareTo(char.ToUpperInvariant(right[rightIndex]));
                    if (characterResult != 0) return characterResult;
                    leftIndex++;
                    rightIndex++;
                }
                return left.Length.CompareTo(right.Length);
            }

            /// <summary>
            /// 取得檔名中連續數字片段的結束索引。
            /// </summary>
            /// <param name="value">要掃描數字片段的檔名。</param>
            /// <param name="start">數字片段的起始字元索引。</param>
            /// <returns>連續數字片段之後的字元索引。</returns>
            private static int FindDigitsEnd(string value, int start)
            {
                int index = start;
                while (index < value.Length && char.IsDigit(value[index])) index++;
                return index;
            }

            /// <summary>
            /// 忽略前導零後比較兩個數字片段的數值大小。
            /// </summary>
            /// <param name="left">包含左側數字片段的檔名。</param>
            /// <param name="leftStart">左側數字片段的起始字元索引。</param>
            /// <param name="leftEnd">左側數字片段結束後的字元索引。</param>
            /// <param name="right">包含右側數字片段的檔名。</param>
            /// <param name="rightStart">右側數字片段的起始字元索引。</param>
            /// <param name="rightEnd">右側數字片段結束後的字元索引。</param>
            /// <returns>左側數字片段小於、等於或大於右側時，分別回傳負數、零或正數。</returns>
            private static int CompareDigits(string left, int leftStart, int leftEnd, string right, int rightStart, int rightEnd)
            {
                while (leftStart < leftEnd && left[leftStart] == '0') leftStart++;
                while (rightStart < rightEnd && right[rightStart] == '0') rightStart++;
                int leftLength = leftEnd - leftStart;
                int rightLength = rightEnd - rightStart;
                if (leftLength != rightLength) return leftLength.CompareTo(rightLength);
                for (int index = 0; index < leftLength; index++)
                {
                    int result = left[leftStart + index].CompareTo(right[rightStart + index]);
                    if (result != 0) return result;
                }
                return 0;
            }
        }
    }
}

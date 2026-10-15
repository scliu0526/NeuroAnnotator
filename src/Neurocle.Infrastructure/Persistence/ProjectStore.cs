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
    public sealed class ProjectStore : IProjectSession
    {
        private const int CurrentSchemaVersion = 7;
        private const int MaximumBackupCount = 5;
        private static readonly Random random = new Random();
        private readonly FileStream lockStream;
        private readonly SqliteConnection connection;
        private readonly Dictionary<string, AnnotationHistory> histories = new Dictionary<string, AnnotationHistory>();
        private bool replayingHistory;
        private SqliteTransaction historyTransaction;

        internal static Func<DateTime> UtcNow = () => DateTime.UtcNow;
        internal static Action<string, string> ReplaceFile = ReplaceFileSafely;
        internal static Action<string, string> NativeReplaceFile = (source, destination) => File.Replace(source, destination, null);
        internal static Action<string, string> MoveFile = File.Move;
        internal static Func<int, int> NextRandom = maximum => random.Next(maximum);

        private ProjectStore(string path, FileStream lockStream, SqliteConnection connection)
        {
            ProjectPath = path;
            ImagesDirectory = Path.Combine(Path.GetDirectoryName(path), Path.GetFileNameWithoutExtension(path) + ".images");
            this.lockStream = lockStream;
            this.connection = connection;
        }

        public string ProjectPath { get; }
        public string ImagesDirectory { get; }

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

        public void CompleteOperation(Action<SqliteConnection, SqliteTransaction> operation)
        {
            if (operation == null) throw new ArgumentNullException(nameof(operation));
            using (SqliteTransaction transaction = connection.BeginTransaction())
            {
                operation(connection, transaction);
                transaction.Commit();
            }
        }

        public void Save()
        {
            Execute(connection, null, "PRAGMA wal_checkpoint(FULL);");
        }

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

        public ImageImportReport ImportFilesWithReport(IEnumerable<string> filePaths, Func<bool> cancellationRequested)
        {
            return ImportFilesWithReport(filePaths, cancellationRequested, null);
        }

        public ImageImportReport ImportFilesWithReport(IEnumerable<string> filePaths, Func<bool> cancellationRequested, Action<ImageImportProgress> progress)
        {
            if (filePaths == null) throw new ArgumentNullException(nameof(filePaths));
            return ImportSourcesWithReport(OrderSources(filePaths), cancellationRequested, null, progress);
        }

        public ImageImportReport ImportDirectory(string directoryPath, bool includeSubdirectories, Func<bool> cancellationRequested)
        {
            return ImportDirectory(directoryPath, includeSubdirectories, cancellationRequested, null);
        }

        public ImageImportReport ImportDirectory(string directoryPath, bool includeSubdirectories, Func<bool> cancellationRequested, Action<ImageImportProgress> progress)
        {
            if (directoryPath == null) throw new ArgumentNullException(nameof(directoryPath));
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

        public string ExportAnnotationJson(string annotationSetId, string outputPath)
        {
            AnnotationSet set = GetAnnotationSets().Single(value => value.Id == annotationSetId);
            return JsonAnnotationExport.Write(this, set, outputPath);
        }

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

        public void RenameAnnotationSet(string annotationSetId, string name)
        {
            Execute(connection, null, "UPDATE AnnotationSet SET Name = $name WHERE Id = $id;", new SqliteParameter("$name", NormalizeName(name)), new SqliteParameter("$id", annotationSetId));
        }

        public bool DeleteAnnotationSet(string annotationSetId, string confirmationName)
        {
            AnnotationSet set = GetAnnotationSets().SingleOrDefault(item => item.Id == annotationSetId);
            if (set == null || !string.Equals(set.Name, confirmationName, StringComparison.Ordinal)) return false;
            CompleteOperation((db, transaction) => Execute(db, transaction, "DELETE FROM AnnotationSet WHERE Id = $id;", new SqliteParameter("$id", annotationSetId)));
            histories.Remove(annotationSetId);
            return true;
        }

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

        public AnnotationClass AddClass(string annotationSetId, string name, string color)
        {
            string nextColor = color == null ? NextClassColor(GetClasses(annotationSetId).Select(item => item.Color)) : AnnotationColorFormat.Normalize(color);
            var value = new AnnotationClass(Guid.NewGuid().ToString("D"), annotationSetId, NormalizeName(name), nextColor);
            Execute(connection, null, "INSERT INTO AnnotationClass (Id, AnnotationSetId, Name, Color) VALUES ($id, $set, $name, $color);", new SqliteParameter("$id", value.Id), new SqliteParameter("$set", value.AnnotationSetId), new SqliteParameter("$name", value.Name), new SqliteParameter("$color", value.Color));
            Record(annotationSetId, () => DeleteClassRaw(value.Id), () => InsertClassRaw(value), EstimateHistoryBytes(value.Id, value.AnnotationSetId, value.Name, value.Color));
            return value;
        }

        public void RenameClass(string classId, string name)
        {
            AnnotationClass previous = GetClass(classId); string next = NormalizeName(name);
            Execute(connection, null, "UPDATE AnnotationClass SET Name = $name WHERE Id = $id;", new SqliteParameter("$name", next), new SqliteParameter("$id", classId));
            Record(previous.AnnotationSetId, () => RenameClassRaw(classId, previous.Name), () => RenameClassRaw(classId, next), EstimateHistoryBytes(classId, previous.Name, next));
        }
        public void ChangeClassColor(string classId, string color)
        {
            AnnotationClass previous = GetClass(classId); string next = AnnotationColorFormat.Normalize(color);
            Execute(connection, null, "UPDATE AnnotationClass SET Color = $color WHERE Id = $id;", new SqliteParameter("$color", next), new SqliteParameter("$id", classId));
            Record(previous.AnnotationSetId, () => ChangeClassColorRaw(classId, previous.Color), () => ChangeClassColorRaw(classId, next), EstimateHistoryBytes(classId, previous.Color, next));
        }
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

        public void SetClaClass(string annotationSetId, string imageId, string classId)
        {
            object setType = ExecuteScalar(connection, "SELECT Type FROM AnnotationSet WHERE Id = $set;", new SqliteParameter("$set", annotationSetId));
            if (setType == null || Convert.ToInt32(setType) != (int)AnnotationSetType.Cla) throw new ArgumentException("The annotation set is not CLA.", nameof(annotationSetId));
            if (classId != null && Convert.ToInt32(ExecuteScalar(connection, "SELECT COUNT(*) FROM AnnotationClass WHERE Id = $class AND AnnotationSetId = $set;", new SqliteParameter("$class", classId), new SqliteParameter("$set", annotationSetId))) != 1) throw new ArgumentException("The class does not belong to the annotation set.", nameof(classId));
            string previous = GetClaClassId(annotationSetId, imageId);
            SetClaRaw(annotationSetId, imageId, classId);
            Record(annotationSetId, () => SetClaRaw(annotationSetId, imageId, previous), () => SetClaRaw(annotationSetId, imageId, classId), EstimateHistoryBytes(annotationSetId, imageId, previous, classId));
        }

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

        public void SetDatasetAssignments(string annotationSetId, IEnumerable<string> imageIds, DatasetAssignmentStatus status)
        {
            if (!Enum.IsDefined(typeof(DatasetAssignmentStatus), status)) throw new ArgumentOutOfRangeException(nameof(status));
            UpdateDatasetAssignments(annotationSetId, imageIds, assignment => new DatasetAssignment(annotationSetId, assignment.ImageId, status, assignment.Retest));
        }

        public void SetRetest(string annotationSetId, IEnumerable<string> imageIds, bool retest)
        {
            UpdateDatasetAssignments(annotationSetId, imageIds, assignment => new DatasetAssignment(annotationSetId, assignment.ImageId, assignment.Status, retest));
        }

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

        public DatasetSplitPlan AutoSplitDataset(string annotationSetId, int trainPercent)
        {
            IReadOnlyList<ImageAnnotationSummary> summaries = GetImageAnnotationSummaries(annotationSetId);
            var classes = summaries.ToDictionary(value => value.ImageId, value => value.ClassIds, StringComparer.Ordinal);
            ProjectImage[] images = GetImages().ToArray();
            DatasetSplitPlan plan = DatasetSplitPlanner.Create(images.Select(image => new DatasetSplitCandidate(image.Id, classes.ContainsKey(image.Id) ? classes[image.Id] : new string[0], !image.IsMissing)), trainPercent, NextRandom);
            var train = new HashSet<string>(plan.TrainImageIds, StringComparer.Ordinal);
            var test = new HashSet<string>(plan.TestImageIds, StringComparer.Ordinal);
            var availableImageIds = new HashSet<string>(images.Where(image => !image.IsMissing).Select(image => image.Id), StringComparer.Ordinal);
            DatasetAssignment[] previous = GetDatasetAssignments(annotationSetId).Where(value => availableImageIds.Contains(value.ImageId)).ToArray();
            DatasetAssignment[] next = previous.Select(value => new DatasetAssignment(annotationSetId, value.ImageId, train.Contains(value.ImageId) ? DatasetAssignmentStatus.Train : test.Contains(value.ImageId) ? DatasetAssignmentStatus.Test : DatasetAssignmentStatus.Unassigned, value.Retest)).ToArray();
            if (previous.Where((value, index) => value.Status != next[index].Status).Any())
            {
                CompleteOperation((db, transaction) => { foreach (DatasetAssignment value in next) SetDatasetAssignmentRaw(db, transaction, value); });
                Record(annotationSetId, () => { foreach (DatasetAssignment value in previous) SetDatasetAssignmentRaw(connection, historyTransaction, value); }, () => { foreach (DatasetAssignment value in next) SetDatasetAssignmentRaw(connection, historyTransaction, value); }, EstimateHistoryBytes(next.Select(value => value.ImageId).ToArray()));
            }
            return plan;
        }

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

        private string[] ValidateDatasetImageIds(string annotationSetId, IEnumerable<string> imageIds)
        {
            if (imageIds == null) throw new ArgumentNullException(nameof(imageIds));
            EnsureAnnotationSetExists(annotationSetId);
            string[] ids = imageIds.ToArray();
            if (ids.Distinct(StringComparer.Ordinal).Count() != ids.Length) throw new ArgumentException("An image can only be assigned once.", nameof(imageIds));
            foreach (string imageId in ids) GetImageSize(imageId);
            return ids;
        }

        private void EnsureAnnotationSetExists(string annotationSetId)
        {
            if (ExecuteScalar(connection, "SELECT Id FROM AnnotationSet WHERE Id = $id;", new SqliteParameter("$id", annotationSetId)) == null) throw new ArgumentException("The annotation set does not exist.", nameof(annotationSetId));
        }

        private static void SetDatasetAssignmentRaw(SqliteConnection db, SqliteTransaction transaction, DatasetAssignment value)
        {
            Execute(db, transaction, "INSERT INTO DatasetAssignment (AnnotationSetId, ImageId, Status, Retest) VALUES ($set, $image, $status, $retest) ON CONFLICT(AnnotationSetId, ImageId) DO UPDATE SET Status = excluded.Status, Retest = excluded.Retest;", new SqliteParameter("$set", value.AnnotationSetId), new SqliteParameter("$image", value.ImageId), new SqliteParameter("$status", (int)value.Status), new SqliteParameter("$retest", value.Retest ? 1 : 0));
        }

        public bool Undo(string annotationSetId) { return RunHistory(annotationSetId, false); }
        public bool Redo(string annotationSetId) { return RunHistory(annotationSetId, true); }
        public bool CanUndo(string annotationSetId) { return GetHistory(annotationSetId).CanUndo; }
        public bool CanRedo(string annotationSetId) { return GetHistory(annotationSetId).CanRedo; }

        private void Record(string annotationSetId, Action undo, Action redo, long sizeBytes)
        {
            if (replayingHistory) return;
            GetHistory(annotationSetId).Push(new HistoryEntry(undo, redo, sizeBytes));
        }

        private static long EstimateHistoryBytes(params string[] values)
        {
            long size = 256;
            foreach (string value in values) if (value != null) size += 64 + (long)value.Length * sizeof(char);
            return size;
        }

        private static long EstimateHistoryBytes(IReadOnlyList<string> values)
        {
            long size = 64 + (long)IntPtr.Size * values.Count;
            foreach (string value in values) size += 64 + (long)value.Length * sizeof(char);
            return size;
        }

        private static long EstimateHistoryBytes(IReadOnlyList<ObdAnnotation> values)
        {
            long size = 64 + (long)IntPtr.Size * values.Count;
            foreach (ObdAnnotation value in values) size += EstimateHistoryBytes(value.Id, value.AnnotationSetId, value.ImageId, value.ClassId) + sizeof(int) * 5;
            return size;
        }

        private static long EstimateHistoryBytes(IReadOnlyList<SegRegion> values)
        {
            long size = 64 + (long)IntPtr.Size * values.Count;
            foreach (SegRegion value in values) size += EstimateHistoryBytes(value.Id, value.AnnotationSetId, value.ImageId, value.ClassId) + sizeof(int) * (6 + value.Points.Count * 2);
            return size;
        }

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

        private bool RunHistory(string annotationSetId, bool redo)
        {
            AnnotationHistory history = GetHistory(annotationSetId);
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

        private void InsertClassRaw(AnnotationClass value)
        {
            Execute(connection, historyTransaction, "INSERT INTO AnnotationClass (Id, AnnotationSetId, Name, Color) VALUES ($id, $set, $name, $color);", new SqliteParameter("$id", value.Id), new SqliteParameter("$set", value.AnnotationSetId), new SqliteParameter("$name", value.Name), new SqliteParameter("$color", value.Color));
        }

        private void DeleteClassRaw(string classId)
        {
            Execute(connection, historyTransaction, "DELETE FROM AnnotationClass WHERE Id = $id;", new SqliteParameter("$id", classId));
        }

        private void RenameClassRaw(string classId, string name)
        {
            Execute(connection, historyTransaction, "UPDATE AnnotationClass SET Name = $name WHERE Id = $id;", new SqliteParameter("$name", name), new SqliteParameter("$id", classId));
        }

        private void ChangeClassColorRaw(string classId, string color)
        {
            Execute(connection, historyTransaction, "UPDATE AnnotationClass SET Color = $color WHERE Id = $id;", new SqliteParameter("$color", color), new SqliteParameter("$id", classId));
        }

        private void SetClaRaw(string annotationSetId, string imageId, string classId)
        {
            Execute(connection, historyTransaction, "DELETE FROM ClaAnnotation WHERE AnnotationSetId = $set AND ImageId = $image;", new SqliteParameter("$set", annotationSetId), new SqliteParameter("$image", imageId));
            if (classId != null) Execute(connection, historyTransaction, "INSERT INTO ClaAnnotation (AnnotationSetId, ImageId, ClassId) VALUES ($set, $image, $class);", new SqliteParameter("$set", annotationSetId), new SqliteParameter("$image", imageId), new SqliteParameter("$class", classId));
        }

        private static ObdAnnotation ReadObdAnnotation(SqliteDataReader reader)
        {
            return new ObdAnnotation(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetInt32(4), reader.GetInt32(5), reader.GetInt32(6), reader.GetInt32(7), reader.GetInt32(8));
        }

        private SegRegion ReadSegRegion(SqliteDataReader reader)
        {
            string id = reader.GetString(0);
            return new SegRegion(id, reader.GetString(1), reader.GetString(2), reader.GetString(3), (SegRegionType)reader.GetInt32(4), reader.GetInt32(5), reader.GetInt32(6), reader.GetInt32(7), reader.GetInt32(8), reader.GetInt32(9), GetSegPoints(id), reader.IsDBNull(10) ? 0 : reader.GetInt32(10));
        }

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

        private void ValidateObd(string annotationSetId, string imageId, string classId, int x, int y, int width, int height)
        {
            object type = ExecuteScalar(connection, "SELECT Type FROM AnnotationSet WHERE Id = $set;", new SqliteParameter("$set", annotationSetId));
            if (type == null || Convert.ToInt32(type) != (int)AnnotationSetType.Obd) throw new ArgumentException("The annotation set is not OBD.", nameof(annotationSetId));
            if (Convert.ToInt32(ExecuteScalar(connection, "SELECT COUNT(*) FROM AnnotationClass WHERE Id = $class AND AnnotationSetId = $set;", new SqliteParameter("$class", classId), new SqliteParameter("$set", annotationSetId))) != 1) throw new ArgumentException("The class does not belong to the annotation set.", nameof(classId));
            ValidateObdGeometry(GetImageSize(imageId), x, y, width, height);
        }

        private static void ValidateObdGeometry(Size imageSize, int x, int y, int width, int height)
        {
            int imageWidth = imageSize.Width; int imageHeight = imageSize.Height;
            if (width < 1 || height < 1 || x < 0 || y < 0 || (long)x + width > imageWidth || (long)y + height > imageHeight) throw new ArgumentOutOfRangeException(nameof(width));
        }

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

        private void InsertObdRaw(ObdAnnotation value)
        {
            Execute(connection, historyTransaction, "INSERT INTO ObdAnnotation (Id, AnnotationSetId, ImageId, ClassId, X, Y, Width, Height, ZOrder) VALUES ($id, $set, $image, $class, $x, $y, $width, $height, $order);", new SqliteParameter("$id", value.Id), new SqliteParameter("$set", value.AnnotationSetId), new SqliteParameter("$image", value.ImageId), new SqliteParameter("$class", value.ClassId), new SqliteParameter("$x", value.X), new SqliteParameter("$y", value.Y), new SqliteParameter("$width", value.Width), new SqliteParameter("$height", value.Height), new SqliteParameter("$order", value.ZOrder));
        }

        private void UpdateObdRaw(ObdAnnotation value)
        {
            UpdateObdRaw(value, historyTransaction);
        }

        private static void UpdateObdRaw(SqliteConnection db, SqliteTransaction transaction, ObdAnnotation value)
        {
            Execute(db, transaction, "UPDATE ObdAnnotation SET ClassId = $class, X = $x, Y = $y, Width = $width, Height = $height, ZOrder = $order WHERE Id = $id;", new SqliteParameter("$class", value.ClassId), new SqliteParameter("$x", value.X), new SqliteParameter("$y", value.Y), new SqliteParameter("$width", value.Width), new SqliteParameter("$height", value.Height), new SqliteParameter("$order", value.ZOrder), new SqliteParameter("$id", value.Id));
        }

        private void UpdateObdRaw(ObdAnnotation value, SqliteTransaction transaction)
        {
            UpdateObdRaw(connection, transaction, value);
        }

        private void DeleteObdRaw(string annotationId)
        {
            Execute(connection, historyTransaction, "DELETE FROM ObdAnnotation WHERE Id = $id;", new SqliteParameter("$id", annotationId));
        }

        private void InsertSegRegionRaw(SegRegion value)
        {
            if (historyTransaction != null) InsertSegRegionRaw(connection, historyTransaction, value);
            else CompleteOperation((db, transaction) => InsertSegRegionRaw(db, transaction, value));
        }

        private static void InsertSegRegionRaw(SqliteConnection db, SqliteTransaction transaction, SegRegion value)
        {
            Execute(db, transaction, "INSERT INTO SegRegion (Id, AnnotationSetId, ImageId, ClassId, Type, X, Y, Width, Height, ZOrder, StrokeWidth) VALUES ($id, $set, $image, $class, $type, $x, $y, $width, $height, $order, $stroke);", new SqliteParameter("$id", value.Id), new SqliteParameter("$set", value.AnnotationSetId), new SqliteParameter("$image", value.ImageId), new SqliteParameter("$class", value.ClassId), new SqliteParameter("$type", (int)value.Type), new SqliteParameter("$x", value.X), new SqliteParameter("$y", value.Y), new SqliteParameter("$width", value.Width), new SqliteParameter("$height", value.Height), new SqliteParameter("$order", value.ZOrder), new SqliteParameter("$stroke", value.StrokeWidth == 0 ? (object)DBNull.Value : value.StrokeWidth));
            InsertSegPointsRaw(db, transaction, value.Id, value.Points);
        }

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

        private void UpdateSegRegionRaw(SegRegion value)
        {
            UpdateSegRegionRaw(connection, historyTransaction, value);
        }

        private static void UpdateSegRegionRaw(SqliteConnection db, SqliteTransaction transaction, SegRegion value)
        {
            Execute(db, transaction, "DELETE FROM SegRegionPoint WHERE RegionId = $id;", new SqliteParameter("$id", value.Id));
            Execute(db, transaction, "UPDATE SegRegion SET ClassId = $class, Type = $type, X = $x, Y = $y, Width = $width, Height = $height, ZOrder = $order, StrokeWidth = $stroke WHERE Id = $id;", new SqliteParameter("$class", value.ClassId), new SqliteParameter("$type", (int)value.Type), new SqliteParameter("$x", value.X), new SqliteParameter("$y", value.Y), new SqliteParameter("$width", value.Width), new SqliteParameter("$height", value.Height), new SqliteParameter("$order", value.ZOrder), new SqliteParameter("$stroke", value.StrokeWidth == 0 ? (object)DBNull.Value : value.StrokeWidth), new SqliteParameter("$id", value.Id));
            InsertSegPointsRaw(db, transaction, value.Id, value.Points);
        }

        public string GetClaClassId(string annotationSetId, string imageId)
        {
            object value = ExecuteScalar(connection, "SELECT ClassId FROM ClaAnnotation WHERE AnnotationSetId = $set AND ImageId = $image;", new SqliteParameter("$set", annotationSetId), new SqliteParameter("$image", imageId));
            return value == null || value == DBNull.Value ? null : Convert.ToString(value);
        }

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

        public ObdAnnotation AddObdAnnotation(string annotationSetId, string imageId, string classId, int x, int y, int width, int height)
        {
            ValidateObd(annotationSetId, imageId, classId, x, y, width, height);
            int zOrder = Convert.ToInt32(ExecuteScalar(connection, "SELECT COALESCE(MAX(ZOrder), -1) + 1 FROM ObdAnnotation WHERE AnnotationSetId = $set AND ImageId = $image;", new SqliteParameter("$set", annotationSetId), new SqliteParameter("$image", imageId)));
            var value = new ObdAnnotation(Guid.NewGuid().ToString("D"), annotationSetId, imageId, classId, x, y, width, height, zOrder);
            InsertObdRaw(value);
            Record(annotationSetId, () => DeleteObdRaw(value.Id), () => InsertObdRaw(value), EstimateHistoryBytes(value.Id, value.AnnotationSetId, value.ImageId, value.ClassId));
            return value;
        }

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

        public SegRegion AddSegPath(string annotationSetId, string imageId, string classId, SegRegionType type, IReadOnlyList<SegPoint> points, int strokeWidth)
        {
            if (points == null) throw new ArgumentNullException(nameof(points));
            if (type != SegRegionType.PolyLine && type != SegRegionType.PolyGon) throw new ArgumentOutOfRangeException(nameof(type));
            object setType = ExecuteScalar(connection, "SELECT Type FROM AnnotationSet WHERE Id = $set;", new SqliteParameter("$set", annotationSetId));
            if (setType == null || Convert.ToInt32(setType) != (int)AnnotationSetType.Seg) throw new ArgumentException("The annotation set is not SEG.", nameof(annotationSetId));
            if (Convert.ToInt32(ExecuteScalar(connection, "SELECT COUNT(*) FROM AnnotationClass WHERE Id = $class AND AnnotationSetId = $set;", new SqliteParameter("$class", classId), new SqliteParameter("$set", annotationSetId))) != 1) throw new ArgumentException("The class does not belong to the annotation set.", nameof(classId));
            SegPoint[] values = points.ToArray();
            if (values.Any(point => point == null)) throw new ArgumentException("The path contains a null point.", nameof(points));
            if (values.Length < (type == SegRegionType.PolyGon ? 3 : 2) || values.Distinct(new SegPointComparer()).Count() < (type == SegRegionType.PolyGon ? 3 : 2)) throw new ArgumentException("The path has too few distinct points.", nameof(points));
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

        public void EraseSegPath(string annotationSetId, string imageId, string classId, IReadOnlyList<SegPoint> points, int strokeWidth)
        {
            if (points == null) throw new ArgumentNullException(nameof(points));
            object setType = ExecuteScalar(connection, "SELECT Type FROM AnnotationSet WHERE Id = $set;", new SqliteParameter("$set", annotationSetId));
            if (setType == null || Convert.ToInt32(setType) != (int)AnnotationSetType.Seg) throw new ArgumentException("The annotation set is not SEG.", nameof(annotationSetId));
            if (Convert.ToInt32(ExecuteScalar(connection, "SELECT COUNT(*) FROM AnnotationClass WHERE Id = $class AND AnnotationSetId = $set;", new SqliteParameter("$class", classId), new SqliteParameter("$set", annotationSetId))) != 1) throw new ArgumentException("The class does not belong to the annotation set.", nameof(classId));
            SegPoint[] values = points.ToArray();
            if (values.Length < 2 || values.Any(point => point == null)) throw new ArgumentException("The eraser path is invalid.", nameof(points));
            if (strokeWidth < 1 || strokeWidth > 100) throw new ArgumentOutOfRangeException(nameof(strokeWidth));
            Size imageSize = GetImageSize(imageId);
            if (values.Any(point => point.X < 0 || point.Y < 0 || point.X >= imageSize.Width || point.Y >= imageSize.Height)) throw new ArgumentOutOfRangeException(nameof(points));
            int inset = (strokeWidth + 1) / 2;
            if (values.Any(point => point.X < inset || point.Y < inset || point.X > imageSize.Width - inset || point.Y > imageSize.Height - inset)) throw new ArgumentOutOfRangeException(nameof(points));
            var eraser = SegGeometryPrototype.Stroke(ToPath(values), strokeWidth);
            SegRegion[] affected = GetSegRegions(annotationSetId, imageId).Where(value => value.ClassId == classId && HasAreaOverlap(ToPaths(value), eraser)).ToArray();
            if (affected.Length == 0) return;
            var next = new List<SegRegion>();
            int zOrder = NextSegZOrder(annotationSetId, imageId);
            foreach (SegRegion region in affected)
            {
                Paths64 difference = SegGeometryPrototype.Difference(ToPaths(region), eraser);
                next.AddRange(ToPolygonRegions(region.AnnotationSetId, region.ImageId, region.ClassId, difference, imageSize, ref zOrder));
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
            List<SegRegion> next = ToPolygonRegions(value.AnnotationSetId, value.ImageId, value.ClassId, merged, imageSize, ref zOrder);
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

        private int NextSegZOrder(string annotationSetId, string imageId)
        {
            return Convert.ToInt32(ExecuteScalar(connection, "SELECT COALESCE(MAX(ZOrder), -1) + 1 FROM SegRegion WHERE AnnotationSetId = $set AND ImageId = $image;", new SqliteParameter("$set", annotationSetId), new SqliteParameter("$image", imageId)));
        }

        private static bool HasAreaOverlap(Paths64 first, Paths64 second)
        {
            return SegGeometryPrototype.Area(Clipper.Intersect(first, second, FillRule.NonZero)) > 0;
        }

        private static Path64 ToPath(IEnumerable<SegPoint> points)
        {
            return new Path64(points.Select(point => new Point64(point.X, point.Y)).ToList());
        }

        private static Paths64 ToPaths(SegRegion value)
        {
            if (value.Type == SegRegionType.Rect) return new Paths64 { SegGeometryPrototype.Rectangle(value.X, value.Y, value.X + value.Width, value.Y + value.Height) };
            if (value.Type == SegRegionType.Ellipse) return new Paths64 { SegGeometryPrototype.Ellipse(value.X, value.Y, value.Width, value.Height, 32) };
            if (value.Type == SegRegionType.PolyLine) return SegGeometryPrototype.Stroke(ToPath(value.Points), value.StrokeWidth);
            Path64 polygon = ToPath(value.Points);
            if (Clipper.Area(polygon) < 0) polygon.Reverse();
            return new Paths64 { polygon };
        }

        private static List<SegRegion> ToPolygonRegions(string annotationSetId, string imageId, string classId, Paths64 paths, Size imageSize, ref int zOrder)
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
                    if (Math.Sign(Clipper.Area(possibleHole)) != orientation && PointInPath(possibleHole[0], outer))
                    {
                        holes.Add(possibleHole);
                        remaining.RemoveAt(index);
                    }
                }
                Path64 polygon = holes.Count == 0 ? new Path64(outer) : SegGeometryPrototype.BridgeHoles(outer, holes);
                SegPoint[] points = polygon.Select(point => new SegPoint((int)Math.Max(0, Math.Min(imageSize.Width, point.X)), (int)Math.Max(0, Math.Min(imageSize.Height, point.Y)))).ToArray();
                if (points.Distinct(new SegPointComparer()).Count() >= 3) result.Add(new SegRegion(Guid.NewGuid().ToString("D"), annotationSetId, imageId, classId, SegRegionType.PolyGon, 0, 0, 0, 0, zOrder++, points));
            }
            return result;
        }

        private static bool PointInPath(Point64 point, Path64 path)
        {
            bool inside = false;
            for (int current = 0, previous = path.Count - 1; current < path.Count; previous = current++)
            {
                Point64 a = path[current]; Point64 b = path[previous];
                if ((a.Y > point.Y) != (b.Y > point.Y) && point.X < (b.X - a.X) * (point.Y - a.Y) / (double)(b.Y - a.Y) + a.X) inside = !inside;
            }
            return inside;
        }

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
            SegRegion[] next = previous.Select((value, index) => WithZOrder(byId[value.Id], firstZOrder + index)).ToArray();
            CompleteOperation((db, transaction) => { foreach (SegRegion value in next) UpdateSegRegionRaw(db, transaction, value); });
            Record(setId, () => { foreach (SegRegion value in previous) UpdateSegRegionRaw(value); }, () => { foreach (SegRegion value in next) UpdateSegRegionRaw(value); }, EstimateHistoryBytes(previous) + EstimateHistoryBytes(next));
        }

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

        private static SegRegion WithZOrder(SegRegion value, int zOrder)
        {
            return new SegRegion(value.Id, value.AnnotationSetId, value.ImageId, value.ClassId, value.Type, value.X, value.Y, value.Width, value.Height, zOrder, value.Points, value.StrokeWidth);
        }

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
                if (value.Points.Distinct(new SegPointComparer()).Count() < 2 || value.StrokeWidth < 1 || value.StrokeWidth > 100) throw new ArgumentException("The line is invalid.", nameof(value));
                int inset = (value.StrokeWidth + 1) / 2;
                if (value.Points.Any(point => point.X < inset || point.Y < inset || point.X > imageSize.Width - inset || point.Y > imageSize.Height - inset)) throw new ArgumentOutOfRangeException(nameof(value));
            }
            if (value.Type != SegRegionType.PolyGon && value.Type != SegRegionType.PolyLine) throw new ArgumentOutOfRangeException(nameof(value));
        }

        private static bool AreCollinear(IReadOnlyList<SegPoint> points)
        {
            SegPoint first = points[0];
            SegPoint second = points.First(point => point.X != first.X || point.Y != first.Y);
            return points.All(point => (long)(second.X - first.X) * (point.Y - first.Y) == (long)(second.Y - first.Y) * (point.X - first.X));
        }

        private sealed class SegPointComparer : IEqualityComparer<SegPoint>
        {
            public bool Equals(SegPoint first, SegPoint second) { return first.X == second.X && first.Y == second.Y; }
            public int GetHashCode(SegPoint value) { return value.X.GetHashCode() ^ value.Y.GetHashCode(); }
        }

        public void UpdateObdAnnotation(string annotationId, string classId, int x, int y, int width, int height)
        {
            ObdAnnotation previous = GetObdAnnotation(annotationId);
            ValidateObd(previous.AnnotationSetId, previous.ImageId, classId, x, y, width, height);
            int zOrder = Convert.ToInt32(ExecuteScalar(connection, "SELECT COALESCE(MAX(ZOrder), -1) + 1 FROM ObdAnnotation WHERE AnnotationSetId = $set AND ImageId = $image;", new SqliteParameter("$set", previous.AnnotationSetId), new SqliteParameter("$image", previous.ImageId)));
            var next = new ObdAnnotation(previous.Id, previous.AnnotationSetId, previous.ImageId, classId, x, y, width, height, zOrder);
            UpdateObdRaw(next);
            Record(previous.AnnotationSetId, () => UpdateObdRaw(previous), () => UpdateObdRaw(next), EstimateHistoryBytes(previous.Id, previous.ClassId, next.ClassId));
        }

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

        public void DeleteObdAnnotation(string annotationId)
        {
            ObdAnnotation previous = GetObdAnnotation(annotationId);
            DeleteObdRaw(annotationId);
            Record(previous.AnnotationSetId, () => InsertObdRaw(previous), () => DeleteObdRaw(annotationId), EstimateHistoryBytes(previous.Id, previous.AnnotationSetId, previous.ImageId, previous.ClassId));
        }

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

        public IReadOnlyList<ProjectImage> GetMissingImages()
        {
            return GetImages().Where(image => image.IsMissing).ToList();
        }

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

        public IReadOnlyList<ProjectBackup> GetBackups()
        {
            return GetBackups(ProjectPath);
        }

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
                ValidateDatabase(temporary);
            }
            catch
            {
                File.Delete(temporary);
                throw;
            }
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

        public void Dispose()
        {
            connection.Dispose();
            lockStream.Dispose();
        }

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

        private static void ReplaceFileSafely(string source, string destination)
        {
            try { NativeReplaceFile(source, destination); }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is NotSupportedException)
            {
                string original = PendingReplacementPath(destination);
                if (File.Exists(original)) throw new IOException("A previous database replacement has not been recovered.");
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

        private static string PendingReplacementPath(string projectPath)
        {
            return projectPath + ".restore-original";
        }

        private static SqliteConnection OpenConnection(string path)
        {
            var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false }.ToString());
            connection.Open();
            Execute(connection, null, "PRAGMA foreign_keys = ON;");
            return connection;
        }

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

        private static object ExecuteScalar(SqliteConnection connection, string commandText)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = commandText;
                return command.ExecuteScalar();
            }
        }

        private static object ExecuteScalar(SqliteConnection connection, string commandText, params SqliteParameter[] parameters)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = commandText;
                command.Parameters.AddRange(parameters);
                return command.ExecuteScalar();
            }
        }

        private static bool CanReadImage(string path)
        {
            try
            {
                using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read)) return true;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

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

        private ProjectImage ImportFile(string source, int sequence)
        {
            string managedName = NextManagedName(ProjectImageImporter.RequiresPngConversion(source)
                ? Path.GetFileNameWithoutExtension(source) + ".png"
                : Path.GetFileName(source));
            ProjectImage image = ProjectImageImporter.Read(source, managedName, sequence);
            string destination = Path.Combine(ImagesDirectory, managedName);
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

        private static List<string> OrderSources(IEnumerable<string> filePaths)
        {
            return filePaths.OrderBy(Path.GetFileName, NaturalFileNameComparer.Instance).ToList();
        }

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

        private string BackupDirectory
        {
            get { return GetBackupDirectory(ProjectPath); }
        }

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

        private void CreateBackup(string name)
        {
            Save();
            CreateBackupFile(ProjectPath, name);
        }

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

        private static IReadOnlyList<ProjectBackup> GetBackups(string projectPath)
        {
            string directory = GetBackupDirectory(projectPath);
            if (!Directory.Exists(directory)) return new List<ProjectBackup>();
            return Directory.GetFiles(directory, "*.neurocle")
                .Select(path => new ProjectBackup(path, File.GetLastWriteTimeUtc(path)))
                .OrderByDescending(backup => backup.CreatedUtc).ThenByDescending(backup => backup.Path, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static string GetBackupDirectory(string projectPath)
        {
            return Path.Combine(Path.GetDirectoryName(projectPath), Path.GetFileNameWithoutExtension(projectPath) + ".backups");
        }

        private static void TrimBackups(string directory)
        {
            foreach (string oldBackup in Directory.GetFiles(directory, "*.neurocle").OrderByDescending(File.GetLastWriteTimeUtc).ThenByDescending(file => file, StringComparer.OrdinalIgnoreCase).Skip(MaximumBackupCount)) File.Delete(oldBackup);
        }

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

        private static void CopyDirectory(string source, string target)
        {
            Directory.CreateDirectory(target);
            foreach (string file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
            foreach (string directory in Directory.GetDirectories(source)) CopyDirectory(directory, Path.Combine(target, Path.GetFileName(directory)));
        }

        private string NextManagedName(string originalName)
        {
            string stem = Path.GetFileNameWithoutExtension(originalName);
            string extension = Path.GetExtension(originalName);
            string candidate = originalName;
            int suffix = 1;
            while (File.Exists(Path.Combine(ImagesDirectory, candidate))) candidate = stem + " (" + suffix++ + ")" + extension;
            return candidate;
        }

        private static void CreateImageTable(SqliteConnection db, SqliteTransaction transaction)
        {
            Execute(db, transaction, "CREATE TABLE IF NOT EXISTS ImageRecord (Id TEXT PRIMARY KEY NOT NULL, OriginalFileName TEXT NOT NULL, ManagedFileName TEXT NOT NULL UNIQUE, Sha256 TEXT NOT NULL, Width INTEGER NOT NULL, Height INTEGER NOT NULL, Sequence INTEGER NOT NULL UNIQUE);");
        }

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

        private static string NormalizeName(string name)
        {
            string normalized = (name ?? throw new ArgumentNullException(nameof(name))).Trim();
            if (normalized.Length == 0) throw new ArgumentException("The name cannot be empty.", nameof(name));
            return normalized;
        }

        private string DeletingImagesDirectory
        {
            get { return ImagesDirectory + ".deleting"; }
        }

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

        private bool ImageRecordExists(string managedFileName)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT COUNT(*) FROM ImageRecord WHERE ManagedFileName = $name;";
                command.Parameters.AddWithValue("$name", managedFileName);
                return Convert.ToInt64(command.ExecuteScalar()) != 0;
            }
        }

        private static void RestoreStagedImages(IDictionary<string, string> stagedPaths)
        {
            foreach (KeyValuePair<string, string> stagedPath in stagedPaths)
            {
                if (File.Exists(stagedPath.Value) && !File.Exists(stagedPath.Key)) File.Move(stagedPath.Value, stagedPath.Key);
            }
        }

        private static void DeleteNewProject(string projectPath)
        {
            string imagesDirectory = Path.Combine(Path.GetDirectoryName(projectPath), Path.GetFileNameWithoutExtension(projectPath) + ".images");
            if (Directory.Exists(imagesDirectory)) Directory.Delete(imagesDirectory, true);
            if (File.Exists(projectPath)) File.Delete(projectPath);
            if (File.Exists(projectPath + ".lock")) File.Delete(projectPath + ".lock");
        }

        private sealed class HistoryEntry
        {
            public HistoryEntry(Action undo, Action redo, long sizeBytes)
            {
                Undo = undo ?? throw new ArgumentNullException(nameof(undo));
                Redo = redo ?? throw new ArgumentNullException(nameof(redo));
                SizeBytes = sizeBytes;
            }

            public Action Undo { get; }
            public Action Redo { get; }
            public long SizeBytes { get; }
        }

        private sealed class AnnotationHistory
        {
            private const int MaximumActionCount = 100;
            private const long MaximumSizeBytes = 512L * 1024L * 1024L;
            private readonly List<HistoryEntry> undo = new List<HistoryEntry>();
            private readonly List<HistoryEntry> redo = new List<HistoryEntry>();
            private long undoSizeBytes;
            private long redoSizeBytes;

            public bool CanUndo { get { return undo.Count != 0; } }
            public bool CanRedo { get { return redo.Count != 0; } }

            public void Push(HistoryEntry entry)
            {
                redo.Clear();
                redoSizeBytes = 0;
                undo.Add(entry);
                undoSizeBytes += entry.SizeBytes;
                Trim(undo, ref undoSizeBytes);
            }

            public HistoryEntry TakeUndo()
            {
                return Take(undo, ref undoSizeBytes);
            }

            public HistoryEntry TakeRedo()
            {
                return Take(redo, ref redoSizeBytes);
            }

            public void ReturnUndo(HistoryEntry entry)
            {
                undo.Add(entry);
                undoSizeBytes += entry.SizeBytes;
            }

            public void ReturnRedo(HistoryEntry entry)
            {
                redo.Add(entry);
                redoSizeBytes += entry.SizeBytes;
            }

            public void MoveUndoToRedo(HistoryEntry entry)
            {
                redo.Add(entry);
                redoSizeBytes += entry.SizeBytes;
                Trim(redo, ref redoSizeBytes);
            }

            public void MoveRedoToUndo(HistoryEntry entry)
            {
                undo.Add(entry);
                undoSizeBytes += entry.SizeBytes;
                Trim(undo, ref undoSizeBytes);
            }

            private static HistoryEntry Take(List<HistoryEntry> entries, ref long sizeBytes)
            {
                if (entries.Count == 0) return null;
                int index = entries.Count - 1;
                HistoryEntry entry = entries[index];
                entries.RemoveAt(index);
                sizeBytes -= entry.SizeBytes;
                return entry;
            }

            private static void Trim(List<HistoryEntry> entries, ref long sizeBytes)
            {
                while (entries.Count > MaximumActionCount || sizeBytes > MaximumSizeBytes)
                {
                    sizeBytes -= entries[0].SizeBytes;
                    entries.RemoveAt(0);
                }
            }
        }

        private sealed class NaturalFileNameComparer : IComparer<string>
        {
            public static readonly NaturalFileNameComparer Instance = new NaturalFileNameComparer();

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

            private static int FindDigitsEnd(string value, int start)
            {
                int index = start;
                while (index < value.Length && char.IsDigit(value[index])) index++;
                return index;
            }

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

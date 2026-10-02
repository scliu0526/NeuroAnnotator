using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using Neurocle.Application.Projects;

namespace Neurocle.Infrastructure.Persistence
{
    public sealed class ProjectStore : IProjectSession
    {
        private const int CurrentSchemaVersion = 3;
        private const int MaximumBackupCount = 5;
        private readonly FileStream lockStream;
        private readonly SqliteConnection connection;
        private readonly Dictionary<string, AnnotationHistory> histories = new Dictionary<string, AnnotationHistory>();
        private bool replayingHistory;
        private SqliteTransaction historyTransaction;

        internal static Func<DateTime> UtcNow = () => DateTime.UtcNow;
        internal static Action<string, string> ReplaceFile = (source, destination) => File.Replace(source, destination, null);

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
            if (filePaths == null) throw new ArgumentNullException(nameof(filePaths));
            return ImportSourcesWithReport(OrderSources(filePaths), cancellationRequested);
        }

        public ImageImportReport ImportDirectory(string directoryPath, bool includeSubdirectories, Func<bool> cancellationRequested)
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
            return ImportSourcesWithReport(OrderSources(sources), cancellationRequested, failures);
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

        public AnnotationSet CreateAnnotationSet(string name, AnnotationSetType type)
        {
            string normalized = NormalizeName(name);
            var set = new AnnotationSet(Guid.NewGuid().ToString("D"), normalized, type);
            CompleteOperation((db, transaction) =>
            {
                Execute(db, transaction, "INSERT INTO AnnotationSet (Id, Name, Type) VALUES ($id, $name, $type);", new SqliteParameter("$id", set.Id), new SqliteParameter("$name", set.Name), new SqliteParameter("$type", (int)set.Type));
                Execute(db, transaction, "INSERT INTO AnnotationClass (Id, AnnotationSetId, Name, Color) VALUES ($id, $set, 'class1', '#FF0000');", new SqliteParameter("$id", Guid.NewGuid().ToString("D")), new SqliteParameter("$set", set.Id));
            });
            return set;
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
            var value = new AnnotationClass(Guid.NewGuid().ToString("D"), annotationSetId, NormalizeName(name), color ?? throw new ArgumentNullException(nameof(color)));
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
            AnnotationClass previous = GetClass(classId); string next = color ?? throw new ArgumentNullException(nameof(color));
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
            Execute(connection, null, "DELETE FROM AnnotationClass WHERE Id = $id;", new SqliteParameter("$id", classId));
            Record(setId, () => { InsertClassRaw(deleted); foreach (var assignment in assignments) SetClaRaw(setId, assignment, classId); }, () => DeleteClassRaw(classId), EstimateHistoryBytes(deleted.Id, deleted.AnnotationSetId, deleted.Name, deleted.Color) + EstimateHistoryBytes(assignments));
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

        public string GetClaClassId(string annotationSetId, string imageId)
        {
            object value = ExecuteScalar(connection, "SELECT ClassId FROM ClaAnnotation WHERE AnnotationSetId = $set AND ImageId = $image;", new SqliteParameter("$set", annotationSetId), new SqliteParameter("$image", imageId));
            return value == null || value == DBNull.Value ? null : Convert.ToString(value);
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
                File.Copy(sourcePath, temporary);
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

        private ImageImportReport ImportSourcesWithReport(IReadOnlyList<string> sources, Func<bool> cancellationRequested, IReadOnlyList<ImageImportFailureDetail> initialFailures = null)
        {
            var imported = new List<ProjectImage>();
            var failures = initialFailures == null ? new List<ImageImportFailureDetail>() : new List<ImageImportFailureDetail>(initialFailures);
            long existing = Convert.ToInt64(ExecuteScalar(connection, "SELECT COUNT(*) FROM ImageRecord;"));
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
            }
            return new ImageImportReport(imported, failures, 0);
        }

        private ProjectImage ImportFile(string source, int sequence)
        {
            string managedName = NextManagedName(Path.GetFileName(source));
            ProjectImage image = ProjectImageImporter.Read(source, managedName, sequence);
            string destination = Path.Combine(ImagesDirectory, managedName);
            File.Copy(source, destination);
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

        private static void EnsureCurrentSchema(SqliteConnection db, string projectPath)
        {
            int version = Convert.ToInt32(ExecuteScalar(db, "SELECT Version FROM SchemaInfo LIMIT 1;"));
            if (version == CurrentSchemaVersion) return;
            if (version != 1 && version != 2) throw new NotSupportedException("The project schema version is not supported.");

            CreateMigrationBackup(db, projectPath, version);
            using (SqliteTransaction transaction = db.BeginTransaction())
            {
                CreateImageTable(db, transaction);
                CreateAnnotationTables(db, transaction);
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
                if (version != 1 && version != 2 && version != CurrentSchemaVersion) throw new NotSupportedException("The backup schema version is not supported.");
                ValidateTableColumns(db, "SchemaInfo", new[] { "Version" }, null, null);
                ValidateTableColumns(db, "ProjectInfo", new[] { "ProjectId", "CreatedUtc" }, null, null);
                if (version >= 2) ValidateTableColumns(db, "ImageRecord", new[] { "Id", "OriginalFileName", "ManagedFileName", "Sha256", "Width", "Height", "Sequence" }, "Id", new[] { "ManagedFileName", "Sequence" });
                if (version == CurrentSchemaVersion)
                {
                    ValidateTableColumns(db, "AnnotationSet", new[] { "Id", "Name", "Type" }, "Id", null);
                    ValidateTableColumns(db, "AnnotationClass", new[] { "Id", "AnnotationSetId", "Name", "Color" }, "Id", null);
                    ValidateTableColumns(db, "ClaAnnotation", new[] { "AnnotationSetId", "ImageId", "ClassId" }, null, null);
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

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
        private const int CurrentSchemaVersion = 2;
        private readonly FileStream lockStream;
        private readonly SqliteConnection connection;

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
                    Execute(connection, transaction, "INSERT INTO SchemaInfo (Version) VALUES ($version);", new SqliteParameter("$version", CurrentSchemaVersion));
                    Execute(connection, transaction, "INSERT INTO ProjectInfo (ProjectId, CreatedUtc) VALUES ($id, $created);", new SqliteParameter("$id", Guid.NewGuid().ToString("D")), new SqliteParameter("$created", DateTime.UtcNow.ToString("O")));
                    transaction.Commit();
                }

                var store = new ProjectStore(fullPath, lockHandle, connection);
                Directory.CreateDirectory(store.ImagesDirectory);
                store.RecoverStagedImages();
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
                connection = OpenConnection(fullPath);
                EnsureCurrentSchema(connection, fullPath);

                var store = new ProjectStore(fullPath, lockHandle, connection);
                Directory.CreateDirectory(store.ImagesDirectory);
                store.RecoverStagedImages();
                return store;
            }
            catch
            {
                connection?.Dispose();
                lockHandle.Dispose();
                throw;
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
            var sources = filePaths.OrderBy(Path.GetFileName, NaturalFileNameComparer.Instance).ToList();
            if (sources.Count == 0) return new List<ProjectImage>();
            long existing = Convert.ToInt64(ExecuteScalar(connection, "SELECT COUNT(*) FROM ImageRecord;"));
            if (existing + sources.Count > 10000) throw new ImageImportException(ImageImportFailure.ProjectImageLimit);

            var imported = new List<ProjectImage>();
            foreach (string source in sources)
            {
                string managedName = NextManagedName(Path.GetFileName(source));
                ProjectImage image = ProjectImageImporter.Read(source, managedName, checked((int)existing + imported.Count + 1));
                string destination = Path.Combine(ImagesDirectory, managedName);
                File.Copy(source, destination);
                try
                {
                    CompleteOperation((db, transaction) => Execute(db, transaction,
                        "INSERT INTO ImageRecord (Id, OriginalFileName, ManagedFileName, Sha256, Width, Height, Sequence) VALUES ($id, $original, $managed, $sha, $width, $height, $sequence);",
                        new SqliteParameter("$id", image.Id), new SqliteParameter("$original", image.OriginalFileName), new SqliteParameter("$managed", image.ManagedFileName), new SqliteParameter("$sha", image.Sha256), new SqliteParameter("$width", image.Width), new SqliteParameter("$height", image.Height), new SqliteParameter("$sequence", image.Sequence)));
                    imported.Add(image);
                }
                catch
                {
                    File.Delete(destination);
                    throw;
                }
            }
            return imported;
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
                        images.Add(new ProjectImage(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetInt32(4), reader.GetInt32(5), reader.GetInt32(6)));
                    }
                }
            }
            return images;
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

        private static void EnsureCurrentSchema(SqliteConnection db, string projectPath)
        {
            int version = Convert.ToInt32(ExecuteScalar(db, "SELECT Version FROM SchemaInfo LIMIT 1;"));
            if (version == CurrentSchemaVersion) return;
            if (version != 1) throw new NotSupportedException("The project schema version is not supported.");

            CreateMigrationBackup(db, projectPath, version);
            using (SqliteTransaction transaction = db.BeginTransaction())
            {
                CreateImageTable(db, transaction);
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
            File.SetLastWriteTimeUtc(backupPath, DateTime.UtcNow);
            foreach (string oldBackup in Directory.GetFiles(directory, "*.neurocle").OrderByDescending(File.GetLastWriteTimeUtc).ThenByDescending(file => file, StringComparer.OrdinalIgnoreCase).Skip(5)) File.Delete(oldBackup);
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

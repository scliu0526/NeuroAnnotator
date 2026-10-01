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
        private const int CurrentSchemaVersion = 1;
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
                    Execute(connection, transaction, "INSERT INTO SchemaInfo (Version) VALUES (1);");
                    Execute(connection, transaction, "INSERT INTO ProjectInfo (ProjectId, CreatedUtc) VALUES ($id, $created);", new SqliteParameter("$id", Guid.NewGuid().ToString("D")), new SqliteParameter("$created", DateTime.UtcNow.ToString("O")));
                    transaction.Commit();
                }

                var store = new ProjectStore(fullPath, lockHandle, connection);
                CreateImageTable(connection, null);
                Directory.CreateDirectory(store.ImagesDirectory);
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
                object version = ExecuteScalar(connection, "SELECT Version FROM SchemaInfo LIMIT 1;");
                if (Convert.ToInt32(version) != CurrentSchemaVersion)
                {
                    throw new NotSupportedException("The project schema version is not supported.");
                }

                var store = new ProjectStore(fullPath, lockHandle, connection);
                Directory.CreateDirectory(store.ImagesDirectory);
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
            var sources = filePaths.OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).ToList();
            if (sources.Count == 0) return new List<ProjectImage>();
            long existing = Convert.ToInt64(ExecuteScalar(connection, "SELECT COUNT(*) FROM ImageRecord;"));
            if (existing + sources.Count > 10000) throw new InvalidOperationException("The project image limit would be exceeded.");

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

        private static void DeleteNewProject(string projectPath)
        {
            string imagesDirectory = Path.Combine(Path.GetDirectoryName(projectPath), Path.GetFileNameWithoutExtension(projectPath) + ".images");
            if (Directory.Exists(imagesDirectory)) Directory.Delete(imagesDirectory, true);
            if (File.Exists(projectPath)) File.Delete(projectPath);
            if (File.Exists(projectPath + ".lock")) File.Delete(projectPath + ".lock");
        }
    }
}

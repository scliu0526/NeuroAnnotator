using System;
using System.IO;
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
                    Execute(connection, transaction, "INSERT INTO SchemaInfo (Version) VALUES (1);");
                    Execute(connection, transaction, "INSERT INTO ProjectInfo (ProjectId, CreatedUtc) VALUES ($id, $created);", new SqliteParameter("$id", Guid.NewGuid().ToString("D")), new SqliteParameter("$created", DateTime.UtcNow.ToString("O")));
                    transaction.Commit();
                }

                var store = new ProjectStore(fullPath, lockHandle, connection);
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

        private static void DeleteNewProject(string projectPath)
        {
            string imagesDirectory = Path.Combine(Path.GetDirectoryName(projectPath), Path.GetFileNameWithoutExtension(projectPath) + ".images");
            if (Directory.Exists(imagesDirectory)) Directory.Delete(imagesDirectory, true);
            if (File.Exists(projectPath)) File.Delete(projectPath);
            if (File.Exists(projectPath + ".lock")) File.Delete(projectPath + ".lock");
        }
    }
}

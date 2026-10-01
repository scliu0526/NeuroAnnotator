using System;
using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Neurocle.Infrastructure.Persistence;

namespace Neurocle.Tests
{
    [TestClass]
    public sealed class ProjectStoreTests
    {
        [TestMethod]
        public void Create_open_and_completed_operation_persist_atomically()
        {
            string path = TemporaryProjectPath();
            try
            {
                using (ProjectStore store = ProjectStore.Create(path))
                {
                    store.CompleteOperation((connection, transaction) =>
                    {
                        using (var command = connection.CreateCommand())
                        {
                            command.Transaction = transaction;
                            command.CommandText = "CREATE TABLE TestValue (Value INTEGER NOT NULL); INSERT INTO TestValue (Value) VALUES (7);";
                            command.ExecuteNonQuery();
                        }
                    });
                    store.Save();
                }

                using (ProjectStore reopened = ProjectStore.Open(path))
                using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()))
                {
                    connection.Open();
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = "SELECT Value FROM TestValue;";
                        Assert.AreEqual(7L, (long)command.ExecuteScalar());
                    }
                }
            }
            finally { DeleteTemporaryProject(path); }
        }

        [TestMethod]
        public void Failed_completed_operation_rolls_back()
        {
            string path = TemporaryProjectPath();
            try
            {
                using (ProjectStore store = ProjectStore.Create(path))
                {
                    Assert.ThrowsException<InvalidOperationException>(() => store.CompleteOperation((connection, transaction) =>
                    {
                        using (var command = connection.CreateCommand())
                        {
                            command.Transaction = transaction;
                            command.CommandText = "CREATE TABLE RollbackValue (Value INTEGER);";
                            command.ExecuteNonQuery();
                        }
                        throw new InvalidOperationException("Simulated failure");
                    }));
                }

                using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()))
                {
                    connection.Open();
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='RollbackValue';";
                        Assert.AreEqual(0L, (long)command.ExecuteScalar());
                    }
                }
            }
            finally { DeleteTemporaryProject(path); }
        }

        [TestMethod]
        public void Opening_same_project_twice_is_rejected()
        {
            string path = TemporaryProjectPath();
            try
            {
                using (ProjectStore first = ProjectStore.Create(path))
                {
                    Assert.ThrowsException<InvalidOperationException>(() => ProjectStore.Open(path));
                }
            }
            finally { DeleteTemporaryProject(path); }
        }

        [TestMethod]
        public void Known_cloud_sync_directory_is_rejected()
        {
            string path = Path.Combine(Path.GetTempPath(), "Dropbox", Guid.NewGuid().ToString("N"), "project.neurocle");

            Assert.ThrowsException<NotSupportedException>(() => ProjectStore.Create(path));
        }

        [TestMethod]
        public void Existing_managed_images_directory_is_never_deleted_on_create_failure()
        {
            string path = TemporaryProjectPath();
            string imagesDirectory = Path.Combine(Path.GetDirectoryName(path), "project.images");
            string sentinel = Path.Combine(imagesDirectory, "keep.txt");
            try
            {
                Directory.CreateDirectory(imagesDirectory);
                File.WriteAllText(sentinel, "user data");

                Assert.ThrowsException<IOException>(() => ProjectStore.Create(path));
                Assert.IsTrue(File.Exists(sentinel));
            }
            finally { DeleteTemporaryProject(path); }
        }

        private static string TemporaryProjectPath()
        {
            string directory = Path.Combine(Path.GetTempPath(), "Neurocle.Tests", Guid.NewGuid().ToString("N"));
            return Path.Combine(directory, "project.neurocle");
        }

        private static void DeleteTemporaryProject(string path)
        {
            string directory = Path.GetDirectoryName(path);
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}

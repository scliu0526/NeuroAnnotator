using System;
using System.Collections.Generic;
using System.IO;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Neurocle.Infrastructure.Persistence;
using Neurocle.Application.Projects;

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

        [TestMethod]
        public void Import_copies_images_with_hash_metadata_and_collision_suffix()
        {
            string path = TemporaryProjectPath();
            string sourceRoot = Path.Combine(Path.GetDirectoryName(path), "sources");
            try
            {
                string first = CreateBitmap(Path.Combine(sourceRoot, "one", "photo.bmp"), Color.Red);
                string second = CreateBitmap(Path.Combine(sourceRoot, "two", "photo.bmp"), Color.Blue);
                using (ProjectStore store = ProjectStore.Create(path))
                {
                    var images = store.ImportFiles(new[] { second, first });
                    Assert.AreEqual(2, images.Count);
                    Assert.AreEqual("photo.bmp", images[0].ManagedFileName);
                    Assert.AreEqual("photo (1).bmp", images[1].ManagedFileName);
                    Assert.AreEqual(1, images[0].Sequence);
                    Assert.AreEqual(2, images[1].Sequence);
                    Assert.AreEqual(3, images[0].Width);
                    Assert.AreEqual(2, images[0].Height);
                    Assert.IsTrue(File.Exists(Path.Combine(store.ImagesDirectory, images[0].ManagedFileName)));
                    Assert.AreNotEqual(images[0].Sha256, images[1].Sha256);
                }

                using (ProjectStore reopened = ProjectStore.Open(path))
                {
                    var persisted = reopened.GetImages();
                    Assert.AreEqual(2, persisted.Count);
                    Assert.AreEqual("photo.bmp", persisted[0].OriginalFileName);
                    Assert.AreEqual("photo (1).bmp", persisted[1].ManagedFileName);
                    Assert.AreEqual(1, persisted[0].Sequence);
                    Assert.AreEqual(2, persisted[1].Sequence);
                }
            }
            finally { DeleteTemporaryProject(path); }
        }

        [TestMethod]
        public void Import_accepts_all_supported_formats_in_natural_filename_order()
        {
            string path = TemporaryProjectPath();
            string sourceRoot = Path.Combine(Path.GetDirectoryName(path), "sources");
            try
            {
                string ten = CreateBitmap(Path.Combine(sourceRoot, "10.png"), Color.Red, ImageFormat.Png);
                string two = CreateBitmap(Path.Combine(sourceRoot, "2.jpg"), Color.Green, ImageFormat.Jpeg);
                string one = CreateBitmap(Path.Combine(sourceRoot, "1.jpeg"), Color.Blue, ImageFormat.Jpeg);
                string eleven = CreateBitmap(Path.Combine(sourceRoot, "11.bmp"), Color.Black, ImageFormat.Bmp);
                using (ProjectStore store = ProjectStore.Create(path))
                {
                    var images = store.ImportFiles(new[] { ten, two, eleven, one });
                    CollectionAssert.AreEqual(new[] { "1.jpeg", "2.jpg", "10.png", "11.bmp" }, images.Select(image => image.OriginalFileName).ToArray());
                    CollectionAssert.AreEqual(new[] { 1, 2, 3, 4 }, images.Select(image => image.Sequence).ToArray());
                }
            }
            finally { DeleteTemporaryProject(path); }
        }

        [TestMethod]
        public void Opening_a_version_one_project_migrates_image_storage_before_import()
        {
            string path = TemporaryProjectPath();
            string source = Path.Combine(Path.GetDirectoryName(path), "source.bmp");
            try
            {
                CreateVersionOneProject(path);
                CreateBitmap(source, Color.Red);
                using (ProjectStore store = ProjectStore.Open(path))
                {
                    Assert.AreEqual(1, store.ImportFiles(new[] { source }).Count);
                }

                string backupDirectory = Path.Combine(Path.GetDirectoryName(path), "project.backups");
                string[] backups = Directory.GetFiles(backupDirectory, "*.neurocle");
                Assert.AreEqual(2, backups.Length);

                using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()))
                {
                    connection.Open();
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = "SELECT Version FROM SchemaInfo;";
                        Assert.AreEqual(3L, (long)command.ExecuteScalar());
                    }
                }

                string migrationBackup = backups.Single(backup => Path.GetFileName(backup).StartsWith("before-migration-v1", StringComparison.Ordinal));
                using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = migrationBackup, Pooling = false }.ToString()))
                {
                    connection.Open();
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = "SELECT Version FROM SchemaInfo;";
                        Assert.AreEqual(1L, (long)command.ExecuteScalar());
                    }
                }
            }
            finally { DeleteTemporaryProject(path); }
        }

        [TestMethod]
        public void Migration_backup_retains_only_the_newest_five_backups()
        {
            string path = TemporaryProjectPath();
            string source = Path.Combine(Path.GetDirectoryName(path), "source.bmp");
            try
            {
                CreateVersionOneProject(path);
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-1));
                string backupDirectory = Path.Combine(Path.GetDirectoryName(path), "project.backups");
                Directory.CreateDirectory(backupDirectory);
                for (int index = 0; index < 5; index++)
                {
                    string oldBackup = Path.Combine(backupDirectory, "old-" + index + ".neurocle");
                    File.WriteAllText(oldBackup, "old backup");
                    File.SetLastWriteTimeUtc(oldBackup, DateTime.UtcNow.AddMinutes(-10 - index));
                }
                CreateBitmap(source, Color.Red);

                using (ProjectStore store = ProjectStore.Open(path)) store.ImportFiles(new[] { source });

                string[] backups = Directory.GetFiles(backupDirectory, "*.neurocle");
                Assert.AreEqual(5, backups.Length);
                Assert.IsTrue(File.Exists(Path.Combine(backupDirectory, "before-migration-v1.neurocle")));
                Assert.IsFalse(File.Exists(Path.Combine(backupDirectory, "old-4.neurocle")));
            }
            finally { DeleteTemporaryProject(path); }
        }

        [TestMethod]
        public void Import_accepts_the_width_boundary_and_rejects_a_wider_image()
        {
            string path = TemporaryProjectPath();
            string sourceRoot = Path.Combine(Path.GetDirectoryName(path), "sources");
            try
            {
                string allowed = CreateBitmap(Path.Combine(sourceRoot, "allowed.bmp"), Color.Red, ImageFormat.Bmp, 10000, 1);
                string rejected = CreateBitmap(Path.Combine(sourceRoot, "rejected.bmp"), Color.Red, ImageFormat.Bmp, 10001, 1);
                using (ProjectStore store = ProjectStore.Create(path))
                {
                    Assert.AreEqual(1, store.ImportFiles(new[] { allowed }).Count);
                    var exception = Assert.ThrowsException<ImageImportException>(() => store.ImportFiles(new[] { rejected }));
                    Assert.AreEqual(ImageImportFailure.DimensionLimit, exception.Failure);
                }
            }
            finally { DeleteTemporaryProject(path); }
        }

        [TestMethod]
        public void Image_import_limits_accept_exact_boundaries_and_reject_excess()
        {
            ProjectImageImporter.ValidateFileSize(100L * 1024L * 1024L);
            ProjectImageImporter.ValidateDimensions(10000, 10000);

            var fileSizeException = Assert.ThrowsException<ImageImportException>(() => ProjectImageImporter.ValidateFileSize((100L * 1024L * 1024L) + 1));
            Assert.AreEqual(ImageImportFailure.FileSizeLimit, fileSizeException.Failure);
            var heightException = Assert.ThrowsException<ImageImportException>(() => ProjectImageImporter.ValidateDimensions(1, 10001));
            Assert.AreEqual(ImageImportFailure.DimensionLimit, heightException.Failure);
        }

        [TestMethod]
        public void Import_rejects_a_batch_that_exceeds_the_project_image_limit()
        {
            string path = TemporaryProjectPath();
            string source = Path.Combine(Path.GetDirectoryName(path), "source.bmp");
            try
            {
                CreateBitmap(source, Color.Red);
                using (ProjectStore store = ProjectStore.Create(path))
                {
                    store.CompleteOperation((connection, transaction) =>
                    {
                        for (int index = 1; index <= 10000; index++)
                        {
                            using (var command = connection.CreateCommand())
                            {
                                command.Transaction = transaction;
                                command.CommandText = "INSERT INTO ImageRecord (Id, OriginalFileName, ManagedFileName, Sha256, Width, Height, Sequence) VALUES ($id, $original, $managed, $sha, 1, 1, $sequence);";
                                command.Parameters.AddWithValue("$id", index.ToString());
                                command.Parameters.AddWithValue("$original", index + ".bmp");
                                command.Parameters.AddWithValue("$managed", index + ".bmp");
                                command.Parameters.AddWithValue("$sha", "hash");
                                command.Parameters.AddWithValue("$sequence", index);
                                command.ExecuteNonQuery();
                            }
                        }
                    });

                    var exception = Assert.ThrowsException<ImageImportException>(() => store.ImportFiles(new[] { source }));
                    Assert.AreEqual(ImageImportFailure.ProjectImageLimit, exception.Failure);
                }
            }
            finally { DeleteTemporaryProject(path); }
        }

        [TestMethod]
        public void Deleting_images_compacts_sequences_and_next_import_continues_from_the_new_count()
        {
            string path = TemporaryProjectPath();
            string sourceRoot = Path.Combine(Path.GetDirectoryName(path), "sources");
            try
            {
                string first = CreateBitmap(Path.Combine(sourceRoot, "1.bmp"), Color.Red);
                string second = CreateBitmap(Path.Combine(sourceRoot, "2.bmp"), Color.Green);
                string third = CreateBitmap(Path.Combine(sourceRoot, "3.bmp"), Color.Blue);
                string fourth = CreateBitmap(Path.Combine(sourceRoot, "4.bmp"), Color.Black);
                using (ProjectStore store = ProjectStore.Create(path))
                {
                    var imported = store.ImportFiles(new[] { third, second, first });
                    store.DeleteImages(new[] { imported[1].Id });

                    var remaining = store.GetImages();
                    CollectionAssert.AreEqual(new[] { "1.bmp", "3.bmp" }, remaining.Select(image => image.OriginalFileName).ToArray());
                    CollectionAssert.AreEqual(new[] { 1, 2 }, remaining.Select(image => image.Sequence).ToArray());
                    Assert.IsFalse(File.Exists(Path.Combine(store.ImagesDirectory, "2.bmp")));

                    var next = store.ImportFiles(new[] { fourth }).Single();
                    Assert.AreEqual(3, next.Sequence);
                }
            }
            finally { DeleteTemporaryProject(path); }
        }

        [TestMethod]
        public void Deleting_a_locked_image_leaves_its_record_and_file_unchanged()
        {
            string path = TemporaryProjectPath();
            string source = Path.Combine(Path.GetDirectoryName(path), "source.bmp");
            try
            {
                CreateBitmap(source, Color.Red);
                using (ProjectStore store = ProjectStore.Create(path))
                {
                    ProjectImage image = store.ImportFiles(new[] { source }).Single();
                    string managedPath = Path.Combine(store.ImagesDirectory, image.ManagedFileName);
                    using (new FileStream(managedPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        Assert.ThrowsException<IOException>(() => store.DeleteImages(new[] { image.Id }));
                    }
                    Assert.AreEqual(1, store.GetImages().Count);
                    Assert.IsTrue(File.Exists(managedPath));
                }
            }
            finally { DeleteTemporaryProject(path); }
        }

        [TestMethod]
        public void Reopening_does_not_delete_a_legitimate_delete_prefixed_image()
        {
            string path = TemporaryProjectPath();
            string source = Path.Combine(Path.GetDirectoryName(path), ".delete-sample.bmp");
            try
            {
                CreateBitmap(source, Color.Red);
                using (ProjectStore store = ProjectStore.Create(path)) store.ImportFiles(new[] { source });
                using (ProjectStore reopened = ProjectStore.Open(path))
                {
                    Assert.AreEqual(1, reopened.GetImages().Count);
                    Assert.IsTrue(File.Exists(Path.Combine(reopened.ImagesDirectory, ".delete-sample.bmp")));
                }
            }
            finally { DeleteTemporaryProject(path); }
        }

        [TestMethod]
        public void Reopening_restores_an_image_staged_before_a_failed_delete_transaction()
        {
            string path = TemporaryProjectPath();
            string source = Path.Combine(Path.GetDirectoryName(path), "source.bmp");
            try
            {
                CreateBitmap(source, Color.Red);
                ProjectImage image;
                string imagesDirectory;
                using (ProjectStore store = ProjectStore.Create(path))
                {
                    image = store.ImportFiles(new[] { source }).Single();
                    imagesDirectory = store.ImagesDirectory;
                    string deletingDirectory = imagesDirectory + ".deleting";
                    Directory.CreateDirectory(deletingDirectory);
                    File.Move(Path.Combine(imagesDirectory, image.ManagedFileName), Path.Combine(deletingDirectory, image.ManagedFileName));
                }

                using (ProjectStore reopened = ProjectStore.Open(path))
                {
                    Assert.AreEqual(1, reopened.GetImages().Count);
                    Assert.IsTrue(File.Exists(Path.Combine(imagesDirectory, image.ManagedFileName)));
                }
            }
            finally { DeleteTemporaryProject(path); }
        }

        [TestMethod]
        public void Deleting_a_reimported_name_succeeds_when_an_old_staged_file_remains()
        {
            string path = TemporaryProjectPath();
            string source = Path.Combine(Path.GetDirectoryName(path), "source.bmp");
            try
            {
                CreateBitmap(source, Color.Red);
                using (ProjectStore store = ProjectStore.Create(path))
                {
                    ProjectImage first = store.ImportFiles(new[] { source }).Single();
                    store.DeleteImages(new[] { first.Id });
                    string oldOperation = Path.Combine(store.ImagesDirectory + ".deleting", "old-operation");
                    Directory.CreateDirectory(oldOperation);
                    File.Copy(source, Path.Combine(oldOperation, first.ManagedFileName));

                    ProjectImage reimported = store.ImportFiles(new[] { source }).Single();
                    store.DeleteImages(new[] { reimported.Id });

                    Assert.AreEqual(0, store.GetImages().Count);
                    Assert.IsFalse(Directory.Exists(store.ImagesDirectory + ".deleting"));
                }
            }
            finally { DeleteTemporaryProject(path); }
        }

        [TestMethod]
        public void Report_import_keeps_valid_files_and_reports_each_invalid_file()
        {
            string path = TemporaryProjectPath();
            string sourceRoot = Path.Combine(Path.GetDirectoryName(path), "sources");
            try
            {
                string first = CreateBitmap(Path.Combine(sourceRoot, "1.bmp"), Color.Red);
                string invalid = Path.Combine(sourceRoot, "2.txt");
                File.WriteAllText(invalid, "not an image");
                string second = CreateBitmap(Path.Combine(sourceRoot, "3.bmp"), Color.Blue);
                using (ProjectStore store = ProjectStore.Create(path))
                {
                    ImageImportReport report = store.ImportFilesWithReport(new[] { second, invalid, first }, null);
                    Assert.AreEqual(2, report.Imported.Count);
                    Assert.AreEqual(1, report.Failures.Count);
                    Assert.AreEqual(0, report.UnprocessedCount);
                    Assert.AreEqual("2.txt", Path.GetFileName(report.Failures[0].FilePath));
                    Assert.AreEqual(ImageImportFailure.UnsupportedFormat, report.Failures[0].Failure);
                    Assert.AreEqual(2, store.GetImages().Count);
                }
            }
            finally { DeleteTemporaryProject(path); }
        }

        [TestMethod]
        public void Report_import_cancellation_keeps_completed_files_and_counts_unprocessed_files()
        {
            string path = TemporaryProjectPath();
            string sourceRoot = Path.Combine(Path.GetDirectoryName(path), "sources");
            try
            {
                string first = CreateBitmap(Path.Combine(sourceRoot, "1.bmp"), Color.Red);
                string second = CreateBitmap(Path.Combine(sourceRoot, "2.bmp"), Color.Green);
                string third = CreateBitmap(Path.Combine(sourceRoot, "3.bmp"), Color.Blue);
                int checks = 0;
                using (ProjectStore store = ProjectStore.Create(path))
                {
                    ImageImportReport report = store.ImportFilesWithReport(new[] { third, second, first }, () => ++checks > 1);
                    Assert.AreEqual(1, report.Imported.Count);
                    Assert.AreEqual(0, report.Failures.Count);
                    Assert.AreEqual(2, report.UnprocessedCount);
                    Assert.AreEqual(1, store.GetImages().Count);
                }
            }
            finally { DeleteTemporaryProject(path); }
        }

        [TestMethod]
        public void Report_import_keeps_valid_files_when_empty_and_corrupt_images_fail()
        {
            string path = TemporaryProjectPath();
            string sourceRoot = Path.Combine(Path.GetDirectoryName(path), "sources");
            try
            {
                string first = CreateBitmap(Path.Combine(sourceRoot, "1.bmp"), Color.Red);
                string empty = Path.Combine(sourceRoot, "2.bmp");
                File.WriteAllBytes(empty, new byte[0]);
                string corrupt = Path.Combine(sourceRoot, "3.bmp");
                File.WriteAllText(corrupt, "not a bitmap");
                string second = CreateBitmap(Path.Combine(sourceRoot, "4.bmp"), Color.Blue);
                using (ProjectStore store = ProjectStore.Create(path))
                {
                    ImageImportReport report = store.ImportFilesWithReport(new[] { second, corrupt, empty, first }, null);
                    Assert.AreEqual(2, report.Imported.Count);
                    Assert.AreEqual(2, report.Failures.Count);
                    CollectionAssert.AreEqual(new[] { ImageImportFailure.InvalidImage, ImageImportFailure.InvalidImage }, report.Failures.Select(failure => failure.Failure).ToArray());
                    Assert.AreEqual(2, store.GetImages().Count);
                }
            }
            finally { DeleteTemporaryProject(path); }
        }

        [TestMethod]
        public void Directory_import_honors_the_subdirectory_choice()
        {
            string path = TemporaryProjectPath();
            string sourceRoot = Path.Combine(Path.GetDirectoryName(path), "sources");
            try
            {
                CreateBitmap(Path.Combine(sourceRoot, "root.bmp"), Color.Red);
                CreateBitmap(Path.Combine(sourceRoot, "sub", "nested.bmp"), Color.Blue);
                using (ProjectStore store = ProjectStore.Create(path))
                {
                    ImageImportReport report = store.ImportDirectory(sourceRoot, false, null);
                    Assert.AreEqual(1, report.Imported.Count);
                    Assert.AreEqual("root.bmp", report.Imported[0].OriginalFileName);
                }

                using (ProjectStore store = ProjectStore.Create(Path.Combine(Path.GetDirectoryName(path), "recursive.neurocle")))
                {
                    ImageImportReport report = store.ImportDirectory(sourceRoot, true, null);
                    Assert.AreEqual(2, report.Imported.Count);
                }
            }
            finally { DeleteTemporaryProject(path); }
        }

        [TestMethod]
        public void Directory_import_reports_a_removed_folder_without_changing_the_project()
        {
            string path = TemporaryProjectPath();
            string sourceRoot = Path.Combine(Path.GetDirectoryName(path), "removed-sources");
            try
            {
                using (ProjectStore store = ProjectStore.Create(path))
                {
                    ImageImportReport report = store.ImportDirectory(sourceRoot, true, null);
                    Assert.AreEqual(0, report.Imported.Count);
                    Assert.AreEqual(1, report.Failures.Count);
                    Assert.AreEqual(0, report.UnprocessedCount);
                    Assert.AreEqual(sourceRoot, report.Failures[0].FilePath);
                    Assert.AreEqual(ImageImportFailure.DirectoryNotFound, report.Failures[0].Failure);
                    Assert.AreEqual(0, store.GetImages().Count);
                }
            }
            finally { DeleteTemporaryProject(path); }
        }

        [TestMethod]
        public void Directory_scan_can_be_cancelled_without_changing_the_project()
        {
            string path = TemporaryProjectPath();
            string sourceRoot = Path.Combine(Path.GetDirectoryName(path), "sources");
            try
            {
                CreateBitmap(Path.Combine(sourceRoot, "1.bmp"), Color.Red);
                CreateBitmap(Path.Combine(sourceRoot, "2.bmp"), Color.Blue);
                int checks = 0;
                using (ProjectStore store = ProjectStore.Create(path))
                {
                    ImageImportReport report = store.ImportDirectory(sourceRoot, true, () => ++checks > 1);
                    Assert.AreEqual(0, report.Imported.Count);
                    Assert.AreEqual(0, report.Failures.Count);
                    Assert.AreEqual(0, report.UnprocessedCount);
                    Assert.IsTrue(report.WasCancelledDuringScan);
                    Assert.AreEqual(0, store.GetImages().Count);
                }
            }
            finally { DeleteTemporaryProject(path); }
        }

        [TestMethod]
        public void Missing_image_is_preserved_and_only_an_identical_file_can_relink_it()
        {
            string path = TemporaryProjectPath();
            string sourceRoot = Path.Combine(Path.GetDirectoryName(path), "sources");
            try
            {
                string source = CreateBitmap(Path.Combine(sourceRoot, "source.bmp"), Color.Red);
                string different = CreateBitmap(Path.Combine(sourceRoot, "different.bmp"), Color.Blue);
                ProjectImage image;
                using (ProjectStore store = ProjectStore.Create(path))
                {
                    image = store.ImportFiles(new[] { source }).Single();
                    File.Delete(Path.Combine(store.ImagesDirectory, image.ManagedFileName));

                    Assert.IsTrue(store.GetImages().Single().IsMissing);
                    Assert.AreEqual(image.Id, store.GetMissingImages().Single().Id);
                    ImageRelinkException mismatch = Assert.ThrowsException<ImageRelinkException>(() => store.RelinkImage(image.Id, different));
                    Assert.AreEqual(ImageRelinkFailure.ContentMismatch, mismatch.Failure);
                    Assert.IsTrue(store.GetMissingImages().Single().IsMissing);

                    store.RelinkImage(image.Id, source);
                    Assert.AreEqual(0, store.GetMissingImages().Count);
                    Assert.IsTrue(File.Exists(Path.Combine(store.ImagesDirectory, image.ManagedFileName)));
                }

                using (ProjectStore reopened = ProjectStore.Open(path))
                {
                    Assert.AreEqual(1, reopened.GetImages().Count);
                    Assert.AreEqual(0, reopened.GetMissingImages().Count);
                }
            }
            finally { DeleteTemporaryProject(path); }
        }

        [TestMethod]
        public void Permanently_deleting_a_missing_image_removes_its_record()
        {
            string path = TemporaryProjectPath();
            string source = Path.Combine(Path.GetDirectoryName(path), "source.bmp");
            try
            {
                CreateBitmap(source, Color.Red);
                using (ProjectStore store = ProjectStore.Create(path))
                {
                    ProjectImage image = store.ImportFiles(new[] { source }).Single();
                    File.Delete(Path.Combine(store.ImagesDirectory, image.ManagedFileName));

                    store.DeleteImages(new[] { image.Id });
                    Assert.AreEqual(0, store.GetImages().Count);
                    Assert.AreEqual(0, store.GetMissingImages().Count);
                }
            }
            finally { DeleteTemporaryProject(path); }
        }

        [TestMethod]
        public void Locked_managed_image_is_treated_as_missing_until_it_is_readable()
        {
            string path = TemporaryProjectPath();
            string source = Path.Combine(Path.GetDirectoryName(path), "source.bmp");
            try
            {
                CreateBitmap(source, Color.Red);
                using (ProjectStore store = ProjectStore.Create(path))
                {
                    ProjectImage image = store.ImportFiles(new[] { source }).Single();
                    string managedPath = Path.Combine(store.ImagesDirectory, image.ManagedFileName);
                    using (File.Open(managedPath, FileMode.Open, FileAccess.Read, FileShare.None))
                    {
                        Assert.IsTrue(store.GetImages().Single().IsMissing);
                        Assert.AreEqual(image.Id, store.GetMissingImages().Single().Id);
                    }
                    Assert.IsFalse(store.GetImages().Single().IsMissing);
                }
            }
            finally { DeleteTemporaryProject(path); }
        }

        [TestMethod]
        public void Daily_backup_is_created_once_per_day_and_keeps_only_database_files()
        {
            string path = TemporaryProjectPath();
            Func<DateTime> originalClock = ProjectStore.UtcNow;
            try
            {
                ProjectStore.UtcNow = () => new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);
                using (ProjectStore created = ProjectStore.Create(path)) { }
                using (ProjectStore opened = ProjectStore.Open(path)) { }

                ProjectStore.UtcNow = () => new DateTime(2026, 10, 2, 8, 0, 0, DateTimeKind.Utc);
                using (ProjectStore opened = ProjectStore.Open(path))
                {
                    CollectionAssert.AreEqual(new[] { "daily-2026-10-02.neurocle", "daily-2026-10-01.neurocle" }, opened.GetBackups().Select(backup => Path.GetFileName(backup.Path)).ToArray());
                }
            }
            finally
            {
                ProjectStore.UtcNow = originalClock;
                DeleteTemporaryProject(path);
            }
        }

        [TestMethod]
        public void Restore_creates_a_current_backup_and_reopens_the_project()
        {
            string path = TemporaryProjectPath();
            string source = Path.Combine(Path.GetDirectoryName(path), "source.bmp");
            Func<DateTime> originalClock = ProjectStore.UtcNow;
            try
            {
                CreateBitmap(source, Color.Red);
                ProjectStore.UtcNow = () => new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);
                using (ProjectStore created = ProjectStore.Create(path)) created.ImportFiles(new[] { source });
                ProjectStore.UtcNow = () => new DateTime(2026, 10, 2, 8, 0, 0, DateTimeKind.Utc);
                using (ProjectStore store = ProjectStore.Open(path))
                {
                    ProjectBackup backup = store.GetBackups().Single(item => Path.GetFileName(item.Path) == "daily-2026-10-02.neurocle");
                    store.DeleteImages(store.GetImages().Select(image => image.Id));

                    using (IProjectSession restored = store.RestoreBackup(backup.Path))
                    {
                        Assert.AreEqual(1, restored.GetImages().Count);
                        Assert.AreEqual(1, restored.GetMissingImages().Count);
                        Assert.IsTrue(restored.GetBackups().Any(item => Path.GetFileName(item.Path).StartsWith("before-restore-", StringComparison.Ordinal)));
                    }
                }
            }
            finally
            {
                ProjectStore.UtcNow = originalClock;
                DeleteTemporaryProject(path);
            }
        }

        [TestMethod]
        public void Corrupt_project_reports_available_backups()
        {
            string path = TemporaryProjectPath();
            try
            {
                using (ProjectStore created = ProjectStore.Create(path)) { }
                File.WriteAllText(path, "not a sqlite database");

                ProjectOpenException exception = Assert.ThrowsException<ProjectOpenException>(() => ProjectStore.Open(path));
                Assert.AreEqual(path, exception.ProjectPath);
                Assert.IsTrue(exception.Backups.Any());
                using (ProjectStore restored = ProjectStore.Restore(path, exception.Backups[0].Path))
                {
                    Assert.IsTrue(restored.GetBackups().Any(backup => Path.GetFileName(backup.Path).StartsWith("before-restore-", StringComparison.Ordinal)));
                }
            }
            finally { DeleteTemporaryProject(path); }
        }

        [TestMethod]
        public void Failed_restore_reopens_the_original_project_session()
        {
            string path = TemporaryProjectPath();
            Action<string, string> originalReplace = ProjectStore.ReplaceFile;
            try
            {
                using (ProjectStore store = ProjectStore.Create(path))
                {
                    ProjectBackup backup = store.GetBackups().Single();
                    ProjectStore.ReplaceFile = (source, destination) => { throw new IOException("Simulated replace failure"); };

                    ProjectRestoreException exception = Assert.ThrowsException<ProjectRestoreException>(() => store.RestoreBackup(backup.Path));
                    using (IProjectSession recovered = exception.RecoveredSession)
                    {
                        Assert.AreEqual(path, recovered.ProjectPath);
                        recovered.Save();
                    }
                }
            }
            finally
            {
                ProjectStore.ReplaceFile = originalReplace;
                DeleteTemporaryProject(path);
            }
        }

        [TestMethod]
        public void Restoring_the_oldest_of_five_backups_keeps_the_selected_backup_available_for_restore()
        {
            string path = TemporaryProjectPath();
            Func<DateTime> originalClock = ProjectStore.UtcNow;
            try
            {
                ProjectStore.UtcNow = () => new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);
                using (ProjectStore created = ProjectStore.Create(path)) { }
                for (int day = 2; day <= 5; day++)
                {
                    int capturedDay = day;
                    ProjectStore.UtcNow = () => new DateTime(2026, 10, capturedDay, 8, 0, 0, DateTimeKind.Utc);
                    using (ProjectStore opened = ProjectStore.Open(path)) { }
                }

                ProjectStore.UtcNow = () => new DateTime(2026, 10, 6, 8, 0, 0, DateTimeKind.Utc);
                using (ProjectStore store = ProjectStore.Open(path))
                {
                    ProjectBackup oldest = store.GetBackups().Last();
                    using (IProjectSession restored = store.RestoreBackup(oldest.Path))
                    {
                        Assert.IsNotNull(restored);
                        Assert.IsTrue(restored.GetBackups().Any(backup => Path.GetFileName(backup.Path).StartsWith("before-restore-", StringComparison.Ordinal)));
                    }
                }
            }
            finally
            {
                ProjectStore.UtcNow = originalClock;
                DeleteTemporaryProject(path);
            }
        }

        [TestMethod]
        public void Restore_rejects_a_backup_without_the_required_schema_before_replacing_the_project()
        {
            string path = TemporaryProjectPath();
            try
            {
                using (ProjectStore store = ProjectStore.Create(path))
                {
                    string invalidBackup = Path.Combine(Path.GetDirectoryName(path), "project.backups", "invalid.neurocle");
                    using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = invalidBackup, Pooling = false }.ToString()))
                    {
                        connection.Open();
                        using (var command = connection.CreateCommand())
                        {
                            command.CommandText = "CREATE TABLE SchemaInfo (Version INTEGER NOT NULL); INSERT INTO SchemaInfo (Version) VALUES (2);";
                            command.ExecuteNonQuery();
                        }
                    }

                    Assert.ThrowsException<InvalidDataException>(() => store.RestoreBackup(invalidBackup));
                    store.Save();
                }
            }
            finally { DeleteTemporaryProject(path); }
        }

        [TestMethod]
        public void Restore_rejects_a_backup_with_an_incomplete_image_record_schema_before_replacing_the_project()
        {
            string path = TemporaryProjectPath();
            try
            {
                using (ProjectStore store = ProjectStore.Create(path))
                {
                    string invalidBackup = Path.Combine(Path.GetDirectoryName(path), "project.backups", "invalid-image-record.neurocle");
                    using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = invalidBackup, Pooling = false }.ToString()))
                    {
                        connection.Open();
                        using (var command = connection.CreateCommand())
                        {
                            command.CommandText = "CREATE TABLE SchemaInfo (Version INTEGER NOT NULL); CREATE TABLE ProjectInfo (ProjectId TEXT NOT NULL, CreatedUtc TEXT NOT NULL); CREATE TABLE ImageRecord (Id TEXT PRIMARY KEY NOT NULL); INSERT INTO SchemaInfo (Version) VALUES (2); INSERT INTO ProjectInfo (ProjectId, CreatedUtc) VALUES ('id', 'time');";
                            command.ExecuteNonQuery();
                        }
                    }

                    Assert.ThrowsException<InvalidDataException>(() => store.RestoreBackup(invalidBackup));
                    store.Save();
                }
            }
            finally { DeleteTemporaryProject(path); }
        }

        [TestMethod]
        public void Image_list_projection_searches_and_sorts_ten_thousand_text_rows_without_creating_images()
        {
            ProjectImage[] images = Enumerable.Range(1, 10000)
                .Select(index => new ProjectImage(index.ToString(), "image" + index + ".png", "managed" + index + ".png", "hash", 1, 1, index))
                .Reverse().ToArray();

            IReadOnlyList<ProjectImage> byName = ImageListProjection.FilterAndSort(images, null, ImageListSortColumn.FileName, ImageListSortDirection.Ascending);
            Assert.AreEqual(10000, byName.Count);
            Assert.AreEqual("image1.png", byName[0].OriginalFileName);
            Assert.AreEqual("image10000.png", byName[9999].OriginalFileName);

            IReadOnlyList<ProjectImage> filtered = ImageListProjection.FilterAndSort(images, "IMAGE999", ImageListSortColumn.Sequence, ImageListSortDirection.Descending);
            CollectionAssert.AreEqual(new[] { "image9999.png", "image9998.png", "image9997.png", "image9996.png", "image9995.png", "image9994.png", "image9993.png", "image9992.png", "image9991.png", "image9990.png", "image999.png" }, filtered.Select(image => image.OriginalFileName).ToArray());
        }

        [TestMethod]
        public void Canvas_viewport_keeps_cursor_image_coordinate_and_clamps_zoom_at_all_supported_dpi_sizes()
        {
            foreach (float dpiScale in new[] { 1f, 1.25f, 1.5f, 1.75f, 2f })
            {
                var viewport = new CanvasViewport(new Size(5000, 3000), new Size((int)(1366 / dpiScale), (int)(768 / dpiScale)));
                var cursor = new PointF(200f, 150f);
                PointF before = viewport.ScreenToImage(cursor);
                viewport.SetZoomAt(2d, cursor);
                PointF after = viewport.ScreenToImage(cursor);
                Assert.AreEqual(before.X, after.X, 0.001f);
                Assert.AreEqual(before.Y, after.Y, 0.001f);
                Assert.IsFalse(viewport.IsFitted);
                viewport.SetZoomAt(100d, cursor);
                Assert.AreEqual(4d, viewport.Zoom, 0.0001d);
                viewport.SetZoomAt(0d, cursor);
                Assert.AreEqual(0.01d, viewport.Zoom, 0.0001d);
            }
        }

        [TestMethod]
        public void Canvas_viewport_refits_when_the_viewport_changes_while_fit_mode_is_selected()
        {
            var viewport = new CanvasViewport(new Size(1000, 1000), new Size(1000, 500));
            Assert.IsTrue(viewport.IsFitted);
            Assert.AreEqual(0.5d, viewport.Zoom, 0.0001d);
            viewport.SetViewportSize(new Size(1000, 1000));
            Assert.AreEqual(1d, viewport.Zoom, 0.0001d);
        }

        [TestMethod]
        public void Canvas_viewport_pan_exits_fit_mode()
        {
            var viewport = new CanvasViewport(new Size(1000, 1000), new Size(1000, 500));
            viewport.Pan(10f, 0f);
            Assert.IsFalse(viewport.IsFitted);
            viewport.SetViewportSize(new Size(1000, 1000));
            Assert.AreEqual(0.5d, viewport.Zoom, 0.0001d);
        }

        [TestMethod]
        public void Annotation_sets_classes_and_cla_assignments_are_isolated_and_persist_after_reopen()
        {
            string path = TemporaryProjectPath();
            string source = Path.Combine(Path.GetDirectoryName(path), "source.png");
            try
            {
                CreateBitmap(source, Color.Red, ImageFormat.Png);
                string setId;
                string classId;
                string imageId;
                using (var store = ProjectStore.Create(path))
                {
                    imageId = store.ImportFiles(new[] { source })[0].Id;
                    AnnotationSet first = store.CreateAnnotationSet("same", AnnotationSetType.Cla);
                    AnnotationSet second = store.CreateAnnotationSet("same", AnnotationSetType.Cla);
                    Assert.AreNotEqual(first.Id, second.Id);
                    Assert.AreEqual("class1", store.GetClasses(first.Id).Single().Name);
                    classId = store.AddClass(first.Id, "  cat  ", "#123456").Id;
                    Assert.ThrowsException<ArgumentException>(() => store.SetClaClass(first.Id, imageId, store.GetClasses(second.Id).Single().Id));
                    store.SetClaClass(first.Id, imageId, classId);
                    Assert.AreEqual(classId, store.GetClaClassId(first.Id, imageId));
                    Assert.IsNull(store.GetClaClassId(second.Id, imageId));
                    Assert.ThrowsException<InvalidOperationException>(() => store.DeleteClass(store.GetClasses(second.Id).Single().Id));
                    store.Save();
                    setId = first.Id;
                }
                using (var reopened = ProjectStore.Open(path))
                {
                    Assert.AreEqual(classId, reopened.GetClaClassId(setId, imageId));
                    Assert.AreEqual("cat", reopened.GetClasses(setId).Single(value => value.Id == classId).Name);
                    Assert.IsFalse(reopened.DeleteAnnotationSet(setId, "SAME"));
                    Assert.IsTrue(reopened.DeleteAnnotationSet(setId, "same"));
                    Assert.AreEqual(0, reopened.GetClasses(setId).Count);
                    Assert.IsNull(reopened.GetClaClassId(setId, imageId));
                }
            }
            finally { DeleteTemporaryProject(path); if (File.Exists(source)) File.Delete(source); }
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

        private static string CreateBitmap(string path, Color color)
        {
            return CreateBitmap(path, color, ImageFormat.Bmp);
        }

        private static string CreateBitmap(string path, Color color, ImageFormat format)
        {
            return CreateBitmap(path, color, format, 3, 2);
        }

        private static string CreateBitmap(string path, Color color, ImageFormat format, int width, int height)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using (var bitmap = new Bitmap(width, height))
            {
                bitmap.SetPixel(0, 0, color);
                bitmap.Save(path, format);
            }
            return path;
        }

        private static void CreateVersionOneProject(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()))
            {
                connection.Open();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "CREATE TABLE SchemaInfo (Version INTEGER NOT NULL); CREATE TABLE ProjectInfo (ProjectId TEXT NOT NULL, CreatedUtc TEXT NOT NULL); INSERT INTO SchemaInfo (Version) VALUES (1); INSERT INTO ProjectInfo (ProjectId, CreatedUtc) VALUES ('legacy', '2026-01-01T00:00:00.0000000Z');";
                    command.ExecuteNonQuery();
                }
            }
        }
    }
}

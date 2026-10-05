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
                        Assert.AreEqual(7L, (long)command.ExecuteScalar());
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
        public void Opening_a_version_four_project_preserves_existing_annotations_and_adds_seg_storage()
        {
            string path = TemporaryProjectPath();
            try
            {
                CreateVersionFourProject(path);
                using (var store = ProjectStore.Open(path))
                {
                    Assert.AreEqual("class", store.GetClaClassId("cla-set", "image"));
                    ObdAnnotation annotation = store.GetObdAnnotations("obd-set", "image").Single();
                    Assert.AreEqual("obd-class", annotation.ClassId);
                    Assert.AreEqual(1, annotation.X);
                    Assert.AreEqual(5, annotation.Width);
                    Assert.AreEqual(0, store.GetSegRegions("seg-set", "image").Count);
                }

                string backupDirectory = Path.Combine(Path.GetDirectoryName(path), "project.backups");
                Assert.IsTrue(Directory.GetFiles(backupDirectory, "before-migration-v4*.neurocle").Any());
                using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()))
                {
                    connection.Open();
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = "SELECT Version FROM SchemaInfo;";
                        Assert.AreEqual(7L, (long)command.ExecuteScalar());
                        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE Type = 'table' AND Name = 'SegRegion';";
                        Assert.AreEqual(1L, (long)command.ExecuteScalar());
                        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE Type = 'table' AND Name = 'DatasetAssignment';";
                        Assert.AreEqual(1L, (long)command.ExecuteScalar());
                    }
                }
            }
            finally { DeleteTemporaryProject(path); }
        }

        [TestMethod]
        public void Opening_a_version_five_project_preserves_existing_seg_regions_and_adds_path_storage()
        {
            string path = TemporaryProjectPath();
            try
            {
                CreateVersionFiveProject(path);
                using (var store = ProjectStore.Open(path))
                {
                    SegRegion region = store.GetSegRegions("seg-set", "image").Single();
                    Assert.AreEqual(SegRegionType.Rect, region.Type);
                    Assert.AreEqual(3, region.X);
                    Assert.AreEqual(4, region.Y);
                    Assert.AreEqual(0, region.StrokeWidth);
                    Assert.AreEqual(0, region.Points.Count);
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

        [TestMethod]
        public void Annotation_history_is_isolated_restores_deleted_classes_and_is_cleared_after_reopen()
        {
            string path = TemporaryProjectPath();
            string source = Path.Combine(Path.GetDirectoryName(path), "source.png");
            try
            {
                CreateBitmap(source, Color.Red, ImageFormat.Png);
                string firstSetId;
                string secondSetId;
                string imageId;
                string deletedClassId;
                using (var store = ProjectStore.Create(path))
                {
                    imageId = store.ImportFiles(new[] { source })[0].Id;
                    AnnotationSet first = store.CreateAnnotationSet("first", AnnotationSetType.Cla);
                    AnnotationSet second = store.CreateAnnotationSet("second", AnnotationSetType.Cla);
                    firstSetId = first.Id;
                    secondSetId = second.Id;
                    AnnotationClass firstClass = store.AddClass(first.Id, "cat", "#123456");
                    AnnotationClass secondClass = store.AddClass(second.Id, "dog", "#654321");

                    store.SetClaClass(first.Id, imageId, firstClass.Id);
                    store.SetClaClass(second.Id, imageId, secondClass.Id);
                    Assert.IsTrue(store.CanUndo(first.Id));
                    Assert.IsTrue(store.CanUndo(second.Id));
                    Assert.IsTrue(store.Undo(first.Id));
                    Assert.IsNull(store.GetClaClassId(first.Id, imageId));
                    Assert.AreEqual(secondClass.Id, store.GetClaClassId(second.Id, imageId));
                    Assert.IsTrue(store.CanRedo(first.Id));
                    Assert.IsTrue(store.Redo(first.Id));
                    Assert.AreEqual(firstClass.Id, store.GetClaClassId(first.Id, imageId));

                    deletedClassId = firstClass.Id;
                    store.DeleteClass(firstClass.Id);
                    Assert.IsNull(store.GetClaClassId(first.Id, imageId));
                    Assert.AreEqual(1, store.GetClasses(first.Id).Count);
                    Assert.IsTrue(store.Undo(first.Id));
                    Assert.AreEqual(deletedClassId, store.GetClaClassId(first.Id, imageId));
                    Assert.AreEqual("cat", store.GetClasses(first.Id).Single(item => item.Id == deletedClassId).Name);
                    Assert.IsTrue(store.Redo(first.Id));
                    Assert.IsNull(store.GetClaClassId(first.Id, imageId));

                    store.Undo(first.Id);
                    store.SetClaClass(first.Id, imageId, null);
                    Assert.IsFalse(store.CanRedo(first.Id));
                    store.Save();
                }

                using (var reopened = ProjectStore.Open(path))
                {
                    Assert.IsFalse(reopened.CanUndo(firstSetId));
                    Assert.IsFalse(reopened.CanRedo(firstSetId));
                    Assert.IsFalse(reopened.CanUndo(secondSetId));
                    Assert.IsNull(reopened.GetClaClassId(firstSetId, imageId));
                }
            }
            finally { DeleteTemporaryProject(path); if (File.Exists(source)) File.Delete(source); }
        }

        [TestMethod]
        public void Annotation_history_retains_only_the_most_recent_one_hundred_actions()
        {
            string path = TemporaryProjectPath();
            try
            {
                using (var store = ProjectStore.Create(path))
                {
                    AnnotationSet set = store.CreateAnnotationSet("set", AnnotationSetType.Cla);
                    AnnotationClass value = store.AddClass(set.Id, "start", "#123456");
                    for (int index = 1; index <= 101; index++) store.RenameClass(value.Id, "name" + index);

                    for (int index = 0; index < 100; index++) Assert.IsTrue(store.Undo(set.Id));
                    Assert.AreEqual("name1", store.GetClasses(set.Id).Single(item => item.Id == value.Id).Name);
                    Assert.IsFalse(store.CanUndo(set.Id));
                }
            }
            finally { DeleteTemporaryProject(path); }
        }

        [TestMethod]
        public void Permanently_deleting_an_image_clears_annotation_history_without_replaying_stale_assignments()
        {
            string path = TemporaryProjectPath();
            string source = Path.Combine(Path.GetDirectoryName(path), "source.png");
            try
            {
                CreateBitmap(source, Color.Red, ImageFormat.Png);
                using (var store = ProjectStore.Create(path))
                {
                    ProjectImage image = store.ImportFiles(new[] { source })[0];
                    AnnotationSet set = store.CreateAnnotationSet("set", AnnotationSetType.Cla);
                    AnnotationClass value = store.AddClass(set.Id, "cat", "#123456");
                    store.SetClaClass(set.Id, image.Id, value.Id);
                    store.DeleteClass(value.Id);
                    store.DeleteImages(new[] { image.Id });

                    Assert.IsFalse(store.CanUndo(set.Id));
                    Assert.IsFalse(store.Undo(set.Id));
                    Assert.AreEqual(1, store.GetClasses(set.Id).Count);
                }
            }
            finally { DeleteTemporaryProject(path); if (File.Exists(source)) File.Delete(source); }
        }

        [TestMethod]
        public void Obd_annotations_are_bounded_classed_undoable_and_persist_after_reopen()
        {
            string path = TemporaryProjectPath();
            string source = Path.Combine(Path.GetDirectoryName(path), "source.png");
            try
            {
                CreateBitmap(source, Color.Red, ImageFormat.Png, 20, 10);
                string setId;
                string imageId;
                string annotationId;
                using (var store = ProjectStore.Create(path))
                {
                    imageId = store.ImportFiles(new[] { source })[0].Id;
                    AnnotationSet set = store.CreateAnnotationSet("boxes", AnnotationSetType.Obd);
                    setId = set.Id;
                    AnnotationClass first = store.GetClasses(set.Id).Single();
                    AnnotationClass second = store.AddClass(set.Id, "other", "#123456");
                    ObdAnnotation added = store.AddObdAnnotation(set.Id, imageId, first.Id, 1, 2, 5, 4);
                    annotationId = added.Id;
                    Assert.AreEqual(0, added.ZOrder);
                    Assert.ThrowsException<ArgumentOutOfRangeException>(() => store.AddObdAnnotation(set.Id, imageId, first.Id, 16, 2, 5, 4));
                    store.UpdateObdAnnotation(added.Id, second.Id, 2, 1, 8, 6);
                    ObdAnnotation updated = store.GetObdAnnotations(set.Id, imageId).Single();
                    Assert.AreEqual(second.Id, updated.ClassId);
                    Assert.AreEqual(2, updated.X);
                    Assert.AreEqual(6, updated.Height);
                    Assert.IsTrue(store.Undo(set.Id));
                    Assert.AreEqual(first.Id, store.GetObdAnnotations(set.Id, imageId).Single().ClassId);
                    Assert.IsTrue(store.Redo(set.Id));
                    store.DeleteObdAnnotation(added.Id);
                    Assert.AreEqual(0, store.GetObdAnnotations(set.Id, imageId).Count);
                    Assert.IsTrue(store.Undo(set.Id));
                    Assert.AreEqual(annotationId, store.GetObdAnnotations(set.Id, imageId).Single().Id);
                    store.Save();
                }
                using (var reopened = ProjectStore.Open(path))
                {
                    ObdAnnotation annotation = reopened.GetObdAnnotations(setId, imageId).Single();
                    Assert.AreEqual(annotationId, annotation.Id);
                    Assert.AreEqual(2, annotation.X);
                    Assert.AreEqual(8, annotation.Width);
                    Assert.AreEqual(6, annotation.Height);
                }
            }
            finally { DeleteTemporaryProject(path); if (File.Exists(source)) File.Delete(source); }
        }

        [TestMethod]
        public void Seg_rectangles_and_ellipses_are_bounded_undoable_and_persist_after_reopen()
        {
            string path = TemporaryProjectPath();
            string source = Path.Combine(Path.GetDirectoryName(path), "source.png");
            try
            {
                CreateBitmap(source, Color.Red, ImageFormat.Png, 20, 10);
                string setId;
                string imageId;
                string rectangleId;
                string ellipseId;
                using (var store = ProjectStore.Create(path))
                {
                    imageId = store.ImportFiles(new[] { source })[0].Id;
                    AnnotationSet set = store.CreateAnnotationSet("regions", AnnotationSetType.Seg);
                    setId = set.Id;
                    AnnotationClass value = store.GetClasses(set.Id).Single();
                    SegRegion rectangle = store.AddSegRegion(set.Id, imageId, value.Id, SegRegionType.Rect, 1, 2, 5, 4);
                    SegRegion ellipse = store.AddSegRegion(set.Id, imageId, value.Id, SegRegionType.Ellipse, 10, 5, 3, 2);
                    rectangleId = rectangle.Id;
                    ellipseId = ellipse.Id;

                    Assert.AreEqual(SegRegionType.Rect, rectangle.Type);
                    Assert.AreEqual(0, rectangle.ZOrder);
                    Assert.AreEqual(SegRegionType.Ellipse, ellipse.Type);
                    Assert.AreEqual(1, ellipse.ZOrder);
                    Assert.ThrowsException<ArgumentOutOfRangeException>(() => store.AddSegRegion(set.Id, imageId, value.Id, SegRegionType.Rect, 16, 2, 5, 4));
                    Assert.ThrowsException<ArgumentOutOfRangeException>(() => store.AddSegRegion(set.Id, imageId, value.Id, SegRegionType.Ellipse, 18, 5, 3, 2));

                    Assert.IsTrue(store.Undo(set.Id));
                    Assert.AreEqual(rectangleId, store.GetSegRegions(set.Id, imageId).Single().Id);
                    Assert.IsTrue(store.Redo(set.Id));
                    Assert.AreEqual(2, store.GetSegRegions(set.Id, imageId).Count);
                    store.Save();
                }
                using (var reopened = ProjectStore.Open(path))
                {
                    SegRegion[] regions = reopened.GetSegRegions(setId, imageId).ToArray();
                    Assert.AreEqual(rectangleId, regions[0].Id);
                    Assert.AreEqual(ellipseId, regions[1].Id);
                    Assert.AreEqual(10, regions[1].X);
                    Assert.AreEqual(3, regions[1].Width);
                    Assert.AreEqual(2, regions[1].Height);
                }
            }
            finally { DeleteTemporaryProject(path); if (File.Exists(source)) File.Delete(source); }
        }

        [TestMethod]
        public void Seg_paths_preserve_points_width_and_undo_after_reopen()
        {
            string path = TemporaryProjectPath();
            string source = Path.Combine(Path.GetDirectoryName(path), "source.png");
            try
            {
                CreateBitmap(source, Color.Red, ImageFormat.Png, 100, 100);
                string setId;
                string imageId;
                string brushId;
                using (var store = ProjectStore.Create(path))
                {
                    imageId = store.ImportFiles(new[] { source })[0].Id;
                    AnnotationSet set = store.CreateAnnotationSet("paths", AnnotationSetType.Seg);
                    setId = set.Id;
                    AnnotationClass value = store.GetClasses(set.Id).Single();
                    SegRegion brush = store.AddSegPath(set.Id, imageId, value.Id, SegRegionType.PolyLine, new[] { new SegPoint(10, 10), new SegPoint(20, 10), new SegPoint(20, 20) }, 15);
                    brushId = brush.Id;
                    store.AddSegPath(set.Id, imageId, value.Id, SegRegionType.PolyGon, new[] { new SegPoint(1, 1), new SegPoint(5, 1), new SegPoint(3, 5) }, 0);
                    Assert.AreEqual(15, brush.StrokeWidth);
                    Assert.AreEqual(3, brush.Points.Count);
                    Assert.ThrowsException<ArgumentException>(() => store.AddSegPath(set.Id, imageId, value.Id, SegRegionType.PolyGon, new[] { new SegPoint(1, 1), new SegPoint(2, 2), new SegPoint(3, 3) }, 0));
                    Assert.ThrowsException<ArgumentOutOfRangeException>(() => store.AddSegPath(set.Id, imageId, value.Id, SegRegionType.PolyLine, new[] { new SegPoint(1, 1), new SegPoint(2, 2) }, 101));
                    Assert.IsTrue(store.Undo(set.Id));
                    Assert.AreEqual(brushId, store.GetSegRegions(set.Id, imageId).Single().Id);
                    Assert.IsTrue(store.Redo(set.Id));
                    store.Save();
                }
                using (var reopened = ProjectStore.Open(path))
                {
                    SegRegion restored = reopened.GetSegRegions(setId, imageId).Single(item => item.Id == brushId);
                    Assert.AreEqual(SegRegionType.PolyLine, restored.Type);
                    Assert.AreEqual(15, restored.StrokeWidth);
                    Assert.AreEqual(20, restored.Points[2].X);
                    Assert.AreEqual(20, restored.Points[2].Y);
                    Assert.ThrowsException<ArgumentOutOfRangeException>(() => reopened.AddSegPath(setId, imageId, reopened.GetClasses(setId).Single().Id, SegRegionType.PolyLine, new[] { new SegPoint(7, 10), new SegPoint(20, 10) }, 15));
                }
            }
            finally { DeleteTemporaryProject(path); if (File.Exists(source)) File.Delete(source); }
        }

        [TestMethod]
        public void New_same_class_seg_regions_union_only_when_their_areas_overlap()
        {
            string path = TemporaryProjectPath();
            string source = Path.Combine(Path.GetDirectoryName(path), "source.png");
            try
            {
                CreateBitmap(source, Color.Red, ImageFormat.Png, 100, 100);
                using (var store = ProjectStore.Create(path))
                {
                    ProjectImage image = store.ImportFiles(new[] { source })[0];
                    AnnotationSet set = store.CreateAnnotationSet("regions", AnnotationSetType.Seg);
                    AnnotationClass first = store.GetClasses(set.Id).Single();
                    AnnotationClass second = store.AddClass(set.Id, "other", "#123456");
                    store.AddSegRegion(set.Id, image.Id, first.Id, SegRegionType.Rect, 10, 10, 20, 20);
                    store.AddSegRegion(set.Id, image.Id, first.Id, SegRegionType.Rect, 20, 10, 20, 20);
                    Assert.AreEqual(1, store.GetSegRegions(set.Id, image.Id).Count(item => item.ClassId == first.Id));
                    Assert.AreEqual(SegRegionType.PolyGon, store.GetSegRegions(set.Id, image.Id).Single(item => item.ClassId == first.Id).Type);

                    store.AddSegRegion(set.Id, image.Id, second.Id, SegRegionType.Rect, 20, 10, 20, 20);
                    Assert.AreEqual(2, store.GetSegRegions(set.Id, image.Id).Count);
                    store.AddSegRegion(set.Id, image.Id, first.Id, SegRegionType.Rect, 50, 10, 10, 10);
                    store.AddSegRegion(set.Id, image.Id, first.Id, SegRegionType.Rect, 60, 10, 10, 10);
                    Assert.AreEqual(3, store.GetSegRegions(set.Id, image.Id).Count(item => item.ClassId == first.Id));
                }
            }
            finally { DeleteTemporaryProject(path); if (File.Exists(source)) File.Delete(source); }
        }

        [TestMethod]
        public void Seg_geometry_at_the_right_and_bottom_edges_keeps_its_outer_boundary_after_union_and_erase()
        {
            string path = TemporaryProjectPath();
            string source = Path.Combine(Path.GetDirectoryName(path), "source.png");
            try
            {
                CreateBitmap(source, Color.Red, ImageFormat.Png, 100, 100);
                using (var store = ProjectStore.Create(path))
                {
                    ProjectImage image = store.ImportFiles(new[] { source })[0];
                    AnnotationSet set = store.CreateAnnotationSet("regions", AnnotationSetType.Seg);
                    AnnotationClass value = store.GetClasses(set.Id).Single();
                    store.AddSegRegion(set.Id, image.Id, value.Id, SegRegionType.Rect, 70, 70, 30, 30);
                    store.AddSegRegion(set.Id, image.Id, value.Id, SegRegionType.Rect, 80, 80, 20, 20);
                    SegRegion union = store.GetSegRegions(set.Id, image.Id).Single();
                    Assert.AreEqual(100, union.Points.Max(point => point.X));
                    Assert.AreEqual(100, union.Points.Max(point => point.Y));
                    store.EraseSegPath(set.Id, image.Id, value.Id, new[] { new SegPoint(85, 75), new SegPoint(85, 95) }, 4);
                    Assert.IsTrue(store.GetSegRegions(set.Id, image.Id).Where(region => region.ClassId == value.Id).SelectMany(region => region.Points).Any(point => point.X == 100 || point.Y == 100));
                }
            }
            finally { DeleteTemporaryProject(path); if (File.Exists(source)) File.Delete(source); }
        }

        [TestMethod]
        public void Seg_eraser_only_changes_the_selected_class_and_preserves_holes_and_components()
        {
            string path = TemporaryProjectPath();
            string source = Path.Combine(Path.GetDirectoryName(path), "source.png");
            try
            {
                CreateBitmap(source, Color.Red, ImageFormat.Png, 100, 100);
                using (var store = ProjectStore.Create(path))
                {
                    ProjectImage image = store.ImportFiles(new[] { source })[0];
                    AnnotationSet set = store.CreateAnnotationSet("regions", AnnotationSetType.Seg);
                    AnnotationClass first = store.GetClasses(set.Id).Single();
                    AnnotationClass second = store.AddClass(set.Id, "other", "#123456");
                    store.AddSegRegion(set.Id, image.Id, first.Id, SegRegionType.Rect, 10, 10, 40, 40);
                    store.AddSegRegion(set.Id, image.Id, second.Id, SegRegionType.Rect, 10, 10, 40, 40);
                    store.EraseSegPath(set.Id, image.Id, first.Id, new[] { new SegPoint(30, 10), new SegPoint(30, 50) }, 12);
                    SegRegion[] firstRegions = store.GetSegRegions(set.Id, image.Id).Where(item => item.ClassId == first.Id).ToArray();
                    Assert.IsTrue(firstRegions.Length >= 2);
                    Assert.AreEqual(1, store.GetSegRegions(set.Id, image.Id).Count(item => item.ClassId == second.Id));
                    Assert.IsTrue(store.Undo(set.Id));
                    Assert.AreEqual(1, store.GetSegRegions(set.Id, image.Id).Count(item => item.ClassId == first.Id));
                    store.EraseSegPath(set.Id, image.Id, first.Id, new[] { new SegPoint(30, 20), new SegPoint(30, 40) }, 12);
                    SegRegion hole = store.GetSegRegions(set.Id, image.Id).Single(item => item.ClassId == first.Id);
                    Assert.IsTrue(hole.Points.GroupBy(point => point.X + ":" + point.Y).Any(group => group.Count() > 1));
                }
            }
            finally { DeleteTemporaryProject(path); if (File.Exists(source)) File.Delete(source); }
        }

        [TestMethod]
        public void Dataset_assignments_are_isolated_batch_undoable_and_default_to_unassigned_retest_zero()
        {
            string path = TemporaryProjectPath();
            string firstSource = Path.Combine(Path.GetDirectoryName(path), "one.png");
            string secondSource = Path.Combine(Path.GetDirectoryName(path), "two.png");
            try
            {
                CreateBitmap(firstSource, Color.Red, ImageFormat.Png, 20, 20);
                CreateBitmap(secondSource, Color.Blue, ImageFormat.Png, 20, 20);
                using (var store = ProjectStore.Create(path))
                {
                    ProjectImage[] images = store.ImportFiles(new[] { firstSource, secondSource }).ToArray();
                    AnnotationSet first = store.CreateAnnotationSet("first", AnnotationSetType.Cla);
                    AnnotationSet second = store.CreateAnnotationSet("second", AnnotationSetType.Obd);
                    Assert.IsTrue(store.GetDatasetAssignments(first.Id).All(value => value.Status == DatasetAssignmentStatus.Unassigned && !value.Retest));
                    store.SetDatasetAssignments(first.Id, images.Select(value => value.Id), DatasetAssignmentStatus.Train);
                    store.SetRetest(first.Id, new[] { images[0].Id }, true);
                    Assert.IsTrue(store.GetDatasetAssignments(first.Id).All(value => value.Status == DatasetAssignmentStatus.Train));
                    Assert.IsTrue(store.GetDatasetAssignments(first.Id).Single(value => value.ImageId == images[0].Id).Retest);
                    Assert.IsTrue(store.GetDatasetAssignments(second.Id).All(value => value.Status == DatasetAssignmentStatus.Unassigned && !value.Retest));
                    Assert.IsTrue(store.Undo(first.Id));
                    Assert.IsFalse(store.GetDatasetAssignments(first.Id).Single(value => value.ImageId == images[0].Id).Retest);
                    Assert.IsTrue(store.Undo(first.Id));
                    Assert.IsTrue(store.GetDatasetAssignments(first.Id).All(value => value.Status == DatasetAssignmentStatus.Unassigned));
                    Assert.IsFalse(store.CanUndo(first.Id));
                    store.SetDatasetAssignments(first.Id, images.Select(value => value.Id), DatasetAssignmentStatus.Unassigned);
                    store.SetRetest(first.Id, images.Select(value => value.Id), false);
                    Assert.IsFalse(store.CanUndo(first.Id));
                }
            }
            finally { DeleteTemporaryProject(path); if (File.Exists(firstSource)) File.Delete(firstSource); if (File.Exists(secondSource)) File.Delete(secondSource); }
        }

        [TestMethod]
        public void Image_annotation_summaries_are_batched_and_include_every_distinct_class_on_an_image()
        {
            string path = TemporaryProjectPath();
            string source = Path.Combine(Path.GetDirectoryName(path), "source.png");
            try
            {
                CreateBitmap(source, Color.Red, ImageFormat.Png, 20, 20);
                using (var store = ProjectStore.Create(path))
                {
                    ProjectImage image = store.ImportFiles(new[] { source })[0];
                    AnnotationSet obd = store.CreateAnnotationSet("boxes", AnnotationSetType.Obd);
                    AnnotationClass first = store.GetClasses(obd.Id).Single();
                    AnnotationClass second = store.AddClass(obd.Id, "other", "#123456");
                    store.AddObdAnnotation(obd.Id, image.Id, first.Id, 1, 1, 5, 5);
                    store.AddObdAnnotation(obd.Id, image.Id, second.Id, 10, 10, 5, 5);
                    store.AddObdAnnotation(obd.Id, image.Id, first.Id, 2, 2, 3, 3);

                    ImageAnnotationSummary summary = store.GetImageAnnotationSummaries(obd.Id).Single();
                    CollectionAssert.AreEquivalent(new[] { first.Id, second.Id }, summary.ClassIds.ToArray());
                    Assert.IsFalse(store.GetImageAnnotationSummaries(store.CreateAnnotationSet("empty", AnnotationSetType.Seg).Id).Any());
                }
            }
            finally { DeleteTemporaryProject(path); if (File.Exists(source)) File.Delete(source); }
        }

        [TestMethod]
        public void Dataset_split_planner_preserves_ten_train_images_per_class_when_possible()
        {
            DatasetSplitCandidate[] values = Enumerable.Range(1, 15).Select(index => new DatasetSplitCandidate(index.ToString(), new[] { "class" }, true)).ToArray();
            DatasetSplitPlan plan = DatasetSplitPlanner.Create(values, 80, maximum => 0);
            Assert.AreEqual(12, plan.TrainImageIds.Count);
            Assert.AreEqual(3, plan.TestImageIds.Count);
            Assert.AreEqual(0, plan.Warnings.Count);
        }

        [TestMethod]
        public void Json_annotation_import_is_atomic_and_persists_obd_data()
        {
            string path = TemporaryProjectPath();
            string source = Path.Combine(Path.GetDirectoryName(path), "sample.png");
            try
            {
                CreateBitmap(source, Color.Red, ImageFormat.Png, 20, 20);
                using (var store = ProjectStore.Create(path))
                {
                    ProjectImage image = store.ImportFiles(new[] { source }).Single();
                    string valid = "{\"label_type\":\"obd\",\"source\":\"labelset\",\"time\":\"2026-01-01T00:00:00Z\",\"version\":\"4.5.2.2\",\"classes\":[{\"name\":\"part\",\"color\":\"rgba(1, 2, 3, 1)-custom\"}],\"data\":[{\"fileName\":\"sample.png\",\"set\":\"train\",\"classLabel\":\"\",\"regionLabel\":[{\"className\":\"part\",\"type\":\"Rect\",\"x\":1,\"y\":2,\"width\":3,\"height\":4}],\"retestset\":1,\"rotation_angle\":0,\"width\":20,\"height\":20}]}";
                    AnnotationSet set = store.ImportAnnotationJson(valid, "imported", false);
                    Assert.AreEqual(AnnotationSetType.Obd, set.Type);
                    Assert.AreEqual("rgba(1, 2, 3, 1)-custom", store.GetClasses(set.Id).Single().Color);
                    Assert.AreEqual(1, store.GetObdAnnotations(set.Id, image.Id).Count);
                    DatasetAssignment assignment = store.GetDatasetAssignments(set.Id).Single(value => value.ImageId == image.Id);
                    Assert.AreEqual(DatasetAssignmentStatus.Train, assignment.Status);
                    Assert.IsTrue(assignment.Retest);
                    string invalid = valid.Replace("sample.png", "missing.png");
                    Assert.ThrowsException<JsonAnnotationImportException>(() => store.ImportAnnotationJson(invalid, "bad", false));
                    Assert.AreEqual(1, store.GetAnnotationSets().Count);
                }
            }
            finally { DeleteTemporaryProject(path); if (File.Exists(source)) File.Delete(source); }
        }

        [TestMethod]
        public void Json_annotation_import_preserves_seg_path_points()
        {
            string path = TemporaryProjectPath();
            string source = Path.Combine(Path.GetDirectoryName(path), "sample.png");
            try
            {
                CreateBitmap(source, Color.Red, ImageFormat.Png, 20, 20);
                using (var store = ProjectStore.Create(path))
                {
                    ProjectImage image = store.ImportFiles(new[] { source }).Single();
                    string json = "{\"label_type\":\"seg\",\"source\":\"labelset\",\"time\":\"2026-01-01T00:00:00Z\",\"version\":\"4.5.2.2\",\"classes\":[{\"name\":\"part\",\"color\":\"red\"}],\"data\":[{\"fileName\":\"sample.png\",\"set\":\"\",\"classLabel\":\"\",\"regionLabel\":[{\"className\":\"part\",\"type\":\"PolyLine\",\"strokeWidth\":2,\"points\":[[1,2],[3,4]]}],\"retestset\":0,\"rotation_angle\":0,\"width\":20,\"height\":20}]}";
                    AnnotationSet set = store.ImportAnnotationJson(json, "seg", false);
                    SegRegion region = store.GetSegRegions(set.Id, image.Id).Single();
                    Assert.AreEqual(SegRegionType.PolyLine, region.Type);
                    Assert.AreEqual(2, region.StrokeWidth);
                    CollectionAssert.AreEqual(new[] { 1, 3 }, region.Points.Select(value => value.X).ToArray());
                    CollectionAssert.AreEqual(new[] { 2, 4 }, region.Points.Select(value => value.Y).ToArray());
                }
            }
            finally { DeleteTemporaryProject(path); if (File.Exists(source)) File.Delete(source); }
        }

        [TestMethod]
        public void Json_annotation_import_normalizes_class_references_and_accepts_polygon_image_edges()
        {
            string path = TemporaryProjectPath();
            string source = Path.Combine(Path.GetDirectoryName(path), "sample.png");
            try
            {
                CreateBitmap(source, Color.Red, ImageFormat.Png, 20, 20);
                using (var store = ProjectStore.Create(path))
                {
                    ProjectImage image = store.ImportFiles(new[] { source }).Single();
                    string obd = "{\"label_type\":\"obd\",\"source\":\"labelset\",\"time\":\"2026-01-01T00:00:00Z\",\"version\":\"4.5.2.2\",\"classes\":[{\"name\":\" part \",\"color\":\"red\"}],\"data\":[{\"fileName\":\"sample.png\",\"set\":\"\",\"classLabel\":\"\",\"regionLabel\":[{\"className\":\" part \",\"type\":\"Rect\",\"x\":1,\"y\":1,\"width\":2,\"height\":2}],\"retestset\":0,\"rotation_angle\":0,\"width\":20,\"height\":20}]}";
                    AnnotationSet obdSet = store.ImportAnnotationJson(obd, "obd", false);
                    Assert.AreEqual("part", store.GetClasses(obdSet.Id).Single().Name);
                    string seg = "{\"label_type\":\"seg\",\"source\":\"labelset\",\"time\":\"2026-01-01T00:00:00Z\",\"version\":\"4.5.2.2\",\"classes\":[{\"name\":\"part\",\"color\":\"red\"}],\"data\":[{\"fileName\":\"sample.png\",\"set\":\"\",\"classLabel\":\"\",\"regionLabel\":[{\"className\":\"part\",\"type\":\"PolyGon\",\"points\":[[0,0],[20,0],[20,20],[0,20]]}],\"retestset\":0,\"rotation_angle\":0,\"width\":20,\"height\":20}]}";
                    AnnotationSet segSet = store.ImportAnnotationJson(seg, "seg", false);
                    Assert.AreEqual(4, store.GetSegRegions(segSet.Id, image.Id).Single().Points.Count);
                }
            }
            finally { DeleteTemporaryProject(path); if (File.Exists(source)) File.Delete(source); }
        }

        [TestMethod]
        public void Json_annotation_import_rejects_unknown_nested_fields_until_explicitly_allowed()
        {
            string path = TemporaryProjectPath();
            string source = Path.Combine(Path.GetDirectoryName(path), "sample.png");
            try
            {
                CreateBitmap(source, Color.Red, ImageFormat.Png, 20, 20);
                using (var store = ProjectStore.Create(path))
                {
                    store.ImportFiles(new[] { source });
                    string json = "{\"label_type\":\"obd\",\"source\":\"labelset\",\"time\":\"2026-01-01T00:00:00Z\",\"version\":\"4.5.2.2\",\"classes\":[{\"name\":\"part\",\"color\":\"red\"}],\"data\":[{\"fileName\":\"sample.png\",\"set\":\"\",\"classLabel\":\"\",\"regionLabel\":[{\"className\":\"part\",\"type\":\"Rect\",\"x\":1,\"y\":1,\"width\":2,\"height\":2,\"futureField\":true}],\"retestset\":0,\"rotation_angle\":0,\"width\":20,\"height\":20}]}";
                    Assert.ThrowsException<JsonAnnotationImportException>(() => store.ImportAnnotationJson(json, "strict", false));
                    Assert.AreEqual(0, store.GetAnnotationSets().Count);
                    AnnotationSet set = store.ImportAnnotationJson(json, "allowed", true);
                    Assert.AreEqual(1, store.GetObdAnnotations(set.Id, store.GetImages().Single().Id).Count);
                }
            }
            finally { DeleteTemporaryProject(path); if (File.Exists(source)) File.Delete(source); }
        }

        [TestMethod]
        public void Provided_annotation_json_samples_parse_for_all_annotation_types()
        {
            string labelsDirectory = FindRepositoryLabelsDirectory();
            ImportedAnnotationSet cla = JsonAnnotationImport.Parse(File.ReadAllText(Path.Combine(labelsDirectory, "cla_labels.json")));
            ImportedAnnotationSet obd = JsonAnnotationImport.Parse(File.ReadAllText(Path.Combine(labelsDirectory, "obd_labels.json")));
            ImportedAnnotationSet seg = JsonAnnotationImport.Parse(File.ReadAllText(Path.Combine(labelsDirectory, "seg_labels.json")));
            Assert.AreEqual(AnnotationSetType.Cla, cla.Type);
            Assert.AreEqual(AnnotationSetType.Obd, obd.Type);
            Assert.AreEqual(AnnotationSetType.Seg, seg.Type);
            Assert.IsTrue(seg.Data.Any(image => image.Regions.Any(region => region.Type == SegRegionType.PolyLine)));
        }

        [TestMethod]
        public void Dataset_split_planner_stratifies_annotated_images_before_empty_images()
        {
            DatasetSplitCandidate[] values = Enumerable.Range(1, 12).Select(index => new DatasetSplitCandidate("a" + index, new[] { "a" }, true))
                .Concat(Enumerable.Range(1, 2).Select(index => new DatasetSplitCandidate("b" + index, new[] { "b" }, true)))
                .Concat(Enumerable.Range(1, 5).Select(index => new DatasetSplitCandidate("empty" + index, new string[0], true))).ToArray();
            DatasetSplitPlan plan = DatasetSplitPlanner.Create(values, 80, maximum => 0);
            Assert.AreEqual(4, plan.TestImageIds.Count);
            Assert.IsTrue(plan.TestImageIds.Any(value => value.StartsWith("b", StringComparison.Ordinal)));
            Assert.IsFalse(plan.TestImageIds.Any(value => value.StartsWith("empty", StringComparison.Ordinal)));
            Assert.IsTrue(plan.Warnings.Count > 0);
        }

        [TestMethod]
        public void Auto_split_preserves_retest_and_missing_image_assignment()
        {
            string path = TemporaryProjectPath();
            string source = Path.Combine(Path.GetDirectoryName(path), "source.png");
            try
            {
                CreateBitmap(source, Color.Red, ImageFormat.Png, 20, 20);
                using (var store = ProjectStore.Create(path))
                {
                    ProjectImage[] images = store.ImportFiles(Enumerable.Range(1, 4).Select(index => source)).ToArray();
                    AnnotationSet set = store.CreateAnnotationSet("set", AnnotationSetType.Cla);
                    store.SetRetest(set.Id, new[] { images[0].Id }, true);
                    store.SetDatasetAssignments(set.Id, new[] { images[3].Id }, DatasetAssignmentStatus.NotUsed);
                    File.Delete(Path.Combine(store.ImagesDirectory, images[3].ManagedFileName));
                    DatasetAssignment[] before = store.GetDatasetAssignments(set.Id).ToArray();
                    Func<int, int> original = ProjectStore.NextRandom;
                    try { ProjectStore.NextRandom = maximum => 0; store.AutoSplitDataset(set.Id, 80); }
                    finally { ProjectStore.NextRandom = original; }
                    DatasetAssignment[] assignments = store.GetDatasetAssignments(set.Id).ToArray();
                    Assert.IsTrue(assignments.Single(value => value.ImageId == images[0].Id).Retest);
                    Assert.AreEqual(DatasetAssignmentStatus.NotUsed, assignments.Single(value => value.ImageId == images[3].Id).Status);
                    Assert.IsTrue(assignments.Where(value => value.ImageId != images[3].Id).All(value => value.Status == DatasetAssignmentStatus.Train || value.Status == DatasetAssignmentStatus.Test));
                    Assert.IsTrue(store.Undo(set.Id));
                    AssertDatasetAssignments(before, store.GetDatasetAssignments(set.Id));
                    Assert.IsTrue(store.Redo(set.Id));
                    AssertDatasetAssignments(assignments, store.GetDatasetAssignments(set.Id));
                }
            }
            finally { DeleteTemporaryProject(path); if (File.Exists(source)) File.Delete(source); }
        }

        private static void AssertDatasetAssignments(IEnumerable<DatasetAssignment> expected, IEnumerable<DatasetAssignment> actual)
        {
            DatasetAssignment[] values = actual.ToArray();
            foreach (DatasetAssignment value in expected)
            {
                DatasetAssignment match = values.Single(item => item.ImageId == value.ImageId);
                Assert.AreEqual(value.Status, match.Status);
                Assert.AreEqual(value.Retest, match.Retest);
            }
        }

        [TestMethod]
        public void Clear_annotations_only_affects_requested_images_in_the_current_annotation_set()
        {
            string path = TemporaryProjectPath();
            string firstSource = Path.Combine(Path.GetDirectoryName(path), "one.png");
            string secondSource = Path.Combine(Path.GetDirectoryName(path), "two.png");
            try
            {
                CreateBitmap(firstSource, Color.Red, ImageFormat.Png, 20, 20);
                CreateBitmap(secondSource, Color.Blue, ImageFormat.Png, 20, 20);
                using (var store = ProjectStore.Create(path))
                {
                    ProjectImage[] images = store.ImportFiles(new[] { firstSource, secondSource }).ToArray();
                    AnnotationSet set = store.CreateAnnotationSet("classes", AnnotationSetType.Cla);
                    AnnotationSet other = store.CreateAnnotationSet("other", AnnotationSetType.Cla);
                    AnnotationClass value = store.GetClasses(set.Id).Single();
                    AnnotationClass otherValue = store.GetClasses(other.Id).Single();
                    store.SetClaClass(set.Id, images[0].Id, value.Id);
                    store.SetClaClass(set.Id, images[1].Id, value.Id);
                    store.SetClaClass(other.Id, images[0].Id, otherValue.Id);
                    store.ClearAnnotations(set.Id, new[] { images[0].Id });
                    Assert.IsNull(store.GetClaClassId(set.Id, images[0].Id));
                    Assert.AreEqual(value.Id, store.GetClaClassId(set.Id, images[1].Id));
                    Assert.AreEqual(otherValue.Id, store.GetClaClassId(other.Id, images[0].Id));
                    Assert.IsTrue(store.Undo(set.Id));
                    Assert.AreEqual(value.Id, store.GetClaClassId(set.Id, images[0].Id));
                    Assert.IsTrue(store.Undo(set.Id));
                    Assert.IsTrue(store.Undo(set.Id));
                    Assert.IsFalse(store.CanUndo(set.Id));
                    store.ClearAnnotations(set.Id, images.Select(image => image.Id));
                    Assert.IsFalse(store.CanUndo(set.Id));
                }
            }
            finally { DeleteTemporaryProject(path); if (File.Exists(firstSource)) File.Delete(firstSource); if (File.Exists(secondSource)) File.Delete(secondSource); }
        }

        [TestMethod]
        public void Undoing_class_deletion_restores_cascaded_obd_annotations()
        {
            string path = TemporaryProjectPath();
            string source = Path.Combine(Path.GetDirectoryName(path), "source.png");
            try
            {
                CreateBitmap(source, Color.Red, ImageFormat.Png, 20, 10);
                using (var store = ProjectStore.Create(path))
                {
                    ProjectImage image = store.ImportFiles(new[] { source })[0];
                    AnnotationSet set = store.CreateAnnotationSet("boxes", AnnotationSetType.Obd);
                    AnnotationClass deleted = store.GetClasses(set.Id).Single();
                    store.AddClass(set.Id, "other", "#123456");
                    ObdAnnotation annotation = store.AddObdAnnotation(set.Id, image.Id, deleted.Id, 1, 2, 5, 4);

                    store.DeleteClass(deleted.Id);
                    Assert.AreEqual(0, store.GetObdAnnotations(set.Id, image.Id).Count);

                    Assert.IsTrue(store.Undo(set.Id));
                    ObdAnnotation restored = store.GetObdAnnotations(set.Id, image.Id).Single();
                    Assert.AreEqual(annotation.Id, restored.Id);
                    Assert.AreEqual(deleted.Id, restored.ClassId);
                }
            }
            finally { DeleteTemporaryProject(path); if (File.Exists(source)) File.Delete(source); }
        }

        [TestMethod]
        public void Undoing_class_deletion_restores_cascaded_seg_regions()
        {
            string path = TemporaryProjectPath();
            string source = Path.Combine(Path.GetDirectoryName(path), "source.png");
            try
            {
                CreateBitmap(source, Color.Red, ImageFormat.Png, 20, 10);
                using (var store = ProjectStore.Create(path))
                {
                    ProjectImage image = store.ImportFiles(new[] { source })[0];
                    AnnotationSet set = store.CreateAnnotationSet("regions", AnnotationSetType.Seg);
                    AnnotationClass deleted = store.GetClasses(set.Id).Single();
                    store.AddClass(set.Id, "other", "#123456");
                    SegRegion region = store.AddSegRegion(set.Id, image.Id, deleted.Id, SegRegionType.Ellipse, 10, 5, 3, 2);

                    store.DeleteClass(deleted.Id);
                    Assert.AreEqual(0, store.GetSegRegions(set.Id, image.Id).Count);

                    Assert.IsTrue(store.Undo(set.Id));
                    SegRegion restored = store.GetSegRegions(set.Id, image.Id).Single();
                    Assert.AreEqual(region.Id, restored.Id);
                    Assert.AreEqual(deleted.Id, restored.ClassId);
                }
            }
            finally { DeleteTemporaryProject(path); if (File.Exists(source)) File.Delete(source); }
        }

        [TestMethod]
        public void Editing_an_obd_annotation_moves_it_to_the_persisted_top_layer()
        {
            string path = TemporaryProjectPath();
            string source = Path.Combine(Path.GetDirectoryName(path), "source.png");
            try
            {
                CreateBitmap(source, Color.Red, ImageFormat.Png, 20, 10);
                string setId;
                string imageId;
                string editedId;
                using (var store = ProjectStore.Create(path))
                {
                    imageId = store.ImportFiles(new[] { source })[0].Id;
                    AnnotationSet set = store.CreateAnnotationSet("boxes", AnnotationSetType.Obd);
                    setId = set.Id;
                    AnnotationClass value = store.GetClasses(set.Id).Single();
                    ObdAnnotation edited = store.AddObdAnnotation(set.Id, imageId, value.Id, 1, 1, 5, 4);
                    ObdAnnotation other = store.AddObdAnnotation(set.Id, imageId, value.Id, 2, 2, 5, 4);
                    editedId = edited.Id;

                    store.UpdateObdAnnotation(edited.Id, value.Id, 3, 1, 5, 4);
                    Assert.AreEqual(other.ZOrder + 1, store.GetObdAnnotations(set.Id, imageId).Single(item => item.Id == edited.Id).ZOrder);
                    Assert.IsTrue(store.Undo(set.Id));
                    Assert.AreEqual(edited.ZOrder, store.GetObdAnnotations(set.Id, imageId).Single(item => item.Id == edited.Id).ZOrder);
                    Assert.IsTrue(store.Redo(set.Id));
                    store.Save();
                }
                using (var reopened = ProjectStore.Open(path))
                {
                    Assert.AreEqual(2, reopened.GetObdAnnotations(setId, imageId).Single(item => item.Id == editedId).ZOrder);
                }
            }
            finally { DeleteTemporaryProject(path); if (File.Exists(source)) File.Delete(source); }
        }

        [TestMethod]
        public void Seg_region_group_update_and_delete_are_undoable_and_preserve_relative_layer_order()
        {
            string path = TemporaryProjectPath();
            string source = Path.Combine(Path.GetDirectoryName(path), "source.png");
            try
            {
                CreateBitmap(source, Color.Red, ImageFormat.Png, 30, 20);
                using (var store = ProjectStore.Create(path))
                {
                    ProjectImage image = store.ImportFiles(new[] { source })[0];
                    AnnotationSet set = store.CreateAnnotationSet("regions", AnnotationSetType.Seg);
                    AnnotationClass first = store.GetClasses(set.Id).Single();
                    AnnotationClass second = store.AddClass(set.Id, "other", "#123456");
                    SegRegion firstRegion = store.AddSegRegion(set.Id, image.Id, first.Id, SegRegionType.Rect, 1, 1, 4, 4);
                    SegRegion secondRegion = store.AddSegRegion(set.Id, image.Id, first.Id, SegRegionType.Rect, 10, 1, 4, 4);
                    SegRegion updatedFirst = new SegRegion(firstRegion.Id, set.Id, image.Id, second.Id, SegRegionType.PolyGon, 0, 0, 0, 0, firstRegion.ZOrder, new[] { new SegPoint(2, 2), new SegPoint(7, 2), new SegPoint(2, 7) });
                    SegRegion updatedSecond = new SegRegion(secondRegion.Id, set.Id, image.Id, first.Id, SegRegionType.Rect, 11, 2, 4, 4, secondRegion.ZOrder);

                    store.UpdateSegRegions(new[] { updatedSecond, updatedFirst });
                    SegRegion[] updated = store.GetSegRegions(set.Id, image.Id).ToArray();
                    Assert.AreEqual(second.Id, updated.Single(item => item.Id == firstRegion.Id).ClassId);
                    Assert.AreEqual(SegRegionType.PolyGon, updated.Single(item => item.Id == firstRegion.Id).Type);
                    Assert.AreEqual(2, updated.Single(item => item.Id == firstRegion.Id).ZOrder);
                    Assert.AreEqual(3, updated.Single(item => item.Id == secondRegion.Id).ZOrder);
                    Assert.IsTrue(store.Undo(set.Id));
                    Assert.AreEqual(SegRegionType.Rect, store.GetSegRegions(set.Id, image.Id).Single(item => item.Id == firstRegion.Id).Type);
                    Assert.IsTrue(store.Redo(set.Id));
                    store.DeleteSegRegions(new[] { firstRegion.Id, secondRegion.Id });
                    Assert.AreEqual(0, store.GetSegRegions(set.Id, image.Id).Count);
                    Assert.IsTrue(store.Undo(set.Id));
                    Assert.AreEqual(2, store.GetSegRegions(set.Id, image.Id).Count);
                }
            }
            finally { DeleteTemporaryProject(path); if (File.Exists(source)) File.Delete(source); }
        }

        [TestMethod]
        public void Group_obd_update_and_delete_are_undoable_and_preserve_relative_layer_order()
        {
            string path = TemporaryProjectPath();
            string source = Path.Combine(Path.GetDirectoryName(path), "source.png");
            try
            {
                CreateBitmap(source, Color.Red, ImageFormat.Png, 30, 20);
                using (var store = ProjectStore.Create(path))
                {
                    ProjectImage image = store.ImportFiles(new[] { source })[0];
                    AnnotationSet set = store.CreateAnnotationSet("boxes", AnnotationSetType.Obd);
                    AnnotationClass firstClass = store.GetClasses(set.Id).Single();
                    AnnotationClass secondClass = store.AddClass(set.Id, "other", "#123456");
                    ObdAnnotation first = store.AddObdAnnotation(set.Id, image.Id, firstClass.Id, 1, 1, 4, 4);
                    ObdAnnotation second = store.AddObdAnnotation(set.Id, image.Id, firstClass.Id, 10, 1, 4, 4);
                    ObdAnnotation third = store.AddObdAnnotation(set.Id, image.Id, firstClass.Id, 20, 1, 4, 4);

                    store.UpdateObdAnnotations(new[]
                    {
                        new ObdAnnotation(first.Id, set.Id, image.Id, secondClass.Id, 2, 3, 4, 4, first.ZOrder),
                        new ObdAnnotation(second.Id, set.Id, image.Id, secondClass.Id, 11, 3, 4, 4, second.ZOrder)
                    });
                    ObdAnnotation[] updated = store.GetObdAnnotations(set.Id, image.Id).ToArray();
                    Assert.AreEqual(third.Id, updated[0].Id);
                    Assert.AreEqual(first.Id, updated[1].Id);
                    Assert.AreEqual(second.Id, updated[2].Id);
                    Assert.AreEqual(secondClass.Id, updated[1].ClassId);
                    Assert.AreEqual(2, updated[1].X);
                    Assert.IsTrue(store.Undo(set.Id));
                    Assert.AreEqual(firstClass.Id, store.GetObdAnnotations(set.Id, image.Id).Single(item => item.Id == first.Id).ClassId);
                    Assert.IsTrue(store.Redo(set.Id));

                    store.DeleteObdAnnotations(new[] { first.Id, second.Id });
                    Assert.AreEqual(third.Id, store.GetObdAnnotations(set.Id, image.Id).Single().Id);
                    Assert.IsTrue(store.Undo(set.Id));
                    Assert.AreEqual(3, store.GetObdAnnotations(set.Id, image.Id).Count);
                }
            }
            finally { DeleteTemporaryProject(path); if (File.Exists(source)) File.Delete(source); }
        }

        private static string FindRepositoryLabelsDirectory()
        {
            for (DirectoryInfo directory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory); directory != null; directory = directory.Parent)
            {
                string labelsDirectory = Path.Combine(directory.FullName, "labels");
                if (File.Exists(Path.Combine(labelsDirectory, "cla_labels.json"))) return labelsDirectory;
            }
            throw new DirectoryNotFoundException("The repository labels directory was not found.");
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

        private static void CreateVersionFourProject(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()))
            {
                connection.Open();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "CREATE TABLE SchemaInfo (Version INTEGER NOT NULL); CREATE TABLE ProjectInfo (ProjectId TEXT NOT NULL, CreatedUtc TEXT NOT NULL); CREATE TABLE ImageRecord (Id TEXT PRIMARY KEY NOT NULL, OriginalFileName TEXT NOT NULL, ManagedFileName TEXT NOT NULL UNIQUE, Sha256 TEXT NOT NULL, Width INTEGER NOT NULL, Height INTEGER NOT NULL, Sequence INTEGER NOT NULL UNIQUE); CREATE TABLE AnnotationSet (Id TEXT PRIMARY KEY NOT NULL, Name TEXT NOT NULL, Type INTEGER NOT NULL); CREATE TABLE AnnotationClass (Id TEXT PRIMARY KEY NOT NULL, AnnotationSetId TEXT NOT NULL, Name TEXT NOT NULL, Color TEXT NOT NULL, UNIQUE (AnnotationSetId, Name)); CREATE TABLE ClaAnnotation (AnnotationSetId TEXT NOT NULL, ImageId TEXT NOT NULL, ClassId TEXT NOT NULL, PRIMARY KEY (AnnotationSetId, ImageId)); CREATE TABLE ObdAnnotation (Id TEXT PRIMARY KEY NOT NULL, AnnotationSetId TEXT NOT NULL, ImageId TEXT NOT NULL, ClassId TEXT NOT NULL, X INTEGER NOT NULL, Y INTEGER NOT NULL, Width INTEGER NOT NULL, Height INTEGER NOT NULL, ZOrder INTEGER NOT NULL); INSERT INTO SchemaInfo (Version) VALUES (4); INSERT INTO ProjectInfo (ProjectId, CreatedUtc) VALUES ('legacy', '2026-01-01T00:00:00.0000000Z'); INSERT INTO ImageRecord (Id, OriginalFileName, ManagedFileName, Sha256, Width, Height, Sequence) VALUES ('image', 'image.png', 'image.png', 'hash', 20, 10, 1); INSERT INTO AnnotationSet (Id, Name, Type) VALUES ('cla-set', 'cla', 0), ('obd-set', 'obd', 1), ('seg-set', 'seg', 2); INSERT INTO AnnotationClass (Id, AnnotationSetId, Name, Color) VALUES ('class', 'cla-set', 'class1', '#FF0000'), ('obd-class', 'obd-set', 'class1', '#FF0000'), ('seg-class', 'seg-set', 'class1', '#FF0000'); INSERT INTO ClaAnnotation (AnnotationSetId, ImageId, ClassId) VALUES ('cla-set', 'image', 'class'); INSERT INTO ObdAnnotation (Id, AnnotationSetId, ImageId, ClassId, X, Y, Width, Height, ZOrder) VALUES ('obd', 'obd-set', 'image', 'obd-class', 1, 2, 5, 4, 0);";
                    command.ExecuteNonQuery();
                }
            }
        }

        private static void CreateVersionFiveProject(string path)
        {
            CreateVersionFourProject(path);
            using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()))
            {
                connection.Open();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "CREATE TABLE SegRegion (Id TEXT PRIMARY KEY NOT NULL, AnnotationSetId TEXT NOT NULL, ImageId TEXT NOT NULL, ClassId TEXT NOT NULL, Type INTEGER NOT NULL, X INTEGER NOT NULL, Y INTEGER NOT NULL, Width INTEGER NOT NULL, Height INTEGER NOT NULL, ZOrder INTEGER NOT NULL); INSERT INTO SegRegion (Id, AnnotationSetId, ImageId, ClassId, Type, X, Y, Width, Height, ZOrder) VALUES ('region', 'seg-set', 'image', 'seg-class', 0, 3, 4, 5, 6, 0); UPDATE SchemaInfo SET Version = 5;";
                    command.ExecuteNonQuery();
                }
            }
        }
    }
}

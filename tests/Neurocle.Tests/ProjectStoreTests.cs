using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Clipper2Lib;
using Neurocle.Domain.Segmentation;
using Neurocle.Infrastructure.Persistence;
using Neurocle.Application.Projects;

namespace Neurocle.Tests
{
    [TestClass]
    public sealed class ProjectStoreTests
    {
        [TestMethod]
        public void New_classes_use_unique_random_rgba_colors_within_each_annotation_set()
        {
            string path = TemporaryProjectPath();
            Func<int, int> original = ProjectStore.NextRandom;
            try
            {
                ProjectStore.NextRandom = maximum => 0;
                using (ProjectStore store = ProjectStore.Create(path))
                {
                    AnnotationSet set = store.CreateAnnotationSet("colors", AnnotationSetType.Cla);
                    store.AddClass(set.Id, "custom", "rgba(1, 1, 1, 1)-custom");
                    ProjectStore.NextRandom = maximum => 1;
                    store.AddClass(set.Id, "second", null);
                    string[] colors = store.GetClasses(set.Id).Select(value => value.Color).ToArray();
                    Assert.AreEqual(3, colors.Select(value => value.Replace("-custom", "")).Distinct(StringComparer.Ordinal).Count());
                    Assert.IsTrue(colors.All(value => value.Replace("-custom", "").StartsWith("rgba(") && value.Replace("-custom", "").EndsWith(", 1)")));
                }
            }
            finally { ProjectStore.NextRandom = original; DeleteTemporaryProject(path); }
        }

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
                    Assert.AreEqual("photo.png", images[0].ManagedFileName);
                    Assert.AreEqual("photo (1).png", images[1].ManagedFileName);
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
                    Assert.AreEqual("photo.png", persisted[0].OriginalFileName);
                    Assert.AreEqual("photo (1).png", persisted[1].ManagedFileName);
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
                    CollectionAssert.AreEqual(new[] { "1.jpeg", "2.jpg", "10.png", "11.png" }, images.Select(image => image.OriginalFileName).ToArray());
                    CollectionAssert.AreEqual(new[] { 1, 2, 3, 4 }, images.Select(image => image.Sequence).ToArray());
                }
            }
            finally { DeleteTemporaryProject(path); }
        }

        [TestMethod]
        public void Import_converts_bmp_to_png_and_preserves_other_supported_file_contents()
        {
            string path = TemporaryProjectPath();
            string sourceRoot = Path.Combine(Path.GetDirectoryName(path), "sources");
            try
            {
                string bitmap = CreateBitmap(Path.Combine(sourceRoot, "photo.bmp"), Color.Red, ImageFormat.Bmp);
                string jpeg = CreateBitmap(Path.Combine(sourceRoot, "photo.jpg"), Color.Blue, ImageFormat.Jpeg);
                string png = CreateBitmap(Path.Combine(sourceRoot, "photo.png"), Color.Green, ImageFormat.Png);
                using (ProjectStore store = ProjectStore.Create(path))
                {
                    ProjectImage[] images = store.ImportFiles(new[] { bitmap, jpeg, png }).ToArray();
                    ProjectImage converted = images.Single(image => image.OriginalFileName == "photo.png");
                    Assert.AreEqual("photo.png", converted.ManagedFileName);
                    using (var managedImage = Image.FromFile(Path.Combine(store.ImagesDirectory, converted.ManagedFileName)))
                        Assert.AreEqual(ImageFormat.Png.Guid, managedImage.RawFormat.Guid);

                    ProjectImage importedJpeg = images.Single(image => image.ManagedFileName == "photo.jpg");
                    ProjectImage importedPng = images.Single(image => image.ManagedFileName == "photo (1).png");
                    CollectionAssert.AreEqual(File.ReadAllBytes(jpeg), File.ReadAllBytes(Path.Combine(store.ImagesDirectory, importedJpeg.ManagedFileName)));
                    CollectionAssert.AreEqual(File.ReadAllBytes(png), File.ReadAllBytes(Path.Combine(store.ImagesDirectory, importedPng.ManagedFileName)));
                }
            }
            finally { DeleteTemporaryProject(path); }
        }

        [TestMethod]
        public void Bmp_conversion_cleans_temporary_file_when_final_destination_cannot_be_created()
        {
            string path = TemporaryProjectPath();
            string source = Path.Combine(Path.GetDirectoryName(path), "source.bmp");
            string destination = Path.Combine(Path.GetDirectoryName(path), "managed.png");
            try
            {
                CreateBitmap(source, Color.Red, ImageFormat.Bmp);
                Directory.CreateDirectory(destination);
                Assert.ThrowsException<IOException>(() => ProjectImageImporter.CopyToManagedStorage(source, destination));
                Assert.IsFalse(File.Exists(destination + ".importing"));
            }
            finally { DeleteTemporaryProject(path); if (File.Exists(source)) File.Delete(source); }
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
                    CollectionAssert.AreEqual(new[] { "1.png", "3.png" }, remaining.Select(image => image.OriginalFileName).ToArray());
                    CollectionAssert.AreEqual(new[] { 1, 2 }, remaining.Select(image => image.Sequence).ToArray());
                    Assert.IsFalse(File.Exists(Path.Combine(store.ImagesDirectory, "2.png")));

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
                    Assert.IsTrue(File.Exists(Path.Combine(reopened.ImagesDirectory, ".delete-sample.png")));
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
                    var progress = new List<ImageImportProgress>();
                    ImageImportReport report = store.ImportFilesWithReport(new[] { second, invalid, first }, null, progress.Add);
                    Assert.AreEqual(2, report.Imported.Count);
                    Assert.AreEqual(1, report.Failures.Count);
                    Assert.AreEqual(0, report.UnprocessedCount);
                    Assert.AreEqual("2.txt", Path.GetFileName(report.Failures[0].FilePath));
                    Assert.AreEqual(ImageImportFailure.UnsupportedFormat, report.Failures[0].Failure);
                    Assert.AreEqual(2, store.GetImages().Count);
                    CollectionAssert.AreEqual(new[] { 0, 1, 2, 3 }, progress.Select(item => item.ProcessedCount).ToArray());
                    Assert.AreEqual(3, progress.Last().TotalCount);
                    Assert.AreEqual(2, progress.Last().SuccessfulCount);
                    Assert.AreEqual(1, progress.Last().FailedCount);
                }
            }
            finally { DeleteTemporaryProject(path); }
        }

        [TestMethod]
        public void Report_import_waits_for_an_active_edit_transaction_and_then_succeeds()
        {
            string path = TemporaryProjectPath();
            string source = Path.Combine(Path.GetDirectoryName(path), "concurrent.bmp");
            var editStarted = new ManualResetEventSlim();
            var releaseEdit = new ManualResetEventSlim();
            try
            {
                CreateBitmap(source, Color.Red);
                using (ProjectStore store = ProjectStore.Create(path))
                {
                    Task edit = Task.Run(() => store.CompleteOperation((connection, transaction) =>
                    {
                        using (SqliteCommand command = connection.CreateCommand())
                        {
                            command.Transaction = transaction;
                            command.CommandText = "UPDATE ProjectInfo SET CreatedUtc = CreatedUtc;";
                            command.ExecuteNonQuery();
                        }
                        editStarted.Set();
                        Assert.IsTrue(releaseEdit.Wait(TimeSpan.FromSeconds(5)));
                    }));
                    Assert.IsTrue(editStarted.Wait(TimeSpan.FromSeconds(5)));

                    Task<ImageImportReport> import = Task.Run(() => store.ImportFilesWithReport(new[] { source }, null));

                    Assert.IsFalse(import.Wait(TimeSpan.FromMilliseconds(100)));
                    releaseEdit.Set();
                    Assert.IsTrue(Task.WaitAll(new Task[] { edit, import }, TimeSpan.FromSeconds(5)));
                    Assert.AreEqual(1, import.Result.Imported.Count);
                    Assert.AreEqual(0, import.Result.Failures.Count);
                    Assert.AreEqual(1, store.GetImages().Count);
                }
            }
            finally
            {
                releaseEdit.Set();
                editStarted.Dispose();
                releaseEdit.Dispose();
                DeleteTemporaryProject(path);
            }
        }

        [TestMethod]
        public void Report_import_rejects_a_disposed_project_session()
        {
            string path = TemporaryProjectPath();
            try
            {
                ProjectStore store = ProjectStore.Create(path);
                store.Dispose();

                Assert.ThrowsException<ObjectDisposedException>(() => store.ImportFilesWithReport(new[] { "unused.png" }, null));
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
                    var progress = new List<ImageImportProgress>();
                    ImageImportReport report = store.ImportFilesWithReport(new[] { third, second, first }, () => ++checks > 1, progress.Add);
                    Assert.AreEqual(1, report.Imported.Count);
                    Assert.AreEqual(0, report.Failures.Count);
                    Assert.AreEqual(2, report.UnprocessedCount);
                    Assert.AreEqual(1, store.GetImages().Count);
                    Assert.AreEqual(1, progress.Last().ProcessedCount);
                    Assert.AreEqual(3, progress.Last().TotalCount);
                    Assert.AreEqual(1, progress.Last().SuccessfulCount);
                    Assert.AreEqual(0, progress.Last().FailedCount);
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
                    Assert.AreEqual("root.png", report.Imported[0].OriginalFileName);
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
                string renamedSource = Path.Combine(sourceRoot, "source-renamed.png");
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

                    File.Copy(source, renamedSource);
                    store.RelinkImage(image.Id, renamedSource);
                    Assert.AreEqual(0, store.GetMissingImages().Count);
                    Assert.IsTrue(File.Exists(Path.Combine(store.ImagesDirectory, image.ManagedFileName)));
                    using (var relinked = Image.FromFile(Path.Combine(store.ImagesDirectory, image.ManagedFileName)))
                        Assert.AreEqual(ImageFormat.Png.Guid, relinked.RawFormat.Guid);
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
        public void Image_list_annotation_columns_sort_complete_class_lists_and_numeric_counts()
        {
            ImageListRow[] rows =
            {
                new ImageListRow(new ProjectImage("one", "one.png", "one.png", "hash", 1, 1, 1), new[] { "zeta", "alpha" }, 10, true),
                new ImageListRow(new ProjectImage("two", "two.png", "two.png", "hash", 1, 1, 2), new[] { "beta" }, 2, true),
                new ImageListRow(new ProjectImage("three", "three.png", "three.png", "hash", 1, 1, 3), new string[0], 0, true),
                new ImageListRow(new ProjectImage("four", "four.png", "four.png", "hash", 1, 1, 4), new[] { "beta" }, 2, true)
            };

            CollectionAssert.AreEqual(new[] { "one", "two", "four", "three" }, ImageListProjection.FilterAndSort(rows, null, ImageListSortColumn.Classes, ImageListSortDirection.Ascending).Select(row => row.Image.Id).ToArray());
            CollectionAssert.AreEqual(new[] { "two", "four", "one", "three" }, ImageListProjection.FilterAndSort(rows, null, ImageListSortColumn.Classes, ImageListSortDirection.Descending).Select(row => row.Image.Id).ToArray());
            CollectionAssert.AreEqual(new[] { "three", "two", "four", "one" }, ImageListProjection.FilterAndSort(rows, null, ImageListSortColumn.AnnotationCount, ImageListSortDirection.Ascending).Select(row => row.Image.Id).ToArray());
            CollectionAssert.AreEqual(new[] { "one", "two", "four", "three" }, ImageListProjection.FilterAndSort(rows, null, ImageListSortColumn.AnnotationCount, ImageListSortDirection.Descending).Select(row => row.Image.Id).ToArray());
            Assert.AreEqual("alpha, zeta", rows[0].ClassDisplay);
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
        public void Canvas_viewport_clamps_pan_per_axis_and_centers_small_images_after_every_view_change()
        {
            var viewport = new CanvasViewport(new Size(2000, 1000), new Size(1000, 500));
            viewport.SetZoomAt(1d, new PointF(500f, 250f));
            viewport.Pan(10000f, 10000f);
            Assert.AreEqual(0f, viewport.Origin.X, 0.001f);
            Assert.AreEqual(0f, viewport.Origin.Y, 0.001f);
            viewport.Pan(-10000f, -10000f);
            Assert.AreEqual(-1000f, viewport.Origin.X, 0.001f);
            Assert.AreEqual(-500f, viewport.Origin.Y, 0.001f);

            viewport.SetView(1d, new PointF(-9000f, 9000f), false);
            Assert.AreEqual(-1000f, viewport.Origin.X, 0.001f);
            Assert.AreEqual(0f, viewport.Origin.Y, 0.001f);

            viewport.SetImageSize(new Size(400, 1000));
            viewport.SetZoomAt(1d, new PointF(500f, 250f));
            viewport.Pan(-10000f, -10000f);
            Assert.AreEqual(300f, viewport.Origin.X, 0.001f);
            Assert.AreEqual(-500f, viewport.Origin.Y, 0.001f);

            viewport.SetViewportSize(new Size(800, 500));
            Assert.AreEqual(200f, viewport.Origin.X, 0.001f);
            Assert.AreEqual(-500f, viewport.Origin.Y, 0.001f);
        }

        [TestMethod]
        public void Canvas_viewport_scroll_positions_follow_overflowing_axes_and_clamp_to_image_edges()
        {
            var viewport = new CanvasViewport(new Size(2000, 1000), new Size(1000, 500));
            viewport.SetZoomAt(1d, new PointF(500f, 250f));

            Assert.IsTrue(viewport.CanScrollHorizontally);
            Assert.IsTrue(viewport.CanScrollVertically);
            Assert.AreEqual(1000, viewport.HorizontalScrollMaximum);
            Assert.AreEqual(500, viewport.VerticalScrollMaximum);
            viewport.SetScrollPosition(1000, 500);
            Assert.AreEqual(new PointF(-1000f, -500f), viewport.Origin);
            Assert.AreEqual(1000, viewport.HorizontalScrollPosition);
            Assert.AreEqual(500, viewport.VerticalScrollPosition);

            viewport.SetScrollPosition(-100, 900);
            Assert.AreEqual(0f, viewport.Origin.X, 0.001f);
            Assert.AreEqual(-500f, viewport.Origin.Y, 0.001f);

            viewport.SetImageSize(new Size(400, 200));
            viewport.SetZoomAt(1d, new PointF(500f, 250f));
            Assert.IsFalse(viewport.CanScrollHorizontally);
            Assert.IsFalse(viewport.CanScrollVertically);
            Assert.AreEqual(0, viewport.HorizontalScrollPosition);
            Assert.AreEqual(300f, viewport.Origin.X, 0.001f);
            Assert.AreEqual(150f, viewport.Origin.Y, 0.001f);
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
        public void Cla_assignment_and_class_deletion_do_not_join_undo_history()
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
                string secondClassId;
                using (var store = ProjectStore.Create(path))
                {
                    imageId = store.ImportFiles(new[] { source })[0].Id;
                    AnnotationSet first = store.CreateAnnotationSet("first", AnnotationSetType.Cla);
                    AnnotationSet second = store.CreateAnnotationSet("second", AnnotationSetType.Cla);
                    firstSetId = first.Id;
                    secondSetId = second.Id;
                    AnnotationClass firstClass = store.AddClass(first.Id, "cat", "#123456");
                    AnnotationClass secondClass = store.GetClasses(second.Id).Single();
                    secondClassId = secondClass.Id;
                    AnnotationClass otherClass = store.AddClass(first.Id, "other", "#654321");
                    store.RenameClass(otherClass.Id, "renamed");
                    Assert.IsTrue(store.Undo(first.Id));
                    Assert.IsTrue(store.CanUndo(first.Id));
                    Assert.IsTrue(store.CanRedo(first.Id));

                    store.SetClaClass(first.Id, imageId, firstClass.Id);
                    Assert.AreEqual(firstClass.Id, store.GetClaClassId(first.Id, imageId));
                    store.SetClaClass(first.Id, imageId, otherClass.Id);
                    Assert.AreEqual(otherClass.Id, store.GetClaClassId(first.Id, imageId));
                    store.SetClaClass(first.Id, imageId, null);
                    Assert.IsNull(store.GetClaClassId(first.Id, imageId));
                    Assert.IsTrue(store.CanUndo(first.Id), "CLA assignment, replacement and clearing must not add undo history.");
                    Assert.IsTrue(store.CanRedo(first.Id), "CLA assignment, replacement and clearing must preserve redo history.");

                    store.SetClaClass(second.Id, imageId, secondClass.Id);
                    Assert.IsTrue(store.Redo(first.Id));
                    Assert.AreEqual("renamed", store.GetClasses(first.Id).Single(item => item.Id == otherClass.Id).Name);
                    Assert.IsNull(store.GetClaClassId(first.Id, imageId), "Redo must replay the prior class rename, not a CLA assignment.");
                    Assert.IsFalse(store.CanUndo(second.Id));
                    Assert.IsFalse(store.CanRedo(second.Id));

                    deletedClassId = firstClass.Id;
                    store.SetClaClass(first.Id, imageId, firstClass.Id);
                    store.DeleteClass(firstClass.Id);
                    Assert.IsNull(store.GetClaClassId(first.Id, imageId));
                    Assert.IsFalse(store.GetClasses(first.Id).Any(item => item.Id == deletedClassId));
                    Assert.IsTrue(store.Undo(first.Id));
                    Assert.IsFalse(store.GetClasses(first.Id).Any(item => item.Id == deletedClassId), "Undo must not restore a deleted CLA class.");
                    Assert.IsNull(store.GetClaClassId(first.Id, imageId), "Undo must not restore the deleted class assignment.");
                    Assert.IsTrue(store.Redo(first.Id));
                    Assert.IsNull(store.GetClaClassId(first.Id, imageId));
                    Assert.IsFalse(store.GetClasses(first.Id).Any(item => item.Id == deletedClassId));
                    store.Save();
                }

                using (var reopened = ProjectStore.Open(path))
                {
                    Assert.IsFalse(reopened.CanUndo(firstSetId));
                    Assert.IsFalse(reopened.CanRedo(firstSetId));
                    Assert.IsFalse(reopened.CanUndo(secondSetId));
                    Assert.IsNull(reopened.GetClaClassId(firstSetId, imageId));
                    Assert.IsFalse(reopened.GetClasses(firstSetId).Any(item => item.Id == deletedClassId));
                    Assert.AreEqual(secondClassId, reopened.GetClaClassId(secondSetId, imageId));
                }
            }
            finally { DeleteTemporaryProject(path); if (File.Exists(source)) File.Delete(source); }
        }

        [TestMethod]
        public void Deleting_an_unused_cla_class_remains_undoable()
        {
            string path = TemporaryProjectPath();
            try
            {
                using (var store = ProjectStore.Create(path))
                {
                    AnnotationSet set = store.CreateAnnotationSet("classes", AnnotationSetType.Cla);
                    AnnotationClass value = store.AddClass(set.Id, "unused", "#123456");
                    store.DeleteClass(value.Id);

                    Assert.IsTrue(store.CanUndo(set.Id));
                    Assert.IsTrue(store.Undo(set.Id));
                    Assert.IsTrue(store.GetClasses(set.Id).Any(item => item.Id == value.Id));
                }
            }
            finally { DeleteTemporaryProject(path); }
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
        public void Adding_obd_annotation_copies_skips_out_of_bounds_and_uses_one_undo_redo_step()
        {
            string path = TemporaryProjectPath();
            string source = Path.Combine(Path.GetDirectoryName(path), "source.png");
            string smallPath = Path.Combine(Path.GetDirectoryName(path), "small.png");
            string largePath = Path.Combine(Path.GetDirectoryName(path), "large.png");
            try
            {
                CreateBitmap(source, Color.Red, ImageFormat.Png, 30, 20);
                CreateBitmap(smallPath, Color.Green, ImageFormat.Png, 12, 6);
                CreateBitmap(largePath, Color.Blue, ImageFormat.Png, 40, 30);
                using (var store = ProjectStore.Create(path))
                {
                    ProjectImage image = store.ImportFiles(new[] { source })[0];
                    ProjectImage small = store.ImportFiles(new[] { smallPath })[0];
                    ProjectImage large = store.ImportFiles(new[] { largePath })[0];
                    AnnotationSet set = store.CreateAnnotationSet("boxes", AnnotationSetType.Obd);
                    AnnotationClass firstClass = store.GetClasses(set.Id).Single();
                    AnnotationClass secondClass = store.AddClass(set.Id, "other", "#123456");
                    ObdAnnotation first = store.AddObdAnnotation(set.Id, image.Id, firstClass.Id, 1, 2, 5, 4);
                    ObdAnnotation second = store.AddObdAnnotation(set.Id, image.Id, secondClass.Id, 10, 8, 7, 6);
                    ObdAnnotation[] originals = { first, second };

                    IReadOnlyList<ObdAnnotation> copies = store.AddObdAnnotationCopies(set.Id, new[] { image.Id, small.Id, large.Id }, originals);
                    Assert.AreEqual(5, copies.Count);
                    Assert.AreEqual(5, copies.Select(value => value.Id).Distinct(StringComparer.Ordinal).Count());
                    CollectionAssert.AreEqual(new[] { firstClass.Id, secondClass.Id, firstClass.Id, firstClass.Id, secondClass.Id }, copies.Select(value => value.ClassId).ToArray());
                    CollectionAssert.AreEqual(new[] { image.Id, image.Id, small.Id, large.Id, large.Id }, copies.Select(value => value.ImageId).ToArray());
                    CollectionAssert.AreEqual(new[] { new Rectangle(1, 2, 5, 4), new Rectangle(10, 8, 7, 6), new Rectangle(1, 2, 5, 4), new Rectangle(1, 2, 5, 4), new Rectangle(10, 8, 7, 6) }, copies.Select(value => new Rectangle(value.X, value.Y, value.Width, value.Height)).ToArray());
                    CollectionAssert.AreEqual(new[] { 2, 3, 0, 0, 1 }, copies.Select(value => value.ZOrder).ToArray());

                    Assert.IsTrue(store.Undo(set.Id));
                    CollectionAssert.AreEqual(originals.Select(value => value.Id).ToArray(), store.GetObdAnnotations(set.Id, image.Id).Select(value => value.Id).ToArray());
                    Assert.AreEqual(0, store.GetObdAnnotations(set.Id, small.Id).Count);
                    Assert.AreEqual(0, store.GetObdAnnotations(set.Id, large.Id).Count);
                    Assert.IsTrue(store.Redo(set.Id));
                    CollectionAssert.AreEqual(originals.Concat(copies.Where(value => value.ImageId == image.Id)).Select(value => value.Id).ToArray(), store.GetObdAnnotations(set.Id, image.Id).Select(value => value.Id).ToArray());
                    Assert.AreEqual(1, store.GetObdAnnotations(set.Id, small.Id).Count);
                    Assert.AreEqual(2, store.GetObdAnnotations(set.Id, large.Id).Count);

                    bool canUndoBeforeEmptyCopy = store.CanUndo(set.Id);
                    Assert.AreEqual(0, store.AddObdAnnotationCopies(set.Id, new[] { image.Id }, new ObdAnnotation[0]).Count);
                    Assert.AreEqual(canUndoBeforeEmptyCopy, store.CanUndo(set.Id));
                }
            }
            finally { DeleteTemporaryProject(path); if (File.Exists(source)) File.Delete(source); if (File.Exists(smallPath)) File.Delete(smallPath); if (File.Exists(largePath)) File.Delete(largePath); }
        }

        [TestMethod]
        public void Obd_annotation_copy_batch_rolls_back_all_targets_when_a_persisted_insert_fails()
        {
            string path = TemporaryProjectPath();
            string firstSource = Path.Combine(Path.GetDirectoryName(path), "first.png");
            string secondSource = Path.Combine(Path.GetDirectoryName(path), "second.png");
            try
            {
                CreateBitmap(firstSource, Color.Red, ImageFormat.Png, 40, 30);
                CreateBitmap(secondSource, Color.Blue, ImageFormat.Png, 40, 30);
                using (var store = ProjectStore.Create(path))
                {
                    ProjectImage firstImage = store.ImportFiles(new[] { firstSource })[0];
                    ProjectImage secondImage = store.ImportFiles(new[] { secondSource })[0];
                    AnnotationSet set = store.CreateAnnotationSet("boxes", AnnotationSetType.Obd);
                    AnnotationClass value = store.GetClasses(set.Id).Single();
                    var source = new[] { new ObdAnnotation("snapshot", set.Id, firstImage.Id, value.Id, 2, 3, 10, 8, 0) };
                    bool canUndoBefore = store.CanUndo(set.Id);
                    store.CompleteOperation((db, transaction) =>
                    {
                        using (var command = db.CreateCommand())
                        {
                            command.Transaction = transaction;
                            command.CommandText = "CREATE TRIGGER FailSecondObdCopy BEFORE INSERT ON ObdAnnotation WHEN NEW.ImageId = '" + secondImage.Id + "' BEGIN SELECT RAISE(ABORT, 'injected copy failure'); END;";
                            command.ExecuteNonQuery();
                        }
                    });

                    Assert.ThrowsException<SqliteException>(() => store.AddObdAnnotationCopies(set.Id, new[] { firstImage.Id, secondImage.Id }, source));
                    Assert.AreEqual(0, store.GetObdAnnotations(set.Id, firstImage.Id).Count);
                    Assert.AreEqual(0, store.GetObdAnnotations(set.Id, secondImage.Id).Count);
                    Assert.AreEqual(canUndoBefore, store.CanUndo(set.Id), "A failed batch must not leave a history entry.");
                }
            }
            finally { DeleteTemporaryProject(path); if (File.Exists(firstSource)) File.Delete(firstSource); if (File.Exists(secondSource)) File.Delete(secondSource); }
        }

        [TestMethod]
        public void Adding_seg_region_copies_preserves_overlapping_regions_skips_bounds_and_uses_one_undo_redo_step()
        {
            string path = TemporaryProjectPath();
            string source = Path.Combine(Path.GetDirectoryName(path), "source.png");
            string smallPath = Path.Combine(Path.GetDirectoryName(path), "small.png");
            string largePath = Path.Combine(Path.GetDirectoryName(path), "large.png");
            string exportedJson = Path.Combine(Path.GetDirectoryName(path), "regions.json");
            string setId = null;
            string imageId = null;
            string smallImageId = null;
            string largeImageId = null;
            try
            {
                CreateBitmap(source, Color.Red, ImageFormat.Png, 30, 20);
                CreateBitmap(smallPath, Color.Green, ImageFormat.Png, 12, 8);
                CreateBitmap(largePath, Color.Blue, ImageFormat.Png, 40, 30);
                using (var store = ProjectStore.Create(path))
                {
                    ProjectImage image = store.ImportFiles(new[] { source })[0];
                    ProjectImage small = store.ImportFiles(new[] { smallPath })[0];
                    ProjectImage large = store.ImportFiles(new[] { largePath })[0];
                    AnnotationSet set = store.CreateAnnotationSet("regions", AnnotationSetType.Seg);
                    setId = set.Id;
                    imageId = image.Id;
                    smallImageId = small.Id;
                    largeImageId = large.Id;
                    AnnotationClass value = store.GetClasses(set.Id).Single();
                    SegRegion rectangle = store.AddSegRegion(set.Id, image.Id, value.Id, SegRegionType.Rect, 1, 2, 5, 4);
                    SegRegion ellipse = store.AddSegRegion(set.Id, image.Id, value.Id, SegRegionType.Ellipse, 20, 5, 2, 2);
                    SegRegion polygon = store.AddSegPath(set.Id, image.Id, value.Id, SegRegionType.PolyGon, new[] { new SegPoint(10, 10), new SegPoint(15, 10), new SegPoint(12, 15) }, 0);
                    SegRegion line = store.AddSegPath(set.Id, image.Id, value.Id, SegRegionType.PolyLine, new[] { new SegPoint(20, 15), new SegPoint(25, 15) }, 3);
                    SegRegion[] originals = { rectangle, ellipse, polygon, line };

                    IReadOnlyList<SegRegion> copies = store.AddSegRegionCopies(set.Id, new[] { image.Id, small.Id, large.Id }, originals);

                    Assert.AreEqual(9, copies.Count);
                    Assert.AreEqual(9, copies.Select(region => region.Id).Distinct(StringComparer.Ordinal).Count());
                    CollectionAssert.AreEqual(new[] { image.Id, image.Id, image.Id, image.Id, small.Id, large.Id, large.Id, large.Id, large.Id }, copies.Select(region => region.ImageId).ToArray());
                    CollectionAssert.AreEqual(new[] { SegRegionType.Rect, SegRegionType.Ellipse, SegRegionType.PolyGon, SegRegionType.PolyLine, SegRegionType.Rect, SegRegionType.Rect, SegRegionType.Ellipse, SegRegionType.PolyGon, SegRegionType.PolyLine }, copies.Select(region => region.Type).ToArray());
                    Assert.AreEqual(8, store.GetSegRegions(set.Id, image.Id).Count, "An exact same-class overlap must remain an independent copy.");
                    Assert.AreEqual(1, store.GetSegRegions(set.Id, small.Id).Count, "Only the rectangle fits the small image.");
                    Assert.AreEqual(4, store.GetSegRegions(set.Id, large.Id).Count);
                    foreach (SegRegion expected in originals)
                    {
                        SegRegion copy = copies.Single(region => region.ImageId == large.Id && region.Type == expected.Type);
                        Assert.AreEqual(expected.ClassId, copy.ClassId);
                        Assert.AreEqual(expected.X, copy.X);
                        Assert.AreEqual(expected.Y, copy.Y);
                        Assert.AreEqual(expected.Width, copy.Width);
                        Assert.AreEqual(expected.Height, copy.Height);
                        Assert.AreEqual(expected.StrokeWidth, copy.StrokeWidth);
                        CollectionAssert.AreEqual(expected.Points.Select(point => new Point(point.X, point.Y)).ToArray(), copy.Points.Select(point => new Point(point.X, point.Y)).ToArray());
                    }

                    Assert.IsTrue(store.Undo(set.Id));
                    CollectionAssert.AreEqual(originals.Select(region => region.Id).ToArray(), store.GetSegRegions(set.Id, image.Id).Select(region => region.Id).ToArray());
                    Assert.AreEqual(0, store.GetSegRegions(set.Id, small.Id).Count);
                    Assert.AreEqual(0, store.GetSegRegions(set.Id, large.Id).Count);
                    Assert.IsTrue(store.Redo(set.Id));
                    Assert.AreEqual(8, store.GetSegRegions(set.Id, image.Id).Count);
                    Assert.AreEqual(1, store.GetSegRegions(set.Id, small.Id).Count);
                    Assert.AreEqual(4, store.GetSegRegions(set.Id, large.Id).Count);
                    store.ExportAnnotationJson(set.Id, exportedJson);
                }
                using (var reopened = ProjectStore.Open(path))
                {
                    Assert.AreEqual(8, reopened.GetSegRegions(setId, imageId).Count);
                    Assert.AreEqual(1, reopened.GetSegRegions(setId, smallImageId).Count);
                    Assert.AreEqual(4, reopened.GetSegRegions(setId, largeImageId).Count);
                    AnnotationSet imported = reopened.ImportAnnotationJson(File.ReadAllText(exportedJson), "regions roundtrip", false);
                    Assert.AreEqual(8, reopened.GetSegRegions(imported.Id, imageId).Count, "JSON roundtrip must retain same-class overlapping copies as separate regions.");
                }
            }
            finally { DeleteTemporaryProject(path); if (File.Exists(source)) File.Delete(source); if (File.Exists(smallPath)) File.Delete(smallPath); if (File.Exists(largePath)) File.Delete(largePath); if (File.Exists(exportedJson)) File.Delete(exportedJson); }
        }

        [TestMethod]
        public void Seg_region_copy_batch_rolls_back_all_targets_when_persisted_insert_fails()
        {
            string path = TemporaryProjectPath();
            string firstSource = Path.Combine(Path.GetDirectoryName(path), "first.png");
            string secondSource = Path.Combine(Path.GetDirectoryName(path), "second.png");
            try
            {
                CreateBitmap(firstSource, Color.Red, ImageFormat.Png, 40, 30);
                CreateBitmap(secondSource, Color.Blue, ImageFormat.Png, 40, 30);
                using (var store = ProjectStore.Create(path))
                {
                    ProjectImage firstImage = store.ImportFiles(new[] { firstSource })[0];
                    ProjectImage secondImage = store.ImportFiles(new[] { secondSource })[0];
                    AnnotationSet set = store.CreateAnnotationSet("regions", AnnotationSetType.Seg);
                    AnnotationClass value = store.GetClasses(set.Id).Single();
                    SegRegion source = store.AddSegPath(set.Id, firstImage.Id, value.Id, SegRegionType.PolyGon, new[] { new SegPoint(2, 3), new SegPoint(20, 3), new SegPoint(10, 20) }, 0);
                    bool canUndoBefore = store.CanUndo(set.Id);
                    store.CompleteOperation((db, transaction) =>
                    {
                        using (var command = db.CreateCommand())
                        {
                            command.Transaction = transaction;
                            command.CommandText = "CREATE TRIGGER FailSecondSegCopy BEFORE INSERT ON SegRegion WHEN NEW.ImageId = '" + secondImage.Id + "' BEGIN SELECT RAISE(ABORT, 'injected copy failure'); END;";
                            command.ExecuteNonQuery();
                        }
                    });

                    Assert.ThrowsException<SqliteException>(() => store.AddSegRegionCopies(set.Id, new[] { firstImage.Id, secondImage.Id }, new[] { source }));
                    CollectionAssert.AreEqual(new[] { source.Id }, store.GetSegRegions(set.Id, firstImage.Id).Select(region => region.Id).ToArray());
                    Assert.AreEqual(0, store.GetSegRegions(set.Id, secondImage.Id).Count);
                    Assert.AreEqual(canUndoBefore, store.CanUndo(set.Id), "A failed batch must not leave a history entry.");
                }
            }
            finally { DeleteTemporaryProject(path); if (File.Exists(firstSource)) File.Delete(firstSource); if (File.Exists(secondSource)) File.Delete(secondSource); }
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
        public void Each_released_brush_stroke_can_be_undone_and_redone_independently()
        {
            string path = TemporaryProjectPath();
            string source = Path.Combine(Path.GetDirectoryName(path), "source.png");
            try
            {
                CreateBitmap(source, Color.Red, ImageFormat.Png, 100, 100);
                using (var store = ProjectStore.Create(path))
                {
                    ProjectImage image = store.ImportFiles(new[] { source }).Single();
                    AnnotationSet set = store.CreateAnnotationSet("brush strokes", AnnotationSetType.Seg);
                    AnnotationClass value = store.GetClasses(set.Id).Single();
                    store.AddSegPath(set.Id, image.Id, value.Id, SegRegionType.PolyLine, new[] { new SegPoint(20, 20) }, 15);
                    store.AddSegPath(set.Id, image.Id, value.Id, SegRegionType.PolyLine, new[] { new SegPoint(40, 40) }, 15);
                    store.AddSegPath(set.Id, image.Id, value.Id, SegRegionType.PolyLine, new[] { new SegPoint(60, 60) }, 15);

                    Assert.AreEqual(3, store.GetSegRegions(set.Id, image.Id).Count);
                    Assert.IsTrue(store.Undo(set.Id));
                    CollectionAssert.AreEqual(new[] { 20, 40 }, store.GetSegRegions(set.Id, image.Id).Select(region => region.Points[0].X).ToArray());
                    Assert.IsTrue(store.Undo(set.Id));
                    CollectionAssert.AreEqual(new[] { 20 }, store.GetSegRegions(set.Id, image.Id).Select(region => region.Points[0].X).ToArray());
                    Assert.IsTrue(store.Redo(set.Id));
                    CollectionAssert.AreEqual(new[] { 20, 40 }, store.GetSegRegions(set.Id, image.Id).Select(region => region.Points[0].X).ToArray());
                }
            }
            finally { DeleteTemporaryProject(path); if (File.Exists(source)) File.Delete(source); }
        }

        [TestMethod]
        public void Seg_geometry_erase_is_class_scoped_and_undoable()
        {
            string path = TemporaryProjectPath();
            string source = Path.Combine(Path.GetDirectoryName(path), "source.png");
            try
            {
                CreateBitmap(source, Color.Red, ImageFormat.Png, 100, 100);
                using (var store = ProjectStore.Create(path))
                {
                    ProjectImage image = store.ImportFiles(new[] { source }).Single();
                    AnnotationSet set = store.CreateAnnotationSet("geometry erase", AnnotationSetType.Seg);
                    AnnotationClass target = store.GetClasses(set.Id).Single();
                    AnnotationClass other = store.AddClass(set.Id, "other", "#00ff00");
                    store.AddSegRegion(set.Id, image.Id, target.Id, SegRegionType.Rect, 10, 10, 40, 40);
                    store.AddSegRegion(set.Id, image.Id, other.Id, SegRegionType.Rect, 10, 10, 40, 40);
                    store.EraseSegGeometry(set.Id, image.Id, target.Id, SegRegionType.Rect, 20, 20, 10, 10, null, 0);
                    SegRegion remaining = store.GetSegRegions(set.Id, image.Id).Single(value => value.ClassId == target.Id);
                    Assert.IsTrue(SegGeometryPrototype.Area(new Paths64 { new Path64(remaining.Points.Select(point => new Point64(point.X, point.Y)).ToList()) }) < 1600d);
                    Assert.AreEqual(SegRegionType.Rect, store.GetSegRegions(set.Id, image.Id).Single(value => value.ClassId == other.Id).Type);
                    Assert.IsTrue(store.Undo(set.Id));
                    Assert.AreEqual(2, store.GetSegRegions(set.Id, image.Id).Count);
                }
            }
            finally { DeleteTemporaryProject(path); if (File.Exists(source)) File.Delete(source); }
        }

        [TestMethod]
        public void Single_point_brush_and_eraser_use_round_strokes_and_roundtrip_through_json()
        {
            string path = TemporaryProjectPath();
            string source = Path.Combine(Path.GetDirectoryName(path), "source.png");
            string exportedJson = Path.Combine(Path.GetDirectoryName(path), "single-point.json");
            try
            {
                CreateBitmap(source, Color.Red, ImageFormat.Png, 100, 100);
                string setId;
                string imageId;
                string brushClassId;
                double dotAreaSize;
                using (var store = ProjectStore.Create(path))
                {
                    ProjectImage image = store.ImportFiles(new[] { source }).Single();
                    AnnotationSet set = store.CreateAnnotationSet("single-point", AnnotationSetType.Seg);
                    setId = set.Id;
                    imageId = image.Id;
                    AnnotationClass eraseClass = store.GetClasses(set.Id).Single();
                    AnnotationClass brushClass = store.AddClass(set.Id, "dots", "#123456");
                    brushClassId = brushClass.Id;
                    store.AddSegRegion(set.Id, image.Id, eraseClass.Id, SegRegionType.Rect, 0, 20, 20, 20);
                    SegRegion dot = store.AddSegPath(set.Id, image.Id, brushClass.Id, SegRegionType.PolyLine, new[] { new SegPoint(60, 60) }, 10);
                    Assert.AreEqual(1, dot.Points.Count);
                    Assert.AreEqual(10, dot.StrokeWidth);
                    Paths64 dotArea = SegGeometryPrototype.Stroke(new Path64 { new Point64(60, 60) }, 10);
                    dotAreaSize = SegGeometryPrototype.Area(dotArea);
                    Assert.IsTrue(dotAreaSize > 70d && dotAreaSize < 90d);

                    store.EraseSegPath(set.Id, image.Id, eraseClass.Id, new[] { new SegPoint(5, 30) }, 10);
                    SegRegion erasedRectangle = store.GetSegRegions(set.Id, image.Id).Single(value => value.ClassId == eraseClass.Id);
                    Assert.AreEqual(SegRegionType.PolyGon, erasedRectangle.Type);
                    Assert.IsTrue(erasedRectangle.Points.All(point => point.X >= 0 && point.Y >= 0 && point.X <= 100 && point.Y <= 100));
                    Assert.IsTrue(SegGeometryPrototype.Area(new Paths64 { new Path64(erasedRectangle.Points.Select(point => new Point64(point.X, point.Y)).ToList()) }) < 400d,
                        "A one-click eraser should remove a round area from the annotation.");
                    Assert.IsTrue(store.Undo(set.Id));
                    SegRegion restoredBeforeErase = store.GetSegRegions(set.Id, image.Id).Single(value => value.ClassId == eraseClass.Id);
                    Assert.AreEqual(SegRegionType.Rect, restoredBeforeErase.Type);
                    Assert.AreEqual(400d, SegGeometryPrototype.Area(new Paths64 { SegGeometryPrototype.Rectangle(restoredBeforeErase.X, restoredBeforeErase.Y, restoredBeforeErase.X + restoredBeforeErase.Width, restoredBeforeErase.Y + restoredBeforeErase.Height) }));
                    Assert.IsTrue(store.Redo(set.Id));
                    erasedRectangle = store.GetSegRegions(set.Id, image.Id).Single(value => value.ClassId == eraseClass.Id);
                    Assert.IsTrue(SegGeometryPrototype.Area(new Paths64 { new Path64(erasedRectangle.Points.Select(point => new Point64(point.X, point.Y)).ToList()) }) < 400d);
                    store.ExportAnnotationJson(set.Id, exportedJson);
                    store.Save();
                }

                using (var reopened = ProjectStore.Open(path))
                {
                    SegRegion restoredDot = reopened.GetSegRegions(setId, imageId).Single(value => value.ClassId == brushClassId);
                    Assert.AreEqual(1, restoredDot.Points.Count);
                    Assert.AreEqual(10, restoredDot.StrokeWidth);
                    AnnotationSet imported = reopened.ImportAnnotationJson(File.ReadAllText(exportedJson), "single-point-copy", false);
                    SegRegion importedDot = reopened.GetSegRegions(imported.Id, imageId).Single(value => value.ClassId == reopened.GetClasses(imported.Id).Single(value => value.Name == "dots").Id);
                    Assert.AreEqual(1, importedDot.Points.Count);
                    Assert.AreEqual(10, importedDot.StrokeWidth);
                    Paths64 importedDotArea = SegGeometryPrototype.Stroke(new Path64(importedDot.Points.Select(point => new Point64(point.X, point.Y)).ToList()), importedDot.StrokeWidth);
                    Assert.AreEqual(dotAreaSize, SegGeometryPrototype.Area(importedDotArea));
                }
            }
            finally { DeleteTemporaryProject(path); if (File.Exists(source)) File.Delete(source); if (File.Exists(exportedJson)) File.Delete(exportedJson); }
        }

        [TestMethod]
        [TestCategory("ManualPerformance")]
        public void Seventeen_thousand_point_seg_paths_complete_within_one_second_and_remain_undoable()
        {
            string path = TemporaryProjectPath();
            string source = Path.Combine(Path.GetDirectoryName(path), "source.png");
            try
            {
                CreateBitmap(source, Color.Red, ImageFormat.Png, 1000, 1000);
                using (var store = ProjectStore.Create(path))
                {
                    ProjectImage image = store.ImportFiles(new[] { source })[0];
                    AnnotationSet set = store.CreateAnnotationSet("performance", AnnotationSetType.Seg);
                    string classId = store.GetClasses(set.Id).Single().Id;
                    SegPoint[] points = SegPerformanceFixture.CreateSeventeenThousandPointEllipse();

                    AssertSegCompletionWithinOneSecond(() => store.AddSegPath(set.Id, image.Id, classId, SegRegionType.PolyGon, points, 0), "pen");
                    Assert.IsTrue(store.Undo(set.Id));
                    Assert.AreEqual(0, store.GetSegRegions(set.Id, image.Id).Count);
                    Assert.IsTrue(store.Redo(set.Id));
                    Assert.AreEqual(1, store.GetSegRegions(set.Id, image.Id).Count);

                    AssertSegCompletionWithinOneSecond(() => store.AddSegPath(set.Id, image.Id, classId, SegRegionType.PolyLine, points, 15), "brush");
                    Assert.IsTrue(store.Undo(set.Id));
                    Assert.AreEqual(1, store.GetSegRegions(set.Id, image.Id).Count);
                    Assert.IsTrue(store.Undo(set.Id));
                    Assert.AreEqual(0, store.GetSegRegions(set.Id, image.Id).Count);
                    Assert.IsFalse(store.Undo(set.Id));
                }
            }
            finally { DeleteTemporaryProject(path); if (File.Exists(source)) File.Delete(source); }
        }

        [TestMethod]
        public void Seg_point_insert_failure_rolls_back_region_points_and_undo_entry()
        {
            string path = TemporaryProjectPath();
            string source = Path.Combine(Path.GetDirectoryName(path), "source.png");
            try
            {
                CreateBitmap(source, Color.Red, ImageFormat.Png, 100, 100);
                using (var store = ProjectStore.Create(path))
                {
                    ProjectImage image = store.ImportFiles(new[] { source })[0];
                    AnnotationSet set = store.CreateAnnotationSet("rollback", AnnotationSetType.Seg);
                    string classId = store.GetClasses(set.Id).Single().Id;
                    using (var failureConnection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()))
                    {
                        failureConnection.Open();
                        using (var command = failureConnection.CreateCommand())
                        {
                            command.CommandText = "CREATE TRIGGER FailSegPoint BEFORE INSERT ON SegRegionPoint WHEN NEW.Ordinal = 2 BEGIN SELECT RAISE(ABORT, 'simulated point failure'); END;";
                            command.ExecuteNonQuery();
                        }
                    }

                    Assert.ThrowsException<SqliteException>(() => store.AddSegPath(set.Id, image.Id, classId, SegRegionType.PolyGon,
                        new[] { new SegPoint(10, 10), new SegPoint(30, 10), new SegPoint(30, 30), new SegPoint(10, 30) }, 0));
                    Assert.AreEqual(0, store.GetSegRegions(set.Id, image.Id).Count);
                    Assert.IsFalse(store.CanUndo(set.Id));
                }
            }
            finally { DeleteTemporaryProject(path); if (File.Exists(source)) File.Delete(source); }
        }

        [TestMethod]
        [TestCategory("ManualPerformance")]
        public void Seg_overlap_hole_and_continuous_operations_each_complete_within_one_second()
        {
            string path = TemporaryProjectPath();
            string source = Path.Combine(Path.GetDirectoryName(path), "source.png");
            try
            {
                CreateBitmap(source, Color.Red, ImageFormat.Png, 1000, 1000);
                using (var store = ProjectStore.Create(path))
                {
                    ProjectImage image = store.ImportFiles(new[] { source })[0];
                    AnnotationSet set = store.CreateAnnotationSet("performance", AnnotationSetType.Seg);
                    string classId = store.GetClasses(set.Id).Single().Id;
                    store.AddSegRegion(set.Id, image.Id, classId, SegRegionType.Rect, 100, 100, 700, 700);

                    AssertSegCompletionWithinOneSecond(
                        () => store.AddSegRegion(set.Id, image.Id, classId, SegRegionType.Rect, 700, 700, 200, 200),
                        "overlap union");

                    SegPoint[] closedEraser =
                    {
                        new SegPoint(300, 300), new SegPoint(600, 300), new SegPoint(600, 600),
                        new SegPoint(300, 600), new SegPoint(300, 300)
                    };
                    AssertSegCompletionWithinOneSecond(
                        () => store.EraseSegPath(set.Id, image.Id, classId, closedEraser, 30),
                        "hole generation");

                    string separateClassId = store.AddClass(set.Id, "continuous", "#123456").Id;
                    for (int index = 0; index < 10; index++)
                    {
                        int y = 50 + index * 20;
                        AssertSegCompletionWithinOneSecond(
                            () => store.AddSegPath(set.Id, image.Id, separateClassId, SegRegionType.PolyLine,
                                new[] { new SegPoint(20, y), new SegPoint(80, y) }, 5),
                            "continuous brush " + (index + 1));
                    }

                    Assert.IsTrue(store.Undo(set.Id));
                    Assert.AreEqual(9, store.GetSegRegions(set.Id, image.Id).Count(region => region.ClassId == separateClassId));
                }
            }
            finally { DeleteTemporaryProject(path); if (File.Exists(source)) File.Delete(source); }
        }

        [TestMethod]
        [TestCategory("ManualPerformance")]
        public void Largest_provided_seg_path_completes_within_one_second()
        {
            ImportedAnnotationSet imported = JsonAnnotationImport.Parse(
                File.ReadAllText(Path.Combine(FindRepositoryLabelsDirectory(), "seg_SEG_labeling (5).json")));
            ImportedImage sample = imported.Data
                .Where(image => image.Regions.Any(region => region.Points.Count > 0))
                .OrderByDescending(image => image.Regions.Max(region => region.Points.Count))
                .First();
            ImportedRegion pathRegion = sample.Regions.OrderByDescending(region => region.Points.Count).First();
            string path = TemporaryProjectPath();
            string source = Path.Combine(Path.GetDirectoryName(path), sample.FileName);
            try
            {
                CreateBitmap(source, Color.Red, ImageFormat.Png, sample.Width, sample.Height);
                using (var store = ProjectStore.Create(path))
                {
                    ProjectImage image = store.ImportFiles(new[] { source })[0];
                    AnnotationSet set = store.CreateAnnotationSet("provided sample", AnnotationSetType.Seg);
                    string classId = store.GetClasses(set.Id).Single().Id;
                    AssertSegCompletionWithinOneSecond(
                        () => store.AddSegPath(set.Id, image.Id, classId, pathRegion.Type, pathRegion.Points, pathRegion.StrokeWidth),
                        "provided " + pathRegion.Type);
                    Assert.AreEqual(pathRegion.Points.Count, store.GetSegRegions(set.Id, image.Id).Single().Points.Count);
                }
            }
            finally { DeleteTemporaryProject(path); if (File.Exists(source)) File.Delete(source); }
        }

        private static void AssertSegCompletionWithinOneSecond(Action action, string operation)
        {
            var stopwatch = Stopwatch.StartNew();
            action();
            stopwatch.Stop();
            Console.WriteLine("{0}: {1:F3} ms", operation, stopwatch.Elapsed.TotalMilliseconds);
            Assert.IsTrue(stopwatch.Elapsed <= TimeSpan.FromSeconds(1), "{0} completion took {1:F3} ms.", operation, stopwatch.Elapsed.TotalMilliseconds);
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
        public void Closed_seg_brush_polygon_unions_existing_same_class_area_without_erasing_it()
        {
            string path = TemporaryProjectPath();
            string source = Path.Combine(Path.GetDirectoryName(path), "source.png");
            try
            {
                CreateBitmap(source, Color.Red, ImageFormat.Png, 100, 100);
                string setId;
                string imageId;
                using (var store = ProjectStore.Create(path))
                {
                    ProjectImage image = store.ImportFiles(new[] { source }).Single();
                    AnnotationSet set = store.CreateAnnotationSet("regions", AnnotationSetType.Seg);
                    setId = set.Id;
                    imageId = image.Id;
                    AnnotationClass first = store.GetClasses(set.Id).Single();
                    AnnotationClass second = store.AddClass(set.Id, "other", "#123456");
                    store.AddSegRegion(set.Id, image.Id, first.Id, SegRegionType.Rect, 5, 30, 30, 20);
                    store.AddSegRegion(set.Id, image.Id, second.Id, SegRegionType.Rect, 5, 30, 30, 20);

                    store.AddSegPath(set.Id, image.Id, first.Id, SegRegionType.PolyGon, new[]
                    {
                        new SegPoint(10, 10), new SegPoint(90, 10), new SegPoint(90, 90), new SegPoint(10, 90)
                    }, 0);

                    SegRegion merged = store.GetSegRegions(set.Id, image.Id).Single(value => value.ClassId == first.Id);
                    var mergedPath = new Path64(merged.Points.Select(point => new Point64(point.X, point.Y)).ToList());
                    Paths64 preserved = Clipper.Intersect(new Paths64 { SegGeometryPrototype.Rectangle(5, 30, 35, 50) }, new Paths64 { mergedPath }, FillRule.NonZero);
                    Assert.AreEqual(1, store.GetSegRegions(set.Id, image.Id).Count(value => value.ClassId == first.Id));
                    Assert.AreEqual(600d, SegGeometryPrototype.Area(preserved));
                    Assert.AreEqual(6500d, SegGeometryPrototype.Area(new Paths64 { mergedPath }));
                    Assert.AreEqual(1, store.GetSegRegions(set.Id, image.Id).Count(value => value.ClassId == second.Id));

                    Assert.IsTrue(store.Undo(set.Id));
                    SegRegion restored = store.GetSegRegions(set.Id, image.Id).Single(value => value.ClassId == first.Id);
                    Assert.AreEqual(SegRegionType.Rect, restored.Type);
                    Assert.AreEqual(new Rectangle(5, 30, 30, 20), new Rectangle(restored.X, restored.Y, restored.Width, restored.Height));
                    Assert.IsTrue(store.Redo(set.Id));
                    SegRegion redone = store.GetSegRegions(set.Id, image.Id).Single(value => value.ClassId == first.Id);
                    Assert.AreEqual(6500d, SegGeometryPrototype.Area(new Paths64 { new Path64(redone.Points.Select(point => new Point64(point.X, point.Y)).ToList()) }));
                    store.Save();
                }
                using (var reopened = ProjectStore.Open(path))
                {
                    SegRegion persisted = reopened.GetSegRegions(setId, imageId).Single(value => value.ClassId == reopened.GetClasses(setId).First().Id);
                    Assert.AreEqual(6500d, SegGeometryPrototype.Area(new Paths64 { new Path64(persisted.Points.Select(point => new Point64(point.X, point.Y)).ToList()) }));
                }
            }
            finally { DeleteTemporaryProject(path); if (File.Exists(source)) File.Delete(source); }
        }

        [TestMethod]
        public void Closed_seg_brush_polygon_preserves_existing_same_class_area_when_drawn_counterclockwise()
        {
            string path = TemporaryProjectPath();
            string source = Path.Combine(Path.GetDirectoryName(path), "source.png");
            try
            {
                CreateBitmap(source, Color.Red, ImageFormat.Png, 100, 100);
                using (var store = ProjectStore.Create(path))
                {
                    ProjectImage image = store.ImportFiles(new[] { source }).Single();
                    AnnotationSet set = store.CreateAnnotationSet("regions", AnnotationSetType.Seg);
                    AnnotationClass annotationClass = store.GetClasses(set.Id).Single();
                    store.AddSegRegion(set.Id, image.Id, annotationClass.Id, SegRegionType.Rect, 5, 30, 30, 20);

                    SegPoint[] counterclockwise =
                    {
                        new SegPoint(10, 10), new SegPoint(10, 90), new SegPoint(90, 90), new SegPoint(90, 10)
                    };
                    store.AddSegPath(set.Id, image.Id, annotationClass.Id, SegRegionType.PolyGon, counterclockwise, 0);

                    SegRegion merged = store.GetSegRegions(set.Id, image.Id).Single(value => value.ClassId == annotationClass.Id);
                    var mergedPath = new Path64(merged.Points.Select(point => new Point64(point.X, point.Y)).ToList());
                    Paths64 preserved = Clipper.Intersect(new Paths64 { SegGeometryPrototype.Rectangle(5, 30, 35, 50) }, new Paths64 { mergedPath }, FillRule.NonZero);
                    Assert.AreEqual(600d, SegGeometryPrototype.Area(preserved));
                    Assert.AreEqual(6500d, SegGeometryPrototype.Area(new Paths64 { mergedPath }));
                }
            }
            finally { DeleteTemporaryProject(path); if (File.Exists(source)) File.Delete(source); }
        }

        [TestMethod]
        public void Restore_falls_back_from_native_replace_and_recovers_the_original_database_when_the_second_move_fails()
        {
            string path = TemporaryProjectPath();
            Action<string, string> originalNativeReplace = ProjectStore.NativeReplaceFile;
            Action<string, string> originalMove = ProjectStore.MoveFile;
            try
            {
                using (ProjectStore created = ProjectStore.Create(path)) { }
                string backup;
                using (ProjectStore store = ProjectStore.Open(path)) backup = store.GetBackups().Single().Path;
                ProjectStore.NativeReplaceFile = (source, destination) => { throw new UnauthorizedAccessException("Native replacement is unavailable."); };

                using (ProjectStore restored = ProjectStore.Restore(path, backup)) Assert.IsNotNull(restored);

                int moves = 0;
                ProjectStore.MoveFile = (source, destination) =>
                {
                    moves++;
                    if (moves == 2) throw new IOException("Simulated second move failure.");
                    originalMove(source, destination);
                };
                Assert.ThrowsException<IOException>(() => ProjectStore.Restore(path, backup));
                using (ProjectStore recovered = ProjectStore.Open(path)) Assert.IsNotNull(recovered);
                Assert.IsFalse(File.Exists(path + ".restore-original"));
            }
            finally
            {
                ProjectStore.NativeReplaceFile = originalNativeReplace;
                ProjectStore.MoveFile = originalMove;
                DeleteTemporaryProject(path);
            }
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
        public void Erasing_a_second_hole_rebuilds_the_network_and_undo_restores_the_previous_path()
        {
            string path = TemporaryProjectPath();
            string source = Path.Combine(Path.GetDirectoryName(path), "source.png");
            try
            {
                CreateBitmap(source, Color.Red, ImageFormat.Png, 100, 100);
                using (var store = ProjectStore.Create(path))
                {
                    ProjectImage image = store.ImportFiles(new[] { source })[0];
                    AnnotationSet set = store.CreateAnnotationSet("two holes", AnnotationSetType.Seg);
                    AnnotationClass annotationClass = store.GetClasses(set.Id).Single();
                    store.AddSegRegion(set.Id, image.Id, annotationClass.Id, SegRegionType.Rect, 10, 10, 80, 80);

                    store.EraseSegPath(set.Id, image.Id, annotationClass.Id, new[] { new SegPoint(30, 30) }, 12);
                    SegPoint[] firstPath = store.GetSegRegions(set.Id, image.Id).Single().Points.ToArray();
                    double firstArea = SegGeometryPrototype.Area(new Paths64 { new Path64(firstPath.Select(point => new Point64(point.X, point.Y)).ToList()) });
                    store.EraseSegPath(set.Id, image.Id, annotationClass.Id, new[] { new SegPoint(50, 30) }, 12);

                    SegPoint[] secondPath = store.GetSegRegions(set.Id, image.Id).Single().Points.ToArray();
                    Paths64 contours = Clipper.Union(new Paths64 { new Path64(secondPath.Select(point => new Point64(point.X, point.Y)).ToList()) }, FillRule.NonZero);
                    Assert.AreEqual(3, contours.Count, "The second erase must persist the outer contour and both holes.");
                    Assert.IsTrue(SegGeometryPrototype.Area(new Paths64 { new Path64(secondPath.Select(point => new Point64(point.X, point.Y)).ToList()) }) < firstArea);
                    CollectionAssert.AreEqual(secondPath.Select(PointSignature).ToArray(), SegGeometryPrototype.Rebridge(new Path64(secondPath.Select(point => new Point64(point.X, point.Y)).ToList())).Select(PointSignature).ToArray(), "The stored network must already be the stable minimum after the new hole is added.");
                    Assert.IsTrue(store.Undo(set.Id));
                    CollectionAssert.AreEqual(firstPath.Select(PointSignature).ToArray(), store.GetSegRegions(set.Id, image.Id).Single().Points.Select(PointSignature).ToArray());
                    Assert.IsTrue(store.Redo(set.Id));
                    CollectionAssert.AreEqual(secondPath.Select(PointSignature).ToArray(), store.GetSegRegions(set.Id, image.Id).Single().Points.Select(PointSignature).ToArray());
                }
            }
            finally { DeleteTemporaryProject(path); if (File.Exists(source)) File.Delete(source); }
        }

        [TestMethod]
        public void Updating_a_SEG_polygon_rebuilds_the_shortest_hole_network_and_roundtrips_history_and_json()
        {
            string path = TemporaryProjectPath();
            string source = Path.Combine(Path.GetDirectoryName(path), "source.png");
            string exportedJson = Path.Combine(Path.GetDirectoryName(path), "export.json");
            SegPoint[] originalPoints = SeparateOuterBridgePath();
            SegPoint[] movedPoints = originalPoints.Select(point => new SegPoint(point.X + 1, point.Y + 1)).ToArray();
            string[] expected;
            try
            {
                CreateBitmap(source, Color.Red, ImageFormat.Png, 120, 120);
                string setId;
                string imageId;
                string regionId;
                using (var store = ProjectStore.Create(path))
                {
                    ProjectImage image = store.ImportFiles(new[] { source })[0];
                    AnnotationSet set = store.CreateAnnotationSet("bridges", AnnotationSetType.Seg);
                    AnnotationClass annotationClass = store.GetClasses(set.Id).Single();
                    SegRegion region = store.AddSegPath(set.Id, image.Id, annotationClass.Id, SegRegionType.PolyGon, originalPoints, 0);
                    setId = set.Id;
                    imageId = image.Id;
                    regionId = region.Id;

                    store.UpdateSegRegions(new[] { new SegRegion(region.Id, set.Id, image.Id, annotationClass.Id, SegRegionType.PolyGon, 0, 0, 0, 0, region.ZOrder, movedPoints) });

                    SegRegion updated = store.GetSegRegions(set.Id, image.Id).Single();
                    expected = updated.Points.Select(PointSignature).ToArray();
                    CollectionAssert.AreNotEqual(movedPoints.Select(PointSignature).ToArray(), expected, "The obsolete outer-to-second-hole bridge must be replaced.");
                    Assert.AreEqual(2, CountSegment(updated.Points, 21, 1, 21, 21), "The existing shortest outer-to-first-hole bridge is retained.");
                    Assert.AreEqual(2, CountSegment(updated.Points, 31, 21, 41, 21), "The shorter hole-to-hole bridge joins the network.");
                    Assert.AreEqual(0, CountSegment(updated.Points, 41, 1, 41, 21), "The longer second outer bridge is removed.");
                    Assert.AreEqual(9800d, SegGeometryPrototype.Area(new Paths64 { new Path64(updated.Points.Select(point => new Point64(point.X, point.Y)).ToList()) }));
                    Assert.IsTrue(store.Undo(set.Id));
                    CollectionAssert.AreEqual(originalPoints.Select(PointSignature).ToArray(), store.GetSegRegions(set.Id, image.Id).Single().Points.Select(PointSignature).ToArray());
                    Assert.IsTrue(store.Redo(set.Id));
                    CollectionAssert.AreEqual(expected, store.GetSegRegions(set.Id, image.Id).Single().Points.Select(PointSignature).ToArray());
                    store.ExportAnnotationJson(set.Id, exportedJson);
                    store.Save();
                }

                using (var reopened = ProjectStore.Open(path))
                {
                    CollectionAssert.AreEqual(expected, reopened.GetSegRegions(setId, imageId).Single(value => value.Id == regionId).Points.Select(PointSignature).ToArray());
                    AnnotationSet roundtrip = reopened.ImportAnnotationJson(File.ReadAllText(exportedJson), "roundtrip", false);
                    CollectionAssert.AreEqual(expected, reopened.GetSegRegions(roundtrip.Id, imageId).Single().Points.Select(PointSignature).ToArray());
                }
            }
            finally { DeleteTemporaryProject(path); if (File.Exists(source)) File.Delete(source); }
        }

        [TestMethod]
        public void Unbridgeable_SEG_geometry_update_preserves_the_region_and_does_not_add_undo_history()
        {
            string path = TemporaryProjectPath();
            string source = Path.Combine(Path.GetDirectoryName(path), "source.png");
            SegPoint[] originalPoints =
            {
                new SegPoint(10, 10), new SegPoint(60, 10), new SegPoint(60, 20), new SegPoint(80, 20),
                new SegPoint(80, 30), new SegPoint(90, 30), new SegPoint(90, 20), new SegPoint(80, 20),
                new SegPoint(60, 20), new SegPoint(60, 60), new SegPoint(10, 60)
            };
            try
            {
                CreateBitmap(source, Color.Red, ImageFormat.Png, 100, 100);
                using (var store = ProjectStore.Create(path))
                {
                    ProjectImage image = store.ImportFiles(new[] { source })[0];
                    AnnotationSet set = store.CreateAnnotationSet("unbridgeable", AnnotationSetType.Seg);
                    AnnotationClass annotationClass = store.GetClasses(set.Id).Single();
                    SegRegion region = store.AddSegPath(set.Id, image.Id, annotationClass.Id, SegRegionType.PolyGon, originalPoints, 0);
                    SegPoint[] invalidEdit = originalPoints.Select(point => new SegPoint(point.X + 1, point.Y + 1)).ToArray();

                    Assert.ThrowsException<SegGeometryPrototype.BridgeNetworkException>(() => store.UpdateSegRegions(new[] { new SegRegion(region.Id, set.Id, image.Id, annotationClass.Id, SegRegionType.PolyGon, 0, 0, 0, 0, region.ZOrder, invalidEdit) }));

                    Assert.IsTrue(store.CanUndo(set.Id));
                    Assert.IsFalse(store.CanRedo(set.Id));
                    CollectionAssert.AreEqual(originalPoints.Select(PointSignature).ToArray(), store.GetSegRegions(set.Id, image.Id).Single().Points.Select(PointSignature).ToArray());
                    Assert.IsTrue(store.Undo(set.Id), "A failed edit must not insert its own history record ahead of the original add.");
                    Assert.AreEqual(0, store.GetSegRegions(set.Id, image.Id).Count);
                }
            }
            finally { DeleteTemporaryProject(path); if (File.Exists(source)) File.Delete(source); }
        }

        [TestMethod]
        public void Unedited_imported_SEG_bridge_path_keeps_its_point_order_on_save_and_export()
        {
            string path = TemporaryProjectPath();
            string source = Path.Combine(Path.GetDirectoryName(path), "source.png");
            string exportedJson = Path.Combine(Path.GetDirectoryName(path), "export.json");
            SegPoint[] originalPoints = SeparateOuterBridgePath();
            string pointsJson = String.Join(",", originalPoints.Select(point => "[" + point.X + "," + point.Y + "]"));
            string json = "{\"label_type\":\"seg\",\"source\":\"labelset\",\"time\":\"2026-01-01T00:00:00Z\",\"version\":\"4.5.2.2\",\"classes\":[{\"name\":\"part\",\"color\":\"#FF0000\"}],\"data\":[{\"fileName\":\"source.png\",\"set\":\"\",\"classLabel\":\"\",\"regionLabel\":[{\"className\":\"part\",\"type\":\"PolyGon\",\"points\":[" + pointsJson + "]}],\"retestset\":0,\"rotation_angle\":0,\"width\":120,\"height\":120}]}";
            try
            {
                CreateBitmap(source, Color.Red, ImageFormat.Png, 120, 120);
                string setId;
                string imageId;
                using (var store = ProjectStore.Create(path))
                {
                    ProjectImage image = store.ImportFiles(new[] { source })[0];
                    AnnotationSet set = store.ImportAnnotationJson(json, "imported", false);
                    setId = set.Id;
                    imageId = image.Id;
                    CollectionAssert.AreEqual(originalPoints.Select(PointSignature).ToArray(), store.GetSegRegions(set.Id, image.Id).Single().Points.Select(PointSignature).ToArray());
                    store.Save();
                }
                using (var reopened = ProjectStore.Open(path))
                {
                    CollectionAssert.AreEqual(originalPoints.Select(PointSignature).ToArray(), reopened.GetSegRegions(setId, imageId).Single().Points.Select(PointSignature).ToArray());
                    reopened.ExportAnnotationJson(setId, exportedJson);
                    AnnotationSet roundtrip = reopened.ImportAnnotationJson(File.ReadAllText(exportedJson), "roundtrip", false);
                    CollectionAssert.AreEqual(originalPoints.Select(PointSignature).ToArray(), reopened.GetSegRegions(roundtrip.Id, imageId).Single().Points.Select(PointSignature).ToArray());
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
                    Assert.AreEqual(3, summary.AnnotationCount);
                    Assert.IsFalse(store.GetImageAnnotationSummaries(store.CreateAnnotationSet("empty", AnnotationSetType.Seg).Id).Any());
                }
            }
            finally { DeleteTemporaryProject(path); if (File.Exists(source)) File.Delete(source); }
        }

        [TestMethod]
        public void Image_annotation_summary_count_matches_obd_and_seg_objects_through_history_and_import()
        {
            string path = TemporaryProjectPath();
            string source = Path.Combine(Path.GetDirectoryName(path), "source.png");
            try
            {
                CreateBitmap(source, Color.Red, ImageFormat.Png, 100, 100);
                using (var store = ProjectStore.Create(path))
                {
                    ProjectImage image = store.ImportFiles(new[] { source }).Single();
                    AnnotationSet obd = store.CreateAnnotationSet("boxes", AnnotationSetType.Obd);
                    string obdClassId = store.GetClasses(obd.Id).Single().Id;
                    store.AddObdAnnotation(obd.Id, image.Id, obdClassId, 1, 1, 10, 10);
                    store.AddObdAnnotation(obd.Id, image.Id, obdClassId, 20, 20, 10, 10);
                    Assert.AreEqual(store.GetObdAnnotations(obd.Id, image.Id).Count, store.GetImageAnnotationSummaries(obd.Id).Single().AnnotationCount);
                    store.DeleteObdAnnotation(store.GetObdAnnotations(obd.Id, image.Id)[0].Id);
                    Assert.AreEqual(store.GetObdAnnotations(obd.Id, image.Id).Count, store.GetImageAnnotationSummaries(obd.Id).Single().AnnotationCount);
                    Assert.IsTrue(store.Undo(obd.Id));
                    Assert.AreEqual(store.GetObdAnnotations(obd.Id, image.Id).Count, store.GetImageAnnotationSummaries(obd.Id).Single().AnnotationCount);
                    Assert.IsTrue(store.Redo(obd.Id));
                    Assert.AreEqual(store.GetObdAnnotations(obd.Id, image.Id).Count, store.GetImageAnnotationSummaries(obd.Id).Single().AnnotationCount);

                    AnnotationSet seg = store.CreateAnnotationSet("regions", AnnotationSetType.Seg);
                    string segClassId = store.GetClasses(seg.Id).Single().Id;
                    store.AddSegRegion(seg.Id, image.Id, segClassId, SegRegionType.Rect, 1, 1, 20, 20);
                    store.AddSegRegion(seg.Id, image.Id, segClassId, SegRegionType.Rect, 10, 1, 20, 20);
                    Assert.AreEqual(store.GetSegRegions(seg.Id, image.Id).Count, store.GetImageAnnotationSummaries(seg.Id).Single().AnnotationCount);
                    store.EraseSegPath(seg.Id, image.Id, segClassId, new[] { new SegPoint(15, 2), new SegPoint(15, 11), new SegPoint(15, 20) }, 4);
                    Assert.AreEqual(store.GetSegRegions(seg.Id, image.Id).Count, store.GetImageAnnotationSummaries(seg.Id).Single().AnnotationCount);
                    Assert.IsTrue(store.Undo(seg.Id));
                    Assert.AreEqual(store.GetSegRegions(seg.Id, image.Id).Count, store.GetImageAnnotationSummaries(seg.Id).Single().AnnotationCount);

                    string json = "{\"label_type\":\"seg\",\"source\":\"labelset\",\"time\":\"2026-01-01T00:00:00Z\",\"version\":\"4.5.2.2\",\"classes\":[{\"name\":\"part\",\"color\":\"#FF0000\"}],\"data\":[{\"fileName\":\"source.png\",\"set\":\"\",\"classLabel\":\"\",\"regionLabel\":[{\"className\":\"part\",\"type\":\"Rect\",\"x\":1,\"y\":1,\"width\":2,\"height\":2}],\"retestset\":0,\"rotation_angle\":0,\"width\":100,\"height\":100}]}";
                    AnnotationSet imported = store.ImportAnnotationJson(json, "imported", false);
                    Assert.AreEqual(store.GetSegRegions(imported.Id, image.Id).Count, store.GetImageAnnotationSummaries(imported.Id).Single().AnnotationCount);
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
                    string invalidColor = valid.Replace("rgba(1, 2, 3, 1)-custom", "rgba(256, 2, 3, 1)");
                    Assert.ThrowsException<JsonAnnotationImportException>(() => store.ImportAnnotationJson(invalidColor, "bad-color", false));
                    Assert.AreEqual(1, store.GetAnnotationSets().Count);
                }
            }
            finally { DeleteTemporaryProject(path); if (File.Exists(source)) File.Delete(source); }
        }

        [TestMethod]
        public void Cla_json_import_into_selected_set_adds_only_empty_images_and_reuses_existing_class_color()
        {
            string path = TemporaryProjectPath();
            string first = Path.Combine(Path.GetDirectoryName(path), "first.png");
            string second = Path.Combine(Path.GetDirectoryName(path), "second.png");
            string third = Path.Combine(Path.GetDirectoryName(path), "third.png");
            try
            {
                CreateBitmap(first, Color.Red, ImageFormat.Png, 20, 20);
                CreateBitmap(second, Color.Blue, ImageFormat.Png, 20, 20);
                CreateBitmap(third, Color.Green, ImageFormat.Png, 20, 20);
                using (var store = ProjectStore.Create(path))
                {
                    ProjectImage[] images = store.ImportFiles(new[] { first, second, third }).ToArray();
                    AnnotationSet target = store.CreateAnnotationSet("target", AnnotationSetType.Cla);
                    AnnotationClass existing = store.AddClass(target.Id, "cat", "#123456");
                    store.SetClaClass(target.Id, images[0].Id, existing.Id);
                    bool canUndoBeforeImport = store.CanUndo(target.Id);
                    string json = "{\"label_type\":\"cla\",\"source\":\"labelset\",\"time\":\"2026-01-01T00:00:00Z\",\"version\":\"4.5.2.2\",\"classes\":[{\"name\":\"cat\",\"color\":\"#FFFFFF\"},{\"name\":\"dog\",\"color\":\"#ABCDEF\"}],\"data\":[{\"fileName\":\"first.png\",\"set\":\"train\",\"classLabel\":\"cat\",\"regionLabel\":[],\"retestset\":0,\"rotation_angle\":0,\"width\":20,\"height\":20},{\"fileName\":\"second.png\",\"set\":\"test\",\"classLabel\":\"dog\",\"regionLabel\":[],\"retestset\":1,\"rotation_angle\":0,\"width\":20,\"height\":20}]}";

                    store.ImportAnnotationJsonIntoSet(json, target.Id, true, AnnotationImportMode.AddToEmptyImages, true, DatasetImportMode.AddToUnassignedImages, false);

                    Assert.AreEqual(existing.Id, store.GetClaClassId(target.Id, images[0].Id));
                    Assert.AreEqual("rgba(18, 52, 86, 1)", store.GetClasses(target.Id).Single(value => value.Name == "cat").Color);
                    Assert.AreEqual("dog", store.GetClasses(target.Id).Single(value => value.Name == "dog").Name);
                    Assert.AreEqual(store.GetClasses(target.Id).Single(value => value.Name == "dog").Id, store.GetClaClassId(target.Id, images[1].Id));
                    Assert.AreEqual(DatasetAssignmentStatus.Test, store.GetDatasetAssignments(target.Id).Single(value => value.ImageId == images[1].Id).Status);
                    Assert.IsTrue(store.GetDatasetAssignments(target.Id).Single(value => value.ImageId == images[1].Id).Retest, "SET import must apply the JSON Retest value.");
                    string omittedImageClassId = store.GetClaClassId(target.Id, images[1].Id);
                    Assert.AreEqual(canUndoBeforeImport, store.CanUndo(target.Id), "JSON import must not create Undo history.");

                    string datasetOnly = json.Replace("second.png", "third.png");
                    store.ImportAnnotationJsonIntoSet(datasetOnly, target.Id, false, AnnotationImportMode.AddToEmptyImages, true, DatasetImportMode.AddToUnassignedImages, false);
                    Assert.IsNull(store.GetClaClassId(target.Id, images[2].Id), "Dataset-only import must not write CLA annotations.");
                    Assert.AreEqual(DatasetAssignmentStatus.Test, store.GetDatasetAssignments(target.Id).Single(value => value.ImageId == images[2].Id).Status);
                    Assert.IsTrue(store.GetDatasetAssignments(target.Id).Single(value => value.ImageId == images[2].Id).Retest);

                    string replaceJson = json.Replace("\"classLabel\":\"cat\"", "\"classLabel\":\"\"").Replace("second.png", "third.png");
                    store.SetDatasetAssignments(target.Id, new[] { images[0].Id }, DatasetAssignmentStatus.Test);
                    store.SetRetest(target.Id, new[] { images[0].Id }, true);
                    store.ImportAnnotationJsonIntoSet(replaceJson, target.Id, true, AnnotationImportMode.ReplaceMatchingImages, true, DatasetImportMode.ReplaceMatchingImages, false);
                    Assert.IsNull(store.GetClaClassId(target.Id, images[0].Id), "Replace mode must clear an explicitly empty CLA annotation.");
                    Assert.AreEqual(DatasetAssignmentStatus.Train, store.GetDatasetAssignments(target.Id).Single(value => value.ImageId == images[0].Id).Status);
                    Assert.IsFalse(store.GetDatasetAssignments(target.Id).Single(value => value.ImageId == images[0].Id).Retest, "Replace mode must update Retest to the JSON value.");
                    Assert.AreEqual(omittedImageClassId, store.GetClaClassId(target.Id, images[1].Id), "Images omitted from JSON must retain their existing annotation.");

                    string mismatched = json.Replace("\"label_type\":\"cla\"", "\"label_type\":\"seg\"");
                    Assert.ThrowsException<JsonAnnotationImportException>(() => store.ImportAnnotationJsonIntoSet(mismatched, target.Id, true, AnnotationImportMode.AddToEmptyImages, true, DatasetImportMode.AddToUnassignedImages, false));
                    Assert.ThrowsException<JsonAnnotationImportException>(() => store.ImportAnnotationJsonIntoSet(json, target.Id, true, AnnotationImportMode.AddToAllImages, false, DatasetImportMode.AddToUnassignedImages, false), "CLA must not accept the OBD/SEG-only add-to-all mode.");
                }
            }
            finally { DeleteTemporaryProject(path); if (File.Exists(first)) File.Delete(first); if (File.Exists(second)) File.Delete(second); if (File.Exists(third)) File.Delete(third); }
        }

        [TestMethod]
        public void Cla_json_import_matches_exact_display_names_and_skips_unmatched_or_ambiguous_names()
        {
            string path = TemporaryProjectPath();
            string directory = Path.GetDirectoryName(path);
            string upperPath = Path.Combine(directory, "upper-source.png");
            string lowerPath = Path.Combine(directory, "lower-source.png");
            string duplicateOne = Path.Combine(directory, "duplicate-one-source.png");
            string duplicateTwo = Path.Combine(directory, "duplicate-two-source.png");
            try
            {
                CreateBitmap(upperPath, Color.Red, ImageFormat.Png, 20, 20);
                CreateBitmap(lowerPath, Color.Blue, ImageFormat.Png, 20, 20);
                CreateBitmap(duplicateOne, Color.Green, ImageFormat.Png, 20, 20);
                CreateBitmap(duplicateTwo, Color.Yellow, ImageFormat.Png, 20, 20);
                using (var store = ProjectStore.Create(path))
                {
                    ProjectImage[] importedImages = store.ImportFiles(new[] { upperPath, lowerPath, duplicateOne, duplicateTwo }).ToArray();
                    store.CompleteOperation((connection, transaction) =>
                    {
                        using (var command = connection.CreateCommand())
                        {
                            command.Transaction = transaction;
                            command.CommandText = "UPDATE ImageRecord SET OriginalFileName = $name WHERE Id = $id;";
                            command.Parameters.AddWithValue("$name", "Case.png");
                            command.Parameters.AddWithValue("$id", importedImages.Single(value => value.OriginalFileName == "upper-source.png").Id);
                            command.ExecuteNonQuery();
                            command.Parameters.Clear();
                            command.Parameters.AddWithValue("$name", "case.png");
                            command.Parameters.AddWithValue("$id", importedImages.Single(value => value.OriginalFileName == "lower-source.png").Id);
                            command.ExecuteNonQuery();
                            command.Parameters.Clear();
                            command.Parameters.AddWithValue("$name", "same.png");
                            command.Parameters.AddWithValue("$id", importedImages.Single(value => value.OriginalFileName == "duplicate-one-source.png").Id);
                            command.ExecuteNonQuery();
                            command.Parameters.Clear();
                            command.Parameters.AddWithValue("$name", "same.png");
                            command.Parameters.AddWithValue("$id", importedImages.Single(value => value.OriginalFileName == "duplicate-two-source.png").Id);
                            command.ExecuteNonQuery();
                        }
                    });
                    ProjectImage[] images = store.GetImages().ToArray();
                    ProjectImage upperImage = images.Single(value => value.OriginalFileName == "Case.png");
                    ProjectImage lowerImage = images.Single(value => value.OriginalFileName == "case.png");
                    ProjectImage[] ambiguousImages = images.Where(value => value.OriginalFileName == "same.png").ToArray();
                    AnnotationSet target = store.CreateAnnotationSet("target", AnnotationSetType.Cla);
                    AnnotationClass upperClass = store.AddClass(target.Id, "upper", "#FF0000");
                    AnnotationClass lowerClass = store.AddClass(target.Id, "lower", "#0000FF");
                    string json = "{\"label_type\":\"cla\",\"source\":\"labelset\",\"time\":\"2026-01-01T00:00:00Z\",\"version\":\"4.5.2.2\",\"classes\":[{\"name\":\"upper\",\"color\":\"#FF0000\"},{\"name\":\"lower\",\"color\":\"#0000FF\"},{\"name\":\"ambiguous\",\"color\":\"#00FF00\"}],\"data\":[{\"fileName\":\"Case.png\",\"set\":\"\",\"classLabel\":\"upper\",\"regionLabel\":[],\"retestset\":0,\"rotation_angle\":0,\"width\":20,\"height\":20},{\"fileName\":\"case.png\",\"set\":\"\",\"classLabel\":\"lower\",\"regionLabel\":[],\"retestset\":0,\"rotation_angle\":0,\"width\":20,\"height\":20},{\"fileName\":\"CASE.png\",\"set\":\"\",\"classLabel\":\"upper\",\"regionLabel\":[],\"retestset\":0,\"rotation_angle\":0,\"width\":20,\"height\":20},{\"fileName\":\"same.png\",\"set\":\"\",\"classLabel\":\"ambiguous\",\"regionLabel\":[],\"retestset\":0,\"rotation_angle\":0,\"width\":20,\"height\":20}]}";

                    AnnotationJsonImportResult importResult = store.ImportAnnotationJsonIntoSet(json, target.Id, true, AnnotationImportMode.AddToEmptyImages, false, DatasetImportMode.AddToUnassignedImages, false);

                    Assert.AreEqual(upperClass.Id, store.GetClaClassId(target.Id, upperImage.Id));
                    Assert.AreEqual(lowerClass.Id, store.GetClaClassId(target.Id, lowerImage.Id));
                    Assert.AreEqual(2, ambiguousImages.Length);
                    foreach (ProjectImage image in ambiguousImages) Assert.IsNull(store.GetClaClassId(target.Id, image.Id), "A duplicated display name is ambiguous and must not label either image.");
                    AnnotationJsonImportWarning warning = importResult.Warnings.Single();
                    Assert.AreEqual("same.png", warning.FileName);
                    Assert.AreEqual("data[3]", warning.SourceLocation);
                    Assert.AreEqual(AnnotationJsonImportWarningReason.AmbiguousImageFileName, warning.Reason);
                    Assert.IsTrue(store.GetClasses(target.Id).Any(value => value.Name == "ambiguous"), "All imported CLASS definitions are kept when at least one image uniquely matches.");

                    string zeroMatch = json.Replace("Case.png", " Case.png").Replace("case.png", "case").Replace("CASE.png", "CASE").Replace("same.png", "same").Replace("ambiguous", "unused");
                    int classCountBefore = store.GetClasses(target.Id).Count;
                    AnnotationJsonImportResult zeroMatchResult = store.ImportAnnotationJsonIntoSet(zeroMatch, target.Id, true, AnnotationImportMode.ReplaceMatchingImages, false, DatasetImportMode.AddToUnassignedImages, false);
                    Assert.AreEqual(0, zeroMatchResult.Warnings.Count, "Unmatched images must be skipped without warnings.");
                    Assert.AreEqual(classCountBefore, store.GetClasses(target.Id).Count, "A zero-match import must not add classes.");
                    Assert.AreEqual(upperClass.Id, store.GetClaClassId(target.Id, upperImage.Id), "A zero-match import must not clear existing annotations.");
                    Assert.AreEqual(lowerClass.Id, store.GetClaClassId(target.Id, lowerImage.Id), "A zero-match import must not clear existing annotations.");
                }
            }
            finally { DeleteTemporaryProject(path); }
        }

        [TestMethod]
        public void Json_import_into_obd_and_seg_selected_sets_preserves_model_and_geometry()
        {
            string path = TemporaryProjectPath();
            string source = Path.Combine(Path.GetDirectoryName(path), "sample.png");
            try
            {
                CreateBitmap(source, Color.Red, ImageFormat.Png, 20, 20);
                using (var store = ProjectStore.Create(path))
                {
                    ProjectImage image = store.ImportFiles(new[] { source }).Single();
                    AnnotationSet obd = store.CreateAnnotationSet("obd", AnnotationSetType.Obd);
                    string obdJson = "{\"label_type\":\"obd\",\"source\":\"labelset\",\"time\":\"2026-01-01T00:00:00Z\",\"version\":\"4.5.2.2\",\"classes\":[{\"name\":\"part\",\"color\":\"#FF0000\"}],\"data\":[{\"fileName\":\"sample.png\",\"set\":\"\",\"classLabel\":\"\",\"regionLabel\":[{\"className\":\"part\",\"type\":\"Rect\",\"x\":1,\"y\":2,\"width\":3,\"height\":4}],\"retestset\":0,\"rotation_angle\":0,\"width\":20,\"height\":20}]}";
                    store.ImportAnnotationJsonIntoSet(obdJson, obd.Id, true, AnnotationImportMode.AddToEmptyImages, false, DatasetImportMode.AddToUnassignedImages, false);
                    Assert.AreEqual(1, store.GetObdAnnotations(obd.Id, image.Id).Count);
                    store.ImportAnnotationJsonIntoSet(obdJson, obd.Id, true, AnnotationImportMode.AddToEmptyImages, false, DatasetImportMode.AddToUnassignedImages, false);
                    Assert.AreEqual(1, store.GetObdAnnotations(obd.Id, image.Id).Count, "Add-to-empty mode must leave a nonempty image unchanged.");
                    string obdAppendJson = obdJson.Replace("\"regionLabel\":[{\"className\":\"part\",\"type\":\"Rect\",\"x\":1,\"y\":2,\"width\":3,\"height\":4}]", "\"regionLabel\":[{\"className\":\"part\",\"type\":\"Rect\",\"x\":1,\"y\":2,\"width\":3,\"height\":4},{\"className\":\"part\",\"type\":\"Rect\",\"x\":5,\"y\":2,\"width\":3,\"height\":4},{\"className\":\"part\",\"type\":\"Rect\",\"x\":9,\"y\":2,\"width\":3,\"height\":4}]");
                    store.ImportAnnotationJsonIntoSet(obdAppendJson, obd.Id, true, AnnotationImportMode.AddToAllImages, false, DatasetImportMode.AddToUnassignedImages, false);
                    ObdAnnotation[] obdAnnotations = store.GetObdAnnotations(obd.Id, image.Id).ToArray();
                    Assert.AreEqual(4, obdAnnotations.Length, "Add-to-all mode must preserve old OBD annotations and append every JSON object.");
                    CollectionAssert.AreEqual(new[] { 0, 1, 2, 3 }, obdAnnotations.Select(value => value.ZOrder).ToArray());
                    CollectionAssert.AreEqual(new[] { 1, 1, 5, 9 }, obdAnnotations.Select(value => value.X).ToArray(), "New objects must retain source order above existing annotations.");

                    AnnotationSet seg = store.CreateAnnotationSet("seg", AnnotationSetType.Seg);
                    string segJson = "{\"label_type\":\"seg\",\"source\":\"labelset\",\"time\":\"2026-01-01T00:00:00Z\",\"version\":\"4.5.2.2\",\"classes\":[{\"name\":\"part\",\"color\":\"#FF0000\"}],\"data\":[{\"fileName\":\"sample.png\",\"set\":\"\",\"classLabel\":\"\",\"regionLabel\":[{\"className\":\"part\",\"type\":\"PolyLine\",\"strokeWidth\":2,\"points\":[[1,2],[3,4]]}],\"retestset\":0,\"rotation_angle\":0,\"width\":20,\"height\":20}]}";
                    store.ImportAnnotationJsonIntoSet(segJson, seg.Id, true, AnnotationImportMode.AddToEmptyImages, false, DatasetImportMode.AddToUnassignedImages, false);
                    Assert.AreEqual(1, store.GetSegRegions(seg.Id, image.Id).Count);
                    Assert.AreEqual(SegRegionType.PolyLine, store.GetSegRegions(seg.Id, image.Id).Single().Type);
                    store.ImportAnnotationJsonIntoSet(segJson, seg.Id, true, AnnotationImportMode.AddToAllImages, false, DatasetImportMode.AddToUnassignedImages, false);
                    SegRegion[] segRegions = store.GetSegRegions(seg.Id, image.Id).ToArray();
                    Assert.AreEqual(2, segRegions.Length, "Overlapping appended SEG regions must remain separate.");
                    CollectionAssert.AreEqual(new[] { 0, 1 }, segRegions.Select(value => value.ZOrder).ToArray());
                }
            }
            finally { DeleteTemporaryProject(path); if (File.Exists(source)) File.Delete(source); }
        }

        [TestMethod]
        public void Json_import_skips_dimension_mismatches_and_invalid_regions_with_warnings()
        {
            string path = TemporaryProjectPath();
            string mismatchPath = Path.Combine(Path.GetDirectoryName(path), "mismatch.png");
            string validPath = Path.Combine(Path.GetDirectoryName(path), "valid.png");
            try
            {
                CreateBitmap(mismatchPath, Color.Red, ImageFormat.Png, 20, 20);
                CreateBitmap(validPath, Color.Blue, ImageFormat.Png, 20, 20);
                using (var store = ProjectStore.Create(path))
                {
                    ProjectImage[] images = store.ImportFiles(new[] { mismatchPath, validPath }).ToArray();
                    AnnotationSet target = store.CreateAnnotationSet("target", AnnotationSetType.Obd);
                    AnnotationClass oldClass = store.AddClass(target.Id, "old", "#123456");
                    AnnotationClass importedClass = store.AddClass(target.Id, "part", "#ABCDEF");
                    store.AddObdAnnotation(target.Id, images[0].Id, oldClass.Id, 1, 1, 3, 3);
                    store.AddObdAnnotation(target.Id, images[1].Id, oldClass.Id, 1, 1, 3, 3);
                    string json = "{\"label_type\":\"obd\",\"source\":\"labelset\",\"time\":\"2026-01-01T00:00:00Z\",\"version\":\"4.5.2.2\",\"classes\":[{\"name\":\"part\",\"color\":\"#ABCDEF\"}],\"data\":[{\"fileName\":\"mismatch.png\",\"set\":\"\",\"classLabel\":\"\",\"regionLabel\":[{\"className\":\"part\",\"type\":\"Rect\",\"x\":2,\"y\":2,\"width\":3,\"height\":3}],\"retestset\":0,\"rotation_angle\":0,\"width\":19,\"height\":20},{\"fileName\":\"valid.png\",\"set\":\"\",\"classLabel\":\"\",\"regionLabel\":[{\"className\":\"part\",\"type\":\"Rect\",\"x\":2,\"y\":2,\"width\":3,\"height\":3},{\"className\":\"part\",\"type\":\"Rect\",\"x\":18,\"y\":2,\"width\":4,\"height\":3},{\"className\":\"missing\",\"type\":\"Rect\",\"x\":8,\"y\":8,\"width\":3,\"height\":3}],\"retestset\":0,\"rotation_angle\":0,\"width\":20,\"height\":20}]}";

                    AnnotationJsonImportResult result = store.ImportAnnotationJsonIntoSet(json, target.Id, true, AnnotationImportMode.ReplaceMatchingImages, false, DatasetImportMode.AddToUnassignedImages, false);

                    Assert.AreEqual(oldClass.Id, store.GetObdAnnotations(target.Id, images[0].Id).Single().ClassId, "A size-mismatched image must be left unchanged.");
                    ObdAnnotation[] validAnnotations = store.GetObdAnnotations(target.Id, images[1].Id).ToArray();
                    Assert.AreEqual(1, validAnnotations.Length, "Replace must clear the prior annotation and keep only the valid imported region.");
                    Assert.AreEqual(importedClass.Id, validAnnotations[0].ClassId);
                    Assert.AreEqual(2, validAnnotations[0].X);
                    Assert.AreEqual(3, result.Warnings.Count);
                    Assert.AreEqual("mismatch.png", result.Warnings[0].FileName);
                    Assert.AreEqual("data[0]", result.Warnings[0].SourceLocation);
                    Assert.AreEqual(AnnotationJsonImportWarningReason.ImageDimensionsMismatch, result.Warnings[0].Reason);
                    Assert.AreEqual("data[1].regionLabel[1]", result.Warnings[1].SourceLocation);
                    Assert.AreEqual(AnnotationJsonImportWarningReason.InvalidAnnotation, result.Warnings[1].Reason);
                    Assert.AreEqual("data[1].regionLabel[2]", result.Warnings[2].SourceLocation);
                    Assert.AreEqual(AnnotationJsonImportWarningReason.UnknownClassReference, result.Warnings[2].Reason);
                }
            }
            finally { DeleteTemporaryProject(path); if (File.Exists(mismatchPath)) File.Delete(mismatchPath); if (File.Exists(validPath)) File.Delete(validPath); }
        }

        [TestMethod]
        public void Json_import_does_not_report_annotation_errors_for_unmatched_images()
        {
            string path = TemporaryProjectPath();
            string validPath = Path.Combine(Path.GetDirectoryName(path), "valid.png");
            try
            {
                CreateBitmap(validPath, Color.Blue, ImageFormat.Png, 20, 20);
                using (var store = ProjectStore.Create(path))
                {
                    ProjectImage image = store.ImportFiles(new[] { validPath }).Single();
                    AnnotationSet target = store.CreateAnnotationSet("target", AnnotationSetType.Obd);
                    AnnotationClass value = store.AddClass(target.Id, "part", "#ABCDEF");
                    string json = "{\"label_type\":\"obd\",\"source\":\"labelset\",\"time\":\"2026-01-01T00:00:00Z\",\"version\":\"4.5.2.2\",\"classes\":[{\"name\":\"part\",\"color\":\"#ABCDEF\"}],\"data\":[{\"fileName\":\"missing.png\",\"set\":\"\",\"classLabel\":\"\",\"regionLabel\":[{\"className\":\"missing\",\"type\":\"Rect\",\"x\":1,\"y\":1,\"width\":3,\"height\":3}],\"retestset\":0,\"rotation_angle\":0,\"width\":20,\"height\":20},{\"fileName\":\"valid.png\",\"set\":\"\",\"classLabel\":\"\",\"regionLabel\":[{\"className\":\"part\",\"type\":\"Rect\",\"x\":2,\"y\":2,\"width\":3,\"height\":3}],\"retestset\":0,\"rotation_angle\":0,\"width\":20,\"height\":20}]}";

                    AnnotationJsonImportResult result = store.ImportAnnotationJsonIntoSet(json, target.Id, true, AnnotationImportMode.AddToAllImages, false, DatasetImportMode.AddToUnassignedImages, false);

                    Assert.AreEqual(0, result.Warnings.Count, "Unmatched names are silent, even when their annotations are invalid.");
                    Assert.AreEqual(value.Id, store.GetObdAnnotations(target.Id, image.Id).Single().ClassId);
                }
            }
            finally { DeleteTemporaryProject(path); if (File.Exists(validPath)) File.Delete(validPath); }
        }

        [TestMethod]
        public void Json_import_does_not_report_class_warnings_when_only_dataset_is_selected()
        {
            string path = TemporaryProjectPath();
            string validPath = Path.Combine(Path.GetDirectoryName(path), "valid.png");
            try
            {
                CreateBitmap(validPath, Color.Blue, ImageFormat.Png, 20, 20);
                using (var store = ProjectStore.Create(path))
                {
                    ProjectImage image = store.ImportFiles(new[] { validPath }).Single();
                    AnnotationSet target = store.CreateAnnotationSet("target", AnnotationSetType.Obd);
                    store.AddClass(target.Id, "part", "#ABCDEF");
                    string json = "{\"label_type\":\"obd\",\"source\":\"labelset\",\"time\":\"2026-01-01T00:00:00Z\",\"version\":\"4.5.2.2\",\"classes\":[{\"name\":\"part\",\"color\":\"#ABCDEF\"}],\"data\":[{\"fileName\":\"valid.png\",\"set\":\"train\",\"classLabel\":\"\",\"regionLabel\":[{\"className\":\"missing\",\"type\":\"Rect\",\"x\":1,\"y\":1,\"width\":3,\"height\":3}],\"retestset\":0,\"rotation_angle\":0,\"width\":20,\"height\":20}]}";

                    AnnotationJsonImportResult result = store.ImportAnnotationJsonIntoSet(json, target.Id, false, AnnotationImportMode.AddToEmptyImages, true, DatasetImportMode.ReplaceMatchingImages, false);

                    Assert.AreEqual(0, result.Warnings.Count, "Unselected CLASS errors must not appear on a dataset-only import.");
                    Assert.AreEqual(0, store.GetObdAnnotations(target.Id, image.Id).Count);
                    Assert.AreEqual(DatasetAssignmentStatus.Train, store.GetDatasetAssignments(target.Id).Single(value => value.ImageId == image.Id).Status);
                }
            }
            finally { DeleteTemporaryProject(path); if (File.Exists(validPath)) File.Delete(validPath); }
        }

        [TestMethod]
        public void Json_dataset_add_to_empty_requires_unassigned_and_retest_zero_for_every_set_type()
        {
            string path = TemporaryProjectPath();
            string directory = Path.GetDirectoryName(path);
            string[] imagePaths = { Path.Combine(directory, "one.png"), Path.Combine(directory, "two.png"), Path.Combine(directory, "three.png") };
            try
            {
                CreateBitmap(imagePaths[0], Color.Red, ImageFormat.Png, 20, 20);
                CreateBitmap(imagePaths[1], Color.Green, ImageFormat.Png, 20, 20);
                CreateBitmap(imagePaths[2], Color.Blue, ImageFormat.Png, 20, 20);
                using (var store = ProjectStore.Create(path))
                {
                    Dictionary<string, string> imageIds = store.ImportFiles(imagePaths).ToDictionary(value => value.OriginalFileName, value => value.Id, StringComparer.Ordinal);
                    foreach (AnnotationSetType type in Enum.GetValues(typeof(AnnotationSetType)))
                    {
                        AnnotationSet target = store.CreateAnnotationSet("set-" + type, type);
                        store.SetDatasetAssignments(target.Id, new[] { imageIds["one.png"] }, DatasetAssignmentStatus.Train);
                        store.SetRetest(target.Id, new[] { imageIds["one.png"], imageIds["two.png"] }, true);
                        string labelType = type == AnnotationSetType.Cla ? "cla" : type == AnnotationSetType.Obd ? "obd" : "seg";
                        string json = "{\"label_type\":\"" + labelType + "\",\"source\":\"labelset\",\"time\":\"2026-01-01T00:00:00Z\",\"version\":\"4.5.2.2\",\"classes\":[{\"name\":\"part\",\"color\":\"#ABCDEF\"}],\"data\":[" +
                            "{\"fileName\":\"one.png\",\"set\":\"test\",\"classLabel\":\"\",\"regionLabel\":[],\"retestset\":0,\"rotation_angle\":0,\"width\":20,\"height\":20}," +
                            "{\"fileName\":\"two.png\",\"set\":\"test\",\"classLabel\":\"\",\"regionLabel\":[],\"retestset\":0,\"rotation_angle\":0,\"width\":20,\"height\":20}," +
                            "{\"fileName\":\"three.png\",\"set\":\"test\",\"classLabel\":\"\",\"regionLabel\":[],\"retestset\":1,\"rotation_angle\":0,\"width\":20,\"height\":20}]}";
                        Assert.IsTrue(AnnotationJsonImportPreview.Parse(json).HasRetest, type + " preview must parse source Retest.");

                        store.ImportAnnotationJsonIntoSet(json, target.Id, false, AnnotationImportMode.AddToEmptyImages, true, DatasetImportMode.AddToUnassignedImages, false);

                        DatasetAssignment[] assignments = store.GetDatasetAssignments(target.Id).ToArray();
                        Assert.AreEqual(DatasetAssignmentStatus.Train, assignments.Single(value => value.ImageId == imageIds["one.png"]).Status, type + " must preserve assigned images.");
                        Assert.IsTrue(assignments.Single(value => value.ImageId == imageIds["one.png"]).Retest);
                        Assert.AreEqual(DatasetAssignmentStatus.Unassigned, assignments.Single(value => value.ImageId == imageIds["two.png"]).Status, type + " must preserve unassigned images with Retest enabled.");
                        Assert.IsTrue(assignments.Single(value => value.ImageId == imageIds["two.png"]).Retest);
                        Assert.AreEqual(DatasetAssignmentStatus.Test, assignments.Single(value => value.ImageId == imageIds["three.png"]).Status, type + " must update empty dataset assignments.");
                        Assert.IsTrue(assignments.Single(value => value.ImageId == imageIds["three.png"]).Retest, type + " must import the source Retest value.");
                    }
                }
            }
            finally { DeleteTemporaryProject(path); foreach (string imagePath in imagePaths) if (File.Exists(imagePath)) File.Delete(imagePath); }
        }

        [TestMethod]
        public void Json_import_preserves_existing_dataset_assignment_when_set_is_invalid_and_imports_valid_class()
        {
            string path = TemporaryProjectPath();
            string imagePath = Path.Combine(Path.GetDirectoryName(path), "image.png");
            try
            {
                CreateBitmap(imagePath, Color.Blue, ImageFormat.Png, 20, 20);
                using (var store = ProjectStore.Create(path))
                {
                    ProjectImage image = store.ImportFiles(new[] { imagePath }).Single();
                    AnnotationSet target = store.CreateAnnotationSet("target", AnnotationSetType.Obd);
                    AnnotationClass annotationClass = store.AddClass(target.Id, "part", "#ABCDEF");
                    store.SetDatasetAssignments(target.Id, new[] { image.Id }, DatasetAssignmentStatus.Train);
                    store.SetRetest(target.Id, new[] { image.Id }, true);
                    string json = "{\"label_type\":\"obd\",\"source\":\"labelset\",\"time\":\"2026-01-01T00:00:00Z\",\"version\":\"4.5.2.2\",\"classes\":[{\"name\":\"part\",\"color\":\"#ABCDEF\"}],\"data\":[{\"fileName\":\"image.png\",\"set\":\"invalid\",\"classLabel\":\"\",\"regionLabel\":[{\"className\":\"part\",\"type\":\"Rect\",\"x\":1,\"y\":1,\"width\":3,\"height\":3}],\"retestset\":0,\"rotation_angle\":0,\"width\":20,\"height\":20}]}";

                    AnnotationJsonImportResult result = store.ImportAnnotationJsonIntoSet(json, target.Id, true, AnnotationImportMode.AddToAllImages, true, DatasetImportMode.ReplaceMatchingImages, false);

                    DatasetAssignment assignment = store.GetDatasetAssignments(target.Id).Single(value => value.ImageId == image.Id);
                    Assert.AreEqual(DatasetAssignmentStatus.Train, assignment.Status);
                    Assert.IsTrue(assignment.Retest, "Invalid SET data must leave both assignment status and Retest unchanged.");
                    Assert.AreEqual(annotationClass.Id, store.GetObdAnnotations(target.Id, image.Id).Single().ClassId);
                    Assert.AreEqual(1, result.Warnings.Count);
                    Assert.AreEqual("data[0].set", result.Warnings[0].SourceLocation);
                    Assert.AreEqual(AnnotationJsonImportWarningReason.InvalidDatasetAssignment, result.Warnings[0].Reason);
                }
            }
            finally { DeleteTemporaryProject(path); if (File.Exists(imagePath)) File.Delete(imagePath); }
        }

        [TestMethod]
        public void Json_import_keeps_class_definitions_when_all_uniquely_matched_images_have_wrong_dimensions()
        {
            string path = TemporaryProjectPath();
            string imagePath = Path.Combine(Path.GetDirectoryName(path), "mismatch.png");
            try
            {
                CreateBitmap(imagePath, Color.Blue, ImageFormat.Png, 20, 20);
                using (var store = ProjectStore.Create(path))
                {
                    ProjectImage image = store.ImportFiles(new[] { imagePath }).Single();
                    AnnotationSet target = store.CreateAnnotationSet("target", AnnotationSetType.Obd);
                    string json = "{\"label_type\":\"obd\",\"source\":\"labelset\",\"time\":\"2026-01-01T00:00:00Z\",\"version\":\"4.5.2.2\",\"classes\":[{\"name\":\"part\",\"color\":\"#ABCDEF\"}],\"data\":[{\"fileName\":\"mismatch.png\",\"set\":\"\",\"classLabel\":\"\",\"regionLabel\":[{\"className\":\"part\",\"type\":\"Rect\",\"x\":1,\"y\":1,\"width\":3,\"height\":3}],\"retestset\":0,\"rotation_angle\":0,\"width\":19,\"height\":20}]}";

                    AnnotationJsonImportResult result = store.ImportAnnotationJsonIntoSet(json, target.Id, true, AnnotationImportMode.ReplaceMatchingImages, false, DatasetImportMode.AddToUnassignedImages, false);

                    Assert.AreEqual(1, result.Warnings.Count);
                    Assert.IsTrue(store.GetClasses(target.Id).Any(value => value.Name == "part"), "CLASS definitions are not part of the skipped image row.");
                    Assert.AreEqual(0, store.GetObdAnnotations(target.Id, image.Id).Count);
                }
            }
            finally { DeleteTemporaryProject(path); if (File.Exists(imagePath)) File.Delete(imagePath); }
        }

        [TestMethod]
        public void Json_import_skips_overflowing_seg_point_and_keeps_valid_sibling_region()
        {
            string path = TemporaryProjectPath();
            string imagePath = Path.Combine(Path.GetDirectoryName(path), "image.png");
            try
            {
                CreateBitmap(imagePath, Color.Blue, ImageFormat.Png, 20, 20);
                using (var store = ProjectStore.Create(path))
                {
                    ProjectImage image = store.ImportFiles(new[] { imagePath }).Single();
                    AnnotationSet target = store.CreateAnnotationSet("target", AnnotationSetType.Seg);
                    AnnotationClass annotationClass = store.AddClass(target.Id, "part", "#ABCDEF");
                    string json = "{\"label_type\":\"seg\",\"source\":\"labelset\",\"time\":\"2026-01-01T00:00:00Z\",\"version\":\"4.5.2.2\",\"classes\":[{\"name\":\"part\",\"color\":\"#ABCDEF\"}],\"data\":[{\"fileName\":\"image.png\",\"set\":\"\",\"classLabel\":\"\",\"regionLabel\":[{\"className\":\"part\",\"type\":\"PolyLine\",\"strokeWidth\":2,\"points\":[[2147483648,1],[2,2]]},{\"className\":\"part\",\"type\":\"Rect\",\"x\":2,\"y\":2,\"width\":3,\"height\":3}],\"retestset\":0,\"rotation_angle\":0,\"width\":20,\"height\":20}]}";

                    AnnotationJsonImportResult result = store.ImportAnnotationJsonIntoSet(json, target.Id, true, AnnotationImportMode.AddToAllImages, false, DatasetImportMode.AddToUnassignedImages, false);

                    Assert.AreEqual(1, result.Warnings.Count);
                    Assert.AreEqual("data[0].regionLabel[0]", result.Warnings[0].SourceLocation);
                    Assert.AreEqual(AnnotationJsonImportWarningReason.InvalidAnnotation, result.Warnings[0].Reason);
                    SegRegion region = store.GetSegRegions(target.Id, image.Id).Single();
                    Assert.AreEqual(SegRegionType.Rect, region.Type);
                    Assert.AreEqual(annotationClass.Id, region.ClassId);
                }
            }
            finally { DeleteTemporaryProject(path); if (File.Exists(imagePath)) File.Delete(imagePath); }
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
                    string json = "{\"label_type\":\"seg\",\"source\":\"labelset\",\"time\":\"2026-01-01T00:00:00Z\",\"version\":\"4.5.2.2\",\"classes\":[{\"name\":\"part\",\"color\":\"#FF0000\"}],\"data\":[{\"fileName\":\"sample.png\",\"set\":\"\",\"classLabel\":\"\",\"regionLabel\":[{\"className\":\"part\",\"type\":\"PolyLine\",\"strokeWidth\":2,\"points\":[[1,2],[3,4]]}],\"retestset\":0,\"rotation_angle\":0,\"width\":20,\"height\":20}]}";
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
                    string obd = "{\"label_type\":\"obd\",\"source\":\"labelset\",\"time\":\"2026-01-01T00:00:00Z\",\"version\":\"4.5.2.2\",\"classes\":[{\"name\":\" part \",\"color\":\"#FF0000\"}],\"data\":[{\"fileName\":\"sample.png\",\"set\":\"\",\"classLabel\":\"\",\"regionLabel\":[{\"className\":\" part \",\"type\":\"Rect\",\"x\":1,\"y\":1,\"width\":2,\"height\":2}],\"retestset\":0,\"rotation_angle\":0,\"width\":20,\"height\":20}]}";
                    AnnotationSet obdSet = store.ImportAnnotationJson(obd, "obd", false);
                    Assert.AreEqual("part", store.GetClasses(obdSet.Id).Single().Name);
                    string seg = "{\"label_type\":\"seg\",\"source\":\"labelset\",\"time\":\"2026-01-01T00:00:00Z\",\"version\":\"4.5.2.2\",\"classes\":[{\"name\":\"part\",\"color\":\"#FF0000\"}],\"data\":[{\"fileName\":\"sample.png\",\"set\":\"\",\"classLabel\":\"\",\"regionLabel\":[{\"className\":\"part\",\"type\":\"PolyGon\",\"points\":[[0,0],[20,0],[20,20],[0,20]]}],\"retestset\":0,\"rotation_angle\":0,\"width\":20,\"height\":20}]}";
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
                    string json = "{\"label_type\":\"obd\",\"source\":\"labelset\",\"time\":\"2026-01-01T00:00:00Z\",\"version\":\"4.5.2.2\",\"classes\":[{\"name\":\"part\",\"color\":\"#FF0000\"}],\"data\":[{\"fileName\":\"sample.png\",\"set\":\"\",\"classLabel\":\"\",\"regionLabel\":[{\"className\":\"part\",\"type\":\"Rect\",\"x\":1,\"y\":1,\"width\":2,\"height\":2,\"futureField\":true}],\"retestset\":0,\"rotation_angle\":0,\"width\":20,\"height\":20}]}";
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
        public void Image_zip_export_selects_available_images_preserves_bytes_and_suffixes_existing_output()
        {
            string path = TemporaryProjectPath();
            string firstSource = Path.Combine(Path.GetDirectoryName(path), "first.png");
            string secondSource = Path.Combine(Path.GetDirectoryName(path), "second.png");
            string output = Path.Combine(Path.GetDirectoryName(path), "images.zip");
            try
            {
                CreateBitmap(firstSource, Color.Red, ImageFormat.Png, 20, 20);
                CreateBitmap(secondSource, Color.Blue, ImageFormat.Png, 20, 20);
                using (var store = ProjectStore.Create(path))
                {
                    ProjectImage[] images = store.ImportFiles(new[] { firstSource, secondSource }).ToArray();
                    string selected = store.ExportImagesZip(output, new[] { images[0].Id });
                    Assert.AreEqual(output, selected);
                    using (var archive = ZipFile.OpenRead(selected))
                    {
                        Assert.AreEqual(1, archive.Entries.Count);
                        ZipArchiveEntry entry = archive.Entries.Single();
                        Assert.AreEqual(images[0].ManagedFileName, entry.FullName);
                        using (Stream actual = entry.Open()) using (Stream expected = File.OpenRead(Path.Combine(store.ImagesDirectory, images[0].ManagedFileName))) CollectionAssert.AreEqual(ReadAllBytes(expected), ReadAllBytes(actual));
                    }
                    string all = store.ExportImagesZip(output, null);
                    Assert.AreEqual(Path.Combine(Path.GetDirectoryName(output), "images (1).zip"), all);
                    using (var archive = ZipFile.OpenRead(all)) Assert.AreEqual(2, archive.Entries.Count);
                    File.Delete(Path.Combine(store.ImagesDirectory, images[1].ManagedFileName));
                    string directoryCollision = Path.Combine(Path.GetDirectoryName(path), "folder.zip");
                    Directory.CreateDirectory(directoryCollision);
                    Assert.AreEqual(Path.Combine(Path.GetDirectoryName(path), "folder (1).zip"), store.ExportImagesZip(directoryCollision, null));
                }
            }
            finally { DeleteTemporaryProject(path); if (File.Exists(firstSource)) File.Delete(firstSource); if (File.Exists(secondSource)) File.Delete(secondSource); }
        }

        [TestMethod]
        public void Annotation_json_export_round_trips_a_selected_set_and_skips_missing_images()
        {
            string path = TemporaryProjectPath();
            string source = Path.Combine(Path.GetDirectoryName(path), "sample.png");
            string output = Path.Combine(Path.GetDirectoryName(path), "labels.json");
            try
            {
                CreateBitmap(source, Color.Red, ImageFormat.Png, 20, 20);
                using (var store = ProjectStore.Create(path))
                {
                    ProjectImage image = store.ImportFiles(new[] { source }).Single();
                    AnnotationSet set = store.CreateAnnotationSet("boxes", AnnotationSetType.Obd);
                    AnnotationClass value = store.GetClasses(set.Id).Single();
                    store.AddObdAnnotation(set.Id, image.Id, value.Id, 1, 2, 3, 4);
                    store.SetDatasetAssignments(set.Id, new[] { image.Id }, DatasetAssignmentStatus.Test);
                    store.SetRetest(set.Id, new[] { image.Id }, true);
                    string exported = store.ExportAnnotationJson(set.Id, output);
                    AnnotationSet imported = store.ImportAnnotationJson(File.ReadAllText(exported), "roundtrip", false);
                    Assert.AreEqual(1, store.GetObdAnnotations(imported.Id, image.Id).Count);
                    Assert.AreEqual(DatasetAssignmentStatus.Test, store.GetDatasetAssignments(imported.Id).Single().Status);
                    Assert.IsTrue(store.GetDatasetAssignments(imported.Id).Single().Retest);
                    store.SetDatasetAssignments(set.Id, new[] { image.Id }, DatasetAssignmentStatus.Train);
                    AnnotationSet train = store.ImportAnnotationJson(File.ReadAllText(store.ExportAnnotationJson(set.Id, Path.Combine(Path.GetDirectoryName(path), "train.json"))), "train", false);
                    Assert.AreEqual(DatasetAssignmentStatus.Train, store.GetDatasetAssignments(train.Id).Single().Status);
                    File.Delete(Path.Combine(store.ImagesDirectory, image.ManagedFileName));
                    string skipped = store.ExportAnnotationJson(set.Id, output);
                    Assert.AreEqual(Path.Combine(Path.GetDirectoryName(output), "labels (1).json"), skipped);
                    Assert.AreEqual(0, JsonAnnotationImport.Parse(File.ReadAllText(skipped)).Data.Count);
                }
            }
            finally { DeleteTemporaryProject(path); if (File.Exists(source)) File.Delete(source); }
        }

        [TestMethod]
        public void Annotation_json_export_rejects_duplicate_original_file_names()
        {
            string path = TemporaryProjectPath();
            string first = Path.Combine(Path.GetDirectoryName(path), "one", "same.png");
            string second = Path.Combine(Path.GetDirectoryName(path), "two", "same.png");
            try
            {
                CreateBitmap(first, Color.Red, ImageFormat.Png); CreateBitmap(second, Color.Blue, ImageFormat.Png);
                using (var store = ProjectStore.Create(path))
                {
                    ProjectImage[] images = store.ImportFiles(new[] { first, second }).ToArray();
                    store.CompleteOperation((connection, transaction) =>
                    {
                        using (var command = connection.CreateCommand())
                        {
                            command.Transaction = transaction;
                            command.CommandText = "UPDATE ImageRecord SET OriginalFileName = $name WHERE Id = $id;";
                            command.Parameters.AddWithValue("$name", images[0].OriginalFileName);
                            command.Parameters.AddWithValue("$id", images[1].Id);
                            command.ExecuteNonQuery();
                        }
                    });
                    AnnotationSet set = store.CreateAnnotationSet("set", AnnotationSetType.Cla);
                    Assert.ThrowsException<InvalidOperationException>(() => store.ExportAnnotationJson(set.Id, Path.Combine(Path.GetDirectoryName(path), "labels.json")));
                }
            }
            finally { DeleteTemporaryProject(path); }
        }

        [TestMethod]
        public void Annotation_json_export_round_trips_cla_and_seg_shapes()
        {
            string path = TemporaryProjectPath(); string source = Path.Combine(Path.GetDirectoryName(path), "sample.png");
            try
            {
                CreateBitmap(source, Color.Red, ImageFormat.Png, 20, 20);
                using (var store = ProjectStore.Create(path))
                {
                    ProjectImage image = store.ImportFiles(new[] { source }).Single();
                    AnnotationSet cla = store.CreateAnnotationSet("cla", AnnotationSetType.Cla); AnnotationClass claClass = store.GetClasses(cla.Id).Single();
                    store.SetClaClass(cla.Id, image.Id, claClass.Id); store.SetDatasetAssignments(cla.Id, new[] { image.Id }, DatasetAssignmentStatus.NotUsed);
                    AnnotationSet claCopy = store.ImportAnnotationJson(File.ReadAllText(store.ExportAnnotationJson(cla.Id, Path.Combine(Path.GetDirectoryName(path), "cla.json"))), "cla copy", false);
                    Assert.AreEqual(claClass.Name, store.GetClasses(claCopy.Id).Single(value => value.Id == store.GetClaClassId(claCopy.Id, image.Id)).Name);
                    Assert.AreEqual(DatasetAssignmentStatus.NotUsed, store.GetDatasetAssignments(claCopy.Id).Single().Status);
                    AnnotationSet seg = store.CreateAnnotationSet("seg", AnnotationSetType.Seg); AnnotationClass segClass = store.GetClasses(seg.Id).Single();
                    store.AddSegRegion(seg.Id, image.Id, segClass.Id, SegRegionType.Rect, 1, 1, 2, 2); store.AddSegRegion(seg.Id, image.Id, segClass.Id, SegRegionType.Ellipse, 10, 10, 2, 3);
                    store.AddSegPath(seg.Id, image.Id, segClass.Id, SegRegionType.PolyLine, new[] { new SegPoint(3, 3), new SegPoint(5, 5) }, 1);
                    store.AddSegPath(seg.Id, image.Id, segClass.Id, SegRegionType.PolyGon, new[] { new SegPoint(1, 10), new SegPoint(3, 10), new SegPoint(2, 12) }, 0);
                    AnnotationSet segCopy = store.ImportAnnotationJson(File.ReadAllText(store.ExportAnnotationJson(seg.Id, Path.Combine(Path.GetDirectoryName(path), "seg.json"))), "seg copy", false);
                    CollectionAssert.AreEquivalent(new[] { SegRegionType.Rect, SegRegionType.Ellipse, SegRegionType.PolyLine, SegRegionType.PolyGon }, store.GetSegRegions(segCopy.Id, image.Id).Select(value => value.Type).ToArray());
                    SegRegion[] copied = store.GetSegRegions(segCopy.Id, image.Id).ToArray();
                    SegRegion rectangle = copied.Single(value => value.Type == SegRegionType.Rect); Assert.AreEqual(1, rectangle.X); Assert.AreEqual(2, rectangle.Width);
                    SegRegion ellipse = copied.Single(value => value.Type == SegRegionType.Ellipse); Assert.AreEqual(2, ellipse.Width); Assert.AreEqual(3, ellipse.Height);
                    SegRegion line = copied.Single(value => value.Type == SegRegionType.PolyLine); Assert.AreEqual(1, line.StrokeWidth); CollectionAssert.AreEqual(new[] { 3, 5 }, line.Points.Select(value => value.X).ToArray());
                    SegRegion polygon = copied.Single(value => value.Type == SegRegionType.PolyGon); CollectionAssert.AreEqual(new[] { 10, 10, 12 }, polygon.Points.Select(value => value.Y).ToArray());
                    Assert.AreEqual(DatasetAssignmentStatus.Unassigned, store.GetDatasetAssignments(segCopy.Id).Single().Status); Assert.IsFalse(store.GetDatasetAssignments(segCopy.Id).Single().Retest);
                }
            }
            finally { DeleteTemporaryProject(path); if (File.Exists(source)) File.Delete(source); }
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

        [TestMethod]
        public void Auto_split_selected_only_updates_selected_images_and_keeps_outside_state()
        {
            string path = TemporaryProjectPath();
            string source = Path.Combine(Path.GetDirectoryName(path), "source.png");
            try
            {
                CreateBitmap(source, Color.Red, ImageFormat.Png, 20, 20);
                using (var store = ProjectStore.Create(path))
                {
                    ProjectImage[] images = store.ImportFiles(Enumerable.Range(1, 4).Select(index => source)).ToArray();
                    AnnotationSet set = store.CreateAnnotationSet("selected split", AnnotationSetType.Cla);
                    store.SetDatasetAssignments(set.Id, new[] { images[2].Id }, DatasetAssignmentStatus.NotUsed);
                    store.SetRetest(set.Id, new[] { images[0].Id }, true);
                    DatasetAssignment[] before = store.GetDatasetAssignments(set.Id).ToArray();
                    Func<int, int> original = ProjectStore.NextRandom;
                    try { ProjectStore.NextRandom = maximum => 0; store.AutoSplitDataset(set.Id, 80, new[] { images[0].Id, images[1].Id }); }
                    finally { ProjectStore.NextRandom = original; }
                    DatasetAssignment[] after = store.GetDatasetAssignments(set.Id).ToArray();
                    Assert.IsTrue(after.Single(value => value.ImageId == images[0].Id).Retest);
                    Assert.AreEqual(DatasetAssignmentStatus.NotUsed, after.Single(value => value.ImageId == images[2].Id).Status);
                    Assert.AreEqual(DatasetAssignmentStatus.Unassigned, after.Single(value => value.ImageId == images[3].Id).Status);
                    Assert.IsTrue(after.Where(value => value.ImageId == images[0].Id || value.ImageId == images[1].Id).All(value => value.Status == DatasetAssignmentStatus.Train || value.Status == DatasetAssignmentStatus.Test));
                    Assert.IsTrue(store.Undo(set.Id));
                    AssertDatasetAssignments(before, store.GetDatasetAssignments(set.Id));
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

        private static byte[] ReadAllBytes(Stream source)
        {
            using (var buffer = new MemoryStream())
            {
                source.CopyTo(buffer);
                return buffer.ToArray();
            }
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

        private static SegPoint[] SeparateOuterBridgePath()
        {
            return new[]
            {
                new SegPoint(0, 0), new SegPoint(20, 0), new SegPoint(20, 20),
                new SegPoint(20, 30), new SegPoint(30, 30), new SegPoint(30, 20), new SegPoint(20, 20), new SegPoint(20, 0),
                new SegPoint(40, 0), new SegPoint(40, 20), new SegPoint(40, 30), new SegPoint(50, 30), new SegPoint(50, 20), new SegPoint(40, 20), new SegPoint(40, 0),
                new SegPoint(100, 0), new SegPoint(100, 100), new SegPoint(0, 100)
            };
        }

        private static string PointSignature(SegPoint point)
        {
            return point.X + "," + point.Y;
        }

        private static string PointSignature(Point64 point)
        {
            return point.X + "," + point.Y;
        }

        private static int CountSegment(IReadOnlyList<SegPoint> points, int firstX, int firstY, int secondX, int secondY)
        {
            int count = 0;
            for (int index = 0; index < points.Count; index++)
            {
                SegPoint first = points[index];
                SegPoint second = points[(index + 1) % points.Count];
                if ((first.X == firstX && first.Y == firstY && second.X == secondX && second.Y == secondY) ||
                    (first.X == secondX && first.Y == secondY && second.X == firstX && second.Y == firstY)) count++;
            }
            return count;
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

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Neurocle.Application.Projects;
using Neurocle.WinForms;
using Neurocle.Infrastructure.Persistence;

namespace Neurocle.Tests
{
    [TestClass]
    public sealed class ImportJobQueueTests
    {
        [TestMethod]
        public void Image_file_request_keeps_an_immutable_copy_of_source_paths()
        {
            string[] paths = { "first.png", "second.png" };

            var request = new ImageFilesImportJobRequest(paths);
            paths[0] = "changed.png";

            CollectionAssert.AreEqual(new[] { "first.png", "second.png" }, request.FilePaths.ToArray());
            Assert.IsFalse(request.FilePaths is string[]);
        }

        [TestMethod]
        public void Image_jobs_execute_in_fifo_order_without_overlap()
        {
            var executionOrder = new List<string>();
            var firstStarted = new ManualResetEventSlim();
            var releaseFirst = new ManualResetEventSlim();
            int running = 0;
            int maximumRunning = 0;

            using (var queue = new ImportJobQueue((request, cancellationRequested, progress) =>
            {
                var paths = ((ImageFilesImportJobRequest)request).FilePaths;
                int current = Interlocked.Increment(ref running);
                maximumRunning = Math.Max(maximumRunning, current);
                lock (executionOrder) executionOrder.Add(paths[0]);
                if (paths[0] == "first.png")
                {
                    firstStarted.Set();
                    Assert.IsTrue(releaseFirst.Wait(TimeSpan.FromSeconds(5)));
                }
                progress(new ImportJobProgress(1, 1, 1, 0));
                Interlocked.Decrement(ref running);
                return new ImportJobResult(false, new string[0]);
            }))
            {
                queue.Enqueue(new ImageFilesImportJobRequest(new[] { "first.png" }));
                queue.Enqueue(new ImageFilesImportJobRequest(new[] { "second.png" }));

                Assert.IsTrue(firstStarted.Wait(TimeSpan.FromSeconds(5)));
                CollectionAssert.AreEqual(
                    new[] { ImportJobStatus.Running, ImportJobStatus.Waiting },
                    queue.Jobs.Select(job => job.Status).ToArray());

                releaseFirst.Set();
                Assert.IsTrue(queue.WaitUntilIdle(TimeSpan.FromSeconds(5)));

                CollectionAssert.AreEqual(new[] { "first.png", "second.png" }, executionOrder.ToArray());
                Assert.AreEqual(1, maximumRunning);
                Assert.IsTrue(queue.Jobs.All(job => job.Status == ImportJobStatus.Completed));
            }
        }

        [TestMethod]
        public void Queue_rejects_the_eleventh_waiting_or_running_job()
        {
            var firstStarted = new ManualResetEventSlim();
            var release = new ManualResetEventSlim();
            using (var queue = new ImportJobQueue((request, cancellationRequested, progress) =>
            {
                firstStarted.Set();
                Assert.IsTrue(release.Wait(TimeSpan.FromSeconds(5)));
                return new ImportJobResult(false, new string[0]);
            }))
            {
                queue.Enqueue(new ImageFilesImportJobRequest(new[] { "0.png" }));
                Assert.IsTrue(firstStarted.Wait(TimeSpan.FromSeconds(5)));
                for (int index = 1; index < 10; index++) queue.Enqueue(new ImageFilesImportJobRequest(new[] { index + ".png" }));

                Assert.ThrowsException<ImportQueueCapacityException>(() => queue.Enqueue(new ImageFilesImportJobRequest(new[] { "10.png" })));
                release.Set();
                Assert.IsTrue(queue.WaitUntilIdle(TimeSpan.FromSeconds(5)));
            }
            firstStarted.Dispose();
            release.Dispose();
        }

        [TestMethod]
        public void Queue_retains_only_the_twenty_most_recent_terminal_jobs()
        {
            using (var queue = new ImportJobQueue((request, cancellationRequested, progress) => new ImportJobResult(false, new string[0])))
            {
                for (int index = 0; index < 25; index++)
                {
                    queue.Enqueue(new ImageFilesImportJobRequest(new[] { index + ".png" }));
                    Assert.IsTrue(queue.WaitUntilIdle(TimeSpan.FromSeconds(5)));
                }

                Assert.AreEqual(20, queue.Jobs.Count);
                Assert.AreEqual("5.png", ((ImageFilesImportJobRequest)queue.Jobs[0].Request).FilePaths[0]);
            }
        }

        [TestMethod]
        public void Clear_completed_jobs_keeps_active_jobs_and_removes_terminal_history()
        {
            var started = new ManualResetEventSlim();
            var release = new ManualResetEventSlim();
            using (var queue = new ImportJobQueue((request, cancellationRequested, progress) =>
            {
                if (((ImageFilesImportJobRequest)request).FilePaths[0] == "running.png")
                {
                    started.Set();
                    Assert.IsTrue(release.Wait(TimeSpan.FromSeconds(5)));
                }
                return new ImportJobResult(false, new string[0]);
            }))
            {
                queue.Enqueue(new ImageFilesImportJobRequest(new[] { "completed.png" }));
                Assert.IsTrue(queue.WaitUntilIdle(TimeSpan.FromSeconds(5)));
                queue.Enqueue(new ImageFilesImportJobRequest(new[] { "running.png" }));
                Assert.IsTrue(started.Wait(TimeSpan.FromSeconds(5)));
                queue.ClearCompleted();

                Assert.AreEqual(1, queue.Jobs.Count);
                Assert.AreEqual(ImportJobStatus.Running, queue.Jobs[0].Status);
                release.Set();
                Assert.IsTrue(queue.WaitUntilIdle(TimeSpan.FromSeconds(5)));
            }
            started.Dispose();
            release.Dispose();
        }

        [TestMethod]
        public void Fatal_executor_error_marks_waiting_jobs_failed_without_running_them()
        {
            int executions = 0;
            using (var queue = new ImportJobQueue((request, cancellationRequested, progress) =>
            {
                executions++;
                throw new ObjectDisposedException("project");
            }))
            {
                queue.Enqueue(new ImageFilesImportJobRequest(new[] { "fatal.png" }));
                queue.Enqueue(new ImageFilesImportJobRequest(new[] { "not-run.png" }));

                Assert.IsTrue(queue.WaitUntilIdle(TimeSpan.FromSeconds(5)));
                Assert.AreEqual(1, executions);
                Assert.IsTrue(queue.Jobs.All(job => job.Status == ImportJobStatus.Failed));
            }
        }

        [TestMethod]
        public void CancelAll_cancels_waiting_and_running_jobs_for_safe_close()
        {
            var started = new ManualResetEventSlim();
            var release = new ManualResetEventSlim();
            using (var queue = new ImportJobQueue((request, cancellationRequested, progress) =>
            {
                started.Set();
                Assert.IsTrue(release.Wait(TimeSpan.FromSeconds(5)));
                return new ImportJobResult(cancellationRequested(), new string[0]);
            }))
            {
                queue.Enqueue(new ImageFilesImportJobRequest(new[] { "running.png" }));
                queue.Enqueue(new ImageFilesImportJobRequest(new[] { "waiting.png" }));
                Assert.IsTrue(started.Wait(TimeSpan.FromSeconds(5)));

                queue.CancelAll();
                release.Set();
                Assert.IsTrue(queue.WaitUntilIdle(TimeSpan.FromSeconds(5)));
                Assert.IsTrue(queue.Jobs.All(job => job.Status == ImportJobStatus.Canceled));
            }
            started.Dispose();
            release.Dispose();
        }

        [TestMethod]
        public void Image_and_folder_jobs_execute_in_one_fifo()
        {
            var executionOrder = new List<ImportJobKind>();
            using (var queue = new ImportJobQueue((request, cancellationRequested, progress) =>
            {
                executionOrder.Add(request.Kind);
                return new ImportJobResult(false, new string[0]);
            }))
            {
                queue.Enqueue(new ImageFilesImportJobRequest(new[] { "first.png" }));
                queue.Enqueue(new FolderImportJobRequest("folder", false));
                queue.Enqueue(new ImageFilesImportJobRequest(new[] { "last.png" }));

                Assert.IsTrue(queue.WaitUntilIdle(TimeSpan.FromSeconds(5)));
                CollectionAssert.AreEqual(new[] { ImportJobKind.ImageFiles, ImportJobKind.Folder, ImportJobKind.ImageFiles }, executionOrder.ToArray());
            }
        }

        [TestMethod]
        public void Image_folder_and_json_jobs_execute_in_one_fifo()
        {
            var executionOrder = new List<ImportJobKind>();
            using (var queue = new ImportJobQueue((request, cancellationRequested, progress) =>
            {
                executionOrder.Add(request.Kind);
                return new ImportJobResult(false, new string[0]);
            }))
            {
                queue.Enqueue(new ImageFilesImportJobRequest(new[] { "first.png" }));
                queue.Enqueue(new FolderImportJobRequest("folder", false));
                queue.Enqueue(new AnnotationJsonImportJobRequest("labels.json", "labels", false));

                Assert.IsTrue(queue.WaitUntilIdle(TimeSpan.FromSeconds(5)));
                CollectionAssert.AreEqual(new[] { ImportJobKind.ImageFiles, ImportJobKind.Folder, ImportJobKind.AnnotationJson }, executionOrder.ToArray());
            }
        }

        [TestMethod]
        public void Json_request_keeps_source_options_immutable()
        {
            var request = new AnnotationJsonImportJobRequest("labels.json", "labels", false);

            Assert.AreEqual("labels.json", request.FilePath);
            Assert.AreEqual("labels", request.AnnotationSetName);
            Assert.IsFalse(request.AllowUnknownFields);
        }

        [TestMethod]
        public void Json_job_executor_imports_the_file_as_one_atomic_operation()
        {
            string directory = Path.Combine(Path.GetTempPath(), "neurocle-json-queue-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                string jsonPath = Path.Combine(directory, "labels.json");
                File.WriteAllText(jsonPath, "{\"label_type\":\"cla\",\"source\":\"labelset\",\"time\":\"2026-01-01T00:00:00Z\",\"version\":\"4.5.2.2\",\"classes\":[{\"name\":\"class1\",\"color\":\"rgba(1, 2, 3, 1)\"}],\"data\":[]}");
                using (ProjectStore store = ProjectStore.Create(Path.Combine(directory, "queue.neurocle")))
                using (var queue = new ImportJobQueue((request, cancellationRequested, progress) => ExecuteMainFormImport(store, request, cancellationRequested, progress)))
                {
                    queue.Enqueue(new AnnotationJsonImportJobRequest(jsonPath, "labels", false));

                    Assert.IsTrue(queue.WaitUntilIdle(TimeSpan.FromSeconds(5)));
                    Assert.AreEqual(ImportJobStatus.Completed, queue.Jobs.Single().Status);
                    Assert.AreEqual(1, store.GetAnnotationSets().Count);
                }
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [TestMethod]
        public void Json_confirmation_pauses_fifo_until_the_user_decides()
        {
            var confirmationReady = new ManualResetEventSlim();
            var executionOrder = new List<ImportJobKind>();
            using (var queue = new ImportJobQueue((request, cancellationRequested, progress) =>
            {
                executionOrder.Add(request.Kind);
                var jsonRequest = request as AnnotationJsonImportJobRequest;
                if (jsonRequest != null && !jsonRequest.AllowUnknownFields)
                {
                    confirmationReady.Set();
                    return new ImportJobResult(false, new string[0], 0, 0, false, true, "JSON contains unknown fields.");
                }
                return new ImportJobResult(false, new string[0]);
            }))
            {
                Guid jsonId = queue.Enqueue(new AnnotationJsonImportJobRequest("labels.json", "labels", false));
                queue.Enqueue(new ImageFilesImportJobRequest(new[] { "after.png" }));

                Assert.IsTrue(confirmationReady.Wait(TimeSpan.FromSeconds(5)));
                Assert.AreEqual(1, executionOrder.Count);
                Assert.AreEqual(ImportJobStatus.Waiting, queue.Jobs[0].Status);
                Assert.IsTrue(queue.Jobs[0].RequiresConfirmation);
                Assert.AreEqual(ImportJobStatus.Waiting, queue.Jobs[1].Status);

                queue.ResolveConfirmation(jsonId, new AnnotationJsonImportJobRequest("labels.json", "labels", true));
                Assert.IsTrue(queue.WaitUntilIdle(TimeSpan.FromSeconds(5)));
                CollectionAssert.AreEqual(new[] { ImportJobKind.AnnotationJson, ImportJobKind.AnnotationJson, ImportJobKind.ImageFiles }, executionOrder.ToArray());
            }
            confirmationReady.Dispose();
        }

        [TestMethod]
        public void Canceling_json_confirmation_releases_the_queue_without_retry()
        {
            var confirmationReady = new ManualResetEventSlim();
            using (var queue = new ImportJobQueue((request, cancellationRequested, progress) =>
            {
                confirmationReady.Set();
                return new ImportJobResult(false, new string[0], 0, 0, false, true, "JSON contains unknown fields.");
            }))
            {
                Guid id = queue.Enqueue(new AnnotationJsonImportJobRequest("labels.json", "labels", false));
                Assert.IsTrue(confirmationReady.Wait(TimeSpan.FromSeconds(5)));
                queue.Cancel(id);
                Assert.IsTrue(queue.WaitUntilIdle(TimeSpan.FromSeconds(5)));
                Assert.AreEqual(ImportJobStatus.Canceled, queue.Jobs.Single().Status);
            }
            confirmationReady.Dispose();
        }

        [TestMethod]
        public void Rejecting_json_confirmation_marks_job_canceled_and_releases_fifo()
        {
            var confirmationReady = new ManualResetEventSlim();
            using (var queue = new ImportJobQueue((request, cancellationRequested, progress) =>
            {
                confirmationReady.Set();
                return new ImportJobResult(false, new string[0], 0, 0, false, true, "JSON contains unknown fields.");
            }))
            {
                Guid id = queue.Enqueue(new AnnotationJsonImportJobRequest("labels.json", "labels", false));
                Assert.IsTrue(confirmationReady.Wait(TimeSpan.FromSeconds(5)));
                queue.ResolveConfirmation(id, null);
                Assert.IsTrue(queue.WaitUntilIdle(TimeSpan.FromSeconds(5)));
                Assert.AreEqual(ImportJobStatus.Canceled, queue.Jobs.Single().Status);
                Assert.IsFalse(queue.Jobs.Single().RequiresConfirmation);
            }
            confirmationReady.Dispose();
        }

        [TestMethod]
        public void CancelAll_during_json_confirmation_result_does_not_wait_for_ui()
        {
            var started = new ManualResetEventSlim();
            var release = new ManualResetEventSlim();
            using (var queue = new ImportJobQueue((request, cancellationRequested, progress) =>
            {
                started.Set();
                Assert.IsTrue(release.Wait(TimeSpan.FromSeconds(5)));
                return new ImportJobResult(false, new string[0], 0, 0, false, true, "JSON contains unknown fields.");
            }))
            {
                queue.Enqueue(new AnnotationJsonImportJobRequest("labels.json", "labels", false));
                Assert.IsTrue(started.Wait(TimeSpan.FromSeconds(5)));
                queue.CancelAll();
                release.Set();
                Assert.IsTrue(queue.WaitUntilIdle(TimeSpan.FromSeconds(5)));
                Assert.AreEqual(ImportJobStatus.Canceled, queue.Jobs.Single().Status);
            }
            started.Dispose();
            release.Dispose();
        }

        [TestMethod]
        public void Completed_image_jobs_add_images_to_the_project()
        {
            string directory = Path.Combine(Path.GetTempPath(), "neurocle-image-queue-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                string first = Path.Combine(directory, "first.png");
                string second = Path.Combine(directory, "second.png");
                using (var bitmap = new Bitmap(4, 4)) bitmap.Save(first, ImageFormat.Png);
                using (var bitmap = new Bitmap(4, 4)) bitmap.Save(second, ImageFormat.Png);
                using (ProjectStore store = ProjectStore.Create(Path.Combine(directory, "queue.neurocle")))
                using (var queue = new ImportJobQueue((request, cancellationRequested, progress) => ImportImages(store, request, cancellationRequested, progress)))
                {
                    queue.Enqueue(new ImageFilesImportJobRequest(new[] { first }));
                    queue.Enqueue(new ImageFilesImportJobRequest(new[] { second }));

                    Assert.IsTrue(queue.WaitUntilIdle(TimeSpan.FromSeconds(5)));
                    Assert.AreEqual(2, store.GetImages().Count);
                    Assert.IsTrue(queue.Jobs.All(job => job.Status == ImportJobStatus.Completed));
                }
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [TestMethod]
        public void Completed_folder_job_reports_partial_success()
        {
            string directory = Path.Combine(Path.GetTempPath(), "neurocle-folder-queue-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                string sourceDirectory = Path.Combine(directory, "source");
                Directory.CreateDirectory(sourceDirectory);
                string valid = Path.Combine(sourceDirectory, "valid.png");
                using (var bitmap = new Bitmap(4, 4)) bitmap.Save(valid, ImageFormat.Png);
                File.WriteAllText(Path.Combine(sourceDirectory, "invalid.txt"), "not an image");
                using (ProjectStore store = ProjectStore.Create(Path.Combine(directory, "queue.neurocle")))
                using (var queue = new ImportJobQueue((request, cancellationRequested, progress) => ImportImportJob(store, request, cancellationRequested, progress)))
                {
                    queue.Enqueue(new FolderImportJobRequest(sourceDirectory, false));

                    Assert.IsTrue(queue.WaitUntilIdle(TimeSpan.FromSeconds(5)));
                    ImportJobSnapshot job = queue.Jobs.Single();
                    Assert.AreEqual(ImportJobStatus.Completed, job.Status);
                    Assert.AreEqual(1, job.Result.ImportedImageIds.Count);
                    Assert.AreEqual(1, job.Result.FailedCount);
                    Assert.AreEqual(0, job.Result.UnprocessedCount);
                    Assert.AreEqual(1, store.GetImages().Count);
                }
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [TestMethod]
        public void Canceling_a_running_folder_job_preserves_completed_images()
        {
            string directory = Path.Combine(Path.GetTempPath(), "neurocle-folder-cancel-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var firstProcessed = new ManualResetEventSlim();
            var release = new ManualResetEventSlim();
            try
            {
                string sourceDirectory = Path.Combine(directory, "source");
                Directory.CreateDirectory(sourceDirectory);
                foreach (string name in new[] { "first.png", "second.png" })
                {
                    using (var bitmap = new Bitmap(4, 4)) bitmap.Save(Path.Combine(sourceDirectory, name), ImageFormat.Png);
                }
                using (ProjectStore store = ProjectStore.Create(Path.Combine(directory, "queue.neurocle")))
                using (var queue = new ImportJobQueue((request, cancellationRequested, progress) => ImportImportJob(store, request, cancellationRequested, progress, value =>
                {
                    if (value.ProcessedCount == 1)
                    {
                        firstProcessed.Set();
                        Assert.IsTrue(release.Wait(TimeSpan.FromSeconds(5)));
                    }
                })))
                {
                    Guid jobId = queue.Enqueue(new FolderImportJobRequest(sourceDirectory, false));
                    Assert.IsTrue(firstProcessed.Wait(TimeSpan.FromSeconds(5)));
                    queue.Cancel(jobId);
                    release.Set();
                    Assert.IsTrue(queue.WaitUntilIdle(TimeSpan.FromSeconds(5)));

                    ImportJobSnapshot job = queue.Jobs.Single();
                    Assert.AreEqual(ImportJobStatus.Canceled, job.Status);
                    Assert.AreEqual(1, job.Result.ImportedImageIds.Count);
                    Assert.AreEqual(1, job.Result.UnprocessedCount);
                    Assert.AreEqual(1, store.GetImages().Count);
                }
            }
            finally
            {
                release.Set();
                firstProcessed.Dispose();
                release.Dispose();
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [TestMethod]
        public void Missing_folder_reports_failure_and_does_not_block_following_job()
        {
            string directory = Path.Combine(Path.GetTempPath(), "neurocle-folder-missing-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                string valid = Path.Combine(directory, "valid.png");
                using (var bitmap = new Bitmap(4, 4)) bitmap.Save(valid, ImageFormat.Png);
                string missing = Path.Combine(directory, "missing");
                using (ProjectStore store = ProjectStore.Create(Path.Combine(directory, "queue.neurocle")))
                using (var queue = new ImportJobQueue((request, cancellationRequested, progress) => ImportImportJob(store, request, cancellationRequested, progress)))
                {
                    queue.Enqueue(new FolderImportJobRequest(missing, false));
                    queue.Enqueue(new ImageFilesImportJobRequest(new[] { valid }));

                    Assert.IsTrue(queue.WaitUntilIdle(TimeSpan.FromSeconds(5)));
                    Assert.AreEqual(ImportJobStatus.Completed, queue.Jobs[0].Status);
                    Assert.AreEqual(1, queue.Jobs[0].Result.FailedCount);
                    Assert.AreEqual(ImportJobStatus.Completed, queue.Jobs[1].Status);
                    Assert.AreEqual(1, queue.Jobs[1].Result.ImportedImageIds.Count);
                    Assert.AreEqual(1, store.GetImages().Count);
                }
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [TestMethod]
        public void Waiting_job_changes_to_canceled_immediately()
        {
            var firstStarted = new ManualResetEventSlim();
            var releaseFirst = new ManualResetEventSlim();
            using (var queue = new ImportJobQueue((request, cancellationRequested, progress) =>
            {
                firstStarted.Set();
                Assert.IsTrue(releaseFirst.Wait(TimeSpan.FromSeconds(5)));
                return new ImportJobResult(false, new string[0]);
            }))
            {
                queue.Enqueue(new ImageFilesImportJobRequest(new[] { "first.png" }));
                Guid waitingId = queue.Enqueue(new ImageFilesImportJobRequest(new[] { "second.png" }));
                Assert.IsTrue(firstStarted.Wait(TimeSpan.FromSeconds(5)));

                queue.Cancel(waitingId);

                Assert.AreEqual(ImportJobStatus.Canceled, queue.Jobs.Single(job => job.Id == waitingId).Status);
                releaseFirst.Set();
                Assert.IsTrue(queue.WaitUntilIdle(TimeSpan.FromSeconds(5)));
            }
        }

        [TestMethod]
        public void Subscriber_failure_does_not_stop_fifo_processing()
        {
            var executed = new List<string>();
            using (var queue = new ImportJobQueue((request, cancellationRequested, progress) =>
            {
                executed.Add(((ImageFilesImportJobRequest)request).FilePaths.Single());
                return new ImportJobResult(false, new string[0]);
            }))
            {
                queue.JobsChanged += jobs => throw new InvalidOperationException("subscriber failed");

                queue.Enqueue(new ImageFilesImportJobRequest(new[] { "first.png" }));
                queue.Enqueue(new ImageFilesImportJobRequest(new[] { "second.png" }));

                Assert.IsTrue(queue.WaitUntilIdle(TimeSpan.FromSeconds(5)));
                CollectionAssert.AreEqual(new[] { "first.png", "second.png" }, executed);
                Assert.IsTrue(queue.Jobs.All(job => job.Status == ImportJobStatus.Completed));
            }
        }

        [TestMethod]
        public void Dispose_waits_until_running_executor_observes_cancellation()
        {
            var started = new ManualResetEventSlim();
            var release = new ManualResetEventSlim();
            var queue = new ImportJobQueue((request, cancellationRequested, progress) =>
            {
                started.Set();
                Assert.IsTrue(release.Wait(TimeSpan.FromSeconds(5)));
                return new ImportJobResult(cancellationRequested(), new string[0]);
            });
            try
            {
                queue.Enqueue(new ImageFilesImportJobRequest(new[] { "first.png" }));
                Assert.IsTrue(started.Wait(TimeSpan.FromSeconds(5)));
                Task dispose = Task.Run(() => queue.Dispose());

                Assert.IsFalse(dispose.Wait(TimeSpan.FromMilliseconds(100)));
                release.Set();
                Assert.IsTrue(dispose.Wait(TimeSpan.FromSeconds(5)));
            }
            finally
            {
                release.Set();
                queue.Dispose();
                started.Dispose();
                release.Dispose();
            }
        }

        [TestMethod]
        public void Existing_image_can_be_edited_between_files_of_a_running_import_job()
        {
            string directory = Path.Combine(Path.GetTempPath(), "neurocle-image-queue-edit-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                string existingPath = Path.Combine(directory, "existing.png");
                string firstPath = Path.Combine(directory, "first.png");
                string secondPath = Path.Combine(directory, "second.png");
                foreach (string path in new[] { existingPath, firstPath, secondPath })
                {
                    using (var bitmap = new Bitmap(4, 4)) bitmap.Save(path, ImageFormat.Png);
                }

                using (ProjectStore store = ProjectStore.Create(Path.Combine(directory, "editable.neurocle")))
                {
                    ProjectImage existing = store.ImportFiles(new[] { existingPath }).Single();
                    AnnotationSet set = store.CreateAnnotationSet("cla", AnnotationSetType.Cla);
                    string classId = store.GetClasses(set.Id).Single().Id;
                    var firstImported = new ManualResetEventSlim();
                    var releaseImport = new ManualResetEventSlim();
                    using (var queue = new ImportJobQueue((request, cancellationRequested, progress) => ImportImages(store, request, cancellationRequested, progress, value =>
                    {
                        if (value.ProcessedCount != 1) return;
                        firstImported.Set();
                        Assert.IsTrue(releaseImport.Wait(TimeSpan.FromSeconds(5)));
                    })))
                    {
                        queue.Enqueue(new ImageFilesImportJobRequest(new[] { firstPath, secondPath }));
                        Assert.IsTrue(firstImported.Wait(TimeSpan.FromSeconds(5)));

                        store.SetClaClass(set.Id, existing.Id, classId);

                        releaseImport.Set();
                        Assert.IsTrue(queue.WaitUntilIdle(TimeSpan.FromSeconds(5)));
                        Assert.AreEqual(classId, store.GetClaClassId(set.Id, existing.Id));
                        Assert.AreEqual(3, store.GetImages().Count);
                        Assert.AreEqual(ImportJobStatus.Completed, queue.Jobs.Single().Status);
                    }
                }
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        private static ImportJobResult ImportImages(ProjectStore store, ImportJobRequest request, Func<bool> cancellationRequested, Action<ImportJobProgress> progress, Action<ImageImportProgress> afterProgress = null)
        {
            return ImportImportJob(store, request, cancellationRequested, progress, afterProgress);
        }

        private static ImportJobResult ExecuteMainFormImport(ProjectStore store, ImportJobRequest request, Func<bool> cancellationRequested, Action<ImportJobProgress> progress)
        {
            MethodInfo method = typeof(MainForm).GetMethod("ExecuteImportJob", BindingFlags.Static | BindingFlags.NonPublic);
            return (ImportJobResult)method.Invoke(null, new object[] { store, request, cancellationRequested, progress });
        }

        private static ImportJobResult ImportImportJob(ProjectStore store, ImportJobRequest request, Func<bool> cancellationRequested, Action<ImportJobProgress> progress, Action<ImageImportProgress> afterProgress = null)
        {
            ImageImportReport report;
            var imageRequest = request as ImageFilesImportJobRequest;
            var folderRequest = request as FolderImportJobRequest;
            Action<ImageImportProgress> reportProgress = value =>
            {
                progress(new ImportJobProgress(value.ProcessedCount, value.TotalCount, value.SuccessfulCount, value.FailedCount));
                if (afterProgress != null) afterProgress(value);
            };
            if (imageRequest != null) report = store.ImportFilesWithReport(imageRequest.FilePaths, cancellationRequested, reportProgress);
            else if (folderRequest != null) report = store.ImportDirectory(folderRequest.DirectoryPath, folderRequest.IncludeSubdirectories, cancellationRequested, reportProgress);
            else throw new InvalidOperationException("Unsupported import job kind: " + request.Kind);
            return new ImportJobResult(
                report.UnprocessedCount > 0 || report.WasCancelledDuringScan,
                report.Imported.Select(image => image.Id).ToArray(), report.Failures.Count, report.UnprocessedCount, report.WasCancelledDuringScan);
        }
    }
}

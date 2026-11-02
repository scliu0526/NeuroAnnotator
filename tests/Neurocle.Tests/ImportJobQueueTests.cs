using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
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
            var imageRequest = (ImageFilesImportJobRequest)request;
            ImageImportReport report = store.ImportFilesWithReport(imageRequest.FilePaths, cancellationRequested, value =>
            {
                progress(new ImportJobProgress(value.ProcessedCount, value.TotalCount, value.SuccessfulCount, value.FailedCount));
                if (afterProgress != null) afterProgress(value);
            });
            return new ImportJobResult(
                report.UnprocessedCount > 0 || report.WasCancelledDuringScan,
                report.Imported.Select(image => image.Id).ToArray());
        }
    }
}

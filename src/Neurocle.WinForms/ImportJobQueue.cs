using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Neurocle.WinForms
{
    internal enum ImportJobKind
    {
        ImageFiles
    }

    internal enum ImportJobStatus
    {
        Waiting,
        Running,
        Completed,
        Failed,
        Canceled
    }

    internal abstract class ImportJobRequest
    {
        protected ImportJobRequest(ImportJobKind kind)
        {
            Kind = kind;
        }

        public ImportJobKind Kind { get; }
    }

    internal sealed class ImageFilesImportJobRequest : ImportJobRequest
    {
        public ImageFilesImportJobRequest(IEnumerable<string> filePaths)
            : base(ImportJobKind.ImageFiles)
        {
            if (filePaths == null) throw new ArgumentNullException(nameof(filePaths));
            FilePaths = Array.AsReadOnly(filePaths.ToArray());
            if (FilePaths.Count == 0) throw new ArgumentException("At least one image path is required.", nameof(filePaths));
        }

        public IReadOnlyList<string> FilePaths { get; }
    }

    internal sealed class ImportJobProgress
    {
        public ImportJobProgress(int processedCount, int totalCount, int successfulCount, int failedCount)
        {
            ProcessedCount = processedCount;
            TotalCount = totalCount;
            SuccessfulCount = successfulCount;
            FailedCount = failedCount;
        }

        public int ProcessedCount { get; }
        public int TotalCount { get; }
        public int SuccessfulCount { get; }
        public int FailedCount { get; }
    }

    internal sealed class ImportJobResult
    {
        public ImportJobResult(bool wasCanceled, IReadOnlyList<string> importedImageIds)
        {
            WasCanceled = wasCanceled;
            ImportedImageIds = Array.AsReadOnly((importedImageIds ?? new string[0]).ToArray());
        }

        public bool WasCanceled { get; }
        public IReadOnlyList<string> ImportedImageIds { get; }
    }

    internal sealed class ImportJobSnapshot
    {
        public ImportJobSnapshot(Guid id, ImportJobRequest request, ImportJobStatus status, ImportJobProgress progress, ImportJobResult result, string errorMessage)
        {
            Id = id;
            Request = request;
            Status = status;
            Progress = progress;
            Result = result;
            ErrorMessage = errorMessage;
        }

        public Guid Id { get; }
        public ImportJobRequest Request { get; }
        public ImportJobStatus Status { get; }
        public ImportJobProgress Progress { get; }
        public ImportJobResult Result { get; }
        public string ErrorMessage { get; }
    }

    internal sealed class ImportJobQueue : IDisposable
    {
        internal delegate ImportJobResult ImportExecutor(ImportJobRequest request, Func<bool> cancellationRequested, Action<ImportJobProgress> progress);

        private sealed class Job
        {
            public Job(ImportJobRequest request)
            {
                Id = Guid.NewGuid();
                Request = request;
                Status = ImportJobStatus.Waiting;
            }

            public Guid Id { get; }
            public ImportJobRequest Request { get; }
            public ImportJobStatus Status { get; set; }
            public ImportJobProgress Progress { get; set; }
            public ImportJobResult Result { get; set; }
            public string ErrorMessage { get; set; }
            public bool CancellationRequested { get; set; }

            public ImportJobSnapshot Snapshot()
            {
                return new ImportJobSnapshot(Id, Request, Status, Progress, Result, ErrorMessage);
            }
        }

        private readonly object gate = new object();
        private readonly Queue<Job> waiting = new Queue<Job>();
        private readonly List<Job> jobs = new List<Job>();
        private readonly ImportExecutor executor;
        private readonly ManualResetEventSlim idle = new ManualResetEventSlim(true);
        private bool workerRunning;
        private bool disposed;

        public ImportJobQueue(ImportExecutor executor)
        {
            this.executor = executor ?? throw new ArgumentNullException(nameof(executor));
        }

        public event Action<IReadOnlyList<ImportJobSnapshot>> JobsChanged;

        public IReadOnlyList<ImportJobSnapshot> Jobs
        {
            get
            {
                lock (gate) return jobs.Select(job => job.Snapshot()).ToArray();
            }
        }

        public bool IsBusy
        {
            get
            {
                lock (gate) return workerRunning;
            }
        }

        public Guid Enqueue(ImportJobRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            var job = new Job(request);
            bool startWorker = false;
            lock (gate)
            {
                ThrowIfDisposed();
                jobs.Add(job);
                waiting.Enqueue(job);
                idle.Reset();
                if (!workerRunning)
                {
                    workerRunning = true;
                    startWorker = true;
                }
            }
            Publish();
            if (startWorker) Task.Run((Action)ProcessQueue);
            return job.Id;
        }

        public void Cancel(Guid jobId)
        {
            lock (gate)
            {
                Job job = jobs.FirstOrDefault(candidate => candidate.Id == jobId);
                if (job == null || job.Status == ImportJobStatus.Completed || job.Status == ImportJobStatus.Failed || job.Status == ImportJobStatus.Canceled) return;
                job.CancellationRequested = true;
                if (job.Status == ImportJobStatus.Waiting) job.Status = ImportJobStatus.Canceled;
            }
            Publish();
        }

        public bool WaitUntilIdle(TimeSpan timeout)
        {
            return idle.Wait(timeout);
        }

        private void ProcessQueue()
        {
            while (true)
            {
                Job job;
                lock (gate)
                {
                    if (waiting.Count == 0)
                    {
                        workerRunning = false;
                        idle.Set();
                        job = null;
                    }
                    else
                    {
                        job = waiting.Dequeue();
                        if (job.CancellationRequested)
                        {
                            job.Status = ImportJobStatus.Canceled;
                            job.Result = new ImportJobResult(true, new string[0]);
                        }
                        else job.Status = ImportJobStatus.Running;
                    }
                }
                Publish();
                if (job == null) return;
                if (job.Status == ImportJobStatus.Canceled) continue;

                try
                {
                    ImportJobResult result = executor(job.Request, () => IsCancellationRequested(job), progress => Report(job, progress));
                    lock (gate)
                    {
                        job.Result = result;
                        job.Status = result.WasCanceled ? ImportJobStatus.Canceled : ImportJobStatus.Completed;
                    }
                }
                catch (Exception exception)
                {
                    lock (gate)
                    {
                        job.ErrorMessage = exception.Message;
                        job.Status = ImportJobStatus.Failed;
                    }
                }
                Publish();
            }
        }

        private bool IsCancellationRequested(Job job)
        {
            lock (gate) return job.CancellationRequested || disposed;
        }

        private void Report(Job job, ImportJobProgress progress)
        {
            lock (gate) job.Progress = progress;
            Publish();
        }

        private void Publish()
        {
            Action<IReadOnlyList<ImportJobSnapshot>> handler = JobsChanged;
            if (handler == null) return;
            IReadOnlyList<ImportJobSnapshot> snapshots = Jobs;
            foreach (Action<IReadOnlyList<ImportJobSnapshot>> subscriber in handler.GetInvocationList())
            {
                try { subscriber(snapshots); }
                catch (Exception) { }
            }
        }

        private void ThrowIfDisposed()
        {
            if (disposed) throw new ObjectDisposedException(nameof(ImportJobQueue));
        }

        public void Dispose()
        {
            lock (gate)
            {
                if (disposed) return;
                disposed = true;
                foreach (Job job in jobs) job.CancellationRequested = true;
            }
            idle.Wait();
            idle.Dispose();
        }
    }
}

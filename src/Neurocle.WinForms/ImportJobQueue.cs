using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Neurocle.WinForms
{
    internal enum ImportJobKind
    {
        ImageFiles,
        Folder,
        AnnotationJson
    }

    internal enum ImportJobStatus
    {
        Waiting,
        Running,
        Completed,
        Failed,
        Canceled
    }

    internal sealed class ImportQueueCapacityException : InvalidOperationException
    {
        public ImportQueueCapacityException()
            : base("The import queue already contains ten waiting or running jobs.")
        {
        }
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

    internal sealed class FolderImportJobRequest : ImportJobRequest
    {
        public FolderImportJobRequest(string directoryPath, bool includeSubdirectories)
            : base(ImportJobKind.Folder)
        {
            if (String.IsNullOrWhiteSpace(directoryPath)) throw new ArgumentException("A directory path is required.", nameof(directoryPath));
            DirectoryPath = directoryPath;
            IncludeSubdirectories = includeSubdirectories;
        }

        public string DirectoryPath { get; }
        public bool IncludeSubdirectories { get; }
    }

    internal sealed class AnnotationJsonImportJobRequest : ImportJobRequest
    {
        public AnnotationJsonImportJobRequest(string filePath, string annotationSetName, bool allowUnknownFields)
            : base(ImportJobKind.AnnotationJson)
        {
            if (String.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("A JSON file path is required.", nameof(filePath));
            if (String.IsNullOrWhiteSpace(annotationSetName)) throw new ArgumentException("An annotation set name is required.", nameof(annotationSetName));
            FilePath = filePath;
            AnnotationSetName = annotationSetName;
            AllowUnknownFields = allowUnknownFields;
        }

        public string FilePath { get; }
        public string AnnotationSetName { get; }
        public bool AllowUnknownFields { get; }
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
        public ImportJobResult(bool wasCanceled, IReadOnlyList<string> importedImageIds, int failedCount = 0, int unprocessedCount = 0, bool wasCanceledDuringScan = false, bool requiresConfirmation = false, string confirmationMessage = null)
        {
            WasCanceled = wasCanceled;
            ImportedImageIds = Array.AsReadOnly((importedImageIds ?? new string[0]).ToArray());
            FailedCount = failedCount;
            UnprocessedCount = unprocessedCount;
            WasCanceledDuringScan = wasCanceledDuringScan;
            RequiresConfirmation = requiresConfirmation;
            ConfirmationMessage = confirmationMessage;
        }

        public bool WasCanceled { get; }
        public IReadOnlyList<string> ImportedImageIds { get; }
        public int FailedCount { get; }
        public int UnprocessedCount { get; }
        public bool WasCanceledDuringScan { get; }
        public bool RequiresConfirmation { get; }
        public string ConfirmationMessage { get; }
    }

    internal sealed class ImportJobSnapshot
    {
        public ImportJobSnapshot(Guid id, ImportJobRequest request, ImportJobStatus status, ImportJobProgress progress, ImportJobResult result, string errorMessage, bool requiresConfirmation = false)
        {
            Id = id;
            Request = request;
            Status = status;
            Progress = progress;
            Result = result;
            ErrorMessage = errorMessage;
            RequiresConfirmation = requiresConfirmation;
        }

        public Guid Id { get; }
        public ImportJobRequest Request { get; }
        public ImportJobStatus Status { get; }
        public ImportJobProgress Progress { get; }
        public ImportJobResult Result { get; }
        public string ErrorMessage { get; }
        public bool RequiresConfirmation { get; }
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
            public ImportJobRequest Request { get; set; }
            public ImportJobStatus Status { get; set; }
            public ImportJobProgress Progress { get; set; }
            public ImportJobResult Result { get; set; }
            public string ErrorMessage { get; set; }
            public bool CancellationRequested { get; set; }
            public bool RequiresConfirmation { get; set; }

            /// <summary>
            /// 將工作目前狀態複製為不可變快照。
            /// </summary>
            /// <returns>包含識別碼、要求、狀態、進度及結果的工作快照。</returns>
            public ImportJobSnapshot Snapshot()
            {
                return new ImportJobSnapshot(Id, Request, Status, Progress, Result, ErrorMessage, RequiresConfirmation);
            }
        }

        private readonly object gate = new object();
        private readonly Queue<Job> waiting = new Queue<Job>();
        private readonly List<Job> jobs = new List<Job>();
        private readonly ImportExecutor executor;
        private readonly ManualResetEventSlim idle = new ManualResetEventSlim(true);
        private readonly ManualResetEventSlim confirmationSignal = new ManualResetEventSlim(false);
        private Job confirmationJob;
        private ImportJobRequest confirmationRetry;
        private bool workerRunning;
        private bool disposed;

        private const int MaxActiveJobs = 10;
        private const int MaxCompletedJobs = 20;

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

        /// <summary>
        /// 將匯入要求加入 FIFO 佇列，必要時啟動工作執行緒。
        /// </summary>
        /// <param name="request">要排入佇列的圖片、資料夾或標註 JSON 要求。</param>
        /// <returns>新工作在本佇列中的識別碼。</returns>
        /// <exception cref="ArgumentNullException">要求為 null。</exception>
        /// <exception cref="ObjectDisposedException">佇列已處置。</exception>
        /// <exception cref="ImportQueueCapacityException">等待中或執行中的工作已達容量上限。</exception>
        public Guid Enqueue(ImportJobRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            var job = new Job(request);
            bool startWorker = false;
            lock (gate)
            {
                ThrowIfDisposed();
                if (jobs.Count(job => job.Status == ImportJobStatus.Waiting || job.Status == ImportJobStatus.Running) >= MaxActiveJobs) throw new ImportQueueCapacityException();
                TrimHistoryLocked();
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

        /// <summary>
        /// 要求取消指定的等待中或執行中工作。
        /// </summary>
        /// <param name="jobId">要取消的工作識別碼；找不到或已結束時不執行動作。</param>
        public void Cancel(Guid jobId)
        {
            lock (gate)
            {
                Job job = jobs.FirstOrDefault(candidate => candidate.Id == jobId);
                if (job == null || job.Status == ImportJobStatus.Completed || job.Status == ImportJobStatus.Failed || job.Status == ImportJobStatus.Canceled) return;
                job.CancellationRequested = true;
                if (job.Status == ImportJobStatus.Waiting) job.Status = ImportJobStatus.Canceled;
                if (confirmationJob != null && confirmationJob.Id == jobId) confirmationSignal.Set();
                TrimHistoryLocked();
            }
            Publish();
        }

        /// <summary>
        /// 回覆目前暫停佇列的 JSON 匯入確認，並可指定重試要求。
        /// </summary>
        /// <param name="jobId">等待確認的工作識別碼。</param>
        /// <param name="retryRequest">使用者確認後重試的要求；null 代表不重試並結束該工作。</param>
        public void ResolveConfirmation(Guid jobId, ImportJobRequest retryRequest)
        {
            lock (gate)
            {
                if (confirmationJob == null || confirmationJob.Id != jobId) return;
                confirmationRetry = retryRequest;
                confirmationSignal.Set();
            }
        }

        /// <summary>
        /// 移除所有已完成、失敗或取消的工作歷史。
        /// </summary>
        public void ClearCompleted()
        {
            lock (gate) jobs.RemoveAll(job => job.Status == ImportJobStatus.Completed || job.Status == ImportJobStatus.Failed || job.Status == ImportJobStatus.Canceled);
            Publish();
        }

        /// <summary>
        /// 要求取消所有等待中與執行中的工作。
        /// </summary>
        public void CancelAll()
        {
            lock (gate)
            {
                foreach (Job job in jobs)
                {
                    if (job.Status == ImportJobStatus.Waiting || job.Status == ImportJobStatus.Running) job.CancellationRequested = true;
                }
                if (confirmationJob != null) confirmationSignal.Set();
            }
            Publish();
        }

        /// <summary>
        /// 等待佇列停止執行，直到佇列閒置或逾時。
        /// </summary>
        /// <param name="timeout">最多等待的時間。</param>
        /// <returns>佇列在期限內進入閒置時為 true；逾時則為 false。</returns>
        /// <exception cref="ArgumentOutOfRangeException">逾時值小於 -1 毫秒。</exception>
        public bool WaitUntilIdle(TimeSpan timeout)
        {
            return idle.Wait(timeout);
        }

        /// <summary>
        /// 依 FIFO 順序執行工作，處理確認、取消、失敗及歷史裁剪。
        /// </summary>
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
                            TrimHistoryLocked();
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
                    if (result.RequiresConfirmation)
                    {
                        bool waitForConfirmation;
                        lock (gate)
                        {
                            waitForConfirmation = !disposed && !job.CancellationRequested;
                            if (!waitForConfirmation)
                            {
                                job.Status = ImportJobStatus.Canceled;
                                job.Result = new ImportJobResult(true, new string[0]);
                                job.RequiresConfirmation = false;
                            }
                            else
                            {
                                job.ErrorMessage = result.ConfirmationMessage;
                                job.Status = ImportJobStatus.Waiting;
                                job.RequiresConfirmation = true;
                                confirmationJob = job;
                                confirmationRetry = null;
                                confirmationSignal.Reset();
                            }
                        }
                        Publish();
                        if (!waitForConfirmation) continue;
                        confirmationSignal.Wait();
                        lock (gate)
                        {
                            if (disposed || job.CancellationRequested)
                            {
                                job.Status = ImportJobStatus.Canceled;
                                job.Result = new ImportJobResult(true, new string[0]);
                                job.RequiresConfirmation = false;
                            }
                            else if (confirmationRetry != null)
                            {
                                job.Request = confirmationRetry;
                                job.ErrorMessage = null;
                                job.Result = null;
                                job.RequiresConfirmation = false;
                                job.Status = ImportJobStatus.Waiting;
                                Job[] laterJobs = waiting.ToArray();
                                waiting.Clear();
                                waiting.Enqueue(job);
                                foreach (Job laterJob in laterJobs) waiting.Enqueue(laterJob);
                            }
                            else
                            {
                                job.Status = ImportJobStatus.Canceled;
                                job.Result = new ImportJobResult(true, new string[0]);
                                job.RequiresConfirmation = false;
                            }
                            confirmationJob = null;
                            confirmationRetry = null;
                        }
                        Publish();
                        continue;
                    }
                    lock (gate)
                    {
                        job.Result = result;
                        job.Status = result.WasCanceled ? ImportJobStatus.Canceled : ImportJobStatus.Completed;
                        TrimHistoryLocked();
                    }
                }
                catch (Exception exception)
                {
                    lock (gate)
                    {
                        job.ErrorMessage = exception.Message;
                        job.Status = ImportJobStatus.Failed;
                        if (IsFatal(exception))
                        {
                            while (waiting.Count > 0)
                            {
                                Job waitingJob = waiting.Dequeue();
                                waitingJob.Status = ImportJobStatus.Failed;
                                waitingJob.ErrorMessage = "Import queue stopped after a fatal project error.";
                            }
                        }
                        TrimHistoryLocked();
                    }
                }
                Publish();
            }
        }

        /// <summary>
        /// 取得指定工作或佇列目前是否要求取消。
        /// </summary>
        /// <param name="job">要檢查的工作。</param>
        /// <returns>工作已取消或佇列已處置時為 true。</returns>
        private bool IsCancellationRequested(Job job)
        {
            lock (gate) return job.CancellationRequested || disposed;
        }

        /// <summary>
        /// 更新指定工作的進度並發布佇列快照。
        /// </summary>
        /// <param name="job">進度所屬的工作。</param>
        /// <param name="progress">最新的匯入進度。</param>
        private void Report(Job job, ImportJobProgress progress)
        {
            lock (gate) job.Progress = progress;
            Publish();
        }

        /// <summary>
        /// 將目前工作快照通知每個訂閱者；單一訂閱者失敗不影響其他訂閱者。
        /// </summary>
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

        /// <summary>
        /// 確認佇列尚未處置。
        /// </summary>
        /// <exception cref="ObjectDisposedException">佇列已處置。</exception>
        private void ThrowIfDisposed()
        {
            if (disposed) throw new ObjectDisposedException(nameof(ImportJobQueue));
        }

        /// <summary>
        /// 在持有佇列鎖時裁剪超出保留上限的最舊終止工作。
        /// </summary>
        private void TrimHistoryLocked()
        {
            while (jobs.Count(job => job.Status == ImportJobStatus.Completed || job.Status == ImportJobStatus.Failed || job.Status == ImportJobStatus.Canceled) > MaxCompletedJobs)
            {
                Job oldest = jobs.FirstOrDefault(job => job.Status == ImportJobStatus.Completed || job.Status == ImportJobStatus.Failed || job.Status == ImportJobStatus.Canceled);
                if (oldest == null) return;
                jobs.Remove(oldest);
            }
        }

        /// <summary>
        /// 判斷工作例外是否表示專案或資料庫已無法繼續安全處理。
        /// </summary>
        /// <param name="exception">工作執行時擲出的例外。</param>
        /// <returns>例外為物件已處置或 SQLite 錯誤時為 true。</returns>
        private static bool IsFatal(Exception exception)
        {
            return exception is ObjectDisposedException || exception.GetType().Name == "SqliteException";
        }

        /// <summary>
        /// 停止接受工作、取消佇列工作並釋放同步控制資源。
        /// </summary>
        public void Dispose()
        {
            lock (gate)
            {
                if (disposed) return;
                disposed = true;
                foreach (Job job in jobs) job.CancellationRequested = true;
                confirmationSignal.Set();
            }
            idle.Wait();
            confirmationSignal.Dispose();
            idle.Dispose();
        }
    }
}

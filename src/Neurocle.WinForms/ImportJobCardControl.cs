using System;
using System.Windows.Forms;
using Neurocle.WinForms.Resources;

namespace Neurocle.WinForms
{
    /// <summary>顯示單一匯入工作的來源、狀態、進度與可用操作。</summary>
    internal sealed partial class ImportJobCardControl : UserControl
    {
        private ImportJobSnapshot job;
        private bool detailsVisible;

        public ImportJobCardControl()
        {
            InitializeComponent();
        }

        public event Action<Guid> CancelRequested;
        public event Action<Guid> ViewImportedImagesRequested;

        /// <summary>取得目前卡片所代表的工作識別碼。</summary>
        public Guid JobId { get { return job == null ? Guid.Empty : job.Id; } }

        /// <summary>套用匯入工作快照並更新卡片內容。</summary>
        public void UpdateJob(ImportJobSnapshot value)
        {
            job = value;
            sourceLabel.Text = KindText(value.Request) + "：" + SourceText(value.Request);
            statusLabel.Text = StatusText(value.Status);
            progressLabel.Text = value.Result == null
                ? value.Status == ImportJobStatus.Waiting ? Strings.ImportQueueWaiting : value.Progress == null ? string.Empty : string.Format(Strings.ImportQueueProgress, value.Progress.ProcessedCount, value.Progress.TotalCount)
                : value.Result.WasCanceledDuringScan ? Strings.ImportScanCancelled : string.Format(Strings.ImportQueueResultSummary, value.Result.ImportedImageIds.Count, value.Result.FailedCount, value.Result.UnprocessedCount);
            detailsLabel.Text = value.ErrorMessage ?? string.Empty;
            detailsLabel.Visible = detailsVisible && detailsLabel.Text.Length > 0;
            detailsButton.Text = detailsVisible ? Strings.HideDetails : Strings.ShowDetails;
            cancelButton.Enabled = value.Status == ImportJobStatus.Waiting || (value.Status == ImportJobStatus.Running && !(value.Request is AnnotationJsonImportJobRequest));
            bool canView = value.Status == ImportJobStatus.Completed
                && !(value.Request is AnnotationJsonImportJobRequest)
                && value.Result != null
                && value.Result.ImportedImageIds.Count > 0;
            viewImportedImagesButton.Visible = canView;
            viewImportedImagesButton.Enabled = canView;
            toolTip.SetToolTip(viewImportedImagesButton, string.Empty);
        }

        private void DetailsButton_Click(object sender, EventArgs args)
        {
            detailsVisible = !detailsVisible;
            if (job != null) UpdateJob(job);
        }

        private void CancelButton_Click(object sender, EventArgs args)
        {
            if (job != null && CancelRequested != null) CancelRequested(job.Id);
        }

        /// <summary>通知要求切換至本次匯入仍存在的圖片。</summary>
        private void ViewImportedImagesButton_Click(object sender, EventArgs args)
        {
            if (job != null && ViewImportedImagesRequested != null) ViewImportedImagesRequested(job.Id);
        }

        /// <summary>更新本次匯入圖片的跳轉可用性及停用原因。</summary>
        public void SetViewImportedImagesAvailability(bool enabled, string unavailableReason)
        {
            viewImportedImagesButton.Enabled = enabled;
            toolTip.SetToolTip(viewImportedImagesButton, enabled ? string.Empty : unavailableReason ?? string.Empty);
        }

        private static string SourceText(ImportJobRequest request)
        {
            var imageRequest = request as ImageFilesImportJobRequest;
            if (imageRequest != null) return imageRequest.FilePaths.Count == 1 ? imageRequest.FilePaths[0] : string.Format(Strings.ImportQueueFileCount, imageRequest.FilePaths.Count);
            var folderRequest = request as FolderImportJobRequest;
            if (folderRequest != null) return folderRequest.DirectoryPath + (folderRequest.IncludeSubdirectories ? Strings.ImportQueueFolderRecursive : string.Empty);
            var jsonRequest = request as AnnotationJsonImportJobRequest;
            if (jsonRequest != null) return jsonRequest.FilePath;
            return request.Kind.ToString();
        }

        private static string KindText(ImportJobRequest request)
        {
            if (request is ImageFilesImportJobRequest) return Strings.ImportImages;
            if (request is FolderImportJobRequest) return Strings.ImportFolder;
            if (request is AnnotationJsonImportJobRequest) return Strings.ImportAnnotationJson;
            return request.Kind.ToString();
        }

        private static string StatusText(ImportJobStatus status)
        {
            switch (status)
            {
                case ImportJobStatus.Waiting: return Strings.ImportQueueWaiting;
                case ImportJobStatus.Running: return Strings.ImportQueueRunning;
                case ImportJobStatus.Completed: return Strings.ImportQueueCompleted;
                case ImportJobStatus.Failed: return Strings.ImportQueueFailed;
                case ImportJobStatus.Canceled: return Strings.ImportQueueCanceled;
                default: return status.ToString();
            }
        }
    }
}

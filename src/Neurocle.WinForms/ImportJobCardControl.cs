using System;
using System.ComponentModel;
using System.ComponentModel.Design;
using System.IO;
using System.Windows.Forms;
using Neurocle.WinForms.Resources;

namespace Neurocle.WinForms
{
    /// <summary>
    /// 顯示單一匯入工作的來源、狀態、進度與可用操作。
    /// </summary>
    internal sealed partial class ImportJobCardControl : UserControl
    {
        private ImportJobSnapshot job;
        private bool detailsVisible;

        public ImportJobCardControl()
        {
            InitializeComponent();
        }

        protected override void OnCreateControl()
        {
            base.OnCreateControl();
            if (IsDesignPreview())
            {
                ConfigureDesignPreview();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (IsDesignPreview())
            {
                TextRenderer.DrawText(e.Graphics, "匯入圖片：sample.png", Font, new System.Drawing.Point(8, 8), ForeColor, BackColor, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            }
        }

        private bool IsDesignPreview()
        {
            return LicenseManager.UsageMode == LicenseUsageMode.Designtime
                || (Site != null && Site.DesignMode)
                || GetService(typeof(IDesignerHost)) != null;
        }

        private void ConfigureDesignPreview()
        {
            sourceLabel.Dock = DockStyle.None;
            sourceLabel.BackColor = System.Drawing.Color.Transparent;
            sourceLabel.ForeColor = System.Drawing.SystemColors.ControlText;
            detailsLabel.Dock = DockStyle.None;
            detailsLabel.Size = System.Drawing.Size.Empty;
            detailsLabel.Visible = false;
            sourceLabel.Location = new System.Drawing.Point(8, 8);
            sourceLabel.Size = new System.Drawing.Size(Math.Max(160, ClientSize.Width - 16), 24);
            sourceLabel.Text = "匯入圖片：sample.png";
            sourceLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            sourceLabel.Visible = true;
            sourceLabel.BringToFront();
            statusLabel.Location = new System.Drawing.Point(8, 36);
            statusLabel.Text = Strings.ImportQueueWaiting;
            statusLabel.BringToFront();
            progressLabel.Location = new System.Drawing.Point(120, 36);
            progressLabel.Text = "0 / 1";
            progressLabel.BringToFront();
            detailsLabel.Visible = false;
            detailsButton.Visible = true;
            detailsButton.Location = new System.Drawing.Point(Math.Max(8, ClientSize.Width - 170), 32);
            cancelButton.Visible = true;
            cancelButton.Location = new System.Drawing.Point(Math.Max(8, ClientSize.Width - 90), 32);
            viewImportedImagesButton.Visible = true;
            viewImportedImagesButton.Location = new System.Drawing.Point(8, 90);
        }

        public event Action<Guid> CancelRequested;
        public event Action<Guid> ViewImportedImagesRequested;
        public event Action<string> OpenExportRequested;

        /// <summary>
        /// 取得目前卡片所代表的工作識別碼。
        /// </summary>
        public Guid JobId { get { return job == null ? Guid.Empty : job.Id; } }

        /// <summary>
        /// 套用匯入工作快照並更新卡片內容。
        /// </summary>
        /// <param name="value">要套用或檢查的值。</param>
        public void UpdateJob(ImportJobSnapshot value)
        {
            job = value;
            sourceLabel.Text = KindText(value.Request) + "：" + SourceText(value.Request);
            statusLabel.Text = StatusText(value.Status);
            progressLabel.Text = value.Result == null
                ? value.Status == ImportJobStatus.Waiting ? Strings.ImportQueueWaiting : value.Progress == null ? string.Empty : string.Format(Strings.ImportQueueProgress, value.Progress.ProcessedCount, value.Progress.TotalCount)
                : value.Result.OutputPath != null ? Path.GetFileName(value.Result.OutputPath) : value.Result.WasCanceledDuringScan ? Strings.ImportScanCancelled : string.Format(Strings.ImportQueueResultSummary, value.Result.ImportedImageIds.Count, value.Result.FailedCount, value.Result.UnprocessedCount);
            int progress = value.Result != null ? 100 : value.Progress == null || value.Progress.TotalCount <= 0 ? 0 : Math.Max(0, Math.Min(100, (int)Math.Round(value.Progress.ProcessedCount * 100d / value.Progress.TotalCount)));
            progressBar.Value = progress;
            toolTip.SetToolTip(sourceLabel, SourceText(value.Request));
            detailsLabel.Text = value.ErrorMessage ?? string.Empty;
            toolTip.SetToolTip(detailsLabel, detailsLabel.Text);
            detailsLabel.Visible = detailsVisible && detailsLabel.Text.Length > 0;
            detailsLabel.Dock = detailsLabel.Visible ? DockStyle.Top : DockStyle.None;
            detailsLabel.Size = detailsLabel.Visible
                ? new System.Drawing.Size(ClientSize.Width, 52)
                : System.Drawing.Size.Empty;
            detailsButton.Text = detailsVisible ? Strings.HideDetails : Strings.ShowDetails;
            cancelButton.Enabled = value.Status == ImportJobStatus.Waiting || (value.Status == ImportJobStatus.Running && !(value.Request is AnnotationJsonImportJobRequest));
            bool canView = (value.Status == ImportJobStatus.Completed || value.Status == ImportJobStatus.Canceled)
                && !(value.Request is AnnotationJsonImportJobRequest)
                && value.Result != null
                && value.Result.ImportedImageIds.Count > 0;
            viewImportedImagesButton.Visible = canView;
            viewImportedImagesButton.Enabled = canView;
            bool canOpenExport = value.Request is ExportJobRequest && value.Status == ImportJobStatus.Completed && value.Result != null && !String.IsNullOrWhiteSpace(value.Result.OutputPath) && File.Exists(value.Result.OutputPath);
            openExportButton.Visible = value.Request is ExportJobRequest;
            openExportButton.Enabled = canOpenExport;
            toolTip.SetToolTip(viewImportedImagesButton, string.Empty);
            toolTip.SetToolTip(openExportButton, canOpenExport ? value.Result.OutputPath : "輸出檔案不存在");
        }

        /// <summary>
        /// 切換目前匯入工作的錯誤詳細資料顯示狀態。
        /// </summary>
        /// <param name="sender">引發事件的詳細資料按鈕。</param>
        /// <param name="args">按鈕點擊事件資料。</param>
        private void DetailsButton_Click(object sender, EventArgs args)
        {
            detailsVisible = !detailsVisible;
            if (job != null) UpdateJob(job);
        }

        /// <summary>
        /// 通知外層要求取消目前匯入工作。
        /// </summary>
        /// <param name="sender">引發事件的取消按鈕。</param>
        /// <param name="args">按鈕點擊事件資料。</param>
        private void CancelButton_Click(object sender, EventArgs args)
        {
            if (job != null && CancelRequested != null) CancelRequested(job.Id);
        }

        /// <summary>
        /// 通知要求切換至本次匯入仍存在的圖片。
        /// </summary>
        /// <param name="sender">觸發此事件的控制項或視窗。</param>
        /// <param name="args">包含此次事件詳細資料的事件資料。</param>
        private void ViewImportedImagesButton_Click(object sender, EventArgs args)
        {
            if (job != null && ViewImportedImagesRequested != null) ViewImportedImagesRequested(job.Id);
        }

        private void OpenExportButton_Click(object sender, EventArgs args)
        {
            if (job?.Result?.OutputPath != null && OpenExportRequested != null) OpenExportRequested(job.Result.OutputPath);
        }

        /// <summary>
        /// 更新本次匯入圖片的跳轉可用性及停用原因。
        /// </summary>
        /// <param name="enabled">是否啟用控制項。</param>
        /// <param name="unavailableReason">功能停用時顯示的原因。</param>
        public void SetViewImportedImagesAvailability(bool enabled, string unavailableReason)
        {
            viewImportedImagesButton.Enabled = enabled;
            toolTip.SetToolTip(viewImportedImagesButton, enabled ? string.Empty : unavailableReason ?? string.Empty);
        }

        /// <summary>
        /// 取得匯入工作卡片顯示的來源文字。
        /// </summary>
        /// <param name="request">提供檔案、資料夾或 JSON 來源資訊的工作要求。</param>
        /// <returns>適合顯示在卡片上的來源名稱或路徑。</returns>
        private static string SourceText(ImportJobRequest request)
        {
            var imageRequest = request as ImageFilesImportJobRequest;
            if (imageRequest != null) return imageRequest.FilePaths.Count == 1 ? imageRequest.FilePaths[0] : string.Format(Strings.ImportQueueFileCount, imageRequest.FilePaths.Count);
            var folderRequest = request as FolderImportJobRequest;
            if (folderRequest != null) return folderRequest.DirectoryPath + (folderRequest.IncludeSubdirectories ? Strings.ImportQueueFolderRecursive : string.Empty);
            var jsonRequest = request as AnnotationJsonImportJobRequest;
            if (jsonRequest != null) return jsonRequest.FilePath;
            var jsonExport = request as JsonExportJobRequest;
            if (jsonExport != null) return jsonExport.OutputPath;
            var zipExport = request as ImageZipExportJobRequest;
            if (zipExport != null) return zipExport.OutputPath;
            return request.Kind.ToString();
        }

        /// <summary>
        /// 取得匯入工作類型的本地化顯示文字。
        /// </summary>
        /// <param name="request">提供匯入工作類型的工作要求。</param>
        /// <returns>圖片、資料夾或標註 JSON 的顯示名稱。</returns>
        private static string KindText(ImportJobRequest request)
        {
            if (request is ImageFilesImportJobRequest) return Strings.ImportImages;
            if (request is FolderImportJobRequest) return Strings.ImportFolder;
            if (request is AnnotationJsonImportJobRequest) return Strings.ImportAnnotationJson;
            if (request is JsonExportJobRequest) return Strings.ExportAnnotationJson;
            if (request is ImageZipExportJobRequest) return ((ImageZipExportJobRequest)request).ExportAll ? Strings.ExportAllImages : Strings.ExportSelectedImages;
            return request.Kind.ToString();
        }

        /// <summary>
        /// 取得匯入工作狀態的本地化顯示文字。
        /// </summary>
        /// <param name="status">要顯示的匯入工作狀態。</param>
        /// <returns>對應狀態的顯示名稱。</returns>
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

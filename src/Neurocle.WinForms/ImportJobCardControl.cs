using System;
using System.ComponentModel;
using System.ComponentModel.Design;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Neurocle.Application.Projects;
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
            statusLabel.Text = StatusText(value.Status, value.Result);
            progressLabel.Text = value.Result == null
                ? value.Status == ImportJobStatus.Waiting ? Strings.ImportQueueWaiting : value.Progress == null ? string.Empty : string.Format(Strings.ImportQueueProgress, value.Progress.ProcessedCount, value.Progress.TotalCount)
                : value.Result.OutputPath != null ? Path.GetFileName(value.Result.OutputPath) : value.Result.WasCanceledDuringScan ? Strings.ImportScanCancelled : string.Format(Strings.ImportQueueResultSummary, value.Result.ImportedImageIds.Count, value.Result.FailedCount, value.Result.UnprocessedCount);
            bool progressUnknown = value.Status == ImportJobStatus.Running && (value.Progress == null || value.Progress.TotalCount <= 0);
            progressBar.Style = progressUnknown ? ProgressBarStyle.Marquee : ProgressBarStyle.Continuous;
            int progress = value.Status == ImportJobStatus.Completed
                ? 100
                : value.Status == ImportJobStatus.Waiting || value.Progress == null || value.Progress.TotalCount <= 0
                    ? 0
                    : Math.Max(0, Math.Min(100, (int)Math.Round(value.Progress.ProcessedCount * 100d / value.Progress.TotalCount)));
            if (!progressUnknown) progressBar.Value = progress;
            toolTip.SetToolTip(sourceLabel, SourceText(value.Request));
            detailsLabel.Text = value.ErrorMessage ?? FormatWarnings(value.Result);
            toolTip.SetToolTip(detailsLabel, detailsLabel.Text);
            detailsLabel.Visible = detailsVisible && detailsLabel.Text.Length > 0;
            copyDetailsButton.Visible = detailsLabel.Visible;
            UpdateDetailsLayout();
            detailsButton.Text = detailsVisible ? Strings.HideDetails : Strings.ShowDetails;
            cancelButton.Enabled = value.Status == ImportJobStatus.Waiting || (value.Status == ImportJobStatus.Running && !(value.Request is AnnotationJsonImportJobRequest));
            bool isImageImport = value.Request is ImageFilesImportJobRequest || value.Request is FolderImportJobRequest;
            bool canView = isImageImport
                && (value.Status == ImportJobStatus.Completed || value.Status == ImportJobStatus.Canceled)
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

        private void CopyDetailsButton_Click(object sender, EventArgs args)
        {
            if (detailsLabel.Text.Length > 0) Clipboard.SetText(detailsLabel.Text);
        }

        private void UpdateDetailsLayout()
        {
            int margin = ScaleLogical(8);
            int actionHeight = ScaleLogical(26);
            int textHeight = ScaleLogical(88);
            int collapsedHeight = ScaleLogical(128);
            int detailsTop = collapsedHeight + margin;
            Height = detailsLabel.Visible ? collapsedHeight + margin + actionHeight + margin + textHeight + margin : collapsedHeight;
            copyDetailsButton.Location = new System.Drawing.Point(Math.Max(margin, ClientSize.Width - copyDetailsButton.Width - margin), detailsTop);
            detailsLabel.Location = new System.Drawing.Point(margin, detailsTop + actionHeight + margin);
            detailsLabel.Size = new System.Drawing.Size(Math.Max(1, ClientSize.Width - margin * 2), textHeight);
            if (Parent != null) Parent.PerformLayout();
        }

        private int ScaleLogical(int value)
        {
            return ScaleLogicalForDpi(value, DeviceDpi);
        }

        internal static int ScaleLogicalForDpi(int value, int dpi)
        {
            return dpi <= 0 ? value : (int)Math.Round(value * (dpi / 96d));
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
        private static string StatusText(ImportJobStatus status, ImportJobResult result)
        {
            switch (status)
            {
                case ImportJobStatus.Waiting: return Strings.ImportQueueWaiting;
                case ImportJobStatus.Running: return Strings.ImportQueueRunning;
                case ImportJobStatus.Completed: return result != null && result.Warnings.Count > 0 ? Strings.ImportQueueCompletedWithWarnings : Strings.ImportQueueCompleted;
                case ImportJobStatus.Failed: return Strings.ImportQueueFailed;
                case ImportJobStatus.Canceled: return Strings.ImportQueueCanceled;
                default: return status.ToString();
            }
        }

        private static string FormatWarnings(ImportJobResult result)
        {
            if (result == null || result.Warnings.Count == 0) return string.Empty;
            return string.Join(Environment.NewLine, result.Warnings.Select(warning => string.Format(
                Strings.ImportJsonWarningItem,
                warning.FileName,
                warning.SourceLocation,
                WarningReasonText(warning.Reason))));
        }

        private static string WarningReasonText(AnnotationJsonImportWarningReason reason)
        {
            switch (reason)
            {
                case AnnotationJsonImportWarningReason.AmbiguousImageFileName: return Strings.ImportJsonWarningAmbiguousImageFileName;
                case AnnotationJsonImportWarningReason.ImageDimensionsMismatch: return Strings.ImportJsonWarningImageDimensionsMismatch;
                case AnnotationJsonImportWarningReason.UnknownClassReference: return Strings.ImportJsonWarningUnknownClassReference;
                case AnnotationJsonImportWarningReason.InvalidAnnotation: return Strings.ImportJsonWarningInvalidAnnotation;
                case AnnotationJsonImportWarningReason.InvalidDatasetAssignment: return Strings.ImportJsonWarningInvalidDatasetAssignment;
                default: return reason.ToString();
            }
        }
    }
}

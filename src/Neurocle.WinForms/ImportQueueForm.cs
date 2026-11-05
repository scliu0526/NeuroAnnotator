using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Neurocle.WinForms.Resources;

namespace Neurocle.WinForms
{
    internal sealed partial class ImportQueueForm : Form
    {
        private IReadOnlyList<ImportJobSnapshot> currentJobs = new ImportJobSnapshot[0];

        public ImportQueueForm()
        {
            InitializeComponent();
        }

        public event Action<Guid> CancelRequested;
        public event Action<Guid> ViewImportedImagesRequested;
        public event Action<string> OpenExportRequested;
        public event Action ClearCompletedRequested;

        /// <summary>
        /// 更新工作清單與動態工作卡片，並同步各工作操作狀態。
        /// </summary>
        /// <param name="jobs">依佇列順序顯示的工作快照清單；null 代表空清單。</param>
        public void UpdateJobs(IReadOnlyList<ImportJobSnapshot> jobs)
        {
            if (IsDisposed || Disposing) return;
            if (InvokeRequired)
            {
                try { BeginInvoke((Action<IReadOnlyList<ImportJobSnapshot>>)UpdateJobs, jobs); }
                catch (InvalidOperationException) { }
                return;
            }

            currentJobs = jobs ?? new ImportJobSnapshot[0];
            Guid? selected = jobsList.SelectedItems.Count == 0 ? (Guid?)null : (Guid)jobsList.SelectedItems[0].Tag;
            jobsList.BeginUpdate();
            jobsList.Items.Clear();
            foreach (ImportJobSnapshot job in currentJobs)
            {
                string source = SourceText(job.Request);
                string progress = job.Result == null
                    ? job.Progress == null ? string.Empty : string.Format(Strings.ImportQueueProgress, job.Progress.ProcessedCount, job.Progress.TotalCount)
                    : job.Result.WasCanceledDuringScan ? Strings.ImportScanCancelled : string.Format(Strings.ImportQueueResultSummary, job.Result.ImportedImageIds.Count, job.Result.FailedCount, job.Result.UnprocessedCount);
                var item = new ListViewItem(source) { Tag = job.Id };
                item.SubItems.Add(StatusText(job.Status));
                item.SubItems.Add(progress);
                jobsList.Items.Add(item);
                if (selected == job.Id) item.Selected = true;
            }
            jobsList.EndUpdate();
            cardsPanel.SuspendLayout();
            foreach (Control card in cardsPanel.Controls.Cast<Control>().ToArray()) card.Dispose();
            cardsPanel.Controls.Clear();
            foreach (ImportJobSnapshot job in currentJobs)
            {
                var card = new ImportJobCardControl { Width = Math.Max(420, cardsPanel.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 8) };
                card.UpdateJob(job);
                card.CancelRequested += id => { if (CancelRequested != null) CancelRequested(id); };
                card.ViewImportedImagesRequested += id => { if (ViewImportedImagesRequested != null) ViewImportedImagesRequested(id); };
                card.OpenExportRequested += path => { if (OpenExportRequested != null) OpenExportRequested(path); };
                cardsPanel.Controls.Add(card);
            }
            cardsPanel.ResumeLayout();
            UpdateCancelButton();
            clearCompletedButton.Enabled = currentJobs.Any(job => job.Status == ImportJobStatus.Completed || job.Status == ImportJobStatus.Failed || job.Status == ImportJobStatus.Canceled);
        }

        /// <summary>
        /// 更新指定工作的匯入圖片跳轉狀態。
        /// </summary>
        /// <param name="jobId">匯入工作的識別碼。</param>
        /// <param name="enabled">是否啟用控制項。</param>
        /// <param name="unavailableReason">功能停用時顯示的原因。</param>
        public void SetImportedImagesAvailability(Guid jobId, bool enabled, string unavailableReason)
        {
            ImportJobSnapshot job = currentJobs.FirstOrDefault(value => value.Id == jobId);
            if (job == null) return;
            foreach (ImportJobCardControl card in cardsPanel.Controls.OfType<ImportJobCardControl>())
            {
                if (card.JobId == jobId) card.SetViewImportedImagesAvailability(enabled, unavailableReason);
            }
        }

        /// <summary>
        /// 取得工作清單顯示的匯入來源文字。
        /// </summary>
        /// <param name="request">提供圖片檔、資料夾或 JSON 來源的工作要求。</param>
        /// <returns>適合顯示在清單中的來源摘要。</returns>
        private static string SourceText(ImportJobRequest request)
        {
            var imageRequest = request as ImageFilesImportJobRequest;
            if (imageRequest != null) return imageRequest.FilePaths.Count == 1 ? Path.GetFileName(imageRequest.FilePaths[0]) : string.Format(Strings.ImportQueueFileCount, imageRequest.FilePaths.Count);
            var folderRequest = request as FolderImportJobRequest;
            if (folderRequest != null) return folderRequest.DirectoryPath + (folderRequest.IncludeSubdirectories ? Strings.ImportQueueFolderRecursive : string.Empty);
            var jsonRequest = request as AnnotationJsonImportJobRequest;
            if (jsonRequest != null) return Path.GetFileName(jsonRequest.FilePath);
            var jsonExport = request as JsonExportJobRequest;
            if (jsonExport != null) return Path.GetFileName(jsonExport.OutputPath);
            return request.Kind.ToString();
        }

        /// <summary>
        /// 取得工作清單顯示的本地化狀態文字。
        /// </summary>
        /// <param name="status">要轉換為顯示文字的工作狀態。</param>
        /// <returns>對應工作狀態的本地化名稱。</returns>
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

        /// <summary>
        /// 依選取的工作重新計算取消按鈕是否可用。
        /// </summary>
        /// <param name="sender">選取項目已變更的工作清單。</param>
        /// <param name="args">選取項目變更事件資料。</param>
        private void JobsList_SelectedIndexChanged(object sender, EventArgs args)
        {
            UpdateCancelButton();
        }

        /// <summary>
        /// 依目前選取工作類型與狀態更新取消按鈕。
        /// </summary>
        private void UpdateCancelButton()
        {
            if (jobsList.SelectedItems.Count == 0)
            {
                cancelButton.Enabled = false;
                return;
            }
            Guid selectedId = (Guid)jobsList.SelectedItems[0].Tag;
            ImportJobSnapshot selected = currentJobs.FirstOrDefault(job => job.Id == selectedId);
            bool isJson = selected != null && selected.Request is AnnotationJsonImportJobRequest;
            cancelButton.Enabled = selected != null && (selected.Status == ImportJobStatus.Waiting || (!isJson && selected.Status == ImportJobStatus.Running));
        }

        /// <summary>
        /// 通知外層要求取消目前選取的工作。
        /// </summary>
        /// <param name="sender">引發事件的取消按鈕。</param>
        /// <param name="args">按鈕點擊事件資料。</param>
        private void CancelButton_Click(object sender, EventArgs args)
        {
            if (jobsList.SelectedItems.Count == 0) return;
            Action<Guid> handler = CancelRequested;
            if (handler != null) handler((Guid)jobsList.SelectedItems[0].Tag);
        }

        /// <summary>
        /// 通知外層清除已完成、失敗及取消的工作。
        /// </summary>
        /// <param name="sender">引發事件的清除按鈕。</param>
        /// <param name="args">按鈕點擊事件資料。</param>
        private void ClearCompletedButton_Click(object sender, EventArgs args)
        {
            Action handler = ClearCompletedRequested;
            if (handler != null) handler();
        }

        /// <summary>
        /// 將使用者關閉佇列視窗轉為隱藏，保留佇列供稍後重開。
        /// </summary>
        /// <param name="args">包含關閉原因及是否取消關閉的事件資料。</param>
        protected override void OnFormClosing(FormClosingEventArgs args)
        {
            if (args.CloseReason == CloseReason.UserClosing)
            {
                args.Cancel = true;
                Hide();
                return;
            }
            base.OnFormClosing(args);
        }
    }
}

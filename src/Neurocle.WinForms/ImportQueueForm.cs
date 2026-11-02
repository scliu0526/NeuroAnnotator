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
        public event Action ClearCompletedRequested;

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
                var card = new ImportJobCardControl { Width = Math.Max(380, cardsPanel.ClientSize.Width - 24) };
                card.UpdateJob(job);
                card.CancelRequested += id => { if (CancelRequested != null) CancelRequested(id); };
                cardsPanel.Controls.Add(card);
            }
            cardsPanel.ResumeLayout();
            UpdateCancelButton();
            clearCompletedButton.Enabled = currentJobs.Any(job => job.Status == ImportJobStatus.Completed || job.Status == ImportJobStatus.Failed || job.Status == ImportJobStatus.Canceled);
        }

        private static string SourceText(ImportJobRequest request)
        {
            var imageRequest = request as ImageFilesImportJobRequest;
            if (imageRequest != null) return imageRequest.FilePaths.Count == 1 ? Path.GetFileName(imageRequest.FilePaths[0]) : string.Format(Strings.ImportQueueFileCount, imageRequest.FilePaths.Count);
            var folderRequest = request as FolderImportJobRequest;
            if (folderRequest != null) return folderRequest.DirectoryPath + (folderRequest.IncludeSubdirectories ? Strings.ImportQueueFolderRecursive : string.Empty);
            var jsonRequest = request as AnnotationJsonImportJobRequest;
            if (jsonRequest != null) return Path.GetFileName(jsonRequest.FilePath);
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

        private void JobsList_SelectedIndexChanged(object sender, EventArgs args)
        {
            UpdateCancelButton();
        }

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

        private void CancelButton_Click(object sender, EventArgs args)
        {
            if (jobsList.SelectedItems.Count == 0) return;
            Action<Guid> handler = CancelRequested;
            if (handler != null) handler((Guid)jobsList.SelectedItems[0].Tag);
        }

        private void ClearCompletedButton_Click(object sender, EventArgs args)
        {
            Action handler = ClearCompletedRequested;
            if (handler != null) handler();
        }

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

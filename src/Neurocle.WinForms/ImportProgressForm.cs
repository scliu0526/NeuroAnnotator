using System;
using System.Threading;
using System.Windows.Forms;
using Neurocle.Application.Projects;
using Neurocle.WinForms.Resources;

namespace Neurocle.WinForms
{
    internal sealed partial class ImportProgressForm : Form
    {
        private int cancellationRequested;

        public ImportProgressForm()
        {
            InitializeComponent();
        }

        public bool IsCancellationRequested { get { return Interlocked.CompareExchange(ref cancellationRequested, 0, 0) != 0; } }

        public void Report(ImageImportProgress progress)
        {
            if (progress == null || IsDisposed || Disposing) return;
            if (InvokeRequired)
            {
                try { BeginInvoke((Action<ImageImportProgress>)Report, progress); }
                catch (InvalidOperationException) { }
                return;
            }
            progressBar.Maximum = Math.Max(progress.TotalCount, 1);
            progressBar.Value = Math.Min(progress.ProcessedCount, progressBar.Maximum);
            progressLabel.Text = string.Format(Strings.ImportProgressCounts, progress.ProcessedCount, progress.TotalCount, progress.SuccessfulCount, progress.FailedCount);
        }

        private void Cancel_Click(object sender, System.EventArgs args)
        {
            Interlocked.Exchange(ref cancellationRequested, 1);
            ((Button)sender).Enabled = false;
        }
    }
}

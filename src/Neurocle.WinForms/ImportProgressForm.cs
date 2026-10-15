using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using Neurocle.Application.Projects;
using Neurocle.WinForms.Resources;

namespace Neurocle.WinForms
{
    internal sealed class ImportProgressForm : Form
    {
        private int cancellationRequested;
        private readonly Label progressLabel;
        private readonly ProgressBar progressBar;

        public ImportProgressForm()
        {
            Text = Strings.ImportProgressTitle;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            ControlBox = false;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(360, 112);

            var label = new Label { AutoSize = true, Location = new Point(18, 18), Text = Strings.ImportInProgress };
            progressLabel = new Label { AutoSize = true, Location = new Point(18, 43), Text = string.Format(Strings.ImportProgressCounts, 0, 0, 0, 0) };
            progressBar = new ProgressBar { Location = new Point(18, 66), Size = new Size(240, 18), Minimum = 0, Maximum = 1 };
            var cancel = new Button { Location = new Point(267, 62), Size = new Size(75, 25), Text = Strings.Cancel };
            cancel.Click += (sender, args) =>
            {
                Interlocked.Exchange(ref cancellationRequested, 1);
                cancel.Enabled = false;
            };
            Controls.Add(label);
            Controls.Add(progressLabel);
            Controls.Add(progressBar);
            Controls.Add(cancel);
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
    }
}

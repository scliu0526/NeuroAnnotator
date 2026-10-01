using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using Neurocle.WinForms.Resources;

namespace Neurocle.WinForms
{
    internal sealed class ImportProgressForm : Form
    {
        private int cancellationRequested;

        public ImportProgressForm()
        {
            Text = Strings.ImportProgressTitle;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            ControlBox = false;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(280, 84);

            var label = new Label { AutoSize = true, Location = new Point(18, 18), Text = Strings.ImportInProgress };
            var cancel = new Button { Location = new Point(187, 45), Size = new Size(75, 25), Text = Strings.Cancel };
            cancel.Click += (sender, args) =>
            {
                Interlocked.Exchange(ref cancellationRequested, 1);
                cancel.Enabled = false;
            };
            Controls.Add(label);
            Controls.Add(cancel);
        }

        public bool IsCancellationRequested { get { return Interlocked.CompareExchange(ref cancellationRequested, 0, 0) != 0; } }
    }
}

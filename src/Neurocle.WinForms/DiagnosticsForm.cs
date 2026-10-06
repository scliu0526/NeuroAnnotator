using System;
using System.Windows.Forms;
using Neurocle.WinForms.Resources;

namespace Neurocle.WinForms
{
    internal sealed class DiagnosticsForm : Form
    {
        private readonly DiagnosticLog diagnosticLog;
        private readonly Label usage;

        public DiagnosticsForm(DiagnosticLog diagnosticLog)
        {
            this.diagnosticLog = diagnosticLog ?? throw new ArgumentNullException(nameof(diagnosticLog));
            Text = Strings.DiagnosticsTitle;
            Width = 380;
            Height = 150;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            usage = new Label { AutoSize = true, Dock = DockStyle.Top, Padding = new Padding(12) };
            var openFolder = new Button { AutoSize = true, Dock = DockStyle.Bottom, Text = Strings.OpenLogsFolder };
            openFolder.Click += (sender, args) => OpenFolder();
            var close = new Button { AutoSize = true, Dock = DockStyle.Bottom, DialogResult = DialogResult.OK, Text = Strings.Close };
            Controls.Add(usage);
            Controls.Add(openFolder);
            Controls.Add(close);
            Shown += (sender, args) => RefreshUsage();
        }

        private void RefreshUsage()
        {
            try { usage.Text = string.Format(Strings.DiagnosticLogUsage, diagnosticLog.GetUsageBytes()); }
            catch (DiagnosticLogException exception) { MessageBox.Show(this, DiagnosticLogMessages.GetFailureMessage(exception), Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private void OpenFolder()
        {
            try { diagnosticLog.OpenFolder(); RefreshUsage(); }
            catch (DiagnosticLogException exception) { MessageBox.Show(this, DiagnosticLogMessages.GetFailureMessage(exception), Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }
    }
}

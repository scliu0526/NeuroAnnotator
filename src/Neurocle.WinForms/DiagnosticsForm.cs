using System;
using System.Windows.Forms;
using Neurocle.WinForms.Resources;

namespace Neurocle.WinForms
{
    internal sealed partial class DiagnosticsForm : Form
    {
        private DiagnosticLog diagnosticLog;

        internal DiagnosticsForm()
        {
            InitializeComponent();
        }

        public DiagnosticsForm(DiagnosticLog diagnosticLog) : this()
        {
            this.diagnosticLog = diagnosticLog ?? throw new ArgumentNullException(nameof(diagnosticLog));
        }

        private void RefreshUsage()
        {
            if (diagnosticLog == null) return;
            try { usage.Text = string.Format(Strings.DiagnosticLogUsage, diagnosticLog.GetUsageBytes()); }
            catch (DiagnosticLogException exception) { MessageBox.Show(this, DiagnosticLogMessages.GetFailureMessage(exception), Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private void OpenFolder()
        {
            if (diagnosticLog == null) return;
            try { diagnosticLog.OpenFolder(); RefreshUsage(); }
            catch (DiagnosticLogException exception) { MessageBox.Show(this, DiagnosticLogMessages.GetFailureMessage(exception), Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private void DiagnosticsForm_Shown(object sender, System.EventArgs args)
        {
            RefreshUsage();
        }

        private void OpenFolder_Click(object sender, System.EventArgs args)
        {
            OpenFolder();
        }
    }
}

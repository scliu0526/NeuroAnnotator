using System;
using System.Windows.Forms;
using Neurocle.WinForms.Resources;

namespace Neurocle.WinForms
{
    // 顯示診斷記錄用量，並提供開啟記錄資料夾的操作。
    internal sealed partial class DiagnosticsForm : Form
    {
        // 提供記錄用量查詢及資料夾開啟功能。
        private DiagnosticLog diagnosticLog;

        /// <summary>初始化診斷資訊表單。</summary>
        internal DiagnosticsForm()
        {
            InitializeComponent();
        }

        /// <summary>建立並設定此表單使用的診斷記錄器。</summary>
        public DiagnosticsForm(DiagnosticLog diagnosticLog) : this()
        {
            this.diagnosticLog = diagnosticLog ?? throw new ArgumentNullException(nameof(diagnosticLog));
        }

        /// <summary>查詢記錄檔總用量並更新畫面文字。</summary>
        private void RefreshUsage()
        {
            if (diagnosticLog == null) return;
            try { usage.Text = string.Format(Strings.DiagnosticLogUsage, diagnosticLog.GetUsageBytes()); }
            catch (DiagnosticLogException exception) { MessageBox.Show(this, DiagnosticLogMessages.GetFailureMessage(exception), Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        /// <summary>開啟記錄檔目錄，並在成功後更新用量資訊。</summary>
        private void OpenFolder()
        {
            if (diagnosticLog == null) return;
            try { diagnosticLog.OpenFolder(); RefreshUsage(); }
            catch (DiagnosticLogException exception) { MessageBox.Show(this, DiagnosticLogMessages.GetFailureMessage(exception), Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        /// <summary>表單顯示時載入目前診斷記錄用量。</summary>
        private void DiagnosticsForm_Shown(object sender, System.EventArgs args)
        {
            RefreshUsage();
        }

        /// <summary>將開啟資料夾按鈕事件委派給記錄資料夾操作。</summary>
        private void OpenFolder_Click(object sender, System.EventArgs args)
        {
            OpenFolder();
        }
    }
}

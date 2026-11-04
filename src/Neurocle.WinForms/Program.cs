using System;
using System.Windows.Forms;
using Neurocle.Infrastructure.Persistence;

namespace Neurocle.WinForms
{
    // 設定 WinForms 執行環境並啟動主視窗。
    internal static class Program
    {
        /// <summary>
        /// 設定視覺樣式、建立診斷記錄器並執行主視窗訊息迴圈。
        /// </summary>
        [STAThread]
        private static void Main()
        {
            System.Windows.Forms.Application.EnableVisualStyles();
            System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
            var diagnosticLog = DiagnosticLog.CreateDefault();
            try { diagnosticLog.Write(DiagnosticEvent.ApplicationStarted); }
            catch (DiagnosticLogException exception)
            {
                MessageBox.Show(DiagnosticLogMessages.GetFailureMessage(exception), Resources.Strings.ApplicationTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            System.Windows.Forms.Application.Run(new MainForm(new ProjectLifecycle(), diagnosticLog));
        }
    }
}

using System;
using System.Threading;
using System.Windows.Forms;
using Neurocle.Application.Projects;
using Neurocle.WinForms.Resources;

namespace Neurocle.WinForms
{
    // 顯示圖片匯入進度，並以執行緒安全旗標傳遞取消要求。
    internal sealed partial class ImportProgressForm : Form
    {
        // 使用原子讀寫保存使用者是否已要求取消匯入。
        private int cancellationRequested;

        /// <summary>初始化匯入進度與取消控制項。</summary>
        public ImportProgressForm()
        {
            InitializeComponent();
        }

        // 以原子讀取取得目前的取消要求狀態。
        public bool IsCancellationRequested { get { return Interlocked.CompareExchange(ref cancellationRequested, 0, 0) != 0; } }

        /// <summary>更新匯入計數與進度列；跨執行緒呼叫時切回 UI 執行緒。</summary>
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

        /// <summary>記錄使用者取消要求並停用取消按鈕。</summary>
        private void Cancel_Click(object sender, System.EventArgs args)
        {
            Interlocked.Exchange(ref cancellationRequested, 1);
            ((Button)sender).Enabled = false;
        }
    }
}

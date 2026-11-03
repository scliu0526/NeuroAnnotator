using System;
using System.Linq;
using System.Windows.Forms;
using Neurocle.Application.Projects;
using Neurocle.WinForms.Resources;

namespace Neurocle.WinForms
{
    // 顯示專案備份並確認後執行還原。
    internal sealed partial class BackupManagementForm : Form
    {
        // 提供目前專案的備份清單與還原操作。
        private IProjectSession project;

        /// <summary>
        /// 初始化備份管理表單的設計器元件。
        /// </summary>
        internal BackupManagementForm()
        {
            InitializeComponent();
        }

        /// <summary>
        /// 載入指定專案的備份清單並初始化還原按鈕。
        /// </summary>
        public BackupManagementForm(IProjectSession project) : this()
        {
            this.project = project ?? throw new ArgumentNullException(nameof(project));
            backups.DataSource = project.GetBackups().Select(backup => new BackupItem(backup)).ToList();
            restore.Enabled = false;
        }

        // 取得還原後的工作階段，或還原失敗後重新開啟的工作階段。
        public IProjectSession RestoredProject { get; private set; }

        /// <summary>
        /// 依目前清單選取項目更新還原按鈕狀態。
        /// </summary>
        /// <param name="sender">觸發此事件的控制項或視窗。</param>
        /// <param name="args">包含此次事件詳細資料的事件資料。</param>
        private void Backups_SelectedIndexChanged(object sender, System.EventArgs args)
        {
            restore.Enabled = backups.SelectedItem != null;
        }

        /// <summary>
        /// 處理還原按鈕事件並執行選取備份的還原流程。
        /// </summary>
        /// <param name="sender">觸發此事件的控制項或視窗。</param>
        /// <param name="args">包含此次事件詳細資料的事件資料。</param>
        private void Restore_Click(object sender, System.EventArgs args)
        {
            RestoreSelectedBackup();
        }

        /// <summary>
        /// 確認使用者操作後還原選取備份並處理還原失敗狀態。
        /// </summary>
        private void RestoreSelectedBackup()
        {
            var item = backups.SelectedItem as BackupItem;
            if (item == null) return;
            if (MessageBox.Show(this, Strings.RestoreBackupPrompt, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            try
            {
                RestoredProject = project.RestoreBackup(item.Backup.Path);
                DialogResult = DialogResult.OK;
            }
            catch (ProjectRestoreException exception)
            {
                RestoredProject = exception.RecoveredSession;
                MessageBox.Show(this, Strings.ProjectOperationFailed, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                DialogResult = DialogResult.OK;
            }
            catch (Exception)
            {
                MessageBox.Show(this, Strings.ProjectOperationFailed, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // 將備份資料格式化為清單中顯示的時間與檔名。
        private sealed class BackupItem
        {
            /// <summary>
            /// 建立供清單繫結使用的備份項目。
            /// </summary>
            public BackupItem(ProjectBackup backup) { Backup = backup; }
            // 取得此清單項目代表的備份資料。
            public ProjectBackup Backup { get; }
            // 取得包含本機時間與檔名的顯示文字。
            public string Path { get { return Backup.CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") + " - " + System.IO.Path.GetFileName(Backup.Path); } }
        }
    }
}

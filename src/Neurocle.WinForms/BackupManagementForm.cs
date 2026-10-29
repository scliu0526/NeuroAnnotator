using System;
using System.Linq;
using System.Windows.Forms;
using Neurocle.Application.Projects;
using Neurocle.WinForms.Resources;

namespace Neurocle.WinForms
{
    internal sealed partial class BackupManagementForm : Form
    {
        private IProjectSession project;

        internal BackupManagementForm()
        {
            InitializeComponent();
        }

        public BackupManagementForm(IProjectSession project) : this()
        {
            this.project = project ?? throw new ArgumentNullException(nameof(project));
            backups.DataSource = project.GetBackups().Select(backup => new BackupItem(backup)).ToList();
            restore.Enabled = false;
        }

        public IProjectSession RestoredProject { get; private set; }

        private void Backups_SelectedIndexChanged(object sender, System.EventArgs args)
        {
            restore.Enabled = backups.SelectedItem != null;
        }

        private void Restore_Click(object sender, System.EventArgs args)
        {
            RestoreSelectedBackup();
        }

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

        private sealed class BackupItem
        {
            public BackupItem(ProjectBackup backup) { Backup = backup; }
            public ProjectBackup Backup { get; }
            public string Path { get { return Backup.CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") + " - " + System.IO.Path.GetFileName(Backup.Path); } }
        }
    }
}

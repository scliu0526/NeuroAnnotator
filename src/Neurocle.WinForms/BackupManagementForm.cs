using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Neurocle.Application.Projects;
using Neurocle.WinForms.Resources;

namespace Neurocle.WinForms
{
    internal sealed class BackupManagementForm : Form
    {
        private readonly IProjectSession project;
        private readonly ListBox backups;
        private readonly Button restore;

        public BackupManagementForm(IProjectSession project)
        {
            this.project = project ?? throw new ArgumentNullException(nameof(project));
            Text = Strings.BackupManagementTitle;
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(480, 320);
            Size = new Size(560, 420);
            backups = new ListBox { Dock = DockStyle.Fill, DisplayMember = nameof(ProjectBackup.Path) };
            backups.SelectedIndexChanged += (sender, args) => restore.Enabled = backups.SelectedItem != null;
            restore = new Button { AutoSize = true, Text = Strings.RestoreBackup };
            restore.Click += (sender, args) => RestoreSelectedBackup();
            var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
            buttons.Controls.Add(new Button { AutoSize = true, DialogResult = DialogResult.Cancel, Text = Strings.Close });
            buttons.Controls.Add(restore);
            Controls.Add(backups);
            Controls.Add(buttons);
            backups.DataSource = project.GetBackups().Select(backup => new BackupItem(backup)).ToList();
            restore.Enabled = false;
        }

        public IProjectSession RestoredProject { get; private set; }

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

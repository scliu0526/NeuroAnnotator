using Neurocle.WinForms.Resources;

namespace Neurocle.WinForms
{
    partial class BackupManagementForm
    {
        private System.ComponentModel.IContainer components = null;
        private System.Windows.Forms.ListBox backups;
        private System.Windows.Forms.Button restore;
        private System.Windows.Forms.FlowLayoutPanel buttonsPanel;
        private System.Windows.Forms.Button closeButton;

        protected override void Dispose(bool disposing)
        {
            if (disposing && components != null) components.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.backups = new System.Windows.Forms.ListBox();
            this.restore = new System.Windows.Forms.Button();
            this.buttonsPanel = new System.Windows.Forms.FlowLayoutPanel();
            this.closeButton = new System.Windows.Forms.Button();
            this.buttonsPanel.SuspendLayout();
            this.SuspendLayout();
            //
            // backups
            //
            this.backups.DisplayMember = "Path";
            this.backups.Dock = System.Windows.Forms.DockStyle.Fill;
            this.backups.Name = "backups";
            this.backups.TabIndex = 0;
            this.backups.SelectedIndexChanged += new System.EventHandler(this.Backups_SelectedIndexChanged);
            //
            // restore
            //
            this.restore.AutoSize = true;
            this.restore.Enabled = false;
            this.restore.Name = "restore";
            this.restore.TabIndex = 1;
            this.restore.Text = Strings.RestoreBackup;
            this.restore.UseVisualStyleBackColor = true;
            this.restore.Click += new System.EventHandler(this.Restore_Click);
            //
            // closeButton
            //
            this.closeButton.AutoSize = true;
            this.closeButton.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.closeButton.Name = "closeButton";
            this.closeButton.TabIndex = 0;
            this.closeButton.Text = Strings.Close;
            this.closeButton.UseVisualStyleBackColor = true;
            //
            // buttonsPanel
            //
            this.buttonsPanel.AutoSize = true;
            this.buttonsPanel.Controls.Add(this.closeButton);
            this.buttonsPanel.Controls.Add(this.restore);
            this.buttonsPanel.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.buttonsPanel.FlowDirection = System.Windows.Forms.FlowDirection.RightToLeft;
            this.buttonsPanel.Name = "buttonsPanel";
            this.buttonsPanel.Padding = new System.Windows.Forms.Padding(8);
            this.buttonsPanel.TabIndex = 1;
            this.buttonsPanel.WrapContents = false;
            //
            // BackupManagementForm
            //
            this.Controls.Add(this.backups);
            this.Controls.Add(this.buttonsPanel);
            this.MinimumSize = new System.Drawing.Size(480, 320);
            this.Name = "BackupManagementForm";
            this.Size = new System.Drawing.Size(560, 420);
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = Strings.BackupManagementTitle;
            this.buttonsPanel.ResumeLayout(false);
            this.buttonsPanel.PerformLayout();
            this.ResumeLayout(false);
        }
    }
}

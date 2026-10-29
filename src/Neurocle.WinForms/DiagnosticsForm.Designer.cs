using Neurocle.WinForms.Resources;

namespace Neurocle.WinForms
{
    partial class DiagnosticsForm
    {
        private System.ComponentModel.IContainer components = null;
        private System.Windows.Forms.Label usage;
        private System.Windows.Forms.Button openFolder;
        private System.Windows.Forms.Button closeButton;

        protected override void Dispose(bool disposing)
        {
            if (disposing && components != null) components.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.usage = new System.Windows.Forms.Label();
            this.openFolder = new System.Windows.Forms.Button();
            this.closeButton = new System.Windows.Forms.Button();
            this.SuspendLayout();
            //
            // usage
            //
            this.usage.AutoSize = true;
            this.usage.Dock = System.Windows.Forms.DockStyle.Top;
            this.usage.Name = "usage";
            this.usage.Padding = new System.Windows.Forms.Padding(12);
            this.usage.TabIndex = 0;
            //
            // openFolder
            //
            this.openFolder.AutoSize = true;
            this.openFolder.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.openFolder.Name = "openFolder";
            this.openFolder.TabIndex = 1;
            this.openFolder.Text = Strings.OpenLogsFolder;
            this.openFolder.UseVisualStyleBackColor = true;
            this.openFolder.Click += new System.EventHandler(this.OpenFolder_Click);
            //
            // closeButton
            //
            this.closeButton.AutoSize = true;
            this.closeButton.DialogResult = System.Windows.Forms.DialogResult.OK;
            this.closeButton.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.closeButton.Name = "closeButton";
            this.closeButton.TabIndex = 2;
            this.closeButton.Text = Strings.Close;
            this.closeButton.UseVisualStyleBackColor = true;
            //
            // DiagnosticsForm
            //
            this.Controls.Add(this.usage);
            this.Controls.Add(this.openFolder);
            this.Controls.Add(this.closeButton);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "DiagnosticsForm";
            this.Size = new System.Drawing.Size(380, 150);
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = Strings.DiagnosticsTitle;
            this.Shown += new System.EventHandler(this.DiagnosticsForm_Shown);
            this.ResumeLayout(false);
            this.PerformLayout();
        }
    }
}

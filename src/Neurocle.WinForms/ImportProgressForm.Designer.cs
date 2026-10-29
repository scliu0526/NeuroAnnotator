using Neurocle.WinForms.Resources;

namespace Neurocle.WinForms
{
    partial class ImportProgressForm
    {
        private System.ComponentModel.IContainer components = null;
        private System.Windows.Forms.Label label;
        private System.Windows.Forms.Label progressLabel;
        private System.Windows.Forms.ProgressBar progressBar;
        private System.Windows.Forms.Button cancelButton;

        protected override void Dispose(bool disposing)
        {
            if (disposing && components != null) components.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.label = new System.Windows.Forms.Label();
            this.progressLabel = new System.Windows.Forms.Label();
            this.progressBar = new System.Windows.Forms.ProgressBar();
            this.cancelButton = new System.Windows.Forms.Button();
            this.SuspendLayout();
            //
            // label
            //
            this.label.AutoSize = true;
            this.label.Location = new System.Drawing.Point(18, 18);
            this.label.Name = "label";
            this.label.TabIndex = 0;
            this.label.Text = Strings.ImportInProgress;
            //
            // progressLabel
            //
            this.progressLabel.AutoSize = true;
            this.progressLabel.Location = new System.Drawing.Point(18, 43);
            this.progressLabel.Name = "progressLabel";
            this.progressLabel.TabIndex = 1;
            this.progressLabel.Text = string.Format(Strings.ImportProgressCounts, 0, 0, 0, 0);
            //
            // progressBar
            //
            this.progressBar.Location = new System.Drawing.Point(18, 66);
            this.progressBar.Maximum = 1;
            this.progressBar.Minimum = 0;
            this.progressBar.Name = "progressBar";
            this.progressBar.Size = new System.Drawing.Size(240, 18);
            this.progressBar.TabIndex = 2;
            //
            // cancelButton
            //
            this.cancelButton.Location = new System.Drawing.Point(267, 62);
            this.cancelButton.Name = "cancelButton";
            this.cancelButton.Size = new System.Drawing.Size(75, 25);
            this.cancelButton.TabIndex = 3;
            this.cancelButton.Text = Strings.Cancel;
            this.cancelButton.UseVisualStyleBackColor = true;
            this.cancelButton.Click += new System.EventHandler(this.Cancel_Click);
            //
            // ImportProgressForm
            //
            this.ClientSize = new System.Drawing.Size(360, 112);
            this.Controls.Add(this.label);
            this.Controls.Add(this.progressLabel);
            this.Controls.Add(this.progressBar);
            this.Controls.Add(this.cancelButton);
            this.ControlBox = false;
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "ImportProgressForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = Strings.ImportProgressTitle;
            this.ResumeLayout(false);
            this.PerformLayout();
        }
    }
}

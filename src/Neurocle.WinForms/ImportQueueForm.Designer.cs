using Neurocle.WinForms.Resources;

namespace Neurocle.WinForms
{
    partial class ImportQueueForm
    {
        private System.ComponentModel.IContainer components = null;
        private System.Windows.Forms.ListView jobsList;
        private System.Windows.Forms.ColumnHeader sourceColumn;
        private System.Windows.Forms.ColumnHeader statusColumn;
        private System.Windows.Forms.ColumnHeader progressColumn;
        private System.Windows.Forms.Button cancelButton;

        protected override void Dispose(bool disposing)
        {
            if (disposing && components != null) components.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.jobsList = new System.Windows.Forms.ListView();
            this.sourceColumn = new System.Windows.Forms.ColumnHeader();
            this.statusColumn = new System.Windows.Forms.ColumnHeader();
            this.progressColumn = new System.Windows.Forms.ColumnHeader();
            this.cancelButton = new System.Windows.Forms.Button();
            this.SuspendLayout();
            this.jobsList.Columns.AddRange(new[] { this.sourceColumn, this.statusColumn, this.progressColumn });
            this.jobsList.Dock = System.Windows.Forms.DockStyle.Fill;
            this.jobsList.FullRowSelect = true;
            this.jobsList.HideSelection = false;
            this.jobsList.MultiSelect = false;
            this.jobsList.Name = "jobsList";
            this.jobsList.View = System.Windows.Forms.View.Details;
            this.jobsList.SelectedIndexChanged += new System.EventHandler(this.JobsList_SelectedIndexChanged);
            this.sourceColumn.Text = Strings.ImportQueueSource;
            this.sourceColumn.Width = 240;
            this.statusColumn.Text = Strings.ImportQueueStatus;
            this.statusColumn.Width = 90;
            this.progressColumn.Text = Strings.ImportQueueProgressColumn;
            this.progressColumn.Width = 90;
            this.cancelButton.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.cancelButton.Enabled = false;
            this.cancelButton.Height = 32;
            this.cancelButton.Name = "cancelButton";
            this.cancelButton.Text = Strings.Cancel;
            this.cancelButton.UseVisualStyleBackColor = true;
            this.cancelButton.Click += new System.EventHandler(this.CancelButton_Click);
            this.ClientSize = new System.Drawing.Size(440, 260);
            this.Controls.Add(this.jobsList);
            this.Controls.Add(this.cancelButton);
            this.MinimumSize = new System.Drawing.Size(360, 220);
            this.Name = "ImportQueueForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = Strings.ImportQueueTitle;
            this.ResumeLayout(false);
        }
    }
}

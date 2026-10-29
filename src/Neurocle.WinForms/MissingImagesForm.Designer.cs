using Neurocle.WinForms.Resources;

namespace Neurocle.WinForms
{
    partial class MissingImagesForm
    {
        private System.ComponentModel.IContainer components = null;
        private System.Windows.Forms.ListBox images;
        private System.Windows.Forms.Button relink;
        private System.Windows.Forms.Button delete;
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
            this.images = new System.Windows.Forms.ListBox();
            this.relink = new System.Windows.Forms.Button();
            this.delete = new System.Windows.Forms.Button();
            this.buttonsPanel = new System.Windows.Forms.FlowLayoutPanel();
            this.closeButton = new System.Windows.Forms.Button();
            this.buttonsPanel.SuspendLayout();
            this.SuspendLayout();
            //
            // images
            //
            this.images.DisplayMember = "OriginalFileName";
            this.images.Dock = System.Windows.Forms.DockStyle.Fill;
            this.images.Name = "images";
            this.images.TabIndex = 0;
            this.images.SelectedIndexChanged += new System.EventHandler(this.Images_SelectedIndexChanged);
            //
            // relink
            //
            this.relink.AutoSize = true;
            this.relink.Name = "relink";
            this.relink.TabIndex = 2;
            this.relink.Text = Strings.RelinkImage;
            this.relink.UseVisualStyleBackColor = true;
            this.relink.Click += new System.EventHandler(this.Relink_Click);
            //
            // delete
            //
            this.delete.AutoSize = true;
            this.delete.Name = "delete";
            this.delete.TabIndex = 1;
            this.delete.Text = Strings.DeleteMissingImage;
            this.delete.UseVisualStyleBackColor = true;
            this.delete.Click += new System.EventHandler(this.Delete_Click);
            //
            // closeButton
            //
            this.closeButton.AutoSize = true;
            this.closeButton.DialogResult = System.Windows.Forms.DialogResult.OK;
            this.closeButton.Name = "closeButton";
            this.closeButton.TabIndex = 0;
            this.closeButton.Text = Strings.Close;
            this.closeButton.UseVisualStyleBackColor = true;
            //
            // buttonsPanel
            //
            this.buttonsPanel.AutoSize = true;
            this.buttonsPanel.Controls.Add(this.closeButton);
            this.buttonsPanel.Controls.Add(this.delete);
            this.buttonsPanel.Controls.Add(this.relink);
            this.buttonsPanel.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.buttonsPanel.FlowDirection = System.Windows.Forms.FlowDirection.RightToLeft;
            this.buttonsPanel.Name = "buttonsPanel";
            this.buttonsPanel.Padding = new System.Windows.Forms.Padding(8);
            this.buttonsPanel.TabIndex = 1;
            this.buttonsPanel.WrapContents = false;
            //
            // MissingImagesForm
            //
            this.Controls.Add(this.images);
            this.Controls.Add(this.buttonsPanel);
            this.MinimumSize = new System.Drawing.Size(480, 320);
            this.Name = "MissingImagesForm";
            this.Size = new System.Drawing.Size(560, 420);
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = Strings.MissingImagesTitle;
            this.buttonsPanel.ResumeLayout(false);
            this.buttonsPanel.PerformLayout();
            this.ResumeLayout(false);
        }
    }
}

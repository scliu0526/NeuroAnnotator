using System.Drawing;
using System.ComponentModel;
using System.Windows.Forms;
using Neurocle.WinForms.Resources;

namespace Neurocle.WinForms
{
    partial class ImportJobCardControl
    {
        private IContainer components;
        private Label sourceLabel;
        private Label statusLabel;
        private Label progressLabel;
        private Label detailsLabel;
        private Button detailsButton;
        private Button cancelButton;
        private Button viewImportedImagesButton;
        private ToolTip toolTip;

        private void InitializeComponent()
        {
            components = new Container();
            sourceLabel = new Label();
            statusLabel = new Label();
            progressLabel = new Label();
            detailsLabel = new Label();
            detailsButton = new Button();
            cancelButton = new Button();
            viewImportedImagesButton = new Button();
            toolTip = new ToolTip(components);
            SuspendLayout();
            sourceLabel.AutoEllipsis = true;
            sourceLabel.Dock = DockStyle.None;
            sourceLabel.Location = new Point(8, 8);
            sourceLabel.Size = new Size(230, 24);
            sourceLabel.Name = "sourceLabel";
            sourceLabel.Text = Strings.ImportImages + "：sample.png";
            statusLabel.AutoSize = true;
            statusLabel.Name = "statusLabel";
            statusLabel.Location = new Point(8, 36);
            statusLabel.Text = Strings.ImportQueueWaiting;
            progressLabel.AutoSize = true;
            progressLabel.Name = "progressLabel";
            progressLabel.Location = new Point(120, 36);
            progressLabel.Text = "0 / 1";
            detailsLabel.AutoEllipsis = true;
            detailsLabel.Dock = DockStyle.Top;
            detailsLabel.Height = 36;
            detailsLabel.Name = "detailsLabel";
            detailsLabel.Visible = false;
            detailsButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            detailsButton.AutoSize = true;
            detailsButton.Location = new Point(250, 28);
            detailsButton.Name = "detailsButton";
            detailsButton.Text = Strings.ShowDetails;
            detailsButton.Visible = true;
            detailsButton.Click += DetailsButton_Click;
            cancelButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            cancelButton.AutoSize = true;
            cancelButton.Location = new Point(325, 28);
            cancelButton.Name = "cancelButton";
            cancelButton.Text = Strings.Cancel;
            cancelButton.Visible = true;
            cancelButton.Click += CancelButton_Click;
            viewImportedImagesButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            viewImportedImagesButton.AutoSize = true;
            viewImportedImagesButton.Location = new Point(8, 60);
            viewImportedImagesButton.Name = "viewImportedImagesButton";
            viewImportedImagesButton.Text = Strings.ViewImportedImages;
            viewImportedImagesButton.Visible = true;
            viewImportedImagesButton.Click += ViewImportedImagesButton_Click;
            AutoScaleMode = AutoScaleMode.Font;
            BorderStyle = BorderStyle.FixedSingle;
            Controls.Add(detailsLabel);
            Controls.Add(cancelButton);
            Controls.Add(viewImportedImagesButton);
            Controls.Add(detailsButton);
            Controls.Add(progressLabel);
            Controls.Add(statusLabel);
            Controls.Add(sourceLabel);
            Margin = new Padding(4);
            MinimumSize = new Size(420, 88);
            Name = "ImportJobCardControl";
            Size = new Size(420, 96);
            ResumeLayout(false);
            PerformLayout();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && components != null) components.Dispose();
            base.Dispose(disposing);
        }
    }
}

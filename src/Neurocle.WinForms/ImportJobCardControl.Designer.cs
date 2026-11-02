using System.Drawing;
using System.Windows.Forms;
using Neurocle.WinForms.Resources;

namespace Neurocle.WinForms
{
    partial class ImportJobCardControl
    {
        private Label sourceLabel;
        private Label statusLabel;
        private Label progressLabel;
        private Label detailsLabel;
        private Button detailsButton;
        private Button cancelButton;

        private void InitializeComponent()
        {
            sourceLabel = new Label();
            statusLabel = new Label();
            progressLabel = new Label();
            detailsLabel = new Label();
            detailsButton = new Button();
            cancelButton = new Button();
            SuspendLayout();
            sourceLabel.AutoEllipsis = true;
            sourceLabel.Dock = DockStyle.Top;
            sourceLabel.Height = 28;
            sourceLabel.Name = "sourceLabel";
            statusLabel.AutoSize = true;
            statusLabel.Name = "statusLabel";
            statusLabel.Location = new Point(8, 34);
            progressLabel.AutoSize = true;
            progressLabel.Name = "progressLabel";
            progressLabel.Location = new Point(120, 34);
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
            detailsButton.Click += DetailsButton_Click;
            cancelButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            cancelButton.AutoSize = true;
            cancelButton.Location = new Point(325, 28);
            cancelButton.Name = "cancelButton";
            cancelButton.Text = Strings.Cancel;
            cancelButton.Click += CancelButton_Click;
            AutoScaleMode = AutoScaleMode.Font;
            BorderStyle = BorderStyle.FixedSingle;
            Controls.Add(detailsLabel);
            Controls.Add(cancelButton);
            Controls.Add(detailsButton);
            Controls.Add(progressLabel);
            Controls.Add(statusLabel);
            Controls.Add(sourceLabel);
            Margin = new Padding(4);
            MinimumSize = new Size(420, 64);
            Name = "ImportJobCardControl";
            Size = new Size(420, 72);
            ResumeLayout(false);
            PerformLayout();
        }
    }
}

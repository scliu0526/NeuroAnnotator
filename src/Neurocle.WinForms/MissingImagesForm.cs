using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Neurocle.Application.Projects;
using Neurocle.WinForms.Resources;

namespace Neurocle.WinForms
{
    internal sealed class MissingImagesForm : Form
    {
        private readonly IProjectSession project;
        private readonly ListBox images;
        private readonly Button relink;
        private readonly Button delete;

        public MissingImagesForm(IProjectSession project)
        {
            this.project = project ?? throw new ArgumentNullException(nameof(project));
            Text = Strings.MissingImagesTitle;
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(480, 320);
            Size = new Size(560, 420);

            images = new ListBox { Dock = DockStyle.Fill, DisplayMember = nameof(ProjectImage.OriginalFileName) };
            images.SelectedIndexChanged += (sender, args) => UpdateButtons();
            relink = new Button { AutoSize = true, Text = Strings.RelinkImage };
            relink.Click += (sender, args) => RelinkSelectedImage();
            delete = new Button { AutoSize = true, Text = Strings.DeleteMissingImage };
            delete.Click += (sender, args) => DeleteSelectedImage();
            var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
            buttons.Controls.Add(new Button { AutoSize = true, DialogResult = DialogResult.OK, Text = Strings.Close });
            buttons.Controls.Add(delete);
            buttons.Controls.Add(relink);
            Controls.Add(images);
            Controls.Add(buttons);
            RefreshImages();
        }

        private void RefreshImages()
        {
            images.DataSource = project.GetMissingImages().ToList();
            UpdateButtons();
        }

        private void UpdateButtons()
        {
            bool hasSelection = images.SelectedItem != null;
            relink.Enabled = hasSelection;
            delete.Enabled = hasSelection;
        }

        private void RelinkSelectedImage()
        {
            ProjectImage image = images.SelectedItem as ProjectImage;
            if (image == null) return;
            using (var dialog = new OpenFileDialog { Filter = Strings.ImageFileFilter, Multiselect = false })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    project.RelinkImage(image.Id, dialog.FileName);
                    RefreshImages();
                }
                catch (ImageRelinkException exception)
                {
                    MessageBox.Show(this, GetErrorMessage(exception), Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                catch (Exception)
                {
                    MessageBox.Show(this, Strings.ProjectOperationFailed, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void DeleteSelectedImage()
        {
            ProjectImage image = images.SelectedItem as ProjectImage;
            if (image == null) return;
            string message = string.Format(Strings.DeleteMissingImagePrompt, image.OriginalFileName);
            if (MessageBox.Show(this, message, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            try
            {
                project.DeleteImages(new[] { image.Id });
                RefreshImages();
            }
            catch (Exception)
            {
                MessageBox.Show(this, Strings.ProjectOperationFailed, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static string GetErrorMessage(ImageRelinkException exception)
        {
            switch (exception.Failure)
            {
                case ImageRelinkFailure.SourceNotFound: return Strings.RelinkSourceNotFound;
                case ImageRelinkFailure.ContentMismatch: return Strings.RelinkContentMismatch;
                default: return Strings.ProjectOperationFailed;
            }
        }
    }
}

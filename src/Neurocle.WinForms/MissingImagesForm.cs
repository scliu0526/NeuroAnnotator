using System;
using System.Linq;
using System.Windows.Forms;
using Neurocle.Application.Projects;
using Neurocle.WinForms.Resources;

namespace Neurocle.WinForms
{
    internal sealed partial class MissingImagesForm : Form
    {
        private IProjectSession project;

        internal MissingImagesForm()
        {
            InitializeComponent();
        }

        public MissingImagesForm(IProjectSession project) : this()
        {
            this.project = project ?? throw new ArgumentNullException(nameof(project));
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

        private void Images_SelectedIndexChanged(object sender, EventArgs args)
        {
            UpdateButtons();
        }

        private void Relink_Click(object sender, EventArgs args)
        {
            RelinkSelectedImage();
        }

        private void Delete_Click(object sender, EventArgs args)
        {
            DeleteSelectedImage();
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

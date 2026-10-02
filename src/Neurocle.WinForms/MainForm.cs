using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Neurocle.Application.Projects;
using Neurocle.WinForms.Resources;

namespace Neurocle.WinForms
{
    public sealed class MainForm : Form
    {
        private readonly IProjectLifecycle projectLifecycle;
        private IProjectSession project;
        private readonly Label status;
        private readonly ToolStripMenuItem projectMenu;
        private readonly ImageBrowserControl imageBrowser;
        private readonly ImageCanvasControl canvas;
        private string selectedImagePath;
        private bool importInProgress;

        public MainForm(IProjectLifecycle projectLifecycle)
        {
            this.projectLifecycle = projectLifecycle ?? throw new ArgumentNullException(nameof(projectLifecycle));
            AutoScaleMode = AutoScaleMode.Dpi;
            MinimumSize = new Size(640, 360);
            StartPosition = FormStartPosition.CenterScreen;
            Text = Strings.ApplicationTitle;

            var menu = new MenuStrip();
            projectMenu = new ToolStripMenuItem(Strings.ProjectMenu);
            projectMenu.DropDownItems.Add(Strings.NewProject, null, (sender, args) => RunProjectCommand(CreateProject));
            projectMenu.DropDownItems.Add(Strings.OpenProject, null, (sender, args) => RunProjectCommand(OpenProject));
            projectMenu.DropDownItems.Add(Strings.SaveProject, null, (sender, args) => RunProjectCommand(SaveProject));
            projectMenu.DropDownItems.Add(Strings.SaveProjectAs, null, (sender, args) => RunProjectCommand(SaveProjectAs));
            projectMenu.DropDownItems.Add(Strings.ImportImages, null, async (sender, args) => await ImportFilesAsync());
            projectMenu.DropDownItems.Add(Strings.ImportFolder, null, async (sender, args) => await ImportFolderAsync());
            projectMenu.DropDownItems.Add(Strings.ManageMissingImages, null, (sender, args) => ManageMissingImages());
            projectMenu.DropDownItems.Add(Strings.ManageBackups, null, (sender, args) => ManageBackups());
            menu.Items.Add(projectMenu);

            status = new Label
            {
                AutoSize = true,
                Location = new Point(24, 56),
                Text = Strings.OfflineStatus
            };
            imageBrowser = new ImageBrowserControl { Dock = DockStyle.Bottom, Height = 272 };
            canvas = new ImageCanvasControl { Dock = DockStyle.Fill };
            imageBrowser.ImageSelected += (sender, args) =>
            {
                selectedImagePath = args.Image.IsMissing ? null : args.Path;
                if (selectedImagePath == null) canvas.LoadImage(null);
            };
            var pages = new TabControl { Dock = DockStyle.Fill };
            var imagesPage = new TabPage(Strings.ImagesPage);
            imagesPage.Controls.Add(imageBrowser);
            var annotationPage = new TabPage(Strings.AnnotationPage);
            var fit = new Button { Dock = DockStyle.Top, Text = Strings.FitToWindow };
            fit.Click += (sender, args) => canvas.FitToWindow();
            var zoom = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList };
            zoom.Items.AddRange(new object[] { Strings.FitToWindow, "10%", "50%", "75%", "100%", "125%", "150%", "200%" });
            zoom.SelectedIndex = 0;
            zoom.SelectedIndexChanged += (sender, args) =>
            {
                string selected = zoom.SelectedItem as string;
                if (selected == Strings.FitToWindow) canvas.FitToWindow();
                else canvas.SetZoomPercent(double.Parse(selected.TrimEnd('%')));
            };
            annotationPage.Controls.Add(canvas);
            annotationPage.Controls.Add(zoom);
            annotationPage.Controls.Add(fit);
            pages.TabPages.Add(imagesPage);
            pages.TabPages.Add(annotationPage);
            pages.TabPages.Add(new TabPage(Strings.DatasetPage));
            pages.TabPages.Add(new TabPage(Strings.ExportPage));
            pages.SelectedIndexChanged += (sender, args) =>
            {
                canvas.DiscardUnfinishedShape();
                if (pages.SelectedTab == annotationPage && selectedImagePath != null)
                {
                    try { canvas.LoadImage(selectedImagePath); }
                    catch { }
                }
            };

            MainMenuStrip = menu;
            Controls.Add(menu);
            Controls.Add(status);
            Controls.Add(pages);
            FormClosing += (sender, args) =>
            {
                if (importInProgress) args.Cancel = true;
            };
            FormClosed += (sender, args) => project?.Dispose();
        }

        private void CreateProject()
        {
            using (var dialog = CreateProjectDialog())
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                ReplaceProject(projectLifecycle.Create(dialog.FileName));
            }
        }

        private void OpenProject()
        {
            using (var dialog = new OpenFileDialog { Filter = Strings.ProjectFileFilter })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    ReplaceProject(projectLifecycle.Open(dialog.FileName));
                }
                catch (ProjectOpenException exception)
                {
                    RestoreCorruptProject(exception);
                }
            }
        }

        private void SaveProject()
        {
            project?.Save();
        }

        private void SaveProjectAs()
        {
            if (project == null) return;
            using (var dialog = CreateProjectDialog())
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                IProjectSession copy = project.SaveAs(dialog.FileName);
                ReplaceProject(copy);
            }
        }

        private void ManageMissingImages()
        {
            if (project == null) return;
            using (var dialog = new MissingImagesForm(project)) dialog.ShowDialog(this);
            UpdateProjectStatus();
            imageBrowser.LoadImages(project.GetImages(), GetImagesDirectory(project.ProjectPath));
        }

        private void ManageBackups()
        {
            if (project == null) return;
            using (var dialog = new BackupManagementForm(project))
            {
                if (dialog.ShowDialog(this) == DialogResult.OK && dialog.RestoredProject != null) ReplaceProject(dialog.RestoredProject);
            }
        }

        private void RestoreCorruptProject(ProjectOpenException exception)
        {
            if (exception.Backups.Count == 0)
            {
                MessageBox.Show(this, Strings.CorruptProjectNoBackup, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            if (MessageBox.Show(this, Strings.CorruptProjectRestorePrompt, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Error) != DialogResult.Yes) return;
            try
            {
                ReplaceProject(projectLifecycle.Restore(exception.ProjectPath, exception.Backups[0].Path));
            }
            catch (Exception)
            {
                MessageBox.Show(this, Strings.ProjectOperationFailed, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async Task ImportFilesAsync()
        {
            if (project == null) return;
            using (var dialog = new OpenFileDialog { Filter = Strings.ImageFileFilter, Multiselect = true })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                IProjectSession session = project;
                string[] selectedFiles = dialog.FileNames.ToArray();
                await ImportAsync(progress => session.ImportFilesWithReport(selectedFiles, () => progress.IsCancellationRequested));
            }
        }

        private async Task ImportFolderAsync()
        {
            if (project == null) return;
            using (var dialog = new FolderBrowserDialog())
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                DialogResult choice = MessageBox.Show(this, Strings.IncludeSubdirectoriesPrompt, Text, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (choice == DialogResult.Cancel) return;
                IProjectSession session = project;
                string selectedPath = dialog.SelectedPath;
                await ImportAsync(progress => session.ImportDirectory(selectedPath, choice == DialogResult.Yes, () => progress.IsCancellationRequested));
            }
        }

        private async Task ImportAsync(Func<ImportProgressForm, ImageImportReport> import)
        {
            using (var progress = new ImportProgressForm())
            {
                importInProgress = true;
                projectMenu.Enabled = false;
                progress.Show(this);
                try
                {
                    ImageImportReport report = await Task.Run(() => import(progress));
                    MessageBox.Show(this, FormatImportReport(report), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception exception)
                {
                    MessageBox.Show(this, GetErrorMessage(exception), Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally
                {
                    progress.Close();
                    projectMenu.Enabled = true;
                    importInProgress = false;
                    if (project != null) imageBrowser.LoadImages(project.GetImages(), GetImagesDirectory(project.ProjectPath));
                }
            }
        }

        private static SaveFileDialog CreateProjectDialog()
        {
            return new SaveFileDialog { DefaultExt = "neurocle", AddExtension = true, Filter = Strings.ProjectFileFilter };
        }

        private void ReplaceProject(IProjectSession replacement)
        {
            IProjectSession previous = project;
            project = replacement;
            previous?.Dispose();
            UpdateProjectStatus();
            imageBrowser.LoadImages(project.GetImages(), GetImagesDirectory(project.ProjectPath));
        }

        private void UpdateProjectStatus()
        {
            string projectName = Path.GetFileName(project.ProjectPath);
            int missingCount = project.GetMissingImages().Count;
            status.Text = missingCount == 0 ? projectName : projectName + " - " + string.Format(Strings.MissingImagesStatus, missingCount);
        }

        private static string GetImagesDirectory(string projectPath)
        {
            return Path.Combine(Path.GetDirectoryName(projectPath), Path.GetFileNameWithoutExtension(projectPath) + ".images");
        }

        private void RunProjectCommand(Action command)
        {
            try
            {
                command();
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, GetErrorMessage(exception), Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static string GetErrorMessage(Exception exception)
        {
            var imageImportException = exception as ImageImportException;
            if (imageImportException == null) return Strings.ProjectOperationFailed;
            switch (imageImportException.Failure)
            {
                case ImageImportFailure.UnsupportedFormat: return Strings.ImageImportUnsupportedFormat;
                case ImageImportFailure.FileSizeLimit: return Strings.ImageImportFileSizeLimit;
                case ImageImportFailure.DimensionLimit: return Strings.ImageImportDimensionLimit;
                case ImageImportFailure.ProjectImageLimit: return Strings.ImageImportProjectLimit;
                case ImageImportFailure.FileNotFound: return Strings.ImageImportFileNotFound;
                case ImageImportFailure.InvalidImage: return Strings.ImageImportInvalidImage;
                case ImageImportFailure.DirectoryNotFound: return Strings.ImageImportDirectoryNotFound;
                case ImageImportFailure.DirectoryAccess: return Strings.ImageImportDirectoryAccess;
                default: return Strings.ProjectOperationFailed;
            }
        }

        private static string FormatImportReport(ImageImportReport report)
        {
            string unprocessedCount = report.WasCancelledDuringScan ? Strings.ImportReportUnknownCount : report.UnprocessedCount.ToString();
            string summary = string.Format(Strings.ImportReportSummary, report.Imported.Count, report.Failures.Count, unprocessedCount);
            if (report.WasCancelledDuringScan) summary += Environment.NewLine + Strings.ImportScanCancelled;
            if (report.Failures.Count == 0) return summary;
            string failures = string.Join(Environment.NewLine, report.Failures.Select(failure => Path.GetFileName(failure.FilePath) + ": " + GetImportFailureMessage(failure.Failure)));
            return summary + Environment.NewLine + failures;
        }

        private static string GetImportFailureMessage(ImageImportFailure failure)
        {
            switch (failure)
            {
                case ImageImportFailure.UnsupportedFormat: return Strings.ImageImportUnsupportedFormat;
                case ImageImportFailure.FileSizeLimit: return Strings.ImageImportFileSizeLimit;
                case ImageImportFailure.DimensionLimit: return Strings.ImageImportDimensionLimit;
                case ImageImportFailure.ProjectImageLimit: return Strings.ImageImportProjectLimit;
                case ImageImportFailure.FileNotFound: return Strings.ImageImportFileNotFound;
                case ImageImportFailure.InvalidImage: return Strings.ImageImportInvalidImage;
                case ImageImportFailure.DirectoryNotFound: return Strings.ImageImportDirectoryNotFound;
                case ImageImportFailure.DirectoryAccess: return Strings.ImageImportDirectoryAccess;
                default: return Strings.ImageImportUnknownFailure;
            }
        }
    }
}

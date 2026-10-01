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
            menu.Items.Add(projectMenu);

            status = new Label
            {
                AutoSize = true,
                Location = new Point(24, 56),
                Text = Strings.OfflineStatus
            };

            MainMenuStrip = menu;
            Controls.Add(menu);
            Controls.Add(status);
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
                ReplaceProject(projectLifecycle.Open(dialog.FileName));
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
            status.Text = Path.GetFileName(project.ProjectPath);
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

using System;
using System.Drawing;
using System.IO;
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

        public MainForm(IProjectLifecycle projectLifecycle)
        {
            this.projectLifecycle = projectLifecycle ?? throw new ArgumentNullException(nameof(projectLifecycle));
            AutoScaleMode = AutoScaleMode.Dpi;
            MinimumSize = new Size(640, 360);
            StartPosition = FormStartPosition.CenterScreen;
            Text = Strings.ApplicationTitle;

            var menu = new MenuStrip();
            var projectMenu = new ToolStripMenuItem(Strings.ProjectMenu);
            projectMenu.DropDownItems.Add(Strings.NewProject, null, (sender, args) => RunProjectCommand(CreateProject));
            projectMenu.DropDownItems.Add(Strings.OpenProject, null, (sender, args) => RunProjectCommand(OpenProject));
            projectMenu.DropDownItems.Add(Strings.SaveProject, null, (sender, args) => RunProjectCommand(SaveProject));
            projectMenu.DropDownItems.Add(Strings.SaveProjectAs, null, (sender, args) => RunProjectCommand(SaveProjectAs));
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
                MessageBox.Show(this, exception.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}

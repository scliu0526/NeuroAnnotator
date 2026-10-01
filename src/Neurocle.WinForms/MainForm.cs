using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Neurocle.Infrastructure.Persistence;
using Neurocle.WinForms.Resources;

namespace Neurocle.WinForms
{
    public sealed class MainForm : Form
    {
        private ProjectStore project;
        private readonly Label status;

        public MainForm()
        {
            AutoScaleMode = AutoScaleMode.Dpi;
            MinimumSize = new Size(640, 360);
            StartPosition = FormStartPosition.CenterScreen;
            Text = Strings.ApplicationTitle;

            var menu = new MenuStrip();
            var projectMenu = new ToolStripMenuItem("專案");
            projectMenu.DropDownItems.Add("新增", null, (sender, args) => RunProjectCommand(CreateProject));
            projectMenu.DropDownItems.Add("開啟", null, (sender, args) => RunProjectCommand(OpenProject));
            projectMenu.DropDownItems.Add("儲存", null, (sender, args) => RunProjectCommand(SaveProject));
            projectMenu.DropDownItems.Add("另存新檔", null, (sender, args) => RunProjectCommand(SaveProjectAs));
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
                ReplaceProject(ProjectStore.Create(dialog.FileName));
            }
        }

        private void OpenProject()
        {
            using (var dialog = new OpenFileDialog { Filter = "Neurocle Project (*.neurocle)|*.neurocle" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                ReplaceProject(ProjectStore.Open(dialog.FileName));
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
                ProjectStore copy = project.SaveAs(dialog.FileName);
                ReplaceProject(copy);
            }
        }

        private static SaveFileDialog CreateProjectDialog()
        {
            return new SaveFileDialog { DefaultExt = "neurocle", AddExtension = true, Filter = "Neurocle Project (*.neurocle)|*.neurocle" };
        }

        private void ReplaceProject(ProjectStore replacement)
        {
            ProjectStore previous = project;
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

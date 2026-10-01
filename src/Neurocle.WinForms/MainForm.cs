using System.Drawing;
using System.Windows.Forms;
using Neurocle.WinForms.Resources;

namespace Neurocle.WinForms
{
    public sealed class MainForm : Form
    {
        public MainForm()
        {
            AutoScaleMode = AutoScaleMode.Dpi;
            MinimumSize = new Size(640, 360);
            StartPosition = FormStartPosition.CenterScreen;
            Text = Strings.ApplicationTitle;

            var status = new Label
            {
                AutoSize = true,
                Location = new Point(24, 24),
                Text = Strings.OfflineStatus
            };

            Controls.Add(status);
        }
    }
}

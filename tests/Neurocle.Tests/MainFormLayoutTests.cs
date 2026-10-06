using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Neurocle.WinForms;
using Neurocle.Infrastructure.Persistence;

namespace Neurocle.Tests
{
    [TestClass]
    public class MainFormLayoutTests
    {
        [TestMethod]
        public void MinimumSize_fits_the_smallest_supported_viewport_at_each_supported_dpi_scale()
        {
            using (var form = new MainForm(new ProjectLifecycle()))
            {
                foreach (var scale in new[] { 1f, 1.25f, 1.5f, 1.75f, 2f })
                {
                    Assert.IsTrue(form.MinimumSize.Width * scale <= 1366f, "Width does not fit at {0:P0} DPI.", scale);
                    Assert.IsTrue(form.MinimumSize.Height * scale <= 768f, "Height does not fit at {0:P0} DPI.", scale);
                }
            }
        }

        [TestMethod]
        public void Rendered_content_fits_the_smallest_supported_viewport_at_each_supported_dpi_scale()
        {
            foreach (var scale in new[] { 1f, 1.25f, 1.5f, 1.75f, 2f })
            {
                using (var form = new MainForm(new ProjectLifecycle()))
                {
                    form.CreateControl();
                    form.Scale(new SizeF(scale, scale));
                    form.ClientSize = new Size(1366, 768);
                    form.PerformLayout();

                    foreach (Control control in form.Controls)
                    {
                        Assert.IsTrue(control.Left >= 0, "Control starts outside the viewport at {0:P0} DPI.", scale);
                        Assert.IsTrue(control.Top >= 0, "Control starts outside the viewport at {0:P0} DPI.", scale);
                        Assert.IsTrue(control.Right <= form.ClientSize.Width, "Control exceeds viewport width at {0:P0} DPI.", scale);
                        Assert.IsTrue(control.Bottom <= form.ClientSize.Height, "Control exceeds viewport height at {0:P0} DPI.", scale);
                    }
                }
            }
        }

        [TestMethod]
        public void Project_menu_exposes_diagnostics_usage_and_log_folder_commands()
        {
            using (var form = new MainForm(new ProjectLifecycle()))
            {
                Assert.IsTrue(form.MainMenuStrip.Items.OfType<ToolStripMenuItem>().Single().DropDownItems.OfType<ToolStripMenuItem>().Any(item => item.Text == "診斷資訊"));
            }
        }

        [TestMethod]
        public void Page_tabs_are_below_the_menu_and_status()
        {
            using (var form = new MainForm(new ProjectLifecycle()))
            {
                form.ClientSize = new Size(576, 325);
                form.CreateControl();
                form.PerformLayout();

                var pages = form.Controls.OfType<TabControl>().Single();
                var status = form.Controls.OfType<Label>().Single();
                Assert.AreEqual(4, pages.TabPages.Count);
                Assert.IsTrue(status.Top >= form.MainMenuStrip.Bottom, "Status overlaps the menu.");
                Assert.IsTrue(pages.Top >= status.Bottom, "Page tabs overlap the menu or status.");
                Assert.IsTrue(pages.Height > pages.GetTabRect(0).Bottom, "Page tabs do not fit in the viewport.");
            }
        }
    }
}

using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Neurocle.Application.Projects;
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
        public void Dataset_assignment_count_column_is_visible_only_for_obd_and_seg_sets()
        {
            using (var control = new DatasetAssignmentControl())
            {
                ListView list = control.Controls.OfType<ListView>().Single();
                control.LoadRows(Enumerable.Empty<DatasetRow>(), false);
                Assert.IsFalse(list.Columns.Cast<ColumnHeader>().Any(column => column.Text == "標註數量"));
                control.LoadRows(Enumerable.Empty<DatasetRow>(), true);
                Assert.IsTrue(list.Columns.Cast<ColumnHeader>().Any(column => column.Text == "標註數量"));
                control.LoadRows(Enumerable.Empty<DatasetRow>(), false);
                Assert.IsFalse(list.Columns.Cast<ColumnHeader>().Any(column => column.Text == "標註數量"));
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

        [TestMethod]
        public void Image_and_annotation_pages_share_the_persistent_image_workspace_and_view()
        {
            using (var form = new MainForm(new ProjectLifecycle()))
            {
                form.CreateControl();
                form.ClientSize = new Size(1200, 800);
                form.PerformLayout();

                var pages = form.Controls.OfType<TabControl>().Single();
                Assert.IsTrue(form.Controls.Contains(form.ImageBrowser));
                Assert.IsTrue(pages.TabPages[0].Controls.Contains(form.ImageCanvas));
                Assert.IsTrue(pages.TabPages[1].Controls.Contains(form.AnnotationCanvas));

                form.AnnotationCanvas.SetZoomPercent(150);
                Assert.AreEqual(form.AnnotationCanvas.Zoom, form.ImageCanvas.Zoom, 0.0001d);

            }
        }

        [STATestMethod]
        public void Rapid_image_switch_and_missing_image_clear_the_previous_canvas_content()
        {
            string directory = Path.Combine(Path.GetTempPath(), "Neurocle.Tests", Guid.NewGuid().ToString("N"));
            string first = Path.Combine(directory, "first.png");
            string second = Path.Combine(directory, "second.png");
            try
            {
                Directory.CreateDirectory(directory);
                CreateSolidImage(first, Color.Red);
                CreateSolidImage(second, Color.Blue);
                using (var canvas = new ImageCanvasControl { Size = new Size(100, 100) })
                {
                    canvas.CreateControl();
                    canvas.LoadImage(first);
                    canvas.LoadImage(second);

                    DateTime timeout = DateTime.UtcNow.AddSeconds(5);
                    while (canvas.LoadedImagePath != second && DateTime.UtcNow < timeout)
                    {
                        System.Windows.Forms.Application.DoEvents();
                        Thread.Sleep(1);
                    }

                    Assert.AreEqual(second, canvas.LoadedImagePath);
                    canvas.LoadImage(null);
                    Assert.IsNull(canvas.LoadedImagePath);
                }
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [STATestMethod]
        public void Obd_rectangle_mode_creates_over_existing_boxes_without_selecting_or_moving_them()
        {
            string directory = Path.Combine(Path.GetTempPath(), "Neurocle.Tests", Guid.NewGuid().ToString("N"));
            string imagePath = Path.Combine(directory, "source.png");
            Directory.CreateDirectory(directory);
            try
            {
                CreateSolidImage(imagePath, Color.Red, 100);
                using (var canvas = new ImageCanvasControl { Size = new Size(100, 100), ObdEnabled = true, ObdSelectMode = false })
                {
                    canvas.CreateControl();
                    canvas.ObdAnnotations = new[] { new ObdAnnotation("existing", "set", "image", "class", 10, 10, 50, 50, 0) };
                    canvas.LoadImage(imagePath);
                    WaitForImage(canvas, imagePath);
                    Rectangle? created = null;
                    int changed = 0;
                    canvas.ObdRectangleCreated += value => created = value;
                    canvas.ObdRectanglesChanged += value => changed++;

                    RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 20, 20, 0));
                    RaiseMouse(canvas, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, 40, 40, 0));
                    RaiseMouse(canvas, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 0, 40, 40, 0));

                    Assert.IsTrue(created.HasValue);
                    Assert.AreEqual(0, changed);
                    Assert.AreEqual(0, canvas.SelectedObdIds.Count);
                }
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [TestMethod]
        public void Creating_an_annotation_set_selects_the_new_internal_id_even_when_names_match()
        {
            string directory = Path.Combine(Path.GetTempPath(), "Neurocle.Tests", Guid.NewGuid().ToString("N"));
            string path = Path.Combine(directory, "project.neurocle");
            ProjectStore store = null;
            Directory.CreateDirectory(directory);
            try
            {
                store = ProjectStore.Create(path);
                AnnotationSet original = store.CreateAnnotationSet("same", AnnotationSetType.Cla);
                using (var form = new MainForm(new ProjectLifecycle()))
                {
                    Invoke(form, "ReplaceProject", store);
                    foreach (AnnotationSetType type in new[] { AnnotationSetType.Cla, AnnotationSetType.Obd, AnnotationSetType.Seg })
                    {
                        string[] before = store.GetAnnotationSets().Select(item => item.Id).ToArray();
                        Invoke(form, "CreateAnnotationSet", type, "same");
                        AnnotationSet created = store.GetAnnotationSets().Single(item => !before.Contains(item.Id));
                        var annotationSets = (ComboBox)typeof(MainForm).GetField("annotationSets", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
                        Assert.AreEqual(created.Id, ((AnnotationSet)annotationSets.SelectedItem).Id);
                        Assert.AreEqual(1, store.GetClasses(created.Id).Count);
                    }

                    Assert.AreNotEqual(original.Id, ((AnnotationSet)((ComboBox)typeof(MainForm).GetField("annotationSets", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form)).SelectedItem).Id);
                }
            }
            finally
            {
                store?.Dispose();
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [TestMethod]
        public void Annotation_set_type_controls_the_right_side_tools_and_resets_to_selection()
        {
            string directory = Path.Combine(Path.GetTempPath(), "Neurocle.Tests", Guid.NewGuid().ToString("N"));
            string path = Path.Combine(directory, "project.neurocle");
            ProjectStore store = null;
            Directory.CreateDirectory(directory);
            try
            {
                store = ProjectStore.Create(path);
                AnnotationSet cla = store.CreateAnnotationSet("classes", AnnotationSetType.Cla);
                AnnotationSet obd = store.CreateAnnotationSet("boxes", AnnotationSetType.Obd);
                AnnotationSet seg = store.CreateAnnotationSet("regions", AnnotationSetType.Seg);
                using (var form = new MainForm(new ProjectLifecycle()))
                {
                    Invoke(form, "ReplaceProject", store);
                    ComboBox sets = Field<ComboBox>(form, "annotationSets");
                    FlowLayoutPanel tools = Field<FlowLayoutPanel>(form, "annotationTools");

                    sets.SelectedItem = AnnotationSet(sets, cla.Id);
                    Assert.AreEqual(0, tools.Controls.Count);

                    sets.SelectedItem = AnnotationSet(sets, obd.Id);
                    CollectionAssert.AreEquivalent(new[] { "obdSelect", "obdRectangle" }, tools.Controls.Cast<Control>().Select(control => control.Name).ToArray());
                    Assert.IsTrue(form.AnnotationCanvas.ObdSelectMode);
                    Invoke(form, "SelectObdTool", false);
                    Assert.IsFalse(form.AnnotationCanvas.ObdSelectMode);

                    sets.SelectedItem = AnnotationSet(sets, seg.Id);
                    CollectionAssert.AreEquivalent(new[] { "segSelect", "segRectangle", "segEllipse", "segPolygon", "segBrush", "segLine", "segEraser" }, tools.Controls.Cast<Control>().Select(control => control.Name).ToArray());
                    Assert.AreEqual(ImageCanvasControl.SegToolKind.Select, form.AnnotationCanvas.SegTool);

                    sets.SelectedItem = AnnotationSet(sets, obd.Id);
                    Assert.IsTrue(form.AnnotationCanvas.ObdSelectMode);
                    Assert.IsNull(form.AnnotationCanvas.SegTool);
                }
            }
            finally
            {
                store?.Dispose();
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [TestMethod]
        public void Annotation_side_panel_places_add_class_above_classes_and_labels_icon_tools()
        {
            using (var form = new MainForm(new ProjectLifecycle()))
            {
                form.CreateControl();
                form.PerformLayout();
                FlowLayoutPanel classes = Field<FlowLayoutPanel>(form, "annotationClasses");
                Control side = Field<Control>(form, "annotationSidebar");
                Control addClass = side.Controls.Cast<Control>().Single(control => control.Name == "addClass");
                ToolTip toolTips = Field<ToolTip>(form, "annotationToolTips");

                Assert.IsTrue(addClass.Bottom <= classes.Top);
                Assert.AreEqual("SEG 矩形", toolTips.GetToolTip(Field<Control>(form, "segRectangle")));
            }
        }

        private static void CreateSolidImage(string path, Color color, int size = 10)
        {
            using (var bitmap = new Bitmap(size, size))
            {
                bitmap.SetPixel(0, 0, color);
                bitmap.Save(path, ImageFormat.Png);
            }
        }

        private static void WaitForImage(ImageCanvasControl canvas, string path)
        {
            DateTime timeout = DateTime.UtcNow.AddSeconds(5);
            while (canvas.LoadedImagePath != path && DateTime.UtcNow < timeout)
            {
                System.Windows.Forms.Application.DoEvents();
                Thread.Sleep(1);
            }

            Assert.AreEqual(path, canvas.LoadedImagePath);
        }

        private static void RaiseMouse(Control control, string methodName, MouseEventArgs args)
        {
            typeof(ImageCanvasControl).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(control, new object[] { args });
        }

        private static void Invoke(object target, string methodName, params object[] arguments)
        {
            MethodInfo method = target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
                .Single(candidate => candidate.Name == methodName && candidate.GetParameters().Length == arguments.Length);
            method.Invoke(target, arguments);
        }

        private static T Field<T>(object target, string fieldName) where T : class
        {
            return (T)target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
        }

        private static AnnotationSet AnnotationSet(ComboBox sets, string id)
        {
            return sets.Items.Cast<AnnotationSet>().Single(item => item.Id == id);
        }
    }
}

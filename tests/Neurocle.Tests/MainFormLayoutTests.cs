using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
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
        [STATestMethod]
        public void Import_progress_dialog_displays_processed_total_success_and_failure_counts()
        {
            using (var form = new ImportProgressForm())
            {
                form.Report(new ImageImportProgress(2, 5, 1, 1));

                ProgressBar progressBar = form.Controls.OfType<ProgressBar>().Single();
                Label progressLabel = form.Controls.OfType<Label>().Single(label => label.Text.Contains("2 / 5"));
                Assert.AreEqual(5, progressBar.Maximum);
                Assert.AreEqual(2, progressBar.Value);
                StringAssert.Contains(progressLabel.Text, "成功: 1");
                StringAssert.Contains(progressLabel.Text, "失敗: 1");
            }
        }

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
        public void Image_browser_annotation_count_column_is_visible_only_for_obd_and_seg_rows()
        {
            using (var control = new ImageBrowserControl())
            {
                ListView list = control.Controls.OfType<ListView>().Single();
                ProjectImage image = new ProjectImage("image", "image.png", "image.png", "hash", 1, 1, 1);
                control.LoadRows(new[] { new ImageListRow(image, new[] { "class1" }, 1, false) }, Path.GetTempPath());
                Assert.IsFalse(list.Columns.Cast<ColumnHeader>().Any(column => column.Text == "標註數量"));
                control.LoadRows(new[] { new ImageListRow(image, new[] { "class1" }, 1, true) }, Path.GetTempPath());
                Assert.IsTrue(list.Columns.Cast<ColumnHeader>().Any(column => column.Text == "標註數量"));
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

        [TestMethod]
        public void Annotation_canvas_uses_a_compact_wrapping_header_and_matches_image_canvas_height()
        {
            using (var form = new MainForm(new ProjectLifecycle()))
            {
                form.ClientSize = new Size(1200, 800);
                form.Show();
                System.Windows.Forms.Application.DoEvents();
                form.PerformLayout();
                var pages = form.Controls.OfType<TabControl>().Single();
                pages.SelectedIndex = 1;
                System.Windows.Forms.Application.DoEvents();
                form.PerformLayout();
                TabPage images = pages.TabPages[0];
                TabPage annotations = pages.TabPages[1];
                FlowLayoutPanel header = annotations.Controls.OfType<FlowLayoutPanel>().Single(panel => panel.Dock == DockStyle.Top);

                Assert.AreEqual(form.ImageCanvas.Height, form.AnnotationCanvas.Height);
                Assert.IsTrue(form.AnnotationCanvas.Height >= annotations.ClientSize.Height * 0.75d);
                Assert.IsTrue(header.Bottom <= form.AnnotationCanvas.Top);
            }
        }

        [STATestMethod]
        public void Full_hd_workspace_keeps_both_page_canvases_and_annotation_controls_operable()
        {
            string directory = Path.Combine(Path.GetTempPath(), "Neurocle.Tests", Guid.NewGuid().ToString("N"));
            string path = Path.Combine(directory, "project.neurocle");
            ProjectStore store = null;
            Directory.CreateDirectory(directory);
            try
            {
                store = ProjectStore.Create(path);
                AnnotationSet obd = store.CreateAnnotationSet("boxes", AnnotationSetType.Obd);
                AnnotationSet seg = store.CreateAnnotationSet("regions", AnnotationSetType.Seg);
                using (var form = new MainForm(new ProjectLifecycle()))
                {
                    Invoke(form, "ReplaceProject", store);
                    form.Size = new Size(1920, 1080);
                    form.Show();
                    System.Windows.Forms.Application.DoEvents();
                    TabControl pages = form.Controls.OfType<TabControl>().Single();
                    TabPage images = pages.TabPages[0];
                    TabPage annotations = pages.TabPages[1];
                    Panel imageHeader = images.Controls.OfType<Panel>().Single(panel => panel.Dock == DockStyle.Top);
                    FlowLayoutPanel imageControls = imageHeader.Controls.OfType<FlowLayoutPanel>().Single();
                    FlowLayoutPanel annotationHeader = annotations.Controls.OfType<FlowLayoutPanel>().Single(panel => panel.Dock == DockStyle.Top);
                    Control sidebar = Field<Control>(form, "annotationSidebar");
                    ComboBox sets = Field<ComboBox>(form, "annotationSets");

                    Assert.IsTrue(form.ClientSize.Height < form.Height, "The test must use a 1920x1080 window rather than a 1920x1080 client area.");
                    Assert.AreEqual(form.ImageCanvas.Height, form.AnnotationCanvas.Height);
                    Assert.IsTrue(form.AnnotationCanvas.Height >= annotations.ClientSize.Height * 0.75d);
                    Assert.IsTrue(imageHeader.Bottom <= form.ImageCanvas.Top);
                    Assert.IsTrue(annotationHeader.Bottom <= form.AnnotationCanvas.Top);
                    Assert.IsTrue(sidebar.Right <= annotations.ClientSize.Width);
                    Assert.IsTrue(form.AnnotationCanvas.Right <= sidebar.Left);
                    pages.SelectedIndex = 0;
                    System.Windows.Forms.Application.DoEvents();
                    AssertControlsAreUsable(imageControls.Controls.Cast<Control>(), imageControls);
                    AssertCanvasIsUsable(form.ImageCanvas, images);
                    pages.SelectedIndex = 1;
                    System.Windows.Forms.Application.DoEvents();
                    AssertControlsAreUsable(annotationHeader.Controls.Cast<Control>(), annotationHeader);
                    AssertCanvasIsUsable(form.AnnotationCanvas, annotations);

                    sets.SelectedItem = AnnotationSet(sets, obd.Id);
                    System.Windows.Forms.Application.DoEvents();
                    FlowLayoutPanel tools = Field<FlowLayoutPanel>(form, "annotationTools");
                    Control[] obdTools = tools.Controls.Cast<Control>().ToArray();
                    CollectionAssert.AreEquivalent(new[] { "obdSelect", "obdRectangle" }, obdTools.Select(control => control.Name).ToArray());
                    AssertControlsAreUsable(obdTools, sidebar);
                    ((Button)obdTools.Single(control => control.Name == "obdRectangle")).PerformClick();
                    Assert.IsFalse(form.AnnotationCanvas.ObdSelectMode);

                    sets.SelectedItem = AnnotationSet(sets, seg.Id);
                    System.Windows.Forms.Application.DoEvents();
                    Control[] segTools = tools.Controls.Cast<Control>().ToArray();
                    CollectionAssert.AreEquivalent(new[] { "segSelect", "segRectangle", "segEllipse", "segPolygon", "segBrush", "segLine", "segEraser" }, segTools.Select(control => control.Name).ToArray());
                    AssertControlsAreUsable(segTools, sidebar);
                    AssertControlsAreUsable(new Control[] { Field<TrackBar>(form, "annotationOpacity") }, annotationHeader);
                    AssertControlsAreUsable(Descendants<TrackBar>(annotationHeader), annotationHeader);
                    ((Button)segTools.Single(control => control.Name == "segLine")).PerformClick();
                    Assert.AreEqual(ImageCanvasControl.SegToolKind.Line, form.AnnotationCanvas.SegTool);
                }
            }
            finally
            {
                store?.Dispose();
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [TestMethod]
        public void Compact_annotation_header_keeps_canvas_and_sidebar_inside_narrow_high_dpi_pages()
        {
            foreach (float scale in new[] { 1f, 1.25f, 1.5f, 1.75f, 2f })
            {
                using (var form = new MainForm(new ProjectLifecycle()))
                {
                    form.Show();
                    System.Windows.Forms.Application.DoEvents();
                    form.Scale(new SizeF(scale, scale));
                    form.ClientSize = new Size(1366, 768);
                    form.Controls.OfType<TabControl>().Single().SelectedIndex = 1;
                    System.Windows.Forms.Application.DoEvents();
                    form.PerformLayout();
                    TabPage annotations = form.Controls.OfType<TabControl>().Single().TabPages[1];
                    Control sidebar = Field<Control>(form, "annotationSidebar");
                    FlowLayoutPanel header = annotations.Controls.OfType<FlowLayoutPanel>().Single(panel => panel.Dock == DockStyle.Top);

                    Assert.IsTrue(header.Bottom <= annotations.ClientSize.Height, "Header exceeds page height at {0:P0} DPI.", scale);
                    Assert.IsTrue(sidebar.Right <= annotations.ClientSize.Width, "Sidebar exceeds page width at {0:P0} DPI.", scale);
                    Assert.IsTrue(form.AnnotationCanvas.Top >= header.Bottom, "Canvas overlaps header at {0:P0} DPI.", scale);
                    Assert.IsTrue(form.AnnotationCanvas.Right <= sidebar.Left, "Canvas overlaps sidebar at {0:P0} DPI.", scale);
                }
            }
        }

        [STATestMethod]
        public void Zoom_controls_show_actual_shared_scale_and_f_fits_without_intercepting_text_input()
        {
            using (var form = new MainForm(new ProjectLifecycle()))
            {
                form.Show();
                System.Windows.Forms.Application.DoEvents();
                var pages = form.Controls.OfType<TabControl>().Single();
                ComboBox imageZoom = Descendants<ComboBox>(pages.TabPages[0]).Single();
                imageZoom.SelectedItem = "125%";
                Assert.AreEqual(1.25d, form.ImageCanvas.Zoom, 0.0001d);
                Assert.AreEqual(1.25d, form.AnnotationCanvas.Zoom, 0.0001d);
                Assert.IsTrue(Descendants<Label>(form).Count(label => label.Text == "125%") >= 2);

                pages.SelectedIndex = 1;
                RaiseKey(form, new KeyEventArgs(Keys.F));
                Assert.IsTrue(form.AnnotationCanvas.ViewState.IsFitted);
                Assert.AreEqual(form.AnnotationCanvas.Zoom, form.ImageCanvas.Zoom, 0.0001d);
                string fitted = Math.Round(form.AnnotationCanvas.Zoom * 100d).ToString() + "%";
                Assert.IsTrue(Descendants<Label>(form).Count(label => label.Text == fitted) >= 2);

                form.AnnotationCanvas.SetZoomPercent(150);
                TextBox search = Field<TextBox>(form.ImageBrowser, "search");
                search.Focus();
                Assert.IsTrue(search.Focused);
                RaiseKey(form, new KeyEventArgs(Keys.F));
                Assert.AreEqual(1.5d, form.AnnotationCanvas.Zoom, 0.0001d);
            }
        }

        [STATestMethod]
        public void Up_and_down_select_images_in_visible_order_without_intercepting_text_input()
        {
            ProjectImage[] images =
            {
                new ProjectImage("one", "alpha.png", "one.png", "hash", 1, 1, 1),
                new ProjectImage("two", "bravo.png", "two.png", "hash", 1, 1, 2),
                new ProjectImage("three", "charlie.png", "three.png", "hash", 1, 1, 3)
            };
            using (var form = new MainForm(new ProjectLifecycle()))
            {
                form.Show();
                System.Windows.Forms.Application.DoEvents();
                form.ImageBrowser.LoadImages(images, Path.GetTempPath());
                var pages = form.Controls.OfType<TabControl>().Single();
                pages.SelectedIndex = 1;

                RaiseKey(form, new KeyEventArgs(Keys.Down));
                CollectionAssert.AreEqual(new[] { "one" }, form.ImageBrowser.SelectedImageIds.ToArray());
                RaiseKey(form, new KeyEventArgs(Keys.Down));
                CollectionAssert.AreEqual(new[] { "two" }, form.ImageBrowser.SelectedImageIds.ToArray());
                RaiseKey(form, new KeyEventArgs(Keys.Up));
                CollectionAssert.AreEqual(new[] { "one" }, form.ImageBrowser.SelectedImageIds.ToArray());
                RaiseKey(form, new KeyEventArgs(Keys.Up));
                CollectionAssert.AreEqual(new[] { "one" }, form.ImageBrowser.SelectedImageIds.ToArray());

                TextBox search = Field<TextBox>(form.ImageBrowser, "search");
                search.Text = "bravo";
                Assert.AreEqual(0, form.ImageBrowser.SelectedImageIds.Count);
                RaiseKey(form, new KeyEventArgs(Keys.Down));
                CollectionAssert.AreEqual(new[] { "two" }, form.ImageBrowser.SelectedImageIds.ToArray());
                search.Focus();
                RaiseKey(form, new KeyEventArgs(Keys.Down));
                CollectionAssert.AreEqual(new[] { "two" }, form.ImageBrowser.SelectedImageIds.ToArray());
            }
        }

        [STATestMethod]
        public void Seg_completion_applies_only_the_last_queued_image_selection()
        {
            string directory = Path.Combine(Path.GetTempPath(), "Neurocle.Tests", Guid.NewGuid().ToString("N"));
            string projectPath = Path.Combine(directory, "project.neurocle");
            ProjectStore store = null;
            Directory.CreateDirectory(directory);
            try
            {
                store = ProjectStore.Create(projectPath);
                AnnotationSet set = store.CreateAnnotationSet("seg", AnnotationSetType.Seg);
                ProjectImage[] images =
                {
                    new ProjectImage("one", "one.png", "one.png", "hash", 1, 1, 1),
                    new ProjectImage("two", "two.png", "two.png", "hash", 1, 1, 2),
                    new ProjectImage("three", "three.png", "three.png", "hash", 1, 1, 3)
                };
                using (var form = new MainForm(new ProjectLifecycle()))
                {
                    Invoke(form, "ReplaceProject", store);
                    form.Show();
                    System.Windows.Forms.Application.DoEvents();
                    form.ImageBrowser.LoadImages(images, directory);
                    Invoke(form, "ApplyImageSelection", new ImageSelectedEventArgs(images[0], null));
                    SetField(form, "annotationOperationInProgress", true);
                    form.ImageBrowser.SelectImage("two");
                    form.ImageBrowser.SelectImage("three");
                    SetField(form, "annotationOperationInProgress", false);

                    Task completion = (Task)form.GetType().GetMethod("RunSegOperationAsync", BindingFlags.Instance | BindingFlags.NonPublic)
                        .Invoke(form, new object[] { store, set.Id, "one", (Action)(() => { }) });
                    WaitForTask(completion);

                    Assert.AreEqual("three", Field<string>(form, "selectedImageId"));
                    CollectionAssert.AreEqual(new[] { "three" }, form.ImageBrowser.SelectedImageIds.ToArray());
                }
            }
            finally
            {
                store?.Dispose();
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
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
        public void Canvas_select_all_selects_only_current_obd_or_seg_annotations()
        {
            using (var canvas = new ImageCanvasControl())
            {
                canvas.ObdAnnotations = new[]
                {
                    new ObdAnnotation("one", "set", "image", "class", 1, 1, 3, 3, 0),
                    new ObdAnnotation("two", "set", "image", "class", 5, 5, 3, 3, 1)
                };
                canvas.SelectAllObd();
                CollectionAssert.AreEquivalent(new[] { "one", "two" }, canvas.SelectedObdIds.ToArray());
                canvas.ObdAnnotations = new[] { new ObdAnnotation("other", "set", "next-image", "class", 1, 1, 3, 3, 0) };
                Assert.AreEqual(0, canvas.SelectedObdIds.Count);

                canvas.SegRegions = new[]
                {
                    new SegRegion("first", "set", "image", "class", SegRegionType.Rect, 1, 1, 3, 3, 0),
                    new SegRegion("second", "set", "image", "class", SegRegionType.Ellipse, 5, 5, 3, 3, 1)
                };
                canvas.SelectAllSeg();
                CollectionAssert.AreEquivalent(new[] { "first", "second" }, canvas.SelectedSegIds.ToArray());
                canvas.SegRegions = new[] { new SegRegion("other", "next-set", "image", "class", SegRegionType.Rect, 1, 1, 3, 3, 0) };
                Assert.AreEqual(0, canvas.SelectedSegIds.Count);
            }
        }

        [TestMethod]
        public void Seg_tools_share_one_stroke_width_with_a_single_one_to_one_hundred_control()
        {
            using (var form = new MainForm(new ProjectLifecycle()))
            {
                NumericUpDown[] widths = Descendants<NumericUpDown>(form).Where(value => value.Minimum == 1 && value.Maximum == 100).ToArray();
                Assert.AreEqual(1, widths.Length);
                Assert.AreEqual(15, form.AnnotationCanvas.SegStrokeWidth);
                foreach (decimal width in new[] { 1m, 15m, 100m })
                {
                    widths[0].Value = width;
                    Assert.AreEqual((int)width, form.AnnotationCanvas.SegStrokeWidth);
                }
                Assert.ThrowsException<ArgumentOutOfRangeException>(() => form.AnnotationCanvas.SegStrokeWidth = 0);
                Assert.ThrowsException<ArgumentOutOfRangeException>(() => form.AnnotationCanvas.SegStrokeWidth = 101);
            }
        }

        [STATestMethod]
        public void Shared_seg_stroke_width_is_submitted_by_brush_line_and_eraser()
        {
            string directory = Path.Combine(Path.GetTempPath(), "Neurocle.Tests", Guid.NewGuid().ToString("N"));
            string imagePath = Path.Combine(directory, "source.png");
            Directory.CreateDirectory(directory);
            try
            {
                CreateSolidImage(imagePath, Color.Red, 300);
                using (var canvas = new ImageCanvasControl { Size = new Size(100, 100), SegEnabled = true })
                {
                    canvas.CreateControl();
                    canvas.LoadImage(imagePath);
                    WaitForImage(canvas, imagePath);
                    var submitted = new List<int>();
                    canvas.SegPathCreated += (type, points, width) => submitted.Add(width);
                    canvas.SegEraseRequested += (points, width) => submitted.Add(width);
                    foreach (int width in new[] { 1, 15, 100 })
                    {
                        canvas.SegStrokeWidth = width;
                        canvas.SegTool = ImageCanvasControl.SegToolKind.Line;
                        RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 10, 10, 0));
                        RaiseMouse(canvas, "OnMouseMove", new MouseEventArgs(MouseButtons.None, 0, 30, 10, 0));
                        RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 30, 10, 0));
                        canvas.SegTool = ImageCanvasControl.SegToolKind.Brush;
                        RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 20, 20, 0));
                        RaiseMouse(canvas, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, 40, 20, 0));
                        AssertPreviewStrokeThickness(canvas, 30, 20, Math.Max(1, width / 3));
                        RaiseMouse(canvas, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 0, 40, 20, 0));
                        canvas.SegTool = ImageCanvasControl.SegToolKind.Eraser;
                        RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 20, 30, 0));
                        RaiseMouse(canvas, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, 40, 30, 0));
                        AssertPreviewStrokeThickness(canvas, 30, 30, Math.Max(1, width / 3));
                        RaiseMouse(canvas, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 0, 40, 30, 0));
                    }
                    CollectionAssert.AreEqual(new[] { 1, 1, 1, 15, 15, 15, 100, 100, 100 }, submitted);
                }
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [STATestMethod]
        public void Closed_seg_brush_path_submits_a_filled_polygon()
        {
            string directory = Path.Combine(Path.GetTempPath(), "Neurocle.Tests", Guid.NewGuid().ToString("N"));
            string imagePath = Path.Combine(directory, "source.png");
            Directory.CreateDirectory(directory);
            try
            {
                CreateSolidImage(imagePath, Color.Red, 300);
                using (var canvas = new ImageCanvasControl { Size = new Size(100, 100), SegEnabled = true, SegTool = ImageCanvasControl.SegToolKind.Brush })
                {
                    canvas.CreateControl();
                    canvas.LoadImage(imagePath);
                    WaitForImage(canvas, imagePath);
                    SegRegionType? submittedType = null;
                    IReadOnlyList<SegPoint> submittedPoints = null;
                    canvas.SegPathCreated += (type, points, width) => { submittedType = type; submittedPoints = points; };

                    RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 20, 20, 0));
                    RaiseMouse(canvas, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, 70, 20, 0));
                    RaiseMouse(canvas, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, 70, 70, 0));
                    RaiseMouse(canvas, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, 20, 70, 0));
                    RaiseMouse(canvas, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 0, 20, 20, 0));

                    Assert.AreEqual(SegRegionType.PolyGon, submittedType);
                    Assert.IsNotNull(submittedPoints);
                    Assert.AreEqual(4, submittedPoints.Count);
                }
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        private static void AssertPreviewStrokeThickness(ImageCanvasControl canvas, int x, int y, int expected)
        {
            using (var preview = new Bitmap(canvas.Width, canvas.Height))
            {
                canvas.DrawToBitmap(preview, canvas.ClientRectangle);
                int thickness = Enumerable.Range(0, preview.Height)
                    .Count(row =>
                    {
                        Color pixel = preview.GetPixel(x, row);
                        return pixel.R > 220 && pixel.G > 220 && pixel.B > 220;
                    });
                Assert.IsTrue(Math.Abs(thickness - expected) <= 2,
                    $"Expected preview thickness near {expected}px at ({x}, {y}), but was {thickness}px.");
            }
        }

        [STATestMethod]
        public void Ctrl_a_selects_current_annotation_canvas_but_not_cla_pages_or_text_input()
        {
            string directory = Path.Combine(Path.GetTempPath(), "Neurocle.Tests", Guid.NewGuid().ToString("N"));
            string path = Path.Combine(directory, "project.neurocle");
            ProjectStore store = null;
            Directory.CreateDirectory(directory);
            try
            {
                store = ProjectStore.Create(path);
                AnnotationSet cla = store.CreateAnnotationSet("cla", AnnotationSetType.Cla);
                AnnotationSet obd = store.CreateAnnotationSet("obd", AnnotationSetType.Obd);
                using (var form = new MainForm(new ProjectLifecycle()))
                {
                    Invoke(form, "ReplaceProject", store);
                    form.Show();
                    System.Windows.Forms.Application.DoEvents();
                    ComboBox sets = Field<ComboBox>(form, "annotationSets");
                    TabControl pages = form.Controls.OfType<TabControl>().Single();
                    pages.SelectedIndex = 1;
                    sets.SelectedItem = AnnotationSet(sets, obd.Id);
                    form.AnnotationCanvas.ObdEnabled = true;
                    form.AnnotationCanvas.ObdAnnotations = new[]
                    {
                        new ObdAnnotation("one", obd.Id, "image", "class", 1, 1, 3, 3, 0),
                        new ObdAnnotation("two", obd.Id, "image", "class", 5, 5, 3, 3, 1)
                    };

                    RaiseKey(form, new KeyEventArgs(Keys.Control | Keys.A));
                    CollectionAssert.AreEquivalent(new[] { "one", "two" }, form.AnnotationCanvas.SelectedObdIds.ToArray());

                    sets.SelectedItem = AnnotationSet(sets, cla.Id);
                    form.AnnotationCanvas.ObdAnnotations = new[] { new ObdAnnotation("cla", cla.Id, "image", "class", 1, 1, 3, 3, 0) };
                    RaiseKey(form, new KeyEventArgs(Keys.Control | Keys.A));
                    Assert.AreEqual(0, form.AnnotationCanvas.SelectedObdIds.Count);

                    sets.SelectedItem = AnnotationSet(sets, obd.Id);
                    form.AnnotationCanvas.ObdEnabled = true;
                    form.AnnotationCanvas.ObdAnnotations = new[] { new ObdAnnotation("text", obd.Id, "image", "class", 1, 1, 3, 3, 0) };
                    TextBox search = Field<TextBox>(form.ImageBrowser, "search");
                    search.Focus();
                    Assert.IsTrue(search.Focused);
                    RaiseKey(form, new KeyEventArgs(Keys.Control | Keys.A));
                    Assert.AreEqual(0, form.AnnotationCanvas.SelectedObdIds.Count);
                }
            }
            finally
            {
                store?.Dispose();
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [STATestMethod]
        public void Ctrl_click_adds_and_removes_annotations_only_in_selection_modes()
        {
            string directory = Path.Combine(Path.GetTempPath(), "Neurocle.Tests", Guid.NewGuid().ToString("N"));
            string imagePath = Path.Combine(directory, "source.png");
            Directory.CreateDirectory(directory);
            try
            {
                CreateSolidImage(imagePath, Color.Red, 100);
                using (var canvas = new ImageCanvasControl { Size = new Size(100, 100), ObdEnabled = true, ObdSelectMode = true })
                {
                    canvas.ModifierKeysProvider = () => Keys.Control;
                    canvas.CreateControl();
                    canvas.LoadImage(imagePath);
                    WaitForImage(canvas, imagePath);
                    canvas.ObdAnnotations = new[] { new ObdAnnotation("obd", "set", "image", "class", 10, 10, 50, 50, 0) };
                    RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 20, 20, 0));
                    CollectionAssert.AreEquivalent(new[] { "obd" }, canvas.SelectedObdIds.ToArray());
                    RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 20, 20, 0));
                    Assert.AreEqual(0, canvas.SelectedObdIds.Count);

                    canvas.ObdSelectMode = false;
                    Rectangle? created = null;
                    canvas.ObdRectangleCreated += rectangle => created = rectangle;
                    RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 20, 20, 0));
                    RaiseMouse(canvas, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, 40, 40, 0));
                    RaiseMouse(canvas, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 0, 40, 40, 0));
                    Assert.IsTrue(created.HasValue);
                    Assert.AreEqual(0, canvas.SelectedObdIds.Count);

                    canvas.ObdEnabled = false;
                    canvas.SegEnabled = true;
                    canvas.SegTool = ImageCanvasControl.SegToolKind.Select;
                    canvas.SegRegions = new[] { new SegRegion("seg", "set", "image", "class", SegRegionType.Rect, 10, 10, 50, 50, 0) };
                    RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 20, 20, 0));
                    CollectionAssert.AreEquivalent(new[] { "seg" }, canvas.SelectedSegIds.ToArray());
                    RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 20, 20, 0));
                    Assert.AreEqual(0, canvas.SelectedSegIds.Count);

                    canvas.SegTool = ImageCanvasControl.SegToolKind.Rect;
                    RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 20, 20, 0));
                    Assert.AreEqual(0, canvas.SelectedSegIds.Count);
                    canvas.DiscardUnfinishedShape();
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

        [STATestMethod]
        public void Annotation_opacity_slider_applies_to_obd_seg_fills_and_seg_lines_then_resets_for_each_set()
        {
            string directory = Path.Combine(Path.GetTempPath(), "Neurocle.Tests", Guid.NewGuid().ToString("N"));
            string imagePath = Path.Combine(directory, "source.png");
            string projectPath = Path.Combine(directory, "project.neurocle");
            string reopenedProjectPath = Path.Combine(directory, "reopened.neurocle");
            ProjectStore store = null;
            Directory.CreateDirectory(directory);
            try
            {
                using (var image = new Bitmap(100, 100))
                using (var graphics = Graphics.FromImage(image))
                {
                    graphics.Clear(Color.Black);
                    image.Save(imagePath, ImageFormat.Png);
                }
                using (var canvas = new ImageCanvasControl { Size = new Size(100, 100) })
                {
                    canvas.CreateControl();
                    canvas.LoadImage(imagePath);
                    WaitForImage(canvas, imagePath);
                    var classes = new Dictionary<string, AnnotationClass> { { "class", new AnnotationClass("class", "set", "label", "#FFFFFF") } };
                    canvas.ObdClasses = classes;
                    canvas.ObdAnnotations = new[] { new ObdAnnotation("obd", "set", "image", "class", 20, 20, 60, 60, 0) };
                    AssertOpacity(canvas, value => canvas.ObdOpacity = value, 50, 50);
                    canvas.ObdOpacity = 0f;
                    canvas.SelectAllObd();
                    Assert.AreEqual(Color.White.ToArgb(), RenderPixel(canvas, 20, 50).ToArgb(), "OBD outline must remain visible at 0% opacity.");
                    Assert.AreEqual(Color.White.ToArgb(), RenderPixel(canvas, 20, 20).ToArgb(), "Selected OBD handle must remain visible at 0% opacity.");
                    Assert.IsTrue(ContainsBrightPixel(canvas, new Rectangle(25, 22, 50, 16)), "OBD class text must remain visible at 0% opacity.");

                    canvas.ObdAnnotations = null;
                    canvas.SegClasses = classes;
                    canvas.SegRegions = new[] { new SegRegion("rect", "set", "image", "class", SegRegionType.Rect, 20, 20, 60, 60, 0) };
                    AssertOpacity(canvas, value => canvas.SegOpacity = value, 50, 50);

                    canvas.SegRegions = new[] { new SegRegion("line", "set", "image", "class", SegRegionType.PolyLine, 0, 0, 0, 0, 0, new[] { new SegPoint(10, 50), new SegPoint(90, 50) }, 10) };
                    AssertOpacity(canvas, value => canvas.SegOpacity = value, 50, 50);
                }

                store = ProjectStore.Create(projectPath);
                AnnotationSet obd = store.CreateAnnotationSet("obd", AnnotationSetType.Obd);
                AnnotationSet seg = store.CreateAnnotationSet("seg", AnnotationSetType.Seg);
                using (var form = new MainForm(new ProjectLifecycle()))
                {
                    Invoke(form, "ReplaceProject", store);
                    ComboBox sets = Field<ComboBox>(form, "annotationSets");
                    TrackBar opacity = Field<TrackBar>(form, "annotationOpacity");
                    sets.SelectedItem = AnnotationSet(sets, obd.Id);
                    opacity.Value = 0;
                    Assert.AreEqual(0f, form.AnnotationCanvas.ObdOpacity);
                    Assert.AreEqual(0f, form.AnnotationCanvas.SegOpacity);

                    Invoke(form, "RefreshAnnotationSets", new object[] { null });
                    Assert.AreEqual(0, opacity.Value, "Refreshing the current annotation set must not reset opacity.");

                    SetField(form.AnnotationCanvas, "segLineStart", new PointF(10, 10));
                    Invoke(form, "RefreshAnnotationSets", new object[] { seg.Id });
                    Assert.IsNull(form.AnnotationCanvas.GetType().GetField("segLineStart", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form.AnnotationCanvas), "A programmatic annotation-set switch must discard unfinished SEG shapes.");
                    Assert.AreEqual(40, opacity.Value);
                    Assert.AreEqual(0.4f, form.AnnotationCanvas.ObdOpacity);
                    Assert.AreEqual(0.4f, form.AnnotationCanvas.SegOpacity);

                    opacity.Value = 100;
                    ProjectStore reopened = ProjectStore.Create(reopenedProjectPath);
                    Invoke(form, "ReplaceProject", reopened);
                    store = reopened;
                    Assert.AreEqual(40, opacity.Value, "Opening a project must reset opacity even when it has no annotation sets.");
                    Assert.AreEqual(0.4f, form.AnnotationCanvas.ObdOpacity);
                    Assert.AreEqual(0.4f, form.AnnotationCanvas.SegOpacity);
                }
                store.Dispose();
                store = null;
            }
            finally
            {
                store?.Dispose();
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        private static void AssertOpacity(ImageCanvasControl canvas, Action<float> setOpacity, int x, int y)
        {
            setOpacity(0f);
            Assert.AreEqual(Color.Black.ToArgb(), RenderPixel(canvas, x, y).ToArgb());
            setOpacity(0.4f);
            int fortyPercent = RenderPixel(canvas, x, y).R;
            Assert.IsTrue(fortyPercent > 0 && fortyPercent < 255, "40% opacity must blend the annotation with the image.");
            setOpacity(1f);
            Assert.AreEqual(Color.White.ToArgb(), RenderPixel(canvas, x, y).ToArgb());
        }

        private static Color RenderPixel(ImageCanvasControl canvas, int x, int y)
        {
            using (var rendered = new Bitmap(canvas.Width, canvas.Height))
            {
                canvas.DrawToBitmap(rendered, canvas.ClientRectangle);
                return rendered.GetPixel(x, y);
            }
        }

        private static bool ContainsBrightPixel(ImageCanvasControl canvas, Rectangle area)
        {
            using (var rendered = new Bitmap(canvas.Width, canvas.Height))
            {
                canvas.DrawToBitmap(rendered, canvas.ClientRectangle);
                return Enumerable.Range(area.Left, area.Width).Any(x => Enumerable.Range(area.Top, area.Height)
                    .Any(y => rendered.GetPixel(x, y).R > 200));
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

        private static void WaitForTask(Task task)
        {
            DateTime timeout = DateTime.UtcNow.AddSeconds(5);
            while (!task.IsCompleted && DateTime.UtcNow < timeout)
            {
                System.Windows.Forms.Application.DoEvents();
                Thread.Sleep(1);
            }
            Assert.IsTrue(task.IsCompleted, "The asynchronous operation did not complete.");
            task.GetAwaiter().GetResult();
        }

        private static void RaiseMouse(Control control, string methodName, MouseEventArgs args)
        {
            typeof(ImageCanvasControl).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(control, new object[] { args });
        }

        private static void RaiseKey(Control control, KeyEventArgs args)
        {
            typeof(Control).GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(control, new object[] { args });
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

        private static void SetField(object target, string fieldName, object value)
        {
            target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
        }

        private static AnnotationSet AnnotationSet(ComboBox sets, string id)
        {
            return sets.Items.Cast<AnnotationSet>().Single(item => item.Id == id);
        }

        private static void AssertControlsAreUsable(IEnumerable<Control> controls, Control container)
        {
            Rectangle containerBounds = container.RectangleToScreen(container.ClientRectangle);
            foreach (Control control in controls)
            {
                Assert.IsTrue(control.Visible, "{0} is hidden.", control.Name ?? control.Text);
                Assert.IsTrue(control.Enabled, "{0} is disabled.", control.Name ?? control.Text);
                Assert.IsTrue(containerBounds.Contains(control.RectangleToScreen(control.ClientRectangle)), "{0} is clipped by its container.", control.Name ?? control.Text);
            }
        }

        private static void AssertCanvasIsUsable(Control canvas, TabPage page)
        {
            Assert.IsTrue(canvas.Width > 0, "The canvas has no horizontal working area.");
            Assert.IsTrue(page.ClientRectangle.Contains(canvas.Bounds), "The canvas exceeds its page.");
        }

        private static IEnumerable<T> Descendants<T>(Control control) where T : Control
        {
            return control.Controls.Cast<Control>().SelectMany(Descendants<T>).Concat(control is T ? new[] { (T)control } : Enumerable.Empty<T>());
        }
    }
}

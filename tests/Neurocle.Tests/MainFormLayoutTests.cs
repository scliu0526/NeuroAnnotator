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
using Clipper2Lib;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Neurocle.Application.Projects;
using Neurocle.Domain.Segmentation;
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
        public void Main_window_starts_maximized_and_can_be_restored_and_resized()
        {
            using (var form = new MainForm(new ProjectLifecycle()))
            {
                Assert.AreEqual(FormWindowState.Maximized, form.WindowState);
                form.WindowState = FormWindowState.Normal;
                form.Size = new Size(1024, 640);
                Assert.AreEqual(FormWindowState.Normal, form.WindowState);
                Assert.AreEqual(new Size(1024, 640), form.Size);
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
        public void Annotation_set_creation_dialog_offers_all_types()
        {
            using (var dialog = new AnnotationSetCreationDialog())
            {
                ComboBox types = dialog.Controls.OfType<ComboBox>().Single();
                CollectionAssert.AreEqual(new[] { AnnotationSetType.Cla, AnnotationSetType.Obd, AnnotationSetType.Seg }, types.Items.Cast<AnnotationSetType>().ToArray());
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
                string firstPath = Path.Combine(directory, "one.png");
                string secondPath = Path.Combine(directory, "two.png");
                string thirdPath = Path.Combine(directory, "three.png");
                CreateSolidImage(firstPath, Color.Red);
                CreateSolidImage(secondPath, Color.Green);
                CreateSolidImage(thirdPath, Color.Blue);
                ProjectImage[] images = store.ImportFiles(new[] { firstPath, secondPath, thirdPath }).ToArray();
                using (var form = new MainForm(new ProjectLifecycle()))
                {
                    Invoke(form, "ReplaceProject", store);
                    form.Show();
                    System.Windows.Forms.Application.DoEvents();
                    Invoke(form, "ApplyImageSelection", new ImageSelectedEventArgs(images[0], Path.Combine(store.ImagesDirectory, images[0].ManagedFileName)));
                    SetField(form, "annotationOperationInProgress", true);
                    form.ImageBrowser.SelectImage(images[1].Id);
                    form.ImageBrowser.SelectImage(images[2].Id);
                    SetField(form, "annotationOperationInProgress", false);

                    Task completion = (Task)form.GetType().GetMethod("RunSegOperationAsync", BindingFlags.Instance | BindingFlags.NonPublic)
                        .Invoke(form, new object[] { store, set.Id, images[0].Id, (Action)(() => { }) });
                    WaitForTask(completion);

                    Assert.AreEqual(images[2].Id, Field<string>(form, "selectedImageId"));
                    CollectionAssert.AreEqual(new[] { images[2].Id }, form.ImageBrowser.SelectedImageIds.ToArray());
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

        [STATestMethod]
        public void Every_selected_obd_and_seg_region_renders_a_bounding_box_and_eight_handles_after_view_changes()
        {
            string directory = Path.Combine(Path.GetTempPath(), "Neurocle.Tests", Guid.NewGuid().ToString("N"));
            string imagePath = Path.Combine(directory, "source.png");
            Directory.CreateDirectory(directory);
            try
            {
                CreateSolidImage(imagePath, Color.Black, 100);
                using (var canvas = new ImageCanvasControl { Size = new Size(160, 160) })
                {
                    canvas.CreateControl();
                    canvas.LoadImage(imagePath);
                    WaitForImage(canvas, imagePath);
                    canvas.ObdClasses = new Dictionary<string, AnnotationClass> { { "class", new AnnotationClass("class", "set", "label", "#00FF00") } };
                    ObdAnnotation[] obds =
                    {
                        new ObdAnnotation("one", "set", "image", "class", 20, 20, 20, 20, 0),
                        new ObdAnnotation("two", "set", "image", "class", 60, 60, 20, 20, 1),
                        new ObdAnnotation("three", "set", "image", "class", 20, 60, 20, 20, 2)
                    };
                    canvas.ObdAnnotations = obds;
                    canvas.SelectAllObd();
                    canvas.SetViewState(new ImageCanvasControl.CanvasViewState(2d, new PointF(-30f, -20f), false));

                    AssertWhiteHandleCenters(canvas, new Rectangle(10, 20, 40, 40));
                    AssertWhiteHandleCenters(canvas, new Rectangle(90, 100, 40, 40));
                    AssertWhiteHandleCenters(canvas, new Rectangle(10, 100, 40, 40));
                    Assert.AreEqual(Color.White.ToArgb(), RenderPixel(canvas, 20, 20).ToArgb(), "The selected OBD bounding-box border must remain aligned after zooming and panning.");

                    canvas.ObdAnnotations = new ObdAnnotation[0];
                    canvas.ObdAnnotations = obds;
                    canvas.ObdEnabled = true;
                    canvas.ObdSelectMode = true;
                    canvas.ModifierKeysProvider = () => Keys.Control;
                    RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 30, 40, 0));
                    RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 110, 120, 0));
                    CollectionAssert.AreEquivalent(new[] { "one", "two" }, canvas.SelectedObdIds.ToArray());
                    AssertWhiteHandleCenters(canvas, new Rectangle(10, 20, 40, 40));
                    AssertWhiteHandleCenters(canvas, new Rectangle(90, 100, 40, 40));
                    AssertNoWhiteHandle(canvas, new Point(10, 100));
                    canvas.ObdAnnotations = new ObdAnnotation[0];
                    canvas.ObdEnabled = false;
                    canvas.SegClasses = new Dictionary<string, AnnotationClass> { { "class", new AnnotationClass("class", "set", "label", "#00FF00") } };
                    SegRegion[] regions =
                    {
                        new SegRegion("first", "set", "image", "class", SegRegionType.Rect, 20, 20, 20, 20, 0),
                        new SegRegion("second", "set", "image", "class", SegRegionType.Rect, 60, 60, 20, 20, 1),
                        new SegRegion("third", "set", "image", "class", SegRegionType.Rect, 20, 60, 20, 20, 2)
                    };
                    canvas.SegRegions = regions;
                    canvas.SelectAllSeg();

                    AssertWhiteHandleCenters(canvas, new Rectangle(10, 20, 40, 40));
                    AssertWhiteHandleCenters(canvas, new Rectangle(90, 100, 40, 40));
                    AssertWhiteHandleCenters(canvas, new Rectangle(10, 100, 40, 40));
                    Assert.AreEqual(Color.White.ToArgb(), RenderPixel(canvas, 20, 20).ToArgb(), "The selected SEG bounding-box border must remain aligned after zooming and panning.");

                    canvas.SegRegions = new SegRegion[0];
                    canvas.SegRegions = regions;
                    canvas.SegEnabled = true;
                    canvas.SegTool = ImageCanvasControl.SegToolKind.Select;
                    RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 30, 40, 0));
                    RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 110, 120, 0));
                    CollectionAssert.AreEquivalent(new[] { "first", "second" }, canvas.SelectedSegIds.ToArray());
                    AssertWhiteHandleCenters(canvas, new Rectangle(10, 20, 40, 40));
                    AssertWhiteHandleCenters(canvas, new Rectangle(90, 100, 40, 40));
                    AssertNoWhiteHandle(canvas, new Point(10, 100));
                }
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [STATestMethod]
        public void Dragging_a_handle_changes_only_the_selected_obd_or_seg_region_that_owns_it()
        {
            string directory = Path.Combine(Path.GetTempPath(), "Neurocle.Tests", Guid.NewGuid().ToString("N"));
            string imagePath = Path.Combine(directory, "source.png");
            Directory.CreateDirectory(directory);
            try
            {
                CreateSolidImage(imagePath, Color.Black, 100);
                using (var canvas = new ImageCanvasControl { Size = new Size(100, 100), ObdEnabled = true, ObdSelectMode = true })
                {
                    canvas.CreateControl();
                    canvas.LoadImage(imagePath);
                    WaitForImage(canvas, imagePath);
                    canvas.ObdAnnotations = new[]
                    {
                        new ObdAnnotation("one", "set", "image", "class", 10, 10, 30, 30, 0),
                        new ObdAnnotation("two", "set", "image", "class", 10, 10, 20, 20, 1)
                    };
                    canvas.SelectAllObd();
                    IReadOnlyDictionary<string, Rectangle> changedObd = null;
                    canvas.ObdRectanglesChanged += value => changedObd = value;

                    RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 10, 10, 0));
                    RaiseMouse(canvas, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, 5, 5, 0));
                    RaiseMouse(canvas, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 0, 5, 5, 0));

                    Assert.IsNotNull(changedObd);
                    CollectionAssert.AreEqual(new[] { "two" }, changedObd.Keys.ToArray());
                    Assert.AreEqual(new Rectangle(5, 5, 25, 25), changedObd["two"]);

                    changedObd = null;
                    RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 20, 20, 0));
                    RaiseMouse(canvas, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, 25, 25, 0));
                    RaiseMouse(canvas, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 0, 25, 25, 0));
                    Assert.IsNotNull(changedObd);
                    CollectionAssert.AreEquivalent(new[] { "one", "two" }, changedObd.Keys.ToArray(), "Dragging a selected OBD body must keep group movement.");

                    canvas.ObdEnabled = false;
                    canvas.SegEnabled = true;
                    canvas.SegTool = ImageCanvasControl.SegToolKind.Select;
                    canvas.SegRegions = new[]
                    {
                        new SegRegion("first", "set", "image", "class", SegRegionType.Rect, 10, 10, 30, 30, 0),
                        new SegRegion("second", "set", "image", "class", SegRegionType.Rect, 10, 10, 20, 20, 1)
                    };
                    canvas.SelectAllSeg();
                    IReadOnlyList<SegRegion> changedSeg = null;
                    canvas.SegRegionsChanged += value => changedSeg = value;

                    RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 10, 10, 0));
                    RaiseMouse(canvas, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, 5, 5, 0));
                    RaiseMouse(canvas, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 0, 5, 5, 0));

                    Assert.IsNotNull(changedSeg);
                    Assert.AreEqual(1, changedSeg.Count);
                    Assert.AreEqual("second", changedSeg[0].Id);
                    Assert.AreEqual(new Rectangle(5, 5, 25, 25), new Rectangle(changedSeg[0].X, changedSeg[0].Y, changedSeg[0].Width, changedSeg[0].Height));

                    changedSeg = null;
                    RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 20, 20, 0));
                    RaiseMouse(canvas, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, 25, 25, 0));
                    RaiseMouse(canvas, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 0, 25, 25, 0));
                    Assert.IsNotNull(changedSeg);
                    CollectionAssert.AreEquivalent(new[] { "first", "second" }, changedSeg.Select(region => region.Id).ToArray(), "Dragging a selected SEG body must keep group movement.");
                }
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [TestMethod]
        public void Seg_stroke_width_numeric_input_and_slider_stay_synchronized_from_one_to_one_hundred()
        {
            using (var form = new MainForm(new ProjectLifecycle()))
            {
                NumericUpDown width = Field<NumericUpDown>(form, "segStrokeWidth");
                TrackBar slider = Field<TrackBar>(form, "segStrokeWidthSlider");
                Assert.AreEqual(1, width.Minimum);
                Assert.AreEqual(100, width.Maximum);
                Assert.AreEqual(1, slider.Minimum);
                Assert.AreEqual(100, slider.Maximum);
                Assert.AreEqual(15, width.Value);
                Assert.AreEqual(15, slider.Value);
                Assert.AreEqual(15, form.AnnotationCanvas.SegStrokeWidth);
                foreach (decimal value in new[] { 1m, 37m, 100m })
                {
                    width.Value = value;
                    Assert.AreEqual((int)value, slider.Value);
                    Assert.AreEqual((int)value, form.AnnotationCanvas.SegStrokeWidth);
                }
                foreach (int value in new[] { 1, 63, 100 })
                {
                    slider.Value = value;
                    Assert.AreEqual(value, (int)width.Value);
                    Assert.AreEqual(value, form.AnnotationCanvas.SegStrokeWidth);
                }
                Assert.ThrowsException<ArgumentOutOfRangeException>(() => form.AnnotationCanvas.SegStrokeWidth = 0);
                Assert.ThrowsException<ArgumentOutOfRangeException>(() => form.AnnotationCanvas.SegStrokeWidth = 101);
            }
        }

        [TestMethod]
        public void Numeric_annotation_inputs_reject_non_numeric_text_and_constrain_out_of_range_values_without_diverging()
        {
            using (var form = new MainForm(new ProjectLifecycle()))
            {
                NumericUpDown width = Field<NumericUpDown>(form, "segStrokeWidth");
                TrackBar widthSlider = Field<TrackBar>(form, "segStrokeWidthSlider");
                NumericUpDown opacity = Field<NumericUpDown>(form, "annotationOpacityValue");
                TrackBar opacitySlider = Field<TrackBar>(form, "annotationOpacity");

                width.Value = 37;
                EnterNumericText(width, "abc");
                Assert.AreEqual(37, width.Value);
                Assert.AreEqual(37, widthSlider.Value);
                Assert.AreEqual(37, form.AnnotationCanvas.SegStrokeWidth);
                EnterNumericText(width, "101");
                Assert.AreEqual(100, width.Value);
                Assert.AreEqual(100, widthSlider.Value);
                Assert.AreEqual(100, form.AnnotationCanvas.SegStrokeWidth);
                EnterNumericText(width, "-5");
                Assert.AreEqual(1, width.Value);
                Assert.AreEqual(1, widthSlider.Value);
                Assert.AreEqual(1, form.AnnotationCanvas.SegStrokeWidth);

                opacity.Value = 37;
                EnterNumericText(opacity, "abc");
                Assert.AreEqual(37, opacity.Value);
                Assert.AreEqual(37, opacitySlider.Value);
                Assert.AreEqual(0.37f, form.AnnotationCanvas.ObdOpacity);
                Assert.AreEqual(0.37f, form.AnnotationCanvas.SegOpacity);
                EnterNumericText(opacity, "101");
                Assert.AreEqual(100, opacity.Value);
                Assert.AreEqual(100, opacitySlider.Value);
                Assert.AreEqual(1f, form.AnnotationCanvas.ObdOpacity);
                Assert.AreEqual(1f, form.AnnotationCanvas.SegOpacity);
                EnterNumericText(opacity, "-1");
                Assert.AreEqual(0, opacity.Value);
                Assert.AreEqual(0, opacitySlider.Value);
                Assert.AreEqual(0f, form.AnnotationCanvas.ObdOpacity);
                Assert.AreEqual(0f, form.AnnotationCanvas.SegOpacity);
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
        public void Canvas_tool_cursor_tracks_annotation_mode_and_scales_width_with_zoom()
        {
            string directory = Path.Combine(Path.GetTempPath(), "Neurocle.Tests", Guid.NewGuid().ToString("N"));
            string imagePath = Path.Combine(directory, "source.png");
            Directory.CreateDirectory(directory);
            try
            {
                using (var source = new Bitmap(100, 100))
                {
                    using (Graphics graphics = Graphics.FromImage(source)) graphics.Clear(Color.Black);
                    source.Save(imagePath, ImageFormat.Png);
                }
                using (var canvas = new ImageCanvasControl { Size = new Size(100, 100) })
                {
                    canvas.CreateControl();
                    canvas.LoadImage(imagePath);
                    WaitForImage(canvas, imagePath);
                    canvas.ObdEnabled = true;
                    canvas.ObdSelectMode = false;
                    RaiseMouse(canvas, "OnMouseMove", new MouseEventArgs(MouseButtons.None, 0, 50, 50, 0));
                    Assert.AreNotEqual(Cursors.Arrow, canvas.Cursor, "OBD rectangle mode should hide the system cursor and draw the small crosshair.");
                    Assert.AreNotEqual(Cursors.Cross.Handle, canvas.Cursor.Handle, "The system crosshair should not overlap the custom crosshair.");
                    Assert.AreEqual(Color.White.ToArgb(), RenderPixel(canvas, 44, 50).ToArgb(), "OBD rectangle mode should show a centered crosshair.");

                    canvas.ObdSelectMode = true;
                    Assert.AreEqual(Cursors.Arrow, canvas.Cursor);
                    canvas.ObdEnabled = false;
                    canvas.SegEnabled = true;
                    canvas.SegStrokeWidth = 12;
                    canvas.SegTool = ImageCanvasControl.SegToolKind.Brush;
                    RaiseMouse(canvas, "OnMouseMove", new MouseEventArgs(MouseButtons.None, 0, 50, 50, 0));
                    Assert.AreNotEqual(Cursors.Arrow, canvas.Cursor);
                    Assert.AreEqual(Color.White.ToArgb(), RenderPixel(canvas, 44, 50).ToArgb(), "Brush width should define the cursor ring diameter at 100% zoom.");

                    canvas.SetZoomPercent(200);
                    RaiseMouse(canvas, "OnMouseMove", new MouseEventArgs(MouseButtons.None, 0, 50, 50, 0));
                    Assert.AreEqual(Color.White.ToArgb(), RenderPixel(canvas, 38, 50).ToArgb(), "Cursor ring should scale with canvas zoom.");

                    foreach (ImageCanvasControl.SegToolKind tool in new[]
                    {
                        ImageCanvasControl.SegToolKind.Rect,
                        ImageCanvasControl.SegToolKind.Ellipse,
                        ImageCanvasControl.SegToolKind.Polygon
                    })
                    {
                        canvas.SegTool = tool;
                        Assert.AreNotEqual(Cursors.Arrow, canvas.Cursor, "{0} mode should hide the system cursor and draw a small crosshair.", tool);
                    }

                    canvas.SetZoomPercent(100);
                    foreach (ImageCanvasControl.SegToolKind tool in new[]
                    {
                        ImageCanvasControl.SegToolKind.Brush,
                        ImageCanvasControl.SegToolKind.Line,
                        ImageCanvasControl.SegToolKind.Eraser
                    })
                    {
                        canvas.SegTool = tool;
                        Assert.AreNotEqual(Cursors.Arrow, canvas.Cursor, "{0} mode should hide the system cursor so the ring and symbol remain clear.", tool);
                        RaiseMouse(canvas, "OnMouseMove", new MouseEventArgs(MouseButtons.None, 0, 50, 50, 0));
                        Assert.AreEqual(Color.White.ToArgb(), RenderPixel(canvas, 44, 50).ToArgb(), "{0} should show a ring with the selected width.", tool);
                        Assert.IsTrue(CountNonBlackPixels(canvas, new Rectangle(57, 30, 16, 16)) > 0, "{0} should show its + or − symbol.", tool);
                    }

                    canvas.SegTool = ImageCanvasControl.SegToolKind.Brush;
                    RaiseMouse(canvas, "OnMouseMove", new MouseEventArgs(MouseButtons.None, 0, 0, 50, 0));
                    Assert.AreEqual(Color.White.ToArgb(), RenderPixel(canvas, 0, 50).ToArgb(), "Brush cursor ring should reach the image edge after its center is clamped inward.");
                    canvas.SegStrokeWidth = 30;
                    Assert.AreEqual(Color.White.ToArgb(), RenderPixel(canvas, 0, 50).ToArgb(), "Changing width with the pointer stationary should recalculate the edge-clamped center.");

                    RaiseMouse(canvas, "OnMouseMove", new MouseEventArgs(MouseButtons.None, 0, 50, 50, 0));
                    Assert.AreEqual(Color.White.ToArgb(), RenderPixel(canvas, 35, 50).ToArgb());
                    canvas.LoadImage(imagePath);
                    WaitForImage(canvas, imagePath);
                    Assert.AreEqual(Color.Black.ToArgb(), RenderPixel(canvas, 35, 50).ToArgb(), "Switching images should clear the previous cursor position.");

                    canvas.SetZoomPercent(100);
                    canvas.SegTool = ImageCanvasControl.SegToolKind.Rect;
                    RaiseMouse(canvas, "OnMouseMove", new MouseEventArgs(MouseButtons.None, 0, 50, 50, 0));
                    Assert.AreEqual(Color.White.ToArgb(), RenderPixel(canvas, 44, 50).ToArgb());
                    RaiseEvent(canvas, "OnMouseLeave", EventArgs.Empty);
                    canvas.SetZoomPercent(125);
                    Assert.AreEqual(Color.Black.ToArgb(), RenderPixel(canvas, 44, 50).ToArgb(), "Zooming after pointer leave should not restore a stale cursor.");
                    RaiseMouse(canvas, "OnMouseMove", new MouseEventArgs(MouseButtons.None, 0, 50, 50, 0));
                    Assert.AreEqual(Color.White.ToArgb(), RenderPixel(canvas, 44, 50).ToArgb(), "Moving back over the canvas should restore its cursor.");

                    canvas.SegTool = ImageCanvasControl.SegToolKind.Select;
                    Assert.AreEqual(Cursors.Arrow, canvas.Cursor);
                }
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [STATestMethod]
        public void Annotation_and_eraser_previews_use_the_current_class_color_at_fixed_forty_percent()
        {
            string directory = Path.Combine(Path.GetTempPath(), "Neurocle.Tests", Guid.NewGuid().ToString("N"));
            string imagePath = Path.Combine(directory, "source.png");
            Directory.CreateDirectory(directory);
            try
            {
                using (var source = new Bitmap(100, 100))
                {
                    using (Graphics graphics = Graphics.FromImage(source)) graphics.Clear(Color.Black);
                    source.Save(imagePath, ImageFormat.Png);
                }
                using (var canvas = new ImageCanvasControl { Size = new Size(100, 100) })
                {
                    canvas.CreateControl();
                    canvas.LoadImage(imagePath);
                    WaitForImage(canvas, imagePath);
                    ImageCanvasControl.SegToolKind?[] tools =
                    {
                        null,
                        ImageCanvasControl.SegToolKind.Rect,
                        ImageCanvasControl.SegToolKind.Ellipse,
                        ImageCanvasControl.SegToolKind.Polygon,
                        ImageCanvasControl.SegToolKind.Brush,
                        ImageCanvasControl.SegToolKind.Line,
                        ImageCanvasControl.SegToolKind.Eraser
                    };
                    foreach (string storedColor in new[] { "rgba(248, 0, 255, 1)-custom", "#18A45B" })
                    {
                        Color classColor = AnnotationColorFormat.ToColor(storedColor);
                        canvas.PreviewColor = classColor;
                        foreach (ImageCanvasControl.SegToolKind? tool in tools)
                        {
                            Color? baseline = null;
                            foreach (float resultOpacity in new[] { 0f, 0.4f, 1f })
                            {
                                canvas.ObdOpacity = resultOpacity;
                                canvas.SegOpacity = resultOpacity;
                                Color rendered = RenderPreviewTrack(canvas, tool);
                                AssertFortyPercentBlend(rendered, classColor);
                                if (baseline.HasValue) Assert.AreEqual(baseline.Value.ToArgb(), rendered.ToArgb(), "Preview opacity must not follow result opacity for {0}.", tool.HasValue ? tool.Value.ToString() : "OBD");
                                else baseline = rendered;
                            }
                        }
                    }
                }
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [STATestMethod]
        public void Preview_color_tracks_the_class_used_by_the_current_annotation_tool()
        {
            string directory = Path.Combine(Path.GetTempPath(), "Neurocle.Tests", Guid.NewGuid().ToString("N"));
            string projectPath = Path.Combine(directory, "project.neurocle");
            ProjectStore store = null;
            Directory.CreateDirectory(directory);
            try
            {
                store = ProjectStore.Create(projectPath);
                AnnotationSet obd = store.CreateAnnotationSet("obd", AnnotationSetType.Obd);
                AnnotationSet seg = store.CreateAnnotationSet("seg", AnnotationSetType.Seg);
                AnnotationClass obdClass = store.GetClasses(obd.Id).Single();
                AnnotationClass custom = store.AddClass(seg.Id, "custom", "rgba(248, 0, 255, 1)-custom");
                using (var form = new MainForm(new ProjectLifecycle()))
                {
                    Invoke(form, "ReplaceProject", store);
                    ComboBox sets = Field<ComboBox>(form, "annotationSets");
                    sets.SelectedItem = AnnotationSet(sets, obd.Id);
                    Assert.AreEqual(AnnotationColorFormat.ToColor(obdClass.Color).ToArgb(), form.AnnotationCanvas.PreviewColor.ToArgb());

                    sets.SelectedItem = AnnotationSet(sets, seg.Id);
                    FlowLayoutPanel classes = Field<FlowLayoutPanel>(form, "annotationClasses");
                    Button customClass = classes.Controls.OfType<Button>().Single(button => button.Text == custom.Name);
                    typeof(Button).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(customClass, new object[] { EventArgs.Empty });
                    Assert.AreEqual(AnnotationColorFormat.ToColor(custom.Color).ToArgb(), form.AnnotationCanvas.PreviewColor.ToArgb());
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

        [STATestMethod]
        public void Counterclockwise_seg_brush_loop_preserves_existing_same_class_area_through_main_form()
        {
            string directory = Path.Combine(Path.GetTempPath(), "Neurocle.Tests", Guid.NewGuid().ToString("N"));
            string projectPath = Path.Combine(directory, "project.neurocle");
            string imagePath = Path.Combine(directory, "source.png");
            ProjectStore store = null;
            Directory.CreateDirectory(directory);
            try
            {
                CreateSolidImage(imagePath, Color.Red, 100);
                store = ProjectStore.Create(projectPath);
                ProjectImage image = store.ImportFiles(new[] { imagePath }).Single();
                AnnotationSet set = store.CreateAnnotationSet("regions", AnnotationSetType.Seg);
                AnnotationClass annotationClass = store.GetClasses(set.Id).Single();
                store.AddSegRegion(set.Id, image.Id, annotationClass.Id, SegRegionType.Rect, 5, 30, 30, 20);

                using (var form = new MainForm(new ProjectLifecycle()))
                {
                    Invoke(form, "ReplaceProject", store);
                    form.Size = new Size(1200, 800);
                    form.Show();
                    System.Windows.Forms.Application.DoEvents();
                    Invoke(form, "ApplyImageSelection", new ImageSelectedEventArgs(image, Path.Combine(store.ImagesDirectory, image.ManagedFileName)));
                    ComboBox sets = Field<ComboBox>(form, "annotationSets");
                    sets.SelectedItem = AnnotationSet(sets, set.Id);
                    form.Controls.OfType<TabControl>().Single().SelectedIndex = 1;
                    System.Windows.Forms.Application.DoEvents();
                    WaitForImage(form.ImageCanvas, Path.Combine(store.ImagesDirectory, image.ManagedFileName));
                    WaitForImage(form.AnnotationCanvas, Path.Combine(store.ImagesDirectory, image.ManagedFileName));
                    Field<Button>(form, "segBrush").PerformClick();

                    ImageCanvasControl canvas = form.AnnotationCanvas;
                    Point start = ImagePointToScreen(canvas, 10, 10);
                    RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, start.X, start.Y, 0));
                    foreach (Point point in new[]
                    {
                        ImagePointToScreen(canvas, 10, 90),
                        ImagePointToScreen(canvas, 90, 90),
                        ImagePointToScreen(canvas, 90, 10)
                    })
                        RaiseMouse(canvas, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, point.X, point.Y, 0));
                    RaiseMouse(canvas, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 0, start.X, start.Y, 0));

                    DateTime timeout = DateTime.UtcNow.AddSeconds(5);
                    while (Field<bool>(form, "annotationOperationInProgress") && DateTime.UtcNow < timeout)
                    {
                        System.Windows.Forms.Application.DoEvents();
                        Thread.Sleep(1);
                    }

                    Assert.IsFalse(Field<bool>(form, "annotationOperationInProgress"), "The SEG brush operation did not complete.");
                    SegRegion merged = store.GetSegRegions(set.Id, image.Id).Single(value => value.ClassId == annotationClass.Id);
                    var mergedPath = new Path64(merged.Points.Select(point => new Point64(point.X, point.Y)).ToList());
                    Paths64 preserved = Clipper.Intersect(new Paths64 { SegGeometryPrototype.Rectangle(5, 30, 35, 50) }, new Paths64 { mergedPath }, FillRule.NonZero);
                    Assert.AreEqual(600d, SegGeometryPrototype.Area(preserved));
                    Assert.AreEqual(6500d, SegGeometryPrototype.Area(new Paths64 { mergedPath }));
                    form.Close();
                    System.Windows.Forms.Application.DoEvents();
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

        private static void AssertPreviewStrokeThickness(ImageCanvasControl canvas, int x, int y, int expected)
        {
            using (var preview = new Bitmap(canvas.Width, canvas.Height))
            {
                canvas.DrawToBitmap(preview, canvas.ClientRectangle);
                int thickness = Enumerable.Range(0, preview.Height)
                    .Count(row =>
                    {
                        Color pixel = preview.GetPixel(x, row);
                        return pixel.R > 80 && pixel.G > 80 && pixel.B > 80;
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

        [STATestMethod]
        public void Active_tool_and_class_buttons_reflect_annotation_state_across_images_and_sets()
        {
            string directory = Path.Combine(Path.GetTempPath(), "Neurocle.Tests", Guid.NewGuid().ToString("N"));
            string projectPath = Path.Combine(directory, "project.neurocle");
            ProjectStore store = null;
            Directory.CreateDirectory(directory);
            try
            {
                string firstImagePath = Path.Combine(directory, "one.png");
                string secondImagePath = Path.Combine(directory, "two.png");
                CreateSolidImage(firstImagePath, Color.Red);
                CreateSolidImage(secondImagePath, Color.Blue);
                store = ProjectStore.Create(projectPath);
                ProjectImage[] images = store.ImportFiles(new[] { firstImagePath, secondImagePath }).ToArray();
                AnnotationSet firstObd = store.CreateAnnotationSet("first boxes", AnnotationSetType.Obd);
                AnnotationClass firstObdClass = store.GetClasses(firstObd.Id).Single();
                AnnotationClass secondObdClass = store.AddClass(firstObd.Id, "other box", "#ff00ff");
                AnnotationSet secondObd = store.CreateAnnotationSet("second boxes", AnnotationSetType.Obd);
                AnnotationClass secondObdDefault = store.GetClasses(secondObd.Id).Single();
                AnnotationSet seg = store.CreateAnnotationSet("regions", AnnotationSetType.Seg);
                AnnotationClass segDefault = store.GetClasses(seg.Id).Single();
                AnnotationClass segOther = store.AddClass(seg.Id, "other region", "#00ff00");
                AnnotationSet cla = store.CreateAnnotationSet("image classes", AnnotationSetType.Cla);
                AnnotationClass claDefault = store.GetClasses(cla.Id).Single();
                AnnotationClass claOther = store.AddClass(cla.Id, "other image class", "#0000ff");

                using (var form = new MainForm(new ProjectLifecycle()))
                {
                    Invoke(form, "ReplaceProject", store);
                    form.Show();
                    System.Windows.Forms.Application.DoEvents();
                    form.Controls.OfType<TabControl>().Single().SelectedIndex = 1;
                    System.Windows.Forms.Application.DoEvents();
                    ComboBox sets = Field<ComboBox>(form, "annotationSets");
                    FlowLayoutPanel tools = Field<FlowLayoutPanel>(form, "annotationTools");
                    FlowLayoutPanel classes = Field<FlowLayoutPanel>(form, "annotationClasses");
                    Invoke(form, "ApplyImageSelection", new ImageSelectedEventArgs(images[0], Path.Combine(store.ImagesDirectory, images[0].ManagedFileName)));

                    sets.SelectedItem = AnnotationSet(sets, firstObd.Id);
                    AssertToolButtonIsActive(tools.Controls.OfType<Button>().Single(button => button.Name == "obdSelect"));
                    Button secondObdButton = classes.Controls.OfType<Button>().Single(button => button.Text == secondObdClass.Name);
                    secondObdButton.PerformClick();
                    Assert.AreEqual(secondObdClass.Id, Field<string>(form, "selectedObdClassId"));
                    secondObdButton = classes.Controls.OfType<Button>().Single(button => button.Text == secondObdClass.Name);
                    AssertClassButtonIsActive(secondObdButton, AnnotationColorFormat.ToColor(secondObdClass.Color));

                    Button rectangle = tools.Controls.OfType<Button>().Single(button => button.Name == "obdRectangle");
                    rectangle.PerformClick();
                    AssertToolButtonIsActive(rectangle);
                    AssertToolButtonIsInactive(tools.Controls.OfType<Button>().Single(button => button.Name == "obdSelect"));
                    Invoke(form, "CreateObdRectangle", new Rectangle(1, 1, 5, 5));
                    ObdAnnotation created = store.GetObdAnnotations(firstObd.Id, images[0].Id).Single();
                    Assert.AreEqual(secondObdClass.Id, created.ClassId);

                    form.AnnotationCanvas.SelectAllObd();
                    Button firstObdButton = classes.Controls.OfType<Button>().Single(button => button.Text == firstObdClass.Name);
                    firstObdButton.PerformClick();
                    Assert.AreEqual(firstObdClass.Id, store.GetObdAnnotations(firstObd.Id, images[0].Id).Single().ClassId);
                    Assert.AreEqual(firstObdClass.Id, Field<string>(form, "selectedObdClassId"));
                    firstObdButton = classes.Controls.OfType<Button>().Single(button => button.Text == firstObdClass.Name);
                    AssertClassButtonIsActive(firstObdButton, AnnotationColorFormat.ToColor(firstObdClass.Color));

                    Invoke(form, "ApplyImageSelection", new ImageSelectedEventArgs(images[1], Path.Combine(store.ImagesDirectory, images[1].ManagedFileName)));
                    Assert.AreEqual(firstObdClass.Id, Field<string>(form, "selectedObdClassId"));
                    sets.SelectedItem = AnnotationSet(sets, secondObd.Id);
                    Assert.AreEqual(secondObdDefault.Id, Field<string>(form, "selectedObdClassId"));
                    AssertClassButtonIsActive(classes.Controls.OfType<Button>().Single(button => button.Text == secondObdDefault.Name), AnnotationColorFormat.ToColor(secondObdDefault.Color));

                    sets.SelectedItem = AnnotationSet(sets, seg.Id);
                    Button brush = tools.Controls.OfType<Button>().Single(button => button.Name == "segBrush");
                    foreach (Button tool in tools.Controls.OfType<Button>().ToArray())
                    {
                        tool.PerformClick();
                        foreach (Button candidate in tools.Controls.OfType<Button>())
                        {
                            if (ReferenceEquals(candidate, tool)) AssertToolButtonIsActive(candidate);
                            else AssertToolButtonIsInactive(candidate);
                        }
                    }
                    brush = tools.Controls.OfType<Button>().Single(button => button.Name == "segBrush");
                    brush.PerformClick();
                    Button segOtherButton = classes.Controls.OfType<Button>().Single(button => button.Text == segOther.Name);
                    segOtherButton.PerformClick();
                    Assert.AreEqual(segOther.Id, Field<string>(form, "selectedSegClassId"));
                    Assert.AreEqual(AnnotationColorFormat.ToColor(segOther.Color).ToArgb(), form.AnnotationCanvas.PreviewColor.ToArgb());
                    Invoke(form, "ApplyImageSelection", new ImageSelectedEventArgs(images[1], Path.Combine(store.ImagesDirectory, images[1].ManagedFileName)));
                    Assert.AreEqual(segOther.Id, Field<string>(form, "selectedSegClassId"));
                    Task createSeg = (Task)form.GetType().GetMethod("CreateSegRegionAsync", BindingFlags.Instance | BindingFlags.NonPublic)
                        .Invoke(form, new object[] { SegRegionType.Rect, new Rectangle(2, 2, 4, 4) });
                    WaitForTask(createSeg);
                    Assert.AreEqual(segOther.Id, store.GetSegRegions(seg.Id, images[1].Id).Single().ClassId);
                    sets.SelectedItem = AnnotationSet(sets, firstObd.Id);
                    sets.SelectedItem = AnnotationSet(sets, seg.Id);
                    Assert.AreEqual(segDefault.Id, Field<string>(form, "selectedSegClassId"));
                    AssertClassButtonIsActive(classes.Controls.OfType<Button>().Single(button => button.Text == segDefault.Name), AnnotationColorFormat.ToColor(segDefault.Color));

                    sets.SelectedItem = AnnotationSet(sets, cla.Id);
                    Button claOtherButton = classes.Controls.OfType<Button>().Single(button => button.Text == claOther.Name);
                    claOtherButton.PerformClick();
                    Assert.AreEqual(claOther.Id, store.GetClaClassId(cla.Id, images[1].Id));
                    claOtherButton = classes.Controls.OfType<Button>().Single(button => button.Text == claOther.Name);
                    AssertClassButtonIsActive(claOtherButton, AnnotationColorFormat.ToColor(claOther.Color));
                    AssertClassButtonIsInactive(classes.Controls.OfType<Button>().Single(button => button.Text == claDefault.Name));
                    Invoke(form, "ApplyImageSelection", new ImageSelectedEventArgs(images[0], Path.Combine(store.ImagesDirectory, images[0].ManagedFileName)));
                    Assert.IsFalse(classes.Controls.OfType<Button>().Any(button => button.FlatAppearance.BorderSize > 1));
                    Invoke(form, "ApplyImageSelection", new ImageSelectedEventArgs(images[1], Path.Combine(store.ImagesDirectory, images[1].ManagedFileName)));
                    AssertClassButtonIsActive(classes.Controls.OfType<Button>().Single(button => button.Text == claOther.Name), AnnotationColorFormat.ToColor(claOther.Color));
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
                    NumericUpDown opacityValue = Field<NumericUpDown>(form, "annotationOpacityValue");
                    Assert.AreEqual(0, opacityValue.Minimum);
                    Assert.AreEqual(100, opacityValue.Maximum);
                    Assert.AreEqual(40, opacityValue.Value);
                    sets.SelectedItem = AnnotationSet(sets, obd.Id);
                    opacity.Value = 0;
                    Assert.AreEqual(0, opacityValue.Value);
                    Assert.AreEqual(0f, form.AnnotationCanvas.ObdOpacity);
                    Assert.AreEqual(0f, form.AnnotationCanvas.SegOpacity);

                    opacityValue.Value = 73;
                    Assert.AreEqual(73, opacity.Value);
                    Assert.AreEqual(0.73f, form.AnnotationCanvas.ObdOpacity);
                    Assert.AreEqual(0.73f, form.AnnotationCanvas.SegOpacity);

                    opacity.Value = 100;
                    Assert.AreEqual(100, opacityValue.Value);
                    Assert.AreEqual(1f, form.AnnotationCanvas.ObdOpacity);
                    Assert.AreEqual(1f, form.AnnotationCanvas.SegOpacity);

                    Invoke(form, "RefreshAnnotationSets", new object[] { null });
                    Assert.AreEqual(100, opacity.Value, "Refreshing the current annotation set must not reset opacity.");
                    Assert.AreEqual(100, opacityValue.Value);

                    SetField(form.AnnotationCanvas, "segLineStart", new PointF(10, 10));
                    Invoke(form, "RefreshAnnotationSets", new object[] { seg.Id });
                    Assert.IsNull(form.AnnotationCanvas.GetType().GetField("segLineStart", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form.AnnotationCanvas), "A programmatic annotation-set switch must discard unfinished SEG shapes.");
                    Assert.AreEqual(40, opacity.Value);
                    Assert.AreEqual(40, opacityValue.Value);
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

        private static int CountNonBlackPixels(ImageCanvasControl canvas, Rectangle bounds)
        {
            using (var rendered = new Bitmap(canvas.Width, canvas.Height))
            {
                canvas.DrawToBitmap(rendered, canvas.ClientRectangle);
                int count = 0;
                for (int x = bounds.Left; x < bounds.Right; x++)
                    for (int y = bounds.Top; y < bounds.Bottom; y++)
                        if (rendered.GetPixel(x, y).ToArgb() != Color.Black.ToArgb()) count++;
                return count;
            }
        }

        private static Color RenderPreviewTrack(ImageCanvasControl canvas, ImageCanvasControl.SegToolKind? tool)
        {
            canvas.DiscardUnfinishedShape();
            canvas.ObdEnabled = !tool.HasValue;
            canvas.ObdSelectMode = false;
            canvas.SegEnabled = tool.HasValue;
            canvas.SegTool = tool;
            if (!tool.HasValue || tool == ImageCanvasControl.SegToolKind.Rect || tool == ImageCanvasControl.SegToolKind.Ellipse)
            {
                RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 20, 20, 0));
                RaiseMouse(canvas, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, 60, 60, 0));
                return RenderPixel(canvas, 40, 20);
            }
            if (tool == ImageCanvasControl.SegToolKind.Polygon)
            {
                RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 20, 20, 0));
                RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 60, 20, 0));
                RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 60, 60, 0));
                return RenderPixel(canvas, 40, 20);
            }
            RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 20, 20, 0));
            RaiseMouse(canvas, "OnMouseMove", new MouseEventArgs(tool == ImageCanvasControl.SegToolKind.Line ? MouseButtons.None : MouseButtons.Left, 0, 60, 20, 0));
            return RenderPixel(canvas, 40, 20);
        }

        private static void AssertFortyPercentBlend(Color rendered, Color classColor)
        {
            Assert.IsTrue(Math.Abs(rendered.R - (int)Math.Round(classColor.R * 0.4f)) <= 2, "Unexpected preview red channel: {0} for class {1}.", rendered, classColor);
            Assert.IsTrue(Math.Abs(rendered.G - (int)Math.Round(classColor.G * 0.4f)) <= 2, "Unexpected preview green channel: {0} for class {1}.", rendered, classColor);
            Assert.IsTrue(Math.Abs(rendered.B - (int)Math.Round(classColor.B * 0.4f)) <= 2, "Unexpected preview blue channel: {0} for class {1}.", rendered, classColor);
        }

        private static void AssertWhiteHandleCenters(ImageCanvasControl canvas, Rectangle bounds)
        {
            Point[] centers =
            {
                new Point(bounds.Left, bounds.Top),
                new Point(bounds.Left + bounds.Width / 2, bounds.Top),
                new Point(bounds.Right, bounds.Top),
                new Point(bounds.Right, bounds.Top + bounds.Height / 2),
                new Point(bounds.Right, bounds.Bottom),
                new Point(bounds.Left + bounds.Width / 2, bounds.Bottom),
                new Point(bounds.Left, bounds.Bottom),
                new Point(bounds.Left, bounds.Top + bounds.Height / 2)
            };
            using (var rendered = new Bitmap(canvas.Width, canvas.Height))
            {
                canvas.DrawToBitmap(rendered, canvas.ClientRectangle);
                foreach (Point center in centers)
                {
                    int whitePixels = Enumerable.Range(center.X - 2, 5).Sum(x => Enumerable.Range(center.Y - 2, 5)
                        .Count(y => rendered.GetPixel(x, y).ToArgb() == Color.White.ToArgb()));
                    Assert.IsTrue(whitePixels >= 12, "Expected a visible selection handle at {0}.", center);
                }
            }
        }

        private static void AssertNoWhiteHandle(ImageCanvasControl canvas, Point center)
        {
            using (var rendered = new Bitmap(canvas.Width, canvas.Height))
            {
                canvas.DrawToBitmap(rendered, canvas.ClientRectangle);
                int whitePixels = Enumerable.Range(center.X - 2, 5).Sum(x => Enumerable.Range(center.Y - 2, 5)
                    .Count(y => rendered.GetPixel(x, y).ToArgb() == Color.White.ToArgb()));
                Assert.AreEqual(0, whitePixels, "Deselected annotations must not show selection handles.");
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

        private static void AssertClassButtonIsActive(Button button, Color classColor)
        {
            Assert.AreEqual(classColor.ToArgb(), button.BackColor.ToArgb(), "The active indicator must preserve the class color.");
            Assert.AreEqual(3, button.FlatAppearance.BorderSize);
            Assert.AreEqual(SystemColors.Highlight.ToArgb(), button.FlatAppearance.BorderColor.ToArgb());
        }

        private static void AssertClassButtonIsInactive(Button button)
        {
            Assert.AreEqual(1, button.FlatAppearance.BorderSize);
            Assert.AreNotEqual(3, button.FlatAppearance.BorderSize);
        }

        private static void AssertToolButtonIsActive(Button button)
        {
            Assert.AreEqual(SystemColors.GradientActiveCaption.ToArgb(), button.BackColor.ToArgb());
            Assert.AreEqual(2, button.FlatAppearance.BorderSize);
            Assert.AreEqual(SystemColors.Highlight.ToArgb(), button.FlatAppearance.BorderColor.ToArgb());
        }

        private static void AssertToolButtonIsInactive(Button button)
        {
            Assert.AreEqual(SystemColors.Control.ToArgb(), button.BackColor.ToArgb());
            Assert.AreEqual(1, button.FlatAppearance.BorderSize);
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

        private static void RaiseEvent(Control control, string methodName, EventArgs args)
        {
            typeof(ImageCanvasControl).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(control, new object[] { args });
        }

        private static void RaiseKey(Control control, KeyEventArgs args)
        {
            typeof(Control).GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(control, new object[] { args });
        }

        private static Point ImagePointToScreen(ImageCanvasControl canvas, int x, int y)
        {
            ImageCanvasControl.CanvasViewState state = canvas.ViewState;
            return Point.Round(new PointF((float)(state.Origin.X + x * state.Zoom), (float)(state.Origin.Y + y * state.Zoom)));
        }


        private static void Invoke(object target, string methodName, params object[] arguments)
        {
            MethodInfo method = target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
                .Single(candidate => candidate.Name == methodName && candidate.GetParameters().Length == arguments.Length);
            method.Invoke(target, arguments);
        }

        private static T Field<T>(object target, string fieldName)
        {
            return (T)target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
        }

        private static void SetField(object target, string fieldName, object value)
        {
            target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
        }

        private static void EnterNumericText(NumericUpDown control, string text)
        {
            control.Controls.Cast<Control>().Single(value => value.GetType().Name == "UpDownEdit").Text = text;
            typeof(NumericUpDown).GetMethod("ValidateEditText", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(control, null);
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

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
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
        public void Obd_and_seg_selection_marquees_have_black_dashes_and_leave_the_image_unfilled()
        {
            string directory = Path.Combine(Path.GetTempPath(), "Neurocle.Tests", Guid.NewGuid().ToString("N"));
            string imagePath = Path.Combine(directory, "source.png");
            Directory.CreateDirectory(directory);
            try
            {
                using (var image = new Bitmap(100, 100))
                {
                    using (Graphics graphics = Graphics.FromImage(image)) graphics.Clear(Color.White);
                    image.Save(imagePath, ImageFormat.Png);
                }
                foreach (bool segmentation in new[] { false, true })
                using (var canvas = new ImageCanvasControl
                {
                    Size = new Size(100, 100), ObdEnabled = !segmentation, ObdSelectMode = true,
                    SegEnabled = segmentation, SegTool = ImageCanvasControl.SegToolKind.Select
                })
                {
                    canvas.CreateControl();
                    canvas.LoadImage(imagePath);
                    WaitForImage(canvas, imagePath);
                    canvas.ModifierKeysProvider = () => Keys.None;
                    RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 20, 20, 0));
                    RaiseMouse(canvas, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, 80, 80, 0));
                    using (Bitmap preview = RenderCanvas(canvas))
                    {
                        Assert.AreEqual(Color.White.ToArgb(), preview.GetPixel(50, 50).ToArgb(), "The marquee must not tint the image.");
                        int black = 0, white = 0, thickDashes = 0;
                        for (int x = 25; x < 75; x++)
                        {
                            Color pixel = preview.GetPixel(x, 20);
                            if (pixel.R < 40 && pixel.G < 40 && pixel.B < 40) black++;
                            if (pixel.R < 40 && preview.GetPixel(x, 19).R < 40) thickDashes++;
                            if (pixel.R > 240 && pixel.G > 240 && pixel.B > 240) white++;
                        }
                        Assert.IsTrue(black > 5 && white > 5, "The edge must contain visible black dashes and gaps.");
                        Assert.IsTrue(thickDashes > 5, "Black dashes must span two adjacent pixel rows.");
                    }
                    RaiseMouse(canvas, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 0, 80, 80, 0));
                }
            }
            finally { Directory.Delete(directory, true); }
        }

        [TestMethod]
        public void Canvas_thumbnail_layout_maps_visible_image_area_and_scales_for_dpi()
        {
            ImageCanvasControl.CanvasThumbnailLayout layout;
            Assert.IsFalse(ImageCanvasControl.CanvasThumbnailLayout.TryCreate(new Size(100, 50), new Size(200, 100), 1d, new PointF(50, 25), 1f, out layout));

            Assert.IsTrue(ImageCanvasControl.CanvasThumbnailLayout.TryCreate(new Size(400, 100), new Size(200, 100), 1d, new PointF(-100, 0), 1f, out layout));
            AssertRectangle(new RectangleF(12, 8, 180, 45), layout.ImageBounds);
            AssertRectangle(new RectangleF(57, 8, 90, 45), layout.ViewportBounds);
            Assert.IsTrue(layout.ImageBounds.Contains(layout.ViewportBounds));

            Assert.IsTrue(ImageCanvasControl.CanvasThumbnailLayout.TryCreate(new Size(100, 400), new Size(200, 200), 1d, new PointF(0, -100), 1f, out layout));
            AssertRectangle(new RectangleF(162, 8, 30, 120), layout.ImageBounds);
            AssertRectangle(new RectangleF(162, 38, 30, 60), layout.ViewportBounds);

            Assert.IsTrue(ImageCanvasControl.CanvasThumbnailLayout.TryCreate(new Size(1000, 500), new Size(500, 400), 1d, new PointF(-250, -50), 1.5f, out layout));
            Assert.AreEqual(270f, layout.ImageBounds.Width, 0.01f);
            Assert.AreEqual(135f, layout.ImageBounds.Height, 0.01f);
            Assert.AreEqual(135f, layout.ViewportBounds.Width, 0.01f);
            Assert.AreEqual(108f, layout.ViewportBounds.Height, 0.01f);

            Assert.IsTrue(ImageCanvasControl.CanvasThumbnailLayout.TryCreate(new Size(1000, 500), new Size(900, 700), 1d, new PointF(-50, -20), 2f, out layout));
            Assert.AreEqual(360f, layout.ImageBounds.Width, 0.01f);
            Assert.AreEqual(180f, layout.ImageBounds.Height, 0.01f);
        }

        [STATestMethod]
        public void Canvas_thumbnail_is_rendered_only_when_zoomed_image_exceeds_viewport()
        {
            string directory = Path.Combine(Path.GetTempPath(), "Neurocle.Tests", Guid.NewGuid().ToString("N"));
            string imagePath = Path.Combine(directory, "source.png");
            Directory.CreateDirectory(directory);
            try
            {
                using (var source = new Bitmap(500, 300))
                {
                    using (Graphics graphics = Graphics.FromImage(source)) graphics.Clear(Color.LimeGreen);
                    source.Save(imagePath, ImageFormat.Png);
                }
                using (var canvas = new ImageCanvasControl { Size = new Size(400, 200) })
                {
                    canvas.CreateControl();
                    canvas.LoadImage(imagePath);
                    WaitForImage(canvas, imagePath);
                    canvas.FitToWindow();
                    using (Bitmap fitted = RenderCanvas(canvas))
                    {
                        Assert.AreEqual(Color.FromArgb(45, 45, 48).ToArgb(), fitted.GetPixel(395, 5).ToArgb());
                    }
                    canvas.SetZoomPercent(100d);
                    ImageCanvasControl.CanvasThumbnailLayout layout;
                    Assert.IsTrue(ImageCanvasControl.CanvasThumbnailLayout.TryCreate(new Size(500, 300), canvas.ClientSize, canvas.Zoom, canvas.ViewState.Origin, canvas.DeviceDpi / 96f, out layout));
                    using (Bitmap zoomed = RenderCanvas(canvas))
                    {
                        int borderX = (int)Math.Round(layout.ImageBounds.Left + layout.ImageBounds.Width / 2f);
                        int borderY = (int)Math.Round(layout.ImageBounds.Top);
                        Color border = zoomed.GetPixel(borderX, borderY);
                        Assert.IsTrue(border.R > 200 && border.G > 200 && border.B > 200, "The thumbnail must have a visible border.");
                        int thumbnailX = (int)Math.Round(layout.ImageBounds.Left + 2f);
                        int thumbnailY = (int)Math.Round(layout.ImageBounds.Top + 2f);
                        Assert.AreEqual(Color.LimeGreen.ToArgb(), zoomed.GetPixel(thumbnailX, thumbnailY).ToArgb());
                    }
                }
            }
            finally { Directory.Delete(directory, true); }
        }

        [STATestMethod]
        public void Canvas_thumbnail_click_and_drag_reposition_view_without_creating_annotations()
        {
            string directory = Path.Combine(Path.GetTempPath(), "Neurocle.Tests", Guid.NewGuid().ToString("N"));
            string imagePath = Path.Combine(directory, "source.png");
            Directory.CreateDirectory(directory);
            try
            {
                CreateFilledImage(imagePath, Color.LimeGreen, 1000);
                using (var canvas = new ImageCanvasControl { Size = new Size(400, 300), SegEnabled = true, SegTool = ImageCanvasControl.SegToolKind.Rect })
                {
                    canvas.CreateControl();
                    canvas.LoadImage(imagePath);
                    WaitForImage(canvas, imagePath);
                    canvas.SetZoomPercent(100d);
                    int created = 0;
                    canvas.SegRegionCreated += (type, bounds) => created++;
                    ImageCanvasControl.CanvasThumbnailLayout layout;
                    Assert.IsTrue(ImageCanvasControl.CanvasThumbnailLayout.TryCreate(new Size(1000, 1000), canvas.ClientSize, canvas.Zoom, canvas.ViewState.Origin, canvas.DeviceDpi / 96f, out layout));

                    Point click = new Point((int)(layout.ImageBounds.Left + layout.ImageBounds.Width * 0.7f), (int)(layout.ImageBounds.Top + layout.ImageBounds.Height * 0.55f));
                    PointF target = new PointF((click.X - layout.ImageBounds.Left) / layout.ImageBounds.Width * 1000f, (click.Y - layout.ImageBounds.Top) / layout.ImageBounds.Height * 1000f);
                    RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, click.X, click.Y, 0));
                    RaiseMouse(canvas, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 0, click.X, click.Y, 0));
                    PointF origin = canvas.ViewState.Origin;
                    Assert.AreEqual(target.X, (canvas.ClientSize.Width / 2f - origin.X) / (float)canvas.Zoom, 2f);
                    Assert.AreEqual(target.Y, (canvas.ClientSize.Height / 2f - origin.Y) / (float)canvas.Zoom, 2f);

                    Assert.IsTrue(ImageCanvasControl.CanvasThumbnailLayout.TryCreate(new Size(1000, 1000), canvas.ClientSize, canvas.Zoom, canvas.ViewState.Origin, canvas.DeviceDpi / 96f, out layout));
                    Point dragStart = Point.Round(new PointF(layout.ViewportBounds.Left + layout.ViewportBounds.Width / 2f, layout.ViewportBounds.Top + layout.ViewportBounds.Height / 2f));
                    Point dragEnd = new Point(dragStart.X + 10, dragStart.Y + 8);
                    PointF beforeDrag = canvas.ViewState.Origin;
                    float thumbnailScale = layout.ImageBounds.Width / 1000f;
                    RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, dragStart.X, dragStart.Y, 0));
                    RaiseMouse(canvas, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, dragEnd.X, dragEnd.Y, 0));
                    Assert.AreEqual(beforeDrag.X - 10f / thumbnailScale * (float)canvas.Zoom, canvas.ViewState.Origin.X, 2f);
                    Assert.AreEqual(beforeDrag.Y - 8f / thumbnailScale * (float)canvas.Zoom, canvas.ViewState.Origin.Y, 2f);
                    RaiseMouse(canvas, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 0, dragEnd.X, dragEnd.Y, 0));

                    Assert.IsTrue(ImageCanvasControl.CanvasThumbnailLayout.TryCreate(new Size(1000, 1000), canvas.ClientSize, canvas.Zoom, new PointF(0, 0), canvas.DeviceDpi / 96f, out layout));
                    Point edge = new Point((int)layout.ImageBounds.Left + 1, (int)layout.ImageBounds.Top + 1);
                    RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, edge.X, edge.Y, 0));
                    RaiseMouse(canvas, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 0, edge.X, edge.Y, 0));
                    Assert.IsTrue(canvas.ViewState.Origin.X <= 0f && canvas.ViewState.Origin.X >= canvas.ClientSize.Width - 1000f * (float)canvas.Zoom - 1f);
                    Assert.IsTrue(canvas.ViewState.Origin.Y <= 0f && canvas.ViewState.Origin.Y >= canvas.ClientSize.Height - 1000f * (float)canvas.Zoom - 1f);

                    Assert.IsTrue(ImageCanvasControl.CanvasThumbnailLayout.TryCreate(new Size(1000, 1000), canvas.ClientSize, canvas.Zoom, canvas.ViewState.Origin, canvas.DeviceDpi / 96f, out layout));
                    Point cancelDrag = Point.Round(new PointF(layout.ViewportBounds.Left + layout.ViewportBounds.Width / 2f, layout.ViewportBounds.Top + layout.ViewportBounds.Height / 2f));
                    RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, cancelDrag.X, cancelDrag.Y, 0));
                    canvas.DiscardUnfinishedShape();
                    RaiseMouse(canvas, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, cancelDrag.X + 5, cancelDrag.Y + 5, 0));
                    Point annotationStart = new Point(40, 40);
                    Point annotationEnd = new Point(100, 100);
                    RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, annotationStart.X, annotationStart.Y, 0));
                    RaiseMouse(canvas, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, annotationEnd.X, annotationEnd.Y, 0));
                    RaiseMouse(canvas, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 0, annotationEnd.X, annotationEnd.Y, 0));
                    Assert.AreEqual(1, created, "Canceling a thumbnail drag must allow later annotation gestures.");
                }
            }
            finally { Directory.Delete(directory, true); }
        }

        [STATestMethod]
        public void Canvas_thumbnail_collapse_persists_for_the_session_and_toggle_hides_when_image_fits()
        {
            string directory = Path.Combine(Path.GetTempPath(), "Neurocle.Tests", Guid.NewGuid().ToString("N"));
            string firstImagePath = Path.Combine(directory, "first.png");
            string secondImagePath = Path.Combine(directory, "second.png");
            Directory.CreateDirectory(directory);
            try
            {
                CreateFilledImage(firstImagePath, Color.LimeGreen, 1000);
                CreateFilledImage(secondImagePath, Color.Blue, 1000);
                using (var canvas = new ImageCanvasControl { Size = new Size(400, 300) })
                {
                    canvas.CreateControl();
                    Assert.IsFalse(canvas.IsThumbnailCollapsed, "A fresh canvas should start with the thumbnail expanded.");
                    canvas.LoadImage(firstImagePath);
                    WaitForImage(canvas, firstImagePath);
                    canvas.SetZoomPercent(100d);
                    ImageCanvasControl.CanvasThumbnailLayout layout;
                    Assert.IsTrue(ImageCanvasControl.CanvasThumbnailLayout.TryCreate(new Size(1000, 1000), canvas.ClientSize, canvas.Zoom, canvas.ViewState.Origin, canvas.DeviceDpi / 96f, out layout));
                    Point toggle = Point.Round(new PointF(layout.ToggleBounds.Left + layout.ToggleBounds.Width / 2f, layout.ToggleBounds.Top + layout.ToggleBounds.Height / 2f));
                    RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, toggle.X, toggle.Y, 0));
                    RaiseMouse(canvas, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 0, toggle.X, toggle.Y, 0));
                    Assert.IsTrue(canvas.IsThumbnailCollapsed);
                    using (Bitmap collapsed = RenderCanvas(canvas))
                    {
                        int x = (int)(layout.ImageBounds.Left + layout.ImageBounds.Width / 2f);
                        int y = (int)(layout.ImageBounds.Top + layout.ImageBounds.Height / 2f);
                        Assert.AreEqual(Color.LimeGreen.ToArgb(), collapsed.GetPixel(x, y).ToArgb(), "The collapsed thumbnail image should be hidden.");
                    }

                    canvas.LoadImage(secondImagePath);
                    WaitForImage(canvas, secondImagePath);
                    Assert.IsTrue(canvas.IsThumbnailCollapsed, "Changing images must retain the collapsed state for this session.");
                    canvas.SetZoomPercent(100d);
                    Assert.IsTrue(ImageCanvasControl.CanvasThumbnailLayout.TryCreate(new Size(1000, 1000), canvas.ClientSize, canvas.Zoom, canvas.ViewState.Origin, canvas.DeviceDpi / 96f, out layout));
                    toggle = Point.Round(new PointF(layout.ToggleBounds.Left + layout.ToggleBounds.Width / 2f, layout.ToggleBounds.Top + layout.ToggleBounds.Height / 2f));
                    RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, toggle.X, toggle.Y, 0));
                    RaiseMouse(canvas, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 0, toggle.X, toggle.Y, 0));
                    Assert.IsFalse(canvas.IsThumbnailCollapsed);

                    canvas.FitToWindow();
                    Assert.IsFalse(ImageCanvasControl.CanvasThumbnailLayout.TryCreate(new Size(1000, 1000), canvas.ClientSize, canvas.Zoom, canvas.ViewState.Origin, canvas.DeviceDpi / 96f, out layout), "The thumbnail and its toggle must disappear when the entire image fits.");
                }
                using (var restartedCanvas = new ImageCanvasControl()) Assert.IsFalse(restartedCanvas.IsThumbnailCollapsed, "A new canvas starts expanded after restart.");
            }
            finally { Directory.Delete(directory, true); }
        }

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
        public void Import_queue_designer_uses_an_explicit_column_header_array()
        {
            string designerPath = Path.Combine(FindRepositoryRoot(), "src", "Neurocle.WinForms", "ImportQueueForm.Designer.cs");
            string source = File.ReadAllText(designerPath);

            Assert.IsFalse(source.Contains("Columns.AddRange(new[]"), "Visual Studio Designer cannot parse an implicit array inside InitializeComponent.");
        }

        [STATestMethod]
        public void Auxiliary_form_designer_controls_load_without_runtime_dependencies()
        {
            string directory = Path.Combine(Path.GetTempPath(), "Neurocle.Tests", Guid.NewGuid().ToString("N"));
            string projectPath = Path.Combine(directory, "project.neurocle");
            ProjectStore store = null;
            Directory.CreateDirectory(directory);
            try
            {
                store = ProjectStore.Create(projectPath);
                using (var backup = new BackupManagementForm())
                using (var diagnostics = new DiagnosticsForm())
                using (var progress = new ImportProgressForm())
                using (var shortcuts = new KeyboardShortcutsForm())
                using (var missing = new MissingImagesForm())
                using (var jsonImport = new AnnotationJsonImportDialog(AnnotationSetType.Cla, filePath => new AnnotationJsonPreflightResult(null, String.Empty)))
                {
                    foreach (Form form in new Form[] { backup, diagnostics, progress, shortcuts, missing, jsonImport })
                    {
                        Assert.IsTrue(GetDescendants(form).Any(), form.GetType().Name + " should load its Designer controls.");
                    }
                    Assert.IsTrue(Field<ListBox>(backup, "backups").Dock == DockStyle.Fill);
                    Assert.IsTrue(Field<Panel>(backup, "buttonsPanel").Dock == DockStyle.Bottom);
                    Assert.IsTrue(Field<Label>(diagnostics, "usage").Dock == DockStyle.Top);
                    Assert.IsTrue(Field<ProgressBar>(progress, "progressBar").Maximum >= 1);
                    Assert.AreEqual(3, shortcuts.Shortcuts.Columns.Count);
                    Assert.IsTrue(Field<ListBox>(missing, "images").Dock == DockStyle.Fill);
                    Assert.AreEqual(0, Field<Button>(missing, "closeButton").TabIndex);
                    Assert.AreEqual(1, Field<Button>(missing, "delete").TabIndex);
                    Assert.AreEqual(2, Field<Button>(missing, "relink").TabIndex);
                }

                using (var backup = new BackupManagementForm(store))
                using (var diagnostics = new DiagnosticsForm(new DiagnosticLog(directory)))
                using (var missing = new MissingImagesForm(store))
                {
                    Assert.IsNotNull(Field<ListBox>(backup, "backups").DataSource);
                    Assert.IsNotNull(Field<ListBox>(missing, "images").DataSource);
                    Assert.IsFalse(Field<Button>(backup, "restore").Enabled);
                    Assert.IsFalse(Field<Button>(missing, "relink").Enabled);
                    Assert.IsFalse(Field<Button>(missing, "delete").Enabled);
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
        public void Main_form_designer_loads_static_pages_and_annotation_tools_without_runtime_dependencies()
        {
            using (var form = new MainForm())
            {
                TabControl pages = Field<TabControl>(form, "pages");
                FlowLayoutPanel tools = Field<FlowLayoutPanel>(form, "annotationTools");

                Assert.IsNotNull(form.MainMenuStrip);
                Assert.AreEqual(4, pages.TabPages.Count);
                Assert.IsTrue(pages.TabPages.Cast<TabPage>().All(page => page.Controls.Count > 0));
                Assert.AreEqual(9, tools.Controls.Count);
                Assert.AreEqual("選取 V", tools.Controls[0].Text);
                Assert.AreEqual("橡皮擦 E", tools.Controls[8].Text);
                Assert.AreEqual(300, Field<ImageBrowserControl>(form, "imageBrowser").Width);
                Assert.IsTrue(GetDescendants(form).OfType<CanvasScrollHost>().Count() == 2);
                Button importImages = Field<Button>(form, "importImagesButton");
                Button importFolder = Field<Button>(form, "importFolderButton");
                Button importAnnotation = Field<Button>(form, "importAnnotationButton");
                Assert.IsFalse(importImages.Enabled);
                Assert.IsFalse(importFolder.Enabled);
                Assert.IsFalse(importAnnotation.Enabled);
                Assert.IsFalse(Field<ToolStripMenuItem>(form, "importImagesMenuItem").Enabled);
                Assert.IsFalse(Field<ToolStripMenuItem>(form, "importFolderMenuItem").Enabled);
                Assert.IsFalse(Field<ToolStripMenuItem>(form, "importQueueMenuItem").Enabled);
                Assert.IsTrue(Field<FlowLayoutPanel>(form, "imageControls").Controls.Contains(importImages));
                Assert.IsTrue(Field<FlowLayoutPanel>(form, "imageControls").Controls.Contains(importFolder));
                Assert.IsTrue(Field<FlowLayoutPanel>(form, "annotationHeader").Controls.Contains(importAnnotation));
                ToolStripMenuItem projectMenu = Field<ToolStripMenuItem>(form, "projectMenu");
                Assert.IsFalse(projectMenu.DropDownItems.Contains(Field<ToolStripMenuItem>(form, "importQueueMenuItem")));
                Assert.IsTrue(Field<ToolStripMenuItem>(form, "jobMenu").DropDownItems.Contains(Field<ToolStripMenuItem>(form, "importQueueMenuItem")));
                Assert.IsTrue(Field<ToolStripMenuItem>(form, "manageMenu").DropDownItems.Contains(Field<ToolStripMenuItem>(form, "missingImagesMenuItem")));
                Assert.IsTrue(Field<ToolStripMenuItem>(form, "manageMenu").DropDownItems.Contains(Field<ToolStripMenuItem>(form, "backupsMenuItem")));
                Assert.IsTrue(Field<ToolStripMenuItem>(form, "manageMenu").DropDownItems.Contains(Field<ToolStripMenuItem>(form, "diagnosticsMenuItem")));
                Assert.IsFalse(projectMenu.DropDownItems.Contains(Field<ToolStripMenuItem>(form, "importImagesMenuItem")));
                Assert.IsFalse(projectMenu.DropDownItems.Contains(Field<ToolStripMenuItem>(form, "importFolderMenuItem")));
                Assert.IsFalse(projectMenu.DropDownItems.Contains(Field<ToolStripMenuItem>(form, "importAnnotationMenuItem")));
            }
        }

        [STATestMethod]
        public void Import_queue_window_displays_waiting_running_and_completed_jobs()
        {
            using (var form = new ImportQueueForm())
            {
                form.UpdateJobs(new[]
                {
                    new ImportJobSnapshot(Guid.NewGuid(), new ImageFilesImportJobRequest(new[] { "one.png" }), ImportJobStatus.Waiting, null, null, null),
                    new ImportJobSnapshot(Guid.NewGuid(), new ImageFilesImportJobRequest(new[] { "two.png" }), ImportJobStatus.Running, new ImportJobProgress(1, 2, 1, 0), null, null),
                    new ImportJobSnapshot(Guid.NewGuid(), new ImageFilesImportJobRequest(new[] { "three.png" }), ImportJobStatus.Completed, new ImportJobProgress(1, 1, 1, 0), new ImportJobResult(false, new[] { "image-id" }, 2, 0), null),
                    new ImportJobSnapshot(Guid.NewGuid(), new FolderImportJobRequest("folder", true), ImportJobStatus.Completed, new ImportJobProgress(2, 2, 1, 1), new ImportJobResult(false, new[] { "folder-image-id" }, 1, 0), null)
                });

                ListView jobs = Field<ListView>(form, "jobsList");
                FlowLayoutPanel cards = Field<FlowLayoutPanel>(form, "cardsPanel");
                CollectionAssert.AreEqual(new[] { "等待中", "執行中", "已完成", "已完成" }, jobs.Items.Cast<ListViewItem>().Select(item => item.SubItems[1].Text).ToArray());
                Assert.AreEqual("成功 1，失敗 1，未完成 0", jobs.Items[3].SubItems[2].Text);
                Assert.AreEqual(4, cards.Controls.Count);
                Assert.AreEqual("1 / 2", jobs.Items[1].SubItems[2].Text);
                ProgressBar runningProgress = Field<ProgressBar>(cards.Controls[1], "progressBar");
                ProgressBar completedProgress = Field<ProgressBar>(cards.Controls[2], "progressBar");
                Assert.AreEqual(50, runningProgress.Value);
                Assert.AreEqual(100, completedProgress.Value);
                Assert.AreEqual(520, form.ClientSize.Width);
                Assert.AreEqual(FormBorderStyle.Sizable, form.FormBorderStyle);
                Assert.AreEqual(0, form.MaximumSize.Height, "The queue window must not define a maximum height.");
                Assert.AreEqual(804, ImportQueueForm.ScaleFixedWidthForDpi(form.Width, 96, 144), "The fixed logical width must scale at 150% DPI.");
                Assert.AreEqual(1072, ImportQueueForm.ScaleFixedWidthForDpi(form.Width, 96, 192), "The fixed logical width must scale at 200% DPI.");
                int originalWidth = form.Width;
                form.Size = new Size(originalWidth + 100, 600);
                Assert.AreEqual(originalWidth, form.Width, "The queue window width must remain fixed.");
                Assert.AreEqual(600, form.Height, "The queue window height must remain adjustable.");
                form.Show();
                System.Windows.Forms.Application.DoEvents();
                Assert.IsTrue(form.ClientSize.Height >= form.MinimumSize.Height, "The queue window must retain enough height to display job cards and progress bars.");
                jobs.Items[2].Selected = true;
                Assert.IsFalse(Field<Button>(form, "cancelButton").Enabled);
            }
        }

        [STATestMethod]
        public void Annotation_json_import_dialog_selects_reselects_and_clears_files_with_inline_type_validation()
        {
            string claJson = "{\"label_type\":\"cla\",\"source\":\"labelset\",\"time\":\"2026-01-01T00:00:00Z\",\"version\":\"4.5.2.2\",\"classes\":[{\"name\":\"scratch\",\"color\":\"#FF0000\"},{\"name\":\"毛邊\",\"color\":\"#00FF00\"}],\"data\":[{\"fileName\":\"one.png\",\"set\":\"not_used\",\"classLabel\":\"scratch\",\"regionLabel\":[],\"retestset\":1,\"rotation_angle\":0,\"width\":20,\"height\":20},{\"fileName\":\"two.png\",\"set\":\"train\",\"classLabel\":\"毛邊\",\"regionLabel\":[],\"retestset\":0,\"rotation_angle\":0,\"width\":20,\"height\":20},{\"fileName\":\"three.png\",\"set\":\"test\",\"classLabel\":\"\",\"regionLabel\":[],\"retestset\":0,\"rotation_angle\":0,\"width\":20,\"height\":20}]}";
            string obdJson = claJson.Replace("\"label_type\":\"cla\"", "\"label_type\":\"obd\"").Replace("\"classLabel\":\"scratch\",\"regionLabel\":[]", "\"classLabel\":\"\",\"regionLabel\":[{\"className\":\"scratch\",\"type\":\"Rect\",\"x\":1,\"y\":1,\"width\":2,\"height\":2}]").Replace("\"classLabel\":\"毛邊\"", "\"classLabel\":\"\"");
            string unknownJson = claJson.Insert(1, "\"futureField\":true,");
            string directory = Path.Combine(Path.GetTempPath(), "Neurocle.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string claPath = Path.Combine(directory, "labels.json");
            string obdPath = Path.Combine(directory, "other.json");
            string unknownPath = Path.Combine(directory, "unknown.json");
            File.WriteAllText(claPath, claJson);
            File.WriteAllText(obdPath, obdJson);
            File.WriteAllText(unknownPath, unknownJson);
            try
            {
                Func<string, AnnotationJsonPreflightResult> parse = filePath =>
                {
                    AnnotationJsonImportPreview value = AnnotationJsonImportPreview.Parse(File.ReadAllText(filePath));
                    return new AnnotationJsonPreflightResult(value, value.Type == AnnotationSetType.Cla ? null : "此標註集類型與目前標註集不同");
                };
                using (var dialog = new AnnotationJsonImportDialog(AnnotationSetType.Cla, parse))
                {
                    WaitForTask(dialog.SelectFileAsync(claPath));
                    ComboBox annotationMode = Field<ComboBox>(dialog, "annotationMode");
                    ComboBox datasetMode = Field<ComboBox>(dialog, "datasetMode");
                    TreeView preview = Field<TreeView>(dialog, "preview");
                    CollectionAssert.AreEqual(new[] { "添加到空白圖像", "覆蓋所有圖像" }, annotationMode.Items.Cast<string>().ToArray());
                    CollectionAssert.AreEqual(new[] { "添加到空白圖像", "覆蓋所有圖像" }, datasetMode.Items.Cast<string>().ToArray());
                    Assert.AreEqual("JSON type: CLA", preview.Nodes[0].Text);
                    Assert.AreEqual("CLASS: 2", preview.Nodes[1].Text);
                    CollectionAssert.AreEqual(new[] { "scratch", "毛邊" }, preview.Nodes[1].Nodes.Cast<TreeNode>().Select(node => node.Text).ToArray());
                    Assert.AreEqual("SET: Not used, Retest, Train, Test", preview.Nodes[2].Text);
                    Assert.AreEqual("labels.json", Field<TextBox>(dialog, "sourcePath").Text);
                    Assert.IsTrue(Field<Button>(dialog, "importButton").Enabled);
                    annotationMode.SelectedIndex = 1;
                    Assert.AreEqual(AnnotationImportMode.ReplaceMatchingImages, dialog.AnnotationMode);
                    datasetMode.SelectedIndex = 1;
                    Assert.AreEqual(DatasetImportMode.ReplaceMatchingImages, dialog.DatasetMode);
                    annotationMode.SelectedIndex = 0;
                    datasetMode.SelectedIndex = 0;
                    Assert.AreEqual(0, GetDescendants(dialog).OfType<TextBox>().Count(textBox => !textBox.ReadOnly), "The dialog must not show raw JSON text as editable content.");

                    Field<CheckBox>(dialog, "annotationsAndClasses").Checked = false;
                    Field<CheckBox>(dialog, "dataset").Checked = false;
                    Assert.IsFalse(Field<Button>(dialog, "importButton").Enabled, "At least one import group must remain selected.");

                    WaitForTask(dialog.SelectFileAsync(unknownPath));
                    ListBox unknownFields = Field<ListBox>(dialog, "unknownFields");
                    CheckBox allowUnknownFields = Field<CheckBox>(dialog, "allowUnknownFields");
                    Assert.AreEqual("root.futureField", unknownFields.Items.Cast<string>().Single());
                    Assert.IsFalse(Field<Button>(dialog, "importButton").Enabled, "Unknown fields must require explicit consent before import can be queued.");
                    allowUnknownFields.Checked = true;
                    Field<CheckBox>(dialog, "annotationsAndClasses").Checked = true;
                    Assert.IsTrue(Field<Button>(dialog, "importButton").Enabled);
                    Assert.IsTrue(dialog.AllowUnknownFields);

                    WaitForTask(dialog.SelectFileAsync(obdPath));
                    dialog.Show();
                    System.Windows.Forms.Application.DoEvents();
                    Assert.AreEqual("此標註集類型與目前標註集不同", Field<Label>(dialog, "validationMessage").Text);
                    Assert.AreEqual("JSON type: OBD", preview.Nodes[0].Text, "A mismatched JSON must still show its parsed summary.");
                    Assert.IsFalse(Field<Button>(dialog, "importButton").Enabled);
                    Assert.IsFalse(Field<CheckBox>(dialog, "annotationsAndClasses").Enabled);
                    Assert.AreEqual(1, System.Windows.Forms.Application.OpenForms.Cast<Form>().Count(form => form == dialog), "Type mismatch validation must stay inline without opening another window.");

                    WaitForTask(dialog.SelectFileAsync(null));
                    Assert.IsNull(dialog.FilePath);
                    Assert.AreEqual(String.Empty, Field<TextBox>(dialog, "sourcePath").Text);
                    Assert.AreEqual(0, preview.Nodes.Count);
                    Assert.IsFalse(Field<Button>(dialog, "importButton").Enabled);
                }

                using (var dialog = new AnnotationJsonImportDialog(AnnotationSetType.Obd, filePath => new AnnotationJsonPreflightResult(AnnotationJsonImportPreview.Parse(File.ReadAllText(filePath)), null)))
                {
                    WaitForTask(dialog.SelectFileAsync(obdPath));
                    CollectionAssert.AreEqual(new[] { "添加到空白圖像", "添加到所有圖像", "覆蓋所有圖像" }, Field<ComboBox>(dialog, "annotationMode").Items.Cast<string>().ToArray());
                    CollectionAssert.AreEqual(new[] { "添加到空白圖像", "覆蓋所有圖像" }, Field<ComboBox>(dialog, "datasetMode").Items.Cast<string>().ToArray());
                    Field<ComboBox>(dialog, "annotationMode").SelectedIndex = 1;
                    Assert.AreEqual(AnnotationImportMode.AddToAllImages, dialog.AnnotationMode);
                    Field<ComboBox>(dialog, "annotationMode").SelectedIndex = 2;
                    Assert.AreEqual(AnnotationImportMode.ReplaceMatchingImages, dialog.AnnotationMode);
                    Assert.IsTrue(Field<Button>(dialog, "importButton").Enabled);
                }
            }
            finally { Directory.Delete(directory, true); }
        }

        [TestMethod]
        public void Annotation_json_preflight_rejects_type_mismatch_before_queueing()
        {
            string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
            string json = "{\"label_type\":\"cla\",\"source\":\"labelset\",\"time\":\"2026-01-01T00:00:00Z\",\"version\":\"4.5.2.2\",\"classes\":[{\"name\":\"part\",\"color\":\"#FF0000\"}],\"data\":[{\"fileName\":\"sample.png\",\"set\":\"train\",\"classLabel\":\"part\",\"regionLabel\":[],\"retestset\":0,\"rotation_angle\":0,\"width\":20,\"height\":20}]}";
            File.WriteAllText(path, json);
            try
            {
                Assert.AreEqual(AnnotationSetType.Cla, MainForm.PreflightAnnotationJson(path, AnnotationSetType.Cla).Type);
                Assert.ThrowsException<JsonAnnotationImportException>(() => MainForm.PreflightAnnotationJson(path, AnnotationSetType.Seg));
                string invalidColor = json.Replace("#FF0000", "not-a-color");
                File.WriteAllText(path, invalidColor);
                AnnotationJsonPreflightResult newClassResult = MainForm.InspectAnnotationJson(path, AnnotationSetType.Cla);
                Assert.IsNotNull(newClassResult.ErrorMessage, "An invalid color for a new class must stop preflight.");
                AnnotationJsonPreflightResult existingClassResult = MainForm.InspectAnnotationJson(path, AnnotationSetType.Cla, new[] { new AnnotationClass("class", "set", "part", "#123456") });
                Assert.IsNull(existingClassResult.ErrorMessage, "An existing same-name class keeps its current color and ignores the JSON color.");
            }
            finally { File.Delete(path); }
        }

        [STATestMethod]
        public void Import_queue_updates_each_existing_card_without_resetting_other_progress()
        {
            using (var form = new ImportQueueForm())
            {
                Guid completedId = Guid.NewGuid();
                Guid runningId = Guid.NewGuid();
                form.UpdateJobs(new[]
                {
                    new ImportJobSnapshot(completedId, new ImageFilesImportJobRequest(new[] { "done.png" }), ImportJobStatus.Completed, new ImportJobProgress(4, 4, 4, 0), new ImportJobResult(false, new[] { "done" }), null),
                    new ImportJobSnapshot(runningId, new ImageFilesImportJobRequest(new[] { "running.png" }), ImportJobStatus.Running, new ImportJobProgress(1, 4, 1, 0), null, null)
                });

                FlowLayoutPanel cards = Field<FlowLayoutPanel>(form, "cardsPanel");
                ImportJobCardControl completedCard = cards.Controls.OfType<ImportJobCardControl>().Single(card => card.JobId == completedId);
                ImportJobCardControl runningCard = cards.Controls.OfType<ImportJobCardControl>().Single(card => card.JobId == runningId);

                form.UpdateJobs(new[]
                {
                    new ImportJobSnapshot(completedId, new ImageFilesImportJobRequest(new[] { "done.png" }), ImportJobStatus.Completed, new ImportJobProgress(4, 4, 4, 0), new ImportJobResult(false, new[] { "done" }), null),
                    new ImportJobSnapshot(runningId, new ImageFilesImportJobRequest(new[] { "running.png" }), ImportJobStatus.Running, new ImportJobProgress(2, 4, 2, 0), null, null)
                });

                Assert.AreSame(completedCard, cards.Controls.OfType<ImportJobCardControl>().Single(card => card.JobId == completedId), "Updating another job must not restart a completed card's progress animation.");
                Assert.AreSame(runningCard, cards.Controls.OfType<ImportJobCardControl>().Single(card => card.JobId == runningId), "A running job must update its existing card.");
                Assert.AreEqual(100, Field<ProgressBar>(completedCard, "progressBar").Value);
                Assert.AreEqual(50, Field<ProgressBar>(runningCard, "progressBar").Value);
            }
        }

        [STATestMethod]
        public void Canceled_job_preserves_its_last_known_percentage()
        {
            using (var form = new ImportQueueForm())
            {
                form.UpdateJobs(new[]
                {
                    new ImportJobSnapshot(Guid.NewGuid(), new ImageFilesImportJobRequest(new[] { "one.png", "two.png", "three.png", "four.png" }), ImportJobStatus.Canceled, new ImportJobProgress(1, 4, 1, 0), new ImportJobResult(true, new[] { "one" }, 0, 3), null)
                });

                ImportJobCardControl card = Field<FlowLayoutPanel>(form, "cardsPanel").Controls.OfType<ImportJobCardControl>().Single();
                Assert.AreEqual(25, Field<ProgressBar>(card, "progressBar").Value);
            }
        }

        [STATestMethod]
        public void Import_card_switches_from_unknown_scan_to_actual_percentage_when_total_is_known()
        {
            using (var form = new ImportQueueForm())
            {
                Guid jobId = Guid.NewGuid();
                form.UpdateJobs(new[]
                {
                    new ImportJobSnapshot(jobId, new FolderImportJobRequest("folder", true), ImportJobStatus.Running, null, null, null)
                });
                ImportJobCardControl card = Field<FlowLayoutPanel>(form, "cardsPanel").Controls.OfType<ImportJobCardControl>().Single();
                ProgressBar progress = Field<ProgressBar>(card, "progressBar");
                Assert.AreEqual(ProgressBarStyle.Marquee, progress.Style, "A scan without a known total must not show a fabricated percentage.");

                form.UpdateJobs(new[]
                {
                    new ImportJobSnapshot(jobId, new FolderImportJobRequest("folder", true), ImportJobStatus.Running, new ImportJobProgress(1, 4, 1, 0), null, null)
                });

                Assert.AreSame(card, Field<FlowLayoutPanel>(form, "cardsPanel").Controls.OfType<ImportJobCardControl>().Single());
                Assert.AreEqual(ProgressBarStyle.Continuous, progress.Style);
                Assert.AreEqual(25, progress.Value);
            }
        }

        [STATestMethod]
        public void Completed_import_card_keeps_view_button_clear_of_progress_bar()
        {
            using (var form = new ImportQueueForm())
            {
                form.UpdateJobs(new[]
                {
                    new ImportJobSnapshot(
                        Guid.NewGuid(),
                        new ImageFilesImportJobRequest(new[] { "one.png" }),
                        ImportJobStatus.Completed,
                        new ImportJobProgress(1, 1, 1, 0),
                        new ImportJobResult(false, new[] { "image-id" }),
                        null)
                });

                ImportJobCardControl card = Field<FlowLayoutPanel>(form, "cardsPanel").Controls.OfType<ImportJobCardControl>().Single();
                ProgressBar progress = Field<ProgressBar>(card, "progressBar");
                Button view = Field<Button>(card, "viewImportedImagesButton");

                form.Show();
                System.Windows.Forms.Application.DoEvents();
                Assert.IsTrue(view.Visible);
                Assert.IsFalse(progress.Bounds.IntersectsWith(view.Bounds), "The imported-image button must not be covered by the progress bar.");
            }
        }

        [STATestMethod]
        public void Import_card_expands_details_below_actions_and_supports_copy_and_multiple_cards()
        {
            Assert.AreEqual(128, ImportJobCardControl.ScaleLogicalForDpi(128, 96));
            Assert.AreEqual(192, ImportJobCardControl.ScaleLogicalForDpi(128, 144));
            Assert.AreEqual(256, ImportJobCardControl.ScaleLogicalForDpi(128, 192));

            using (var form = new ImportQueueForm())
            {
                string details = string.Join(" ", Enumerable.Repeat("A long diagnostic message that must wrap within the card and remain available for vertical scrolling.", 12));
                form.UpdateJobs(new[]
                {
                    new ImportJobSnapshot(Guid.NewGuid(), new ImageFilesImportJobRequest(new[] { "first.png" }), ImportJobStatus.Failed, null, null, details),
                    new ImportJobSnapshot(Guid.NewGuid(), new ImageFilesImportJobRequest(new[] { "second.png" }), ImportJobStatus.Failed, null, null, "Second job failure.")
                });
                form.Show();
                System.Windows.Forms.Application.DoEvents();

                FlowLayoutPanel cards = Field<FlowLayoutPanel>(form, "cardsPanel");
                ImportJobCardControl[] jobCards = cards.Controls.OfType<ImportJobCardControl>().OrderBy(card => card.Top).ToArray();
                int collapsedHeight = jobCards[0].Height;
                Button[] toggles = jobCards.Select(card => Field<Button>(card, "detailsButton")).ToArray();
                toggles[0].PerformClick();
                System.Windows.Forms.Application.DoEvents();

                TextBox detailsBox = Field<TextBox>(jobCards[0], "detailsLabel");
                Button copy = Field<Button>(jobCards[0], "copyDetailsButton");
                Assert.IsTrue(jobCards[0].Height > collapsedHeight, "Expanding details must increase the card height.");
                Assert.IsTrue(detailsBox.Visible);
                Assert.IsTrue(detailsBox.Multiline);
                Assert.IsTrue(detailsBox.WordWrap);
                Assert.AreEqual(ScrollBars.Vertical, detailsBox.ScrollBars);
                Assert.IsTrue(detailsBox.Bounds.Bottom <= jobCards[0].ClientSize.Height, "Details must remain within the card's bottom edge.");
                Assert.IsTrue(detailsBox.Top >= Field<Button>(jobCards[0], "viewImportedImagesButton").Bottom, "Details must be below the card's operation buttons.");
                Assert.IsTrue(copy.Visible);
                copy.PerformClick();
                Assert.AreEqual(details, Clipboard.GetText());
                Assert.IsTrue(jobCards[1].Top >= jobCards[0].Bottom, "Expanded cards must reflow without overlapping the following card.");
                Assert.IsFalse(cards.HorizontalScroll.Visible, "Expanded details must not introduce horizontal scrolling.");

                toggles[1].PerformClick();
                System.Windows.Forms.Application.DoEvents();
                Assert.IsTrue(jobCards[0].Height > collapsedHeight);
                Assert.IsTrue(jobCards[1].Height > collapsedHeight);
                Assert.IsTrue(jobCards[1].Top >= jobCards[0].Bottom, "Two expanded cards must reflow without overlap.");
                Assert.IsFalse(cards.HorizontalScroll.Visible, "Two expanded cards must not introduce horizontal scrolling.");

                toggles[0].PerformClick();
                System.Windows.Forms.Application.DoEvents();
                Assert.AreEqual(collapsedHeight, jobCards[0].Height, "Collapsing details must restore the compact card height.");
                Assert.IsTrue(jobCards[1].Height > collapsedHeight, "Collapsing one card must not collapse another.");
                toggles[1].PerformClick();
                System.Windows.Forms.Application.DoEvents();
                Assert.AreEqual(collapsedHeight, jobCards[1].Height, "Each card must collapse independently.");
            }
        }

        [STATestMethod]
        public void Json_import_warnings_are_shown_as_completed_with_details_on_the_job_card()
        {
            using (var form = new ImportQueueForm())
            {
                var warning = new AnnotationJsonImportWarning(
                    "image.png",
                    "data[2].regionLabel[1]",
                    AnnotationJsonImportWarningReason.UnknownClassReference);
                form.UpdateJobs(new[]
                {
                    new ImportJobSnapshot(
                        Guid.NewGuid(),
                        new AnnotationJsonImportJobRequest("labels.json", "target", "target-id", AnnotationSetType.Obd, true, AnnotationImportMode.AddToAllImages, false, DatasetImportMode.AddToUnassignedImages, new string[0]),
                        ImportJobStatus.Completed,
                        null,
                        new ImportJobResult(false, new string[0], warnings: new[] { warning }),
                        null)
                });
                form.Show();
                System.Windows.Forms.Application.DoEvents();

                ImportJobCardControl card = Field<FlowLayoutPanel>(form, "cardsPanel").Controls.OfType<ImportJobCardControl>().Single();
                Assert.AreEqual("完成但有警告", Field<Label>(card, "statusLabel").Text);
                Assert.AreEqual("完成但有警告", Field<ListView>(form, "jobsList").Items[0].SubItems[1].Text);
                Assert.IsFalse(Field<TextBox>(card, "detailsLabel").Visible, "Warnings remain collapsed until details are opened.");

                Field<Button>(card, "detailsButton").PerformClick();
                System.Windows.Forms.Application.DoEvents();
                TextBox details = Field<TextBox>(card, "detailsLabel");
                StringAssert.Contains(details.Text, "image.png");
                StringAssert.Contains(details.Text, "data[2].regionLabel[1]");
                StringAssert.Contains(details.Text, "類別不在 JSON 的 CLASS 清單中");
                Field<Button>(card, "copyDetailsButton").PerformClick();
                Assert.AreEqual(details.Text, Clipboard.GetText());
            }
        }

        [STATestMethod]
        public void Json_import_without_warnings_keeps_normal_completed_status_and_no_details()
        {
            using (var form = new ImportQueueForm())
            {
                form.UpdateJobs(new[]
                {
                    new ImportJobSnapshot(
                        Guid.NewGuid(),
                        new AnnotationJsonImportJobRequest("labels.json", "target", "target-id", AnnotationSetType.Obd, true, AnnotationImportMode.AddToAllImages, false, DatasetImportMode.AddToUnassignedImages, new string[0]),
                        ImportJobStatus.Completed,
                        null,
                        new ImportJobResult(false, new string[0]),
                        null)
                });
                form.Show();
                System.Windows.Forms.Application.DoEvents();

                ImportJobCardControl card = Field<FlowLayoutPanel>(form, "cardsPanel").Controls.OfType<ImportJobCardControl>().Single();
                Assert.AreEqual("已完成", Field<Label>(card, "statusLabel").Text);
                Assert.AreEqual(string.Empty, Field<TextBox>(card, "detailsLabel").Text);
                Assert.IsFalse(Field<TextBox>(card, "detailsLabel").Visible);
            }
        }

        [STATestMethod]
        public void Import_and_export_cards_keep_their_actions_mutually_exclusive()
        {
            using (var form = new ImportQueueForm())
            {
                Guid importId = Guid.NewGuid();
                Guid exportId = Guid.NewGuid();
                form.UpdateJobs(new[]
                {
                    new ImportJobSnapshot(importId, new ImageFilesImportJobRequest(new[] { "one.png" }), ImportJobStatus.Completed, null, new ImportJobResult(false, new[] { "image-id" }), null),
                    new ImportJobSnapshot(exportId, new JsonExportJobRequest("set-id", "labels.json"), ImportJobStatus.Completed, null, new ImportJobResult(false, new[] { "unexpected-image-id" }, outputPath: "labels.json"), null)
                });

                ImportJobCardControl importCard = Field<FlowLayoutPanel>(form, "cardsPanel").Controls[0] as ImportJobCardControl;
                ImportJobCardControl exportCard = Field<FlowLayoutPanel>(form, "cardsPanel").Controls[1] as ImportJobCardControl;
                form.Show();
                System.Windows.Forms.Application.DoEvents();
                Assert.IsTrue(Field<Button>(importCard, "viewImportedImagesButton").Visible);
                Assert.IsFalse(Field<Button>(importCard, "openExportButton").Visible);
                Assert.IsFalse(Field<Button>(exportCard, "viewImportedImagesButton").Visible);
                Assert.IsTrue(Field<Button>(exportCard, "openExportButton").Visible);
            }
        }

        [STATestMethod]
        public void Completed_image_import_card_can_request_viewing_imported_images()
        {
            using (var form = new ImportQueueForm())
            {
                Guid jobId = Guid.NewGuid();
                bool requested = false;
                form.ViewImportedImagesRequested += id => requested = id == jobId;
                form.UpdateJobs(new[]
                {
                    new ImportJobSnapshot(jobId, new ImageFilesImportJobRequest(new[] { "one.png" }), ImportJobStatus.Completed, null, new ImportJobResult(false, new[] { "image-id" }), null)
                });

                ImportJobCardControl card = Field<FlowLayoutPanel>(form, "cardsPanel").Controls.OfType<ImportJobCardControl>().Single();
                Button view = Field<Button>(card, "viewImportedImagesButton");
                form.Show();
                System.Windows.Forms.Application.DoEvents();
                Assert.IsTrue(view.Visible);
                typeof(ImportJobCardControl).GetMethod("ViewImportedImagesButton_Click", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(card, new object[] { view, EventArgs.Empty });
                Assert.IsTrue(requested);
                form.SetImportedImagesAvailability(jobId, false, "該批匯入的圖片已全部刪除");
                Assert.IsFalse(view.Enabled);
            }
        }

        [STATestMethod]
        public void Canceled_image_import_card_can_request_viewing_successfully_imported_images()
        {
            using (var form = new ImportQueueForm())
            {
                Guid jobId = Guid.NewGuid();
                bool requested = false;
                form.ViewImportedImagesRequested += id => requested = id == jobId;
                form.UpdateJobs(new[]
                {
                    new ImportJobSnapshot(jobId, new ImageFilesImportJobRequest(new[] { "one.png", "two.png" }), ImportJobStatus.Canceled, null, new ImportJobResult(true, new[] { "image-id" }, 0, 1), null)
                });

                ImportJobCardControl card = Field<FlowLayoutPanel>(form, "cardsPanel").Controls.OfType<ImportJobCardControl>().Single();
                Button view = Field<Button>(card, "viewImportedImagesButton");
                form.Show();
                System.Windows.Forms.Application.DoEvents();
                Assert.IsTrue(view.Visible, "A canceled job with imported images must allow batch navigation.");
                typeof(ImportJobCardControl).GetMethod("ViewImportedImagesButton_Click", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(card, new object[] { view, EventArgs.Empty });
                Assert.IsTrue(requested);

                form.UpdateJobs(new[]
                {
                    new ImportJobSnapshot(jobId, new ImageFilesImportJobRequest(new[] { "one.png", "two.png" }), ImportJobStatus.Canceled, null, new ImportJobResult(true, new string[0], 0, 2), null)
                });
                card = Field<FlowLayoutPanel>(form, "cardsPanel").Controls.OfType<ImportJobCardControl>().Single();
                Assert.IsFalse(Field<Button>(card, "viewImportedImagesButton").Visible, "A canceled job without imported images must not offer batch navigation.");
            }
        }

        [STATestMethod]
        public void Canceled_image_import_job_navigates_to_its_successfully_imported_images()
        {
            string directory = Path.Combine(Path.GetTempPath(), "Neurocle.Tests", Guid.NewGuid().ToString("N"));
            string projectPath = Path.Combine(directory, "project.neurocle");
            string imagePath = Path.Combine(directory, "existing.png");
            ProjectStore store = null;
            ImportJobQueue queue = null;
            Directory.CreateDirectory(directory);
            try
            {
                CreateFilledImage(imagePath, Color.Black, 10);
                store = ProjectStore.Create(projectPath);
                ProjectImage image = store.ImportFiles(new[] { imagePath }).Single();
                using (var form = new MainForm(new ProjectLifecycle()))
                {
                    Invoke(form, "ReplaceProject", store);
                    ImportJobQueue originalQueue = Field<ImportJobQueue>(form, "importJobQueue");
                    queue = new ImportJobQueue((request, cancellationRequested, progress) => new ImportJobResult(true, new[] { image.Id }, 0, 1));
                    SetField(form, "importJobQueue", queue);
                    originalQueue.Dispose();
                    form.Show();
                    System.Windows.Forms.Application.DoEvents();

                    Guid jobId = queue.Enqueue(new ImageFilesImportJobRequest(new[] { imagePath, "not-imported.png" }));
                    Assert.IsTrue(queue.WaitUntilIdle(TimeSpan.FromSeconds(5)));
                    Invoke(form, "ShowImportQueue");
                    ImportQueueForm queueForm = Field<ImportQueueForm>(form, "importQueueForm");
                    ImportJobCardControl card = Field<FlowLayoutPanel>(queueForm, "cardsPanel").Controls.OfType<ImportJobCardControl>().Single();
                    Assert.IsTrue(Field<Button>(card, "viewImportedImagesButton").Visible);

                    Invoke(form, "ViewImportedImages", jobId);

                    CollectionAssert.AreEqual(new[] { image.Id }, form.ImageBrowser.SelectedImageIds.ToArray());
                    Assert.AreSame(Field<TabPage>(form, "imagesPage"), Field<TabControl>(form, "pages").SelectedTab);
                    form.Close();
                    queue = null;
                    store = null;
                }
            }
            finally
            {
                queue?.Dispose();
                store?.Dispose();
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [STATestMethod]
        public void Image_browser_select_images_clears_filter_and_selects_only_requested_rows()
        {
            using (var browser = new ImageBrowserControl())
            {
                browser.CreateControl();
                var images = new[]
                {
                    new ProjectImage("one", "one.png", "one.png", "sha-one", 10, 10, 1),
                    new ProjectImage("two", "two.png", "two.png", "sha-two", 10, 10, 2),
                    new ProjectImage("three", "three.png", "three.png", "sha-three", 10, 10, 3)
                };
                browser.LoadImages(images, Path.GetTempPath());
                TextBox search = Field<TextBox>(browser, "search");
                search.Text = "three";
                browser.SelectImages(new[] { "one", "three" });
                CollectionAssert.AreEquivalent(new[] { "one", "three" }, browser.SelectedImageIds.ToArray());
                Assert.AreEqual(string.Empty, search.Text);
            }
        }

        [STATestMethod]
        public void Image_browser_shift_wheel_scrolls_horizontally_without_changing_selection_or_filter()
        {
            using (var form = new Form { ClientSize = new Size(260, 230), ShowInTaskbar = false })
            using (var browser = new ImageBrowserControl { Dock = DockStyle.Fill })
            {
                form.Controls.Add(browser);
                var images = Enumerable.Range(0, 40).Select(index => new ProjectImage(
                    "image-" + index.ToString("D2"), "image-" + (40 - index).ToString("D2") + ".png", "image-" + (40 - index).ToString("D2") + ".png",
                    "sha-" + index, 10, 10, index + 1)).ToArray();
                browser.LoadImages(images, Path.GetTempPath());
                form.Show();
                System.Windows.Forms.Application.DoEvents();

                ListView list = Field<ListView>(browser, "list");
                Assert.IsTrue(list.IsHandleCreated);
                TextBox search = Field<TextBox>(browser, "search");
                search.Text = "image-";
                Invoke(browser, "List_ColumnClick", list, new ColumnClickEventArgs(1));
                System.Windows.Forms.Application.DoEvents();
                ScrollInfo horizontalBefore = ReadScrollInfo(list, 0);
                Assert.IsTrue(HasScrollRange(horizontalBefore), "The narrow list must have a horizontal scroll range.");
                ScrollInfo verticalBefore = ReadScrollInfo(list, 1);
                Assert.IsTrue(HasScrollRange(verticalBefore), "The populated list must have a vertical scroll range.");

                int selectionChanges = 0;
                string currentImageId = null;
                browser.ImageSelected += (sender, args) =>
                {
                    selectionChanges++;
                    currentImageId = args.Image.Id;
                };
                list.SelectedIndices.Add(0);
                list.SelectedIndices.Add(1);
                System.Windows.Forms.Application.DoEvents();
                string[] selectedBefore = browser.SelectedImageIds.ToArray();
                string searchBefore = search.Text;
                string firstFileNameBefore = list.Items[0].SubItems[1].Text;
                int selectionChangesBefore = selectionChanges;
                string currentImageBefore = currentImageId;

                SendMouseWheel(list, -120, shift: false);
                System.Windows.Forms.Application.DoEvents();
                ScrollInfo verticalAfterNormalWheel = ReadScrollInfo(list, 1);
                Assert.IsTrue(verticalAfterNormalWheel.Position > verticalBefore.Position, "An unmodified wheel must keep scrolling vertically.");

                ScrollInfo horizontalBeforeShiftWheel = ReadScrollInfo(list, 0);
                SendMouseWheel(list, -120, shift: true);
                System.Windows.Forms.Application.DoEvents();
                ScrollInfo horizontalAfterNegativeWheel = ReadScrollInfo(list, 0);
                Assert.IsTrue(horizontalAfterNegativeWheel.Position > horizontalBeforeShiftWheel.Position, "A negative Shift+wheel delta must move toward later columns.");
                Assert.AreEqual(verticalAfterNormalWheel.Position, ReadScrollInfo(list, 1).Position, "Shift+wheel must not scroll vertically.");

                SendMouseWheel(list, 120, shift: true);
                System.Windows.Forms.Application.DoEvents();
                ScrollInfo horizontalAfterPositiveWheel = ReadScrollInfo(list, 0);
                Assert.IsTrue(horizontalAfterPositiveWheel.Position < horizontalAfterNegativeWheel.Position, "A positive Shift+wheel delta must move toward earlier columns.");
                CollectionAssert.AreEquivalent(selectedBefore, browser.SelectedImageIds.ToArray());
                Assert.AreEqual(currentImageBefore, currentImageId);
                Assert.AreEqual(selectionChangesBefore, selectionChanges, "Scrolling must not raise an image selection change.");
                Assert.AreEqual(searchBefore, search.Text);
                Assert.AreEqual(firstFileNameBefore, list.Items[0].SubItems[1].Text, "Scrolling must not alter the current sort order.");

                form.ClientSize = new Size(1000, 230);
                System.Windows.Forms.Application.DoEvents();
                ScrollInfo horizontalWithoutRange = ReadScrollInfo(list, 0);
                Assert.IsFalse(HasScrollRange(horizontalWithoutRange), "The wide list must not have a horizontal scroll range.");
                int verticalBeforeNoRangeWheel = ReadScrollInfo(list, 1).Position;
                SendMouseWheel(list, -120, shift: true);
                System.Windows.Forms.Application.DoEvents();
                Assert.AreEqual(verticalBeforeNoRangeWheel, ReadScrollInfo(list, 1).Position, "Shift+wheel without a horizontal range must not fall back to vertical scrolling.");
                CollectionAssert.AreEquivalent(selectedBefore, browser.SelectedImageIds.ToArray());
                Assert.AreEqual(selectionChangesBefore, selectionChanges, "A no-range Shift+wheel must not navigate to another image.");
                Assert.AreEqual(currentImageBefore, currentImageId);
            }
        }

        [STATestMethod]
        public void Import_queue_window_explains_unknown_remaining_folder_scan_after_cancel()
        {
            using (var form = new ImportQueueForm())
            {
                form.UpdateJobs(new[]
                {
                    new ImportJobSnapshot(Guid.NewGuid(), new FolderImportJobRequest("folder", false), ImportJobStatus.Canceled, new ImportJobProgress(1, 0, 1, 0), new ImportJobResult(true, new[] { "image-id" }, 0, 0, true), null)
                });

                ListView jobs = Field<ListView>(form, "jobsList");
                Assert.AreEqual("資料夾掃描已取消；尚未掃描的項目數無法確定。", jobs.Items[0].SubItems[2].Text);
            }
        }

        [STATestMethod]
        public void Import_queue_window_disables_cancel_for_running_json_write()
        {
            using (var form = new ImportQueueForm())
            {
                form.UpdateJobs(new[]
                {
                    new ImportJobSnapshot(Guid.NewGuid(), new AnnotationJsonImportJobRequest("labels.json", "labels", false), ImportJobStatus.Running, null, null, null)
                });

                Assert.IsFalse(Field<Button>(form, "cancelButton").Enabled);
            }
        }

        [STATestMethod]
        public void Import_queue_window_exposes_clear_completed_operation()
        {
            using (var form = new ImportQueueForm())
            {
                bool called = false;
                form.ClearCompletedRequested += () => called = true;
                form.UpdateJobs(new[]
                {
                    new ImportJobSnapshot(Guid.NewGuid(), new ImageFilesImportJobRequest(new[] { "done.png" }), ImportJobStatus.Completed, null, new ImportJobResult(false, new string[0]), null)
                });

                Assert.IsTrue(Field<Button>(form, "clearCompletedButton").Enabled);
                typeof(ImportQueueForm).GetMethod("ClearCompletedButton_Click", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, new object[] { form, EventArgs.Empty });
                Assert.IsTrue(called);
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
        public void Manage_menu_exposes_diagnostics_usage_and_log_folder_commands()
        {
            using (var form = new MainForm(new ProjectLifecycle()))
            {
                Assert.IsTrue(form.MainMenuStrip.Items.OfType<ToolStripMenuItem>().Single(item => item.Text == "管理").DropDownItems.OfType<ToolStripMenuItem>().Any(item => item.Text == "診斷資訊"));
            }
        }

        [STATestMethod]
        public void Help_menu_lists_shortcuts_with_their_scope_and_text_focus_conditions()
        {
            using (var form = new MainForm(new ProjectLifecycle()))
            using (var shortcuts = new KeyboardShortcutsForm())
            {
                ToolStripMenuItem help = form.MainMenuStrip.Items.OfType<ToolStripMenuItem>().Single(item => item.Text == "說明");
                Assert.IsTrue(help.DropDownItems.OfType<ToolStripMenuItem>().Any(item => item.Text == "快捷鍵"));
                Assert.AreEqual(3, shortcuts.Shortcuts.Columns.Count);
                string[] rows = shortcuts.Shortcuts.Items.Cast<ListViewItem>().Select(item => item.Text).ToArray();
                CollectionAssert.IsSubsetOf(new[] { "Ctrl+Z", "Ctrl+Shift+Z", "Ctrl+Y", "Ctrl+C", "Ctrl+V", "Ctrl+A", "F", "[", "Z", "X", "↑／↓", "Delete", "Esc" }, rows);
                StringAssert.Contains(shortcuts.Shortcuts.Items.Cast<ListViewItem>().Single(item => item.Text == "Ctrl+C").SubItems[2].Text, "文字輸入時");
                StringAssert.Contains(shortcuts.Shortcuts.Items.Cast<ListViewItem>().Single(item => item.Text == "Ctrl+V").SubItems[2].Text, "多張圖片");
                StringAssert.Contains(shortcuts.Shortcuts.Items.Cast<ListViewItem>().Single(item => item.Text == "Ctrl+V").SubItems[2].Text, "複製快照須屬於目前");
                StringAssert.Contains(shortcuts.Shortcuts.Items.Cast<ListViewItem>().Single(item => item.Text == "Ctrl+A").SubItems[2].Text, "不限制目前工具");
                StringAssert.Contains(shortcuts.Shortcuts.Items.Cast<ListViewItem>().Single(item => item.Text == "Delete").SubItems[2].Text, "不限制目前工具");
                StringAssert.Contains(shortcuts.Shortcuts.Items.Cast<ListViewItem>().Single(item => item.Text == "Delete").SubItems[2].Text, "取得焦點");
                StringAssert.Contains(shortcuts.Shortcuts.Items.Cast<ListViewItem>().Single(item => item.Text == "F").SubItems[2].Text, "文字輸入時不觸發");
                Assert.IsTrue(rows.Contains("滑鼠右鍵拖曳"));
                Assert.IsTrue(rows.Contains("Ctrl+滑鼠滾輪"));

                var textInput = new TextBox { Bounds = new Rectangle(20, 20, 120, 24) };
                form.Controls.Add(textInput);
                form.Show();
                System.Windows.Forms.Application.DoEvents();
                textInput.Focus();
                Assert.IsTrue((bool)typeof(MainForm).GetMethod("HasTextInputFocus", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, null));
                var textUndo = new KeyEventArgs(Keys.Control | Keys.Z);
                RaiseKey(form, textUndo);
                Assert.IsFalse(textUndo.Handled, "Ctrl+Z must remain available to the focused text input.");
            }
        }

        [TestMethod]
        public void Dataset_assignment_count_column_is_visible_only_for_obd_and_seg_sets()
        {
            using (var control = new DatasetAssignmentControl())
            {
                ListView list = GetDescendants(control).OfType<ListView>().Single();
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

        [STATestMethod]
        public void Visible_user_controls_initialize_static_designer_layout_without_runtime_data()
        {
            using (var imageBrowser = new ImageBrowserControl())
            using (var datasetAssignments = new DatasetAssignmentControl())
            using (var canvasHost = new CanvasScrollHost())
            {
                ListView imageList = Field<ListView>(imageBrowser, "list");
                Assert.AreEqual(3, imageList.Columns.Count);
                Assert.AreEqual(0, imageList.VirtualListSize);
                Assert.AreEqual("search", Field<TextBox>(imageBrowser, "search").Name);
                Assert.AreEqual(0, imageList.TabIndex);
                Assert.AreEqual(1, Field<TextBox>(imageBrowser, "search").TabIndex);

                ListView datasetList = Field<ListView>(datasetAssignments, "list");
                FlowLayoutPanel actions = Field<FlowLayoutPanel>(datasetAssignments, "actions");
                FlowLayoutPanel filters = Field<FlowLayoutPanel>(datasetAssignments, "filters");
                TableLayoutPanel workspace = Field<TableLayoutPanel>(datasetAssignments, "workspace");
                Panel listPanel = Field<Panel>(datasetAssignments, "listPanel");
                datasetAssignments.PerformLayout();
                Assert.AreEqual(6, datasetList.Columns.Count);
                Assert.AreEqual(0, datasetList.VirtualListSize);
                Assert.IsTrue(datasetList.Height >= 200, "The Designer surface must leave enough room to inspect the image list.");
                Assert.IsTrue(listPanel.Controls.Contains(datasetList));
                Assert.AreEqual(2, workspace.ColumnCount);
                Assert.AreEqual(DockStyle.Fill, actions.Dock);
                Assert.IsTrue(actions.AutoScroll, "The right operation column must remain usable at small viewports.");
                TableLayoutPanel manualActions = Field<TableLayoutPanel>(datasetAssignments, "manualActions");
                Assert.AreEqual(2, manualActions.ColumnCount);
                Assert.AreEqual(2, manualActions.RowCount);
                Assert.AreEqual(4, manualActions.Controls.Count);
                TableLayoutPanel splitPercentages = Field<TableLayoutPanel>(datasetAssignments, "splitPercentages");
                Assert.AreEqual(4, splitPercentages.ColumnCount);
                Assert.AreEqual(2, splitPercentages.RowCount);
                Assert.AreEqual(6, splitPercentages.Controls.Count);
                Assert.AreEqual(0, splitPercentages.GetPositionFromControl(Field<Label>(datasetAssignments, "trainLabel")).Column);
                Assert.AreEqual(0, splitPercentages.GetPositionFromControl(Field<Label>(datasetAssignments, "trainLabel")).Row);
                Assert.AreEqual(2, splitPercentages.GetPositionFromControl(Field<Label>(datasetAssignments, "testLabel")).Column);
                Assert.AreEqual(0, splitPercentages.GetPositionFromControl(Field<Label>(datasetAssignments, "testLabel")).Row);
                Assert.AreEqual(0, splitPercentages.GetPositionFromControl(Field<NumericUpDown>(datasetAssignments, "trainPercent")).Column);
                Assert.AreEqual(1, splitPercentages.GetPositionFromControl(Field<NumericUpDown>(datasetAssignments, "trainPercent")).Row);
                Assert.AreEqual(2, splitPercentages.GetPositionFromControl(Field<NumericUpDown>(datasetAssignments, "testPercent")).Column);
                Assert.AreEqual(1, splitPercentages.GetPositionFromControl(Field<NumericUpDown>(datasetAssignments, "testPercent")).Row);
                Assert.AreEqual("%", Field<Label>(datasetAssignments, "trainPercentSuffix").Text);
                Assert.AreEqual("%", Field<Label>(datasetAssignments, "testPercentSuffix").Text);
                Assert.AreEqual(2, splitPercentages.GetColumnSpan(Field<Label>(datasetAssignments, "trainLabel")));
                Assert.AreEqual(2, splitPercentages.GetColumnSpan(Field<Label>(datasetAssignments, "testLabel")));
                TableLayoutPanel splitActions = Field<TableLayoutPanel>(datasetAssignments, "splitActions");
                Assert.AreEqual(2, splitActions.ColumnCount);
                Assert.AreEqual(2, splitActions.Controls.Count);
                Assert.AreEqual("隨機切分", Field<Label>(datasetAssignments, "randomSplitLabel").Text);
                Assert.AreEqual("手動分配", Field<Label>(datasetAssignments, "manualSplitLabel").Text);
                Assert.AreEqual("資料集 Retest", Field<Label>(datasetAssignments, "retestSectionLabel").Text);
                Button splitSelected = Field<Button>(datasetAssignments, "splitSelectedButton");
                Assert.IsFalse(splitSelected.Enabled);
                datasetAssignments.CreateControl();
                ProjectImage splitImage = new ProjectImage("split-image", "split.png", "split.png", "hash", 1, 1, 1);
                datasetAssignments.LoadRows(new[] { new DatasetRow(splitImage, new DatasetAssignment("set", splitImage.Id, DatasetAssignmentStatus.Unassigned, false), new string[0], 0) });
                datasetList.SelectedIndices.Add(0);
                Assert.IsTrue(splitSelected.Enabled);
                datasetAssignments.Size = new Size(420, 180);
                datasetAssignments.PerformLayout();
                Assert.IsTrue(actions.VerticalScroll.Visible || actions.DisplayRectangle.Height > actions.ClientSize.Height, "The right operation column must scroll at a short viewport.");
                Assert.IsTrue(datasetList.Bottom <= datasetAssignments.ClientSize.Height, "The image list must remain inside the Designer surface.");
                Assert.AreEqual(4, GetDescendants(datasetAssignments).OfType<ComboBox>().Count());
                Assert.AreEqual(7, GetDescendants(datasetAssignments).OfType<Button>().Count());
                Assert.AreEqual(80m, Field<NumericUpDown>(datasetAssignments, "trainPercent").Value);
                Assert.AreEqual(20m, Field<NumericUpDown>(datasetAssignments, "testPercent").Value);
                Assert.AreEqual(0, datasetList.TabIndex);
                Assert.AreEqual(1, actions.TabIndex);
                Assert.AreEqual(2, filters.TabIndex);

                TableLayoutPanel layout = Field<TableLayoutPanel>(canvasHost, "layout");
                Assert.AreEqual(DockStyle.Fill, layout.Dock);
                Assert.AreEqual(2, layout.ColumnCount);
                Assert.AreEqual(2, layout.RowCount);
                Assert.AreEqual(2, layout.Controls.Count);
                Assert.AreEqual(1, Field<HScrollBar>(canvasHost, "horizontal").TabIndex);
                Assert.AreEqual(2, Field<VScrollBar>(canvasHost, "vertical").TabIndex);
                Assert.IsFalse(GetDescendants(canvasHost).OfType<ImageCanvasControl>().Any(), "The Designer constructor must not create the runtime canvas.");

                Field<NumericUpDown>(datasetAssignments, "trainPercent").Value = 70;
                Assert.AreEqual(30m, Field<NumericUpDown>(datasetAssignments, "testPercent").Value, "Runtime synchronization is wired once after Designer initialization.");
            }
        }

        [STATestMethod]
        public void Class_label_button_renders_color_block_name_and_active_accent()
        {
            using (var button = new ClassLabelButton { Text = "A very long class name", ClassColor = Color.MediumPurple, IsActive = true, Size = new Size(220, 34) })
            using (var rendered = new Bitmap(220, 34))
            {
                button.CreateControl();
                button.DrawToBitmap(rendered, button.ClientRectangle);
                Assert.AreEqual(Color.MediumPurple.ToArgb(), rendered.GetPixel(20, 17).ToArgb());
                Assert.AreEqual(SystemColors.Highlight.ToArgb(), rendered.GetPixel(1, 17).ToArgb());
                Assert.AreEqual("A very long class name", button.Text);
            }
        }

        [STATestMethod]
        public void Active_class_label_button_repaints_an_opaque_background_without_old_pixels()
        {
            using (var button = new ClassLabelButton { Text = "class1", ClassColor = Color.MediumPurple, IsActive = true, BackColor = Color.MediumPurple, Size = new Size(220, 34) })
            using (var firstFrame = new Bitmap(220, 34))
            using (var secondFrame = new Bitmap(220, 34))
            using (Graphics firstGraphics = Graphics.FromImage(firstFrame))
            using (Graphics secondGraphics = Graphics.FromImage(secondFrame))
            {
                firstGraphics.Clear(Color.Red);
                secondGraphics.Clear(Color.Lime);
                MethodInfo paint = typeof(ClassLabelButton).GetMethod("OnPaint", BindingFlags.Instance | BindingFlags.NonPublic);
                paint.Invoke(button, new object[] { new PaintEventArgs(firstGraphics, button.ClientRectangle) });
                paint.Invoke(button, new object[] { new PaintEventArgs(secondGraphics, button.ClientRectangle) });

                Assert.AreEqual(firstFrame.GetPixel(5, 17).ToArgb(), secondFrame.GetPixel(5, 17).ToArgb(), "The selected row background must replace pixels left by the previously displayed control.");
            }
        }

        [STATestMethod]
        public void Annotation_set_creation_dialog_shows_all_types_in_unselected_cards()
        {
            using (var dialog = new AnnotationSetCreationDialog())
            {
                Button[] cards = { Field<Button>(dialog, "claCard"), Field<Button>(dialog, "obdCard"), Field<Button>(dialog, "segCard") };
                Assert.AreEqual(3, Field<TableLayoutPanel>(dialog, "cardLayout").Controls.OfType<Button>().Count());
                CollectionAssert.AreEqual(new[] { "CLA", "OBD", "SEG" }, cards.Select(card => card.Text.Split('\r')[0]).ToArray());
                StringAssert.Contains(cards[0].Text, "影像分類");
                StringAssert.Contains(cards[0].Text, "整張影像");
                StringAssert.Contains(cards[1].Text, "物件偵測");
                StringAssert.Contains(cards[1].Text, "物件位置");
                StringAssert.Contains(cards[2].Text, "影像分割");
                StringAssert.Contains(cards[2].Text, "像素區域");
                Assert.IsFalse(dialog.AnnotationSetType.HasValue, "Opening the dialog should not preselect a type.");
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
                Assert.IsTrue(pages.TabPages[0].Controls.Contains(CanvasHost(form.ImageCanvas)));
                Assert.IsTrue(pages.TabPages[1].Controls.Contains(CanvasHost(form.AnnotationCanvas)));

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
                Assert.IsTrue(header.Bottom <= CanvasHost(form.AnnotationCanvas).Top);
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
                    Assert.IsTrue(imageHeader.Bottom <= CanvasHost(form.ImageCanvas).Top);
                    Assert.IsTrue(annotationHeader.Bottom <= CanvasHost(form.AnnotationCanvas).Top);
                    Assert.IsTrue(sidebar.Right <= annotations.ClientSize.Width);
                    Assert.IsTrue(CanvasHost(form.AnnotationCanvas).Right <= sidebar.Left);
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
                    FlowLayoutPanel classes = Field<FlowLayoutPanel>(form, "annotationClasses");
                    CollectionAssert.AreEquivalent(new[] { "segSelect", "segRectangle", "segEllipse", "segPolygon", "segBrush", "segLine", "segEraser" }, segTools.Select(control => control.Name).ToArray());
                    Assert.IsFalse(tools.AutoScroll, "Annotation tools must wrap without their own scrollbars.");
                    Assert.IsFalse(classes.AutoScroll, "Class labels must be shown without their own scrollbars.");
                    AssertControlsAreUsable(segTools, sidebar);
                    AssertControlsAreUsable(classes.Controls.Cast<Control>(), classes);
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
                    Assert.IsTrue(CanvasHost(form.AnnotationCanvas).Top >= header.Bottom, "Canvas overlaps header at {0:P0} DPI.", scale);
                    Assert.IsTrue(CanvasHost(form.AnnotationCanvas).Right <= sidebar.Left, "Canvas overlaps sidebar at {0:P0} DPI.", scale);
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
        public void F_fits_both_canvas_pages_in_every_annotation_tool_mode()
        {
            using (var form = new MainForm(new ProjectLifecycle()))
            {
                form.Show();
                System.Windows.Forms.Application.DoEvents();
                var pages = form.Controls.OfType<TabControl>().Single();
                form.AnnotationCanvas.SegEnabled = true;
                foreach (ImageCanvasControl.SegToolKind tool in new[]
                {
                    ImageCanvasControl.SegToolKind.Select,
                    ImageCanvasControl.SegToolKind.Rect,
                    ImageCanvasControl.SegToolKind.Ellipse,
                    ImageCanvasControl.SegToolKind.Polygon,
                    ImageCanvasControl.SegToolKind.Brush,
                    ImageCanvasControl.SegToolKind.Line,
                    ImageCanvasControl.SegToolKind.Eraser
                })
                {
                    pages.SelectedIndex = 1;
                    form.AnnotationCanvas.SegTool = tool;
                    form.AnnotationCanvas.SetZoomPercent(150);
                    RaiseKey(form, new KeyEventArgs(Keys.F));
                    Assert.IsTrue(form.AnnotationCanvas.ViewState.IsFitted, "F should fit the canvas in {0} mode.", tool);
                }

                form.AnnotationCanvas.SegEnabled = false;
                form.AnnotationCanvas.ObdEnabled = true;
                foreach (bool selectMode in new[] { false, true })
                {
                    form.AnnotationCanvas.ObdSelectMode = selectMode;
                    form.AnnotationCanvas.SetZoomPercent(150);
                    RaiseKey(form, new KeyEventArgs(Keys.F));
                    Assert.IsTrue(form.AnnotationCanvas.ViewState.IsFitted, "F should fit the canvas in OBD {0} mode.", selectMode ? "selection" : "drawing");
                }

                pages.SelectedIndex = 0;
                form.ImageCanvas.SetZoomPercent(150);
                RaiseKey(form, new KeyEventArgs(Keys.F));
                Assert.IsTrue(form.ImageCanvas.ViewState.IsFitted, "F should fit the image page.");
            }
        }

        [STATestMethod]
        public void F_discards_unfinished_seg_draw_and_move_gestures_without_submitting_them()
        {
            string directory = Path.Combine(Path.GetTempPath(), "Neurocle.Tests", Guid.NewGuid().ToString("N"));
            string path = Path.Combine(directory, "canvas.png");
            try
            {
                Directory.CreateDirectory(directory);
                CreateSolidImage(path, Color.Black, 40);
                using (var form = new MainForm(new ProjectLifecycle()))
                {
                    form.Show();
                    System.Windows.Forms.Application.DoEvents();
                    var pages = form.Controls.OfType<TabControl>().Single();
                    pages.SelectedIndex = 1;
                    ImageCanvasControl canvas = form.AnnotationCanvas;
                    canvas.CreateControl();
                    canvas.LoadImage(path);
                    WaitForImage(canvas, path);
                    canvas.SegEnabled = true;

                    int created = 0;
                    canvas.SegRegionCreated += (type, rectangle) => created++;
                    canvas.SegTool = ImageCanvasControl.SegToolKind.Rect;
                    Point start = ImagePointToScreen(canvas, 5, 5);
                    Point end = ImagePointToScreen(canvas, 25, 25);
                    RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, start.X, start.Y, 0));
                    RaiseMouse(canvas, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, end.X, end.Y, 0));
                    RaiseKey(form, new KeyEventArgs(Keys.F));
                    RaiseMouse(canvas, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, end.X, end.Y, 0));
                    Assert.AreEqual(0, created, "F should cancel the unfinished rectangle before mouse release.");

                    int pathsCreated = 0;
                    canvas.SegPathCreated += (type, points, width) => pathsCreated++;
                    canvas.SegTool = ImageCanvasControl.SegToolKind.Brush;
                    start = ImagePointToScreen(canvas, 5, 5);
                    end = ImagePointToScreen(canvas, 25, 25);
                    RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, start.X, start.Y, 0));
                    RaiseMouse(canvas, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, end.X, end.Y, 0));
                    RaiseKey(form, new KeyEventArgs(Keys.F));
                    RaiseMouse(canvas, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, end.X, end.Y, 0));
                    Assert.AreEqual(0, pathsCreated, "F should cancel the unfinished brush path before mouse release.");

                    int obdCreated = 0;
                    canvas.ObdRectangleCreated += rectangle => obdCreated++;
                    canvas.SegEnabled = false;
                    canvas.ObdEnabled = true;
                    canvas.ObdSelectMode = false;
                    start = ImagePointToScreen(canvas, 5, 5);
                    end = ImagePointToScreen(canvas, 25, 25);
                    RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, start.X, start.Y, 0));
                    RaiseMouse(canvas, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, end.X, end.Y, 0));
                    RaiseKey(form, new KeyEventArgs(Keys.F));
                    RaiseMouse(canvas, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, end.X, end.Y, 0));
                    Assert.AreEqual(0, obdCreated, "F should cancel the unfinished OBD rectangle before mouse release.");

                    SegRegion original = new SegRegion("region", "set", "image", "class", SegRegionType.Rect, 5, 5, 15, 15, 0);
                    canvas.ObdEnabled = false;
                    canvas.SegEnabled = true;
                    canvas.SegRegions = new[] { original };
                    canvas.SelectAllSeg();
                    int changed = 0;
                    canvas.SegRegionsChanged += regions => changed++;
                    canvas.SegTool = ImageCanvasControl.SegToolKind.Select;
                    start = ImagePointToScreen(canvas, 10, 10);
                    end = ImagePointToScreen(canvas, 18, 18);
                    RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, start.X, start.Y, 0));
                    RaiseMouse(canvas, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, end.X, end.Y, 0));
                    RaiseKey(form, new KeyEventArgs(Keys.F));
                    RaiseMouse(canvas, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, end.X, end.Y, 0));
                    Assert.AreEqual(0, changed, "F should discard a move preview without submitting an annotation change.");
                    Assert.AreEqual(5, canvas.SegRegions.Single().X, 0.001f);
                    Assert.AreEqual(5, canvas.SegRegions.Single().Y, 0.001f);
                    CollectionAssert.Contains(canvas.SelectedSegIds.ToArray(), original.Id);
                }
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [STATestMethod]
        public void Annotation_visibility_checkbox_and_bracket_shortcut_sync_across_images_and_sets_and_reset_on_project_open()
        {
            string directory = Path.Combine(Path.GetTempPath(), "Neurocle.Tests", Guid.NewGuid().ToString("N"));
            string projectPath = Path.Combine(directory, "project.neurocle");
            string nextProjectPath = Path.Combine(directory, "next-project.neurocle");
            string firstImagePath = Path.Combine(directory, "one.png");
            string secondImagePath = Path.Combine(directory, "two.png");
            string beforeExport = Path.Combine(directory, "before.json");
            string afterExport = Path.Combine(directory, "after.json");
            ProjectStore store = null;
            ProjectStore replacement = null;
            Directory.CreateDirectory(directory);
            try
            {
                using (var image = new Bitmap(100, 100))
                using (var graphics = Graphics.FromImage(image))
                {
                    graphics.Clear(Color.Black);
                    image.Save(firstImagePath, ImageFormat.Png);
                    image.Save(secondImagePath, ImageFormat.Png);
                }
                store = ProjectStore.Create(projectPath);
                ProjectImage[] images = store.ImportFiles(new[] { firstImagePath, secondImagePath }).ToArray();
                AnnotationSet firstSet = store.CreateAnnotationSet("first boxes", AnnotationSetType.Obd);
                AnnotationSet secondSet = store.CreateAnnotationSet("second boxes", AnnotationSetType.Obd);
                AnnotationClass annotationClass = store.GetClasses(firstSet.Id).Single();
                ObdAnnotation stored = store.AddObdAnnotation(firstSet.Id, images[0].Id, annotationClass.Id, 10, 10, 20, 20);
                store.ExportAnnotationJson(firstSet.Id, beforeExport);

                using (var form = new MainForm(new ProjectLifecycle()))
                {
                    ProjectStore activeStore = store;
                    Invoke(form, "ReplaceProject", activeStore);
                    form.Show();
                    System.Windows.Forms.Application.DoEvents();
                    form.Controls.OfType<TabControl>().Single().SelectedIndex = 1;
                    Invoke(form, "ApplyImageSelection", new ImageSelectedEventArgs(images[0], Path.Combine(activeStore.ImagesDirectory, images[0].ManagedFileName)));
                    WaitForImage(form.ImageCanvas, Path.Combine(activeStore.ImagesDirectory, images[0].ManagedFileName));
                    WaitForImage(form.AnnotationCanvas, Path.Combine(activeStore.ImagesDirectory, images[0].ManagedFileName));
                    CheckBox showAnnotations = Field<CheckBox>(form, "showAnnotations");
                    Assert.IsTrue(showAnnotations.Checked);
                    Assert.IsTrue(form.AnnotationCanvas.AnnotationsVisible);

                    showAnnotations.Checked = false;
                    Assert.IsFalse(form.AnnotationCanvas.AnnotationsVisible);
                    Assert.IsFalse(form.ImageCanvas.AnnotationsVisible);
                    TextBox search = Field<TextBox>(form.ImageBrowser, "search");
                    search.Focus();
                    RaiseKey(form, new KeyEventArgs(Keys.OemOpenBrackets));
                    Assert.IsFalse(showAnnotations.Checked, "The shortcut should not intercept a text input.");
                    showAnnotations.Focus();
                    RaiseKey(form, new KeyEventArgs(Keys.OemOpenBrackets));
                    Assert.IsTrue(showAnnotations.Checked);
                    Assert.IsTrue(form.AnnotationCanvas.AnnotationsVisible);
                    showAnnotations.Checked = false;

                    Invoke(form, "ApplyImageSelection", new ImageSelectedEventArgs(images[1], Path.Combine(activeStore.ImagesDirectory, images[1].ManagedFileName)));
                    WaitForImage(form.ImageCanvas, Path.Combine(activeStore.ImagesDirectory, images[1].ManagedFileName));
                    WaitForImage(form.AnnotationCanvas, Path.Combine(activeStore.ImagesDirectory, images[1].ManagedFileName));
                    Assert.IsFalse(showAnnotations.Checked, "Changing images should preserve the display toggle.");
                    ComboBox sets = Field<ComboBox>(form, "annotationSets");
                    sets.SelectedItem = AnnotationSet(sets, secondSet.Id);
                    Assert.IsFalse(showAnnotations.Checked, "Changing annotation sets should preserve the display toggle.");
                    activeStore.ExportAnnotationJson(firstSet.Id, afterExport);
                    string beforeJson = Regex.Replace(File.ReadAllText(beforeExport), "\"time\":\"[^\"]+\"", "\"time\":\"\"");
                    string afterJson = Regex.Replace(File.ReadAllText(afterExport), "\"time\":\"[^\"]+\"", "\"time\":\"\"");
                    Assert.AreEqual(beforeJson, afterJson, "Display changes must not change exported annotations.");
                    ObdAnnotation after = activeStore.GetObdAnnotations(firstSet.Id, images[0].Id).Single();
                    Assert.AreEqual(stored.Id, after.Id);
                    Assert.AreEqual(stored.X, after.X);
                    Assert.AreEqual(stored.Y, after.Y);

                    replacement = ProjectStore.Create(nextProjectPath);
                    Invoke(form, "ReplaceProject", replacement);
                    Assert.IsTrue(showAnnotations.Checked, "Opening another project should restore the default visible state.");
                    Assert.IsTrue(form.AnnotationCanvas.AnnotationsVisible);
                    form.Close();
                }
            }
            finally
            {
                store?.Dispose();
                replacement?.Dispose();
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [STATestMethod]
        public void Hidden_annotations_cannot_be_selected_but_new_obd_and_seg_previews_remain_visible()
        {
            string directory = Path.Combine(Path.GetTempPath(), "Neurocle.Tests", Guid.NewGuid().ToString("N"));
            string path = Path.Combine(directory, "canvas.png");
            try
            {
                Directory.CreateDirectory(directory);
                using (var image = new Bitmap(100, 100))
                using (var graphics = Graphics.FromImage(image))
                {
                    graphics.Clear(Color.Black);
                    image.Save(path, ImageFormat.Png);
                }
                using (var canvas = new ImageCanvasControl { Size = new Size(100, 100), ObdEnabled = true, ObdSelectMode = true })
                {
                    canvas.CreateControl();
                    canvas.LoadImage(path);
                    WaitForImage(canvas, path);
                    canvas.ObdAnnotations = new[] { new ObdAnnotation("obd", "set", "image", "class", 10, 10, 20, 20, 0) };
                    Point existingObd = ImagePointToScreen(canvas, 15, 15);
                    Assert.IsTrue(RenderPixel(canvas, existingObd.X, existingObd.Y).R > 0, "Visible OBD annotations should render over the image.");
                    canvas.SelectAllObd();
                    Assert.AreEqual(1, canvas.SelectedObdIds.Count);
                    canvas.AnnotationsVisible = false;
                    Assert.AreEqual(Color.Black.ToArgb(), RenderPixel(canvas, existingObd.X, existingObd.Y).ToArgb());
                    Assert.AreEqual(0, canvas.SelectedObdIds.Count, "Hiding annotations should clear an existing selection.");
                    canvas.SelectAllObd();
                    RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, existingObd.X, existingObd.Y, 0));
                    RaiseMouse(canvas, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, existingObd.X, existingObd.Y, 0));
                    Assert.AreEqual(0, canvas.SelectedObdIds.Count, "Hidden OBD annotations must not be selectable.");

                    int obdCreated = 0;
                    canvas.ObdRectangleCreated += rectangle =>
                    {
                        obdCreated++;
                        canvas.ObdAnnotations = canvas.ObdAnnotations.Concat(new[] { new ObdAnnotation("new-obd", "set", "image", "class", rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height, 1) }).ToArray();
                    };
                    canvas.ObdSelectMode = false;
                    Point start = ImagePointToScreen(canvas, 30, 30);
                    Point end = ImagePointToScreen(canvas, 50, 50);
                    RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, start.X, start.Y, 0));
                    RaiseMouse(canvas, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, end.X, end.Y, 0));
                    Assert.AreNotEqual(Color.Black.ToArgb(), RenderPixel(canvas, 35, 35).ToArgb(), "New OBD drawing preview should remain visible while overlays are hidden.");
                    RaiseMouse(canvas, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, end.X, end.Y, 0));
                    Assert.AreEqual(1, obdCreated);
                    Assert.AreEqual(Color.Black.ToArgb(), RenderPixel(canvas, 35, 35).ToArgb(), "Committed OBD annotations should remain hidden.");

                    canvas.ObdEnabled = false;
                    canvas.SegEnabled = true;
                    canvas.SegTool = ImageCanvasControl.SegToolKind.Select;
                    canvas.AnnotationsVisible = true;
                    SegRegion existingSeg = new SegRegion("seg", "set", "image", "class", SegRegionType.Rect, 60, 10, 20, 20, 0);
                    canvas.SegRegions = new[] { existingSeg };
                    Point existingSegPoint = ImagePointToScreen(canvas, 65, 15);
                    Assert.IsTrue(RenderPixel(canvas, existingSegPoint.X, existingSegPoint.Y).R > 0, "Visible SEG annotations should render over the image.");
                    canvas.SelectAllSeg();
                    Assert.AreEqual(1, canvas.SelectedSegIds.Count);
                    canvas.AnnotationsVisible = false;
                    Assert.AreEqual(Color.Black.ToArgb(), RenderPixel(canvas, existingSegPoint.X, existingSegPoint.Y).ToArgb());
                    Assert.AreEqual(0, canvas.SelectedSegIds.Count);
                    canvas.SelectAllSeg();
                    RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, existingSegPoint.X, existingSegPoint.Y, 0));
                    RaiseMouse(canvas, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, existingSegPoint.X, existingSegPoint.Y, 0));
                    Assert.AreEqual(0, canvas.SelectedSegIds.Count, "Hidden SEG annotations must not be selectable.");

                    int segCreated = 0;
                    canvas.SegRegionCreated += (type, rectangle) => segCreated++;
                    canvas.SegTool = ImageCanvasControl.SegToolKind.Rect;
                    start = ImagePointToScreen(canvas, 60, 40);
                    end = ImagePointToScreen(canvas, 80, 60);
                    RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, start.X, start.Y, 0));
                    RaiseMouse(canvas, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, end.X, end.Y, 0));
                    Assert.AreNotEqual(Color.Black.ToArgb(), RenderPixel(canvas, 65, 45).ToArgb(), "New SEG drawing preview should remain visible while overlays are hidden.");
                    RaiseMouse(canvas, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, end.X, end.Y, 0));
                    Assert.AreEqual(1, segCreated);
                    canvas.SegRegions = canvas.SegRegions.Concat(new[] { new SegRegion("new-seg", "set", "image", "class", SegRegionType.Rect, 60, 40, 20, 20, 1) }).ToArray();
                    Assert.AreEqual(Color.Black.ToArgb(), RenderPixel(canvas, 65, 45).ToArgb(), "Committed SEG annotations should remain hidden.");

                    int erases = 0;
                    canvas.SegEraseRequested += (points, width) => erases++;
                    canvas.SegTool = ImageCanvasControl.SegToolKind.Eraser;
                    start = ImagePointToScreen(canvas, 65, 15);
                    end = ImagePointToScreen(canvas, 70, 15);
                    RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, start.X, start.Y, 0));
                    RaiseMouse(canvas, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, end.X, end.Y, 0));
                    RaiseMouse(canvas, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, end.X, end.Y, 0));
                    Assert.AreEqual(1, erases, "The eraser must still submit a hidden-annotation erase gesture.");
                    Assert.IsTrue(canvas.SegRegions.Any(region => region.Id == existingSeg.Id), "The canvas keeps stored regions until the persistence command completes.");
                }
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [STATestMethod]
        public void Arrow_key_messages_switch_images_from_the_canvas_without_a_focus_navigation_press()
        {
            using (var form = new MainForm(new ProjectLifecycle()))
            {
                form.Show();
                form.Controls.OfType<TabControl>().Single().SelectedIndex = 1;
                form.ImageBrowser.LoadImages(new[]
                {
                    new ProjectImage("one", "one.png", "one.png", "hash", 1, 1, 1),
                    new ProjectImage("two", "two.png", "two.png", "hash", 1, 1, 2)
                }, Path.GetTempPath());
                form.AnnotationCanvas.Focus();
                foreach (string expected in new[] { "one", "two", "two" })
                {
                    Message key = Message.Create(form.AnnotationCanvas.Handle, 0x100, (IntPtr)Keys.Down, IntPtr.Zero);
                    form.AnnotationCanvas.PreProcessMessage(ref key);
                    CollectionAssert.AreEqual(new[] { expected }, form.ImageBrowser.SelectedImageIds.ToArray());
                }
                Message up = Message.Create(form.AnnotationCanvas.Handle, 0x100, (IntPtr)Keys.Up, IntPtr.Zero);
                form.AnnotationCanvas.PreProcessMessage(ref up);
                CollectionAssert.AreEqual(new[] { "one" }, form.ImageBrowser.SelectedImageIds.ToArray());
            }
        }

        [STATestMethod]
        public void Arrow_keys_nudge_selected_obd_and_seg_groups_once_and_reject_out_of_bounds_moves()
        {
            string directory = Path.Combine(Path.GetTempPath(), "Neurocle.Tests", Guid.NewGuid().ToString("N"));
            string projectPath = Path.Combine(directory, "project.neurocle");
            string firstImagePath = Path.Combine(directory, "one.png");
            string secondImagePath = Path.Combine(directory, "two.png");
            ProjectStore store = null;
            Directory.CreateDirectory(directory);
            try
            {
                CreateFilledImage(firstImagePath, Color.Black, 100);
                CreateFilledImage(secondImagePath, Color.Black, 100);
                store = ProjectStore.Create(projectPath);
                ProjectImage[] images = store.ImportFiles(new[] { firstImagePath, secondImagePath }).ToArray();
                AnnotationSet obdSet = store.CreateAnnotationSet("boxes", AnnotationSetType.Obd);
                AnnotationClass obdClass = store.GetClasses(obdSet.Id).Single();
                store.AddObdAnnotation(obdSet.Id, images[0].Id, obdClass.Id, 10, 10, 10, 10);
                store.AddObdAnnotation(obdSet.Id, images[0].Id, obdClass.Id, 40, 30, 10, 10);
                AnnotationSet segSet = store.CreateAnnotationSet("regions", AnnotationSetType.Seg);
                AnnotationClass segClass = store.GetClasses(segSet.Id).Single();
                store.AddSegRegion(segSet.Id, images[0].Id, segClass.Id, SegRegionType.Rect, 10, 10, 10, 10);
                store.AddSegRegion(segSet.Id, images[0].Id, segClass.Id, SegRegionType.Rect, 40, 30, 10, 10);

                using (var form = new MainForm(new ProjectLifecycle()))
                {
                    Invoke(form, "ReplaceProject", store);
                    form.Show();
                    System.Windows.Forms.Application.DoEvents();
                    form.Controls.OfType<TabControl>().Single().SelectedIndex = 1;
                    form.ImageBrowser.SelectImage(images[0].Id);
                    Invoke(form, "ApplyImageSelection", new ImageSelectedEventArgs(images[0], Path.Combine(store.ImagesDirectory, images[0].ManagedFileName)));
                    WaitForImage(form.AnnotationCanvas, Path.Combine(store.ImagesDirectory, images[0].ManagedFileName));
                    CollectionAssert.AreEqual(new[] { images[0].Id }, form.ImageBrowser.SelectedImageIds.ToArray());
                    ComboBox sets = Field<ComboBox>(form, "annotationSets");
                    sets.SelectedItem = AnnotationSet(sets, obdSet.Id);
                    ImageCanvasControl canvas = form.AnnotationCanvas;
                    canvas.SelectAllObd();
                    ObdAnnotation[] originalObd = store.GetObdAnnotations(obdSet.Id, images[0].Id).ToArray();

                    RaiseKey(form, new KeyEventArgs(Keys.Right));
                    RaiseKey(form, new KeyEventArgs(Keys.Right));
                    RaiseKey(form, new KeyEventArgs(Keys.Right));
                    CollectionAssert.AreEqual(originalObd.Select(value => value.X).ToArray(), store.GetObdAnnotations(obdSet.Id, images[0].Id).Select(value => value.X).ToArray(), "Repeated keydown should preview movement without writing history yet.");
                    RaiseKeyUp(form, new KeyEventArgs(Keys.Right));
                    ObdAnnotation[] movedObd = store.GetObdAnnotations(obdSet.Id, images[0].Id).ToArray();
                    CollectionAssert.AreEqual(new[] { originalObd[0].X + 3, originalObd[1].X + 3 }, movedObd.Select(value => value.X).ToArray());
                    Assert.AreEqual(originalObd[1].X - originalObd[0].X, movedObd[1].X - movedObd[0].X, "Multi-selection must keep relative positions.");
                    Invoke(form, "UndoOrRedoSelectedAnnotationSet", false);
                    CollectionAssert.AreEqual(originalObd.Select(value => value.X).ToArray(), store.GetObdAnnotations(obdSet.Id, images[0].Id).Select(value => value.X).ToArray(), "One Undo should revert the whole held-key movement.");

                    foreach (Keys key in new[] { Keys.Up, Keys.Down, Keys.Left, Keys.Right })
                    {
                        RaiseKey(form, new KeyEventArgs(key));
                        RaiseKeyUp(form, new KeyEventArgs(key));
                        ObdAnnotation[] moved = store.GetObdAnnotations(obdSet.Id, images[0].Id).ToArray();
                        int expectedX = key == Keys.Left ? -1 : key == Keys.Right ? 1 : 0;
                        int expectedY = key == Keys.Up ? -1 : key == Keys.Down ? 1 : 0;
                        Assert.AreEqual(originalObd[0].X + expectedX, moved[0].X);
                        Assert.AreEqual(originalObd[0].Y + expectedY, moved[0].Y);
                        CollectionAssert.AreEqual(new[] { images[0].Id }, form.ImageBrowser.SelectedImageIds.ToArray(), "Selected annotations should consume Up/Down instead of changing images.");
                        Invoke(form, "UndoOrRedoSelectedAnnotationSet", false);
                    }

                    ObdAnnotation firstObd = originalObd[0];
                    store.UpdateObdAnnotations(new[] { new ObdAnnotation(firstObd.Id, firstObd.AnnotationSetId, firstObd.ImageId, firstObd.ClassId, 0, firstObd.Y, firstObd.Width, firstObd.Height, firstObd.ZOrder) });
                    Invoke(form, "RefreshAnnotationClasses");
                    canvas.SelectAllObd();
                    ObdAnnotation[] atBoundary = store.GetObdAnnotations(obdSet.Id, images[0].Id).ToArray();
                    RaiseKey(form, new KeyEventArgs(Keys.Left));
                    RaiseKeyUp(form, new KeyEventArgs(Keys.Left));
                    CollectionAssert.AreEqual(atBoundary.Select(value => value.X).ToArray(), store.GetObdAnnotations(obdSet.Id, images[0].Id).Select(value => value.X).ToArray(), "One boundary member should prevent the entire group from moving.");

                    var textInput = new TextBox();
                    form.Controls.Add(textInput);
                    textInput.BringToFront();
                    textInput.Focus();
                    RaiseKey(form, new KeyEventArgs(Keys.Right));
                    RaiseKeyUp(form, new KeyEventArgs(Keys.Right));
                    textInput.Dispose();
                    CollectionAssert.AreEqual(atBoundary.Select(value => value.X).ToArray(), store.GetObdAnnotations(obdSet.Id, images[0].Id).Select(value => value.X).ToArray(), "Text input focus should prevent nudging.");

                    sets.SelectedItem = AnnotationSet(sets, segSet.Id);
                    canvas.SelectAllSeg();
                    SegRegion[] originalSeg = store.GetSegRegions(segSet.Id, images[0].Id).ToArray();
                    RaiseKey(form, new KeyEventArgs(Keys.Down));
                    RaiseKey(form, new KeyEventArgs(Keys.Down));
                    RaiseKeyUp(form, new KeyEventArgs(Keys.Down));
                    SegRegion[] movedSeg = store.GetSegRegions(segSet.Id, images[0].Id).ToArray();
                    CollectionAssert.AreEqual(originalSeg.Select(value => value.Y + 2).ToArray(), movedSeg.Select(value => value.Y).ToArray());
                    Invoke(form, "UndoOrRedoSelectedAnnotationSet", false);
                    CollectionAssert.AreEqual(originalSeg.Select(value => value.Y).ToArray(), store.GetSegRegions(segSet.Id, images[0].Id).Select(value => value.Y).ToArray(), "One Undo should revert the whole held-key SEG movement.");
                    form.Close();
                    store = null;
                }
            }
            finally
            {
                store?.Dispose();
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
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
        public void Obd_drag_selection_replaces_existing_selection_and_previews_partial_overlap()
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
                        new ObdAnnotation("first", "set", "image", "class", 5, 5, 15, 15, 0),
                        new ObdAnnotation("second", "set", "image", "class", 40, 40, 15, 15, 1),
                        new ObdAnnotation("third", "set", "image", "class", 65, 65, 15, 15, 2)
                    };
                    canvas.ModifierKeysProvider = () => Keys.None;
                    RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 10, 10, 0));
                    RaiseMouse(canvas, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 0, 10, 10, 0));
                    CollectionAssert.AreEquivalent(new[] { "first" }, canvas.SelectedObdIds.ToArray());

                    RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 30, 30, 0));
                    Assert.IsNotNull(Field<PointF?>(canvas, "selectionStart"), "A blank-canvas press should begin a drag-selection gesture.");
                    RaiseMouse(canvas, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, 45, 45, 0));
                    Assert.AreNotEqual(Color.Black.ToArgb(), RenderPixel(canvas, 32, 33).ToArgb(), "The active selection rectangle should be visible during dragging.");
                    RaiseMouse(canvas, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 0, 45, 45, 0));

                    CollectionAssert.AreEquivalent(new[] { "second" }, canvas.SelectedObdIds.ToArray(), string.Join(",", canvas.SelectedObdIds));
                    Assert.IsNull(Field<RectangleF?>(canvas, "selectionPreview"));
                }
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [STATestMethod]
        public void Ctrl_obd_drag_selection_unions_with_existing_selection_and_empty_click_preserves_it()
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
                        new ObdAnnotation("first", "set", "image", "class", 5, 5, 15, 15, 0),
                        new ObdAnnotation("second", "set", "image", "class", 40, 40, 15, 15, 1),
                        new ObdAnnotation("third", "set", "image", "class", 65, 65, 15, 15, 2)
                    };
                    canvas.ModifierKeysProvider = () => Keys.None;
                    RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 10, 10, 0));
                    RaiseMouse(canvas, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 0, 10, 10, 0));
                    canvas.ModifierKeysProvider = () => Keys.Control;

                    RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 30, 30, 0));
                    RaiseMouse(canvas, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, 80, 80, 0));
                    RaiseMouse(canvas, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 0, 80, 80, 0));
                    CollectionAssert.AreEquivalent(new[] { "first", "second", "third" }, canvas.SelectedObdIds.ToArray(), string.Join(",", canvas.SelectedObdIds));

                    RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 2, 2, 0));
                    RaiseMouse(canvas, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 0, 2, 2, 0));
                    CollectionAssert.AreEquivalent(new[] { "first", "second", "third" }, canvas.SelectedObdIds.ToArray());
                }
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [STATestMethod]
        public void Empty_obd_drag_selection_clears_selection_and_object_drag_still_moves()
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
                    canvas.ObdAnnotations = new[] { new ObdAnnotation("one", "set", "image", "class", 20, 20, 20, 20, 0) };
                    canvas.ModifierKeysProvider = () => Keys.None;
                    RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 25, 25, 0));
                    RaiseMouse(canvas, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 0, 25, 25, 0));
                    RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 2, 2, 0));
                    RaiseMouse(canvas, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 0, 2, 2, 0));
                    Assert.AreEqual(0, canvas.SelectedObdIds.Count, "A plain click on empty space should clear the selection.");
                    RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 25, 25, 0));
                    RaiseMouse(canvas, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 0, 25, 25, 0));
                    RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 2, 2, 0));
                    RaiseMouse(canvas, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, 5, 5, 0));
                    RaiseMouse(canvas, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 0, 5, 5, 0));
                    Assert.AreEqual(0, canvas.SelectedObdIds.Count);

                    RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 25, 25, 0));
                    RaiseMouse(canvas, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, 30, 30, 0));
                    RaiseMouse(canvas, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 0, 30, 30, 0));
                    CollectionAssert.AreEquivalent(new[] { "one" }, canvas.SelectedObdIds.ToArray());
                }
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [STATestMethod]
        public void Canvas_scrollbars_show_only_overflowing_axes_and_right_drag_shares_the_bounded_view()
        {
            string directory = Path.Combine(Path.GetTempPath(), "Neurocle.Tests", Guid.NewGuid().ToString("N"));
            string wideImagePath = Path.Combine(directory, "wide.png");
            string squareImagePath = Path.Combine(directory, "square.png");
            Directory.CreateDirectory(directory);
            try
            {
                using (var bitmap = new Bitmap(200, 50))
                {
                    using (Graphics graphics = Graphics.FromImage(bitmap)) graphics.Clear(Color.Black);
                    bitmap.Save(wideImagePath, ImageFormat.Png);
                }
                CreateSolidImage(squareImagePath, Color.Black, 200);
                using (var canvas = new ImageCanvasControl())
                using (var host = new CanvasScrollHost(canvas))
                using (var form = new Form { ClientSize = new Size(140, 140) })
                {
                    form.Controls.Add(host);
                    form.Show();
                    System.Windows.Forms.Application.DoEvents();
                    canvas.LoadImage(wideImagePath);
                    WaitForImage(canvas, wideImagePath);
                    canvas.SetZoomPercent(100d);
                    Assert.IsTrue(host.HorizontalScrollBar.Visible);
                    Assert.IsFalse(host.VerticalScrollBar.Visible);

                    canvas.LoadImage(squareImagePath);
                    WaitForImage(canvas, squareImagePath);
                    canvas.SetZoomPercent(100d);
                    Assert.IsTrue(host.HorizontalScrollBar.Visible);
                    Assert.IsTrue(host.VerticalScrollBar.Visible);

                    PointF beforeDrag = canvas.ViewState.Origin;
                    RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Right, 1, 60, 60, 0));
                    RaiseMouse(canvas, "OnMouseMove", new MouseEventArgs(MouseButtons.Right, 0, 80, 70, 0));
                    RaiseMouse(canvas, "OnMouseUp", new MouseEventArgs(MouseButtons.Right, 0, 80, 70, 0));
                    Assert.AreEqual(beforeDrag.X + 20f, canvas.ViewState.Origin.X, 0.001f);
                    Assert.AreEqual(beforeDrag.Y + 10f, canvas.ViewState.Origin.Y, 0.001f);
                    Assert.AreEqual((int)Math.Round(-canvas.ViewState.Origin.X), host.HorizontalScrollBar.Value);
                    Assert.AreEqual((int)Math.Round(-canvas.ViewState.Origin.Y), host.VerticalScrollBar.Value);

                    int oldScroll = host.HorizontalScrollBar.Value;
                    int newScroll = Math.Min(oldScroll + 10, host.HorizontalScrollBar.Maximum - host.HorizontalScrollBar.LargeChange + 1);
                    host.HorizontalScrollBar.Value = newScroll;
                    RaiseScroll(host.HorizontalScrollBar, new ScrollEventArgs(ScrollEventType.ThumbPosition, oldScroll, newScroll, ScrollOrientation.HorizontalScroll));
                    Assert.AreEqual(-newScroll, canvas.ViewState.Origin.X, 0.001f, string.Format("Moving the horizontal scrollbar should pan the same viewport (old={0}, new={1}, value={2}, max={3}, large={4}).", oldScroll, newScroll, host.HorizontalScrollBar.Value, host.HorizontalScrollBar.Maximum, host.HorizontalScrollBar.LargeChange));

                    canvas.SetScrollPosition(int.MaxValue, int.MaxValue);
                    Assert.AreEqual(canvas.ClientSize.Width - 200f, canvas.ViewState.Origin.X, 0.001f);
                    Assert.AreEqual(canvas.ClientSize.Height - 200f, canvas.ViewState.Origin.Y, 0.001f);
                    canvas.SetScrollPosition(int.MinValue, int.MinValue);
                    Assert.AreEqual(0f, canvas.ViewState.Origin.X, 0.001f);
                    Assert.AreEqual(0f, canvas.ViewState.Origin.Y, 0.001f);

                    canvas.SetZoomPercent(25d);
                    Assert.IsFalse(host.HorizontalScrollBar.Visible);
                    Assert.IsFalse(host.VerticalScrollBar.Visible);

                    form.ClientSize = new Size(300, 300);
                    System.Windows.Forms.Application.DoEvents();
                    canvas.SetZoomPercent(100d);
                    Assert.IsFalse(host.HorizontalScrollBar.Visible);
                    Assert.IsFalse(host.VerticalScrollBar.Visible);

                    form.ClientSize = new Size(140, 140);
                    System.Windows.Forms.Application.DoEvents();
                    Assert.IsTrue(host.HorizontalScrollBar.Visible);
                    Assert.IsTrue(host.VerticalScrollBar.Visible);
                }
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [STATestMethod]
        public void Main_form_scrollbars_and_right_drag_keep_image_and_annotation_views_synchronized()
        {
            string directory = Path.Combine(Path.GetTempPath(), "Neurocle.Tests", Guid.NewGuid().ToString("N"));
            string imagePath = Path.Combine(directory, "large.png");
            Directory.CreateDirectory(directory);
            try
            {
                CreateSolidImage(imagePath, Color.Black, 1600);
                using (var form = new MainForm(new ProjectLifecycle()))
                {
                    form.WindowState = FormWindowState.Normal;
                    form.ClientSize = new Size(1200, 800);
                    form.Show();
                    System.Windows.Forms.Application.DoEvents();
                    form.ImageCanvas.LoadImage(imagePath);
                    form.AnnotationCanvas.LoadImage(imagePath);
                    WaitForImage(form.ImageCanvas, imagePath);
                    WaitForImage(form.AnnotationCanvas, imagePath);
                    form.ImageCanvas.SetZoomPercent(100d);
                    ImageCanvasControl.CanvasThumbnailLayout imageThumbnail;
                    ImageCanvasControl.CanvasThumbnailLayout annotationThumbnail;
                    Assert.IsTrue(ImageCanvasControl.CanvasThumbnailLayout.TryCreate(new Size(1600, 1600), form.ImageCanvas.ClientSize, form.ImageCanvas.Zoom, form.ImageCanvas.ViewState.Origin, form.ImageCanvas.DeviceDpi / 96f, out imageThumbnail));
                    Assert.IsTrue(ImageCanvasControl.CanvasThumbnailLayout.TryCreate(new Size(1600, 1600), form.AnnotationCanvas.ClientSize, form.AnnotationCanvas.Zoom, form.AnnotationCanvas.ViewState.Origin, form.AnnotationCanvas.DeviceDpi / 96f, out annotationThumbnail));
                    Assert.IsTrue(imageThumbnail.ImageBounds.Contains(imageThumbnail.ViewportBounds));
                    Assert.IsTrue(annotationThumbnail.ImageBounds.Contains(annotationThumbnail.ViewportBounds));

                    CanvasScrollHost imageHost = CanvasHost(form.ImageCanvas);
                    CanvasScrollHost annotationHost = CanvasHost(form.AnnotationCanvas);
                    TabControl pages = form.Controls.OfType<TabControl>().Single();
                    pages.SelectedIndex = 1;
                    System.Windows.Forms.Application.DoEvents();
                    Assert.IsTrue(annotationHost.HorizontalScrollBar.Visible && annotationHost.VerticalScrollBar.Visible);

                    int oldScroll = annotationHost.HorizontalScrollBar.Value;
                    int newScroll = 100;
                    annotationHost.HorizontalScrollBar.Value = newScroll;
                    RaiseScroll(annotationHost.HorizontalScrollBar, new ScrollEventArgs(ScrollEventType.ThumbPosition, oldScroll, newScroll, ScrollOrientation.HorizontalScroll));
                    Assert.AreEqual(form.ImageCanvas.ViewState.Origin.X, form.AnnotationCanvas.ViewState.Origin.X, 0.001f);
                    Assert.AreEqual(newScroll, imageHost.HorizontalScrollBar.Value);
                    int oldVerticalScroll = annotationHost.VerticalScrollBar.Value;
                    int newVerticalScroll = 100;
                    annotationHost.VerticalScrollBar.Value = newVerticalScroll;
                    RaiseScroll(annotationHost.VerticalScrollBar, new ScrollEventArgs(ScrollEventType.ThumbPosition, oldVerticalScroll, newVerticalScroll, ScrollOrientation.VerticalScroll));
                    Assert.AreEqual(-newVerticalScroll, form.AnnotationCanvas.ViewState.Origin.Y, 0.001f);
                    pages.SelectedIndex = 0;
                    System.Windows.Forms.Application.DoEvents();
                    Assert.IsTrue(imageHost.HorizontalScrollBar.Visible && imageHost.VerticalScrollBar.Visible);

                    RaiseMouse(form.ImageCanvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Right, 1, 60, 60, 0));
                    RaiseMouse(form.ImageCanvas, "OnMouseMove", new MouseEventArgs(MouseButtons.Right, 0, 70, 80, 0));
                    RaiseMouse(form.ImageCanvas, "OnMouseUp", new MouseEventArgs(MouseButtons.Right, 0, 70, 80, 0));
                    Assert.AreEqual(form.ImageCanvas.ViewState.Origin, form.AnnotationCanvas.ViewState.Origin);
                    Assert.AreEqual((int)Math.Round(-form.ImageCanvas.ViewState.Origin.Y), imageHost.VerticalScrollBar.Value);
                    Assert.AreEqual((int)Math.Round(-form.AnnotationCanvas.ViewState.Origin.Y), annotationHost.VerticalScrollBar.Value);

                    ImageCanvasControl.CanvasViewState viewBeforeWheel = form.ImageCanvas.ViewState;
                    RaiseMouse(form.ImageCanvas, "OnMouseWheel", new MouseEventArgs(MouseButtons.None, 0, 70, 80, 120));
                    Assert.IsTrue(form.ImageCanvas.Zoom != viewBeforeWheel.Zoom || form.ImageCanvas.ViewState.Origin != viewBeforeWheel.Origin, "The wheel should pan or zoom the viewport.");
                    Assert.AreEqual(form.ImageCanvas.ViewState.Origin, form.AnnotationCanvas.ViewState.Origin);
                    Assert.AreEqual((int)Math.Round(-form.ImageCanvas.ViewState.Origin.Y), imageHost.VerticalScrollBar.Value);
                    Assert.AreEqual((int)Math.Round(-form.AnnotationCanvas.ViewState.Origin.Y), annotationHost.VerticalScrollBar.Value);

                    form.ImageCanvas.SetZoomPercent(125d);
                    Assert.AreEqual(form.ImageCanvas.Zoom, form.AnnotationCanvas.Zoom, 0.001d);
                    Assert.AreEqual((int)Math.Round(-form.ImageCanvas.ViewState.Origin.X), imageHost.HorizontalScrollBar.Value);
                    Assert.AreEqual((int)Math.Round(-form.AnnotationCanvas.ViewState.Origin.X), annotationHost.HorizontalScrollBar.Value);

                    pages.SelectedIndex = 1;
                    form.ClientSize = new Size(1100, 740);
                    System.Windows.Forms.Application.DoEvents();
                    Assert.AreEqual(form.ImageCanvas.ViewState.Origin, form.AnnotationCanvas.ViewState.Origin);
                    Assert.AreEqual((int)Math.Round(-form.ImageCanvas.ViewState.Origin.X), imageHost.HorizontalScrollBar.Value);
                    Assert.AreEqual((int)Math.Round(-form.AnnotationCanvas.ViewState.Origin.X), annotationHost.HorizontalScrollBar.Value);
                }
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [STATestMethod]
        public void Seg_drag_selection_uses_actual_fill_and_stroke_regions_including_holes_and_partial_overlap()
        {
            string directory = Path.Combine(Path.GetTempPath(), "Neurocle.Tests", Guid.NewGuid().ToString("N"));
            string imagePath = Path.Combine(directory, "source.png");
            Directory.CreateDirectory(directory);
            try
            {
                CreateSolidImage(imagePath, Color.Black, 100);
                using (var canvas = new ImageCanvasControl { Size = new Size(100, 100), SegEnabled = true, SegTool = ImageCanvasControl.SegToolKind.Select })
                {
                    canvas.CreateControl();
                    canvas.LoadImage(imagePath);
                    WaitForImage(canvas, imagePath);
                    Path64 donut = SegGeometryPrototype.BridgeHole(
                        SegGeometryPrototype.Rectangle(10, 10, 50, 50),
                        SegGeometryPrototype.Rectangle(20, 20, 40, 40));
                    canvas.SegRegions = new[]
                    {
                        new SegRegion("rect", "set", "image", "class", SegRegionType.Rect, 5, 65, 10, 10, 0),
                        new SegRegion("donut", "set", "image", "class", SegRegionType.PolyGon, 0, 0, 0, 0, 0,
                            donut.Select(point => new SegPoint((int)point.X, (int)point.Y)).ToArray()),
                        new SegRegion("line", "set", "image", "class", SegRegionType.PolyLine, 0, 0, 0, 0, 1,
                            new[] { new SegPoint(60, 10), new SegPoint(90, 40) }, 8),
                        new SegRegion("ellipse", "set", "image", "class", SegRegionType.Ellipse, 70, 80, 10, 8, 2)
                    };
                    canvas.ModifierKeysProvider = () => Keys.None;

                    RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 56, 37, 0));
                    RaiseMouse(canvas, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, 64, 45, 0));
                    RaiseMouse(canvas, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 0, 64, 45, 0));
                    Assert.AreEqual(0, canvas.SelectedSegIds.Count, "The polyline bounding-box corner is empty and should not select the stroke.");

                    RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 25, 25, 0));
                    RaiseMouse(canvas, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, 30, 30, 0));
                    RaiseMouse(canvas, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 0, 30, 30, 0));
                    Assert.AreEqual(0, canvas.SelectedSegIds.Count, "The bridged polygon hole should not count as filled area.");

                    RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 8, 8, 0));
                    RaiseMouse(canvas, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, 15, 15, 0));
                    RaiseMouse(canvas, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 0, 15, 15, 0));
                    CollectionAssert.AreEquivalent(new[] { "donut" }, canvas.SelectedSegIds.ToArray(), "A partial overlap with the filled ring should select the polygon.");

                    canvas.ModifierKeysProvider = () => Keys.Control;
                    RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 65, 25, 0));
                    RaiseMouse(canvas, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, 75, 27, 0));
                    RaiseMouse(canvas, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 0, 75, 27, 0));
                    CollectionAssert.AreEquivalent(new[] { "donut", "line" }, canvas.SelectedSegIds.ToArray(), "Ctrl drag should union a stroke hit with the existing selection.");
                    RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 65, 25, 0));
                    RaiseMouse(canvas, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, 75, 27, 0));
                    RaiseMouse(canvas, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 0, 75, 27, 0));
                    CollectionAssert.AreEquivalent(new[] { "donut", "line" }, canvas.SelectedSegIds.ToArray(), "Ctrl drag must not toggle an already-selected stroke off.");

                    canvas.ModifierKeysProvider = () => Keys.None;
                    RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 65, 25, 0));
                    RaiseMouse(canvas, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, 75, 27, 0));
                    RaiseMouse(canvas, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 0, 75, 27, 0));
                    CollectionAssert.AreEquivalent(new[] { "line" }, canvas.SelectedSegIds.ToArray(), "A normal SEG drag should replace the prior selection.");

                    RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 3, 64, 0));
                    RaiseMouse(canvas, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, 8, 67, 0));
                    RaiseMouse(canvas, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 0, 8, 67, 0));
                    CollectionAssert.AreEquivalent(new[] { "rect" }, canvas.SelectedSegIds.ToArray(), "A partial overlap with a SEG rectangle should select it.");

                    RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 57, 70, 0));
                    RaiseMouse(canvas, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, 71, 81, 0));
                    RaiseMouse(canvas, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 0, 71, 81, 0));
                    CollectionAssert.AreEquivalent(new[] { "ellipse" }, canvas.SelectedSegIds.ToArray(), "A partial overlap with a SEG ellipse should select it.");
                }
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
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
                    CollapseThumbnail(canvas, new Size(100, 100));

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
        public void Rotating_a_selected_seg_handle_rotates_every_region_around_its_own_center_and_previews_rotated_handles()
        {
            string directory = Path.Combine(Path.GetTempPath(), "Neurocle.Tests", Guid.NewGuid().ToString("N"));
            string imagePath = Path.Combine(directory, "source.png");
            Directory.CreateDirectory(directory);
            try
            {
                CreateSolidImage(imagePath, Color.Black, 100);
                using (var canvas = new ImageCanvasControl { Size = new Size(100, 100), SegEnabled = true, SegTool = ImageCanvasControl.SegToolKind.Select })
                {
                    canvas.CreateControl();
                    canvas.LoadImage(imagePath);
                    WaitForImage(canvas, imagePath);
                    canvas.SetViewState(new ImageCanvasControl.CanvasViewState(1d, PointF.Empty, false));
                    canvas.SegClasses = new Dictionary<string, AnnotationClass> { { "class", new AnnotationClass("class", "set", "label", "#00FF00") } };
                    Path64 donut = SegGeometryPrototype.BridgeHole(SegGeometryPrototype.Rectangle(45, 60, 65, 80), SegGeometryPrototype.Rectangle(50, 65, 60, 75));
                    SegRegion[] regions =
                    {
                        new SegRegion("rect", "set", "image", "class", SegRegionType.Rect, 20, 30, 20, 10, 0),
                        new SegRegion("ellipse", "set", "image", "class", SegRegionType.Ellipse, 60, 40, 6, 10, 1),
                        new SegRegion("polygon", "set", "image", "class", SegRegionType.PolyGon, 0, 0, 0, 0, 2, donut.Select(point => new SegPoint((int)point.X, (int)point.Y)).ToArray()),
                        new SegRegion("line", "set", "image", "class", SegRegionType.PolyLine, 0, 0, 0, 0, 3, new[] { new SegPoint(20, 75), new SegPoint(35, 75) }, 4)
                    };
                    canvas.SegRegions = regions;
                    canvas.SelectAllSeg();
                    IReadOnlyList<SegRegion> changed = null;
                    canvas.SegRegionsChanged += value =>
                    {
                        changed = value;
                        Dictionary<string, SegRegion> updates = value.ToDictionary(region => region.Id, StringComparer.Ordinal);
                        canvas.SegRegions = regions.Select(region => updates.ContainsKey(region.Id) ? updates[region.Id] : region).ToArray();
                    };

                    RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 30, 6, 0));
                    RaiseMouse(canvas, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, 58, 35, 0));

                    AssertWhiteRotatedHandleCenters(canvas, new RectangleF(20, 30, 20, 10), new PointF(30, 35), Math.PI / 2d);
                    AssertWhiteRotatedHandleCenters(canvas, new RectangleF(54, 30, 12, 20), new PointF(60, 40), Math.PI / 2d);
                    AssertWhiteRotatedHandleCenters(canvas, new RectangleF(45, 60, 20, 20), new PointF(55, 70), Math.PI / 2d);
                    AssertWhiteRotatedHandleCenters(canvas, new RectangleF(18, 73, 19, 4), new PointF(27.5f, 75), Math.PI / 2d);
                    RaiseMouse(canvas, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 0, 58, 35, 0));

                    Assert.IsNotNull(changed);
                    Assert.AreEqual(4, changed.Count, "A single rotation gesture must update every selected region.");
                    CollectionAssert.AreEquivalent(new[] { "rect", "ellipse", "polygon", "line" }, changed.Select(region => region.Id).ToArray());
                    Assert.AreEqual(SegRegionType.PolyGon, changed.Single(region => region.Id == "rect").Type);
                    Assert.AreEqual(SegRegionType.PolyGon, changed.Single(region => region.Id == "ellipse").Type);
                    Assert.AreEqual(SegRegionType.PolyLine, changed.Single(region => region.Id == "line").Type);
                    Assert.AreEqual(4, changed.Single(region => region.Id == "line").StrokeWidth);
                    Assert.AreEqual(regions.Single(region => region.Id == "polygon").Points.Count, changed.Single(region => region.Id == "polygon").Points.Count, "Rotation must retain the bridged hole path.");
                    CollectionAssert.AreEqual(
                        regions.Single(region => region.Id == "polygon").Points.Select(point => RotateTestPoint(new PointF(point.X, point.Y), new PointF(55, 70), Math.PI / 2d)).Select(point => new Point((int)Math.Round(point.X, MidpointRounding.AwayFromZero), (int)Math.Round(point.Y, MidpointRounding.AwayFromZero))).ToArray(),
                        changed.Single(region => region.Id == "polygon").Points.Select(point => new Point(point.X, point.Y)).ToArray(),
                        "The bridged polygon path must rotate as a whole and retain its hole bridge.");
                    foreach (SegRegion region in changed)
                    {
                        RectangleF oldBounds = GetTestSegBounds(regions.Single(value => value.Id == region.Id));
                        RectangleF newBounds = GetTestSegBounds(region);
                        Assert.AreEqual(oldBounds.Left + oldBounds.Width / 2f, newBounds.Left + newBounds.Width / 2f, 1f, "Rotation must keep each region's own centre.");
                        Assert.AreEqual(oldBounds.Top + oldBounds.Height / 2f, newBounds.Top + newBounds.Height / 2f, 1f, "Rotation must keep each region's own centre.");
                    }
                    AssertWhiteHandleCenters(canvas, new Rectangle(25, 25, 10, 20));
                }
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [STATestMethod]
        public void Multi_seg_rotation_keeps_the_last_legal_preview_when_any_region_would_leave_the_image()
        {
            string directory = Path.Combine(Path.GetTempPath(), "Neurocle.Tests", Guid.NewGuid().ToString("N"));
            string imagePath = Path.Combine(directory, "source.png");
            Directory.CreateDirectory(directory);
            try
            {
                CreateSolidImage(imagePath, Color.Black, 100);
                using (var canvas = new ImageCanvasControl { Size = new Size(100, 100), SegEnabled = true, SegTool = ImageCanvasControl.SegToolKind.Select })
                {
                    canvas.CreateControl();
                    canvas.LoadImage(imagePath);
                    WaitForImage(canvas, imagePath);
                    canvas.SetViewState(new ImageCanvasControl.CanvasViewState(1d, PointF.Empty, false));
                    canvas.SegClasses = new Dictionary<string, AnnotationClass> { { "class", new AnnotationClass("class", "set", "label", "#00FF00") } };
                    canvas.SegRegions = new[]
                    {
                        new SegRegion("pivot", "set", "image", "class", SegRegionType.Rect, 20, 30, 20, 10, 0),
                        new SegRegion("edge", "set", "image", "class", SegRegionType.Rect, 89, 30, 10, 10, 1)
                    };
                    canvas.SelectAllSeg();
                    IReadOnlyList<SegRegion> changed = null;
                    canvas.SegRegionsChanged += value => changed = value;

                    RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 30, 6, 0));
                    RaiseMouse(canvas, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, 35, 6, 0));
                    using (Bitmap lastLegalPreview = RenderCanvas(canvas))
                    {
                        RaiseMouse(canvas, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, 51, 15, 0));
                        using (Bitmap afterRejectedAngle = RenderCanvas(canvas)) AssertBitmapEquals(lastLegalPreview, afterRejectedAngle, "If one region would exceed the image, the whole group must keep its last legal preview.");
                    }

                    RaiseMouse(canvas, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 0, 51, 15, 0));

                    Assert.IsNotNull(changed);
                    Assert.AreEqual(2, changed.Count);
                    Assert.IsTrue(changed.All(region => region.Type == SegRegionType.PolyGon), "All selected regions must receive the same last accepted rotation.");
                    Assert.IsTrue(changed.SelectMany(region => region.Points).All(point => point.X >= 0 && point.Y >= 0 && point.X <= 100 && point.Y <= 100));
                }
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [STATestMethod]
        public void Multi_seg_rotation_updates_as_one_undo_redo_operation_and_persists_after_reopen()
        {
            string directory = Path.Combine(Path.GetTempPath(), "Neurocle.Tests", Guid.NewGuid().ToString("N"));
            string path = Path.Combine(directory, "project.neurocle");
            string imagePath = Path.Combine(directory, "source.png");
            ProjectStore store = null;
            Directory.CreateDirectory(directory);
            try
            {
                CreateSolidImage(imagePath, Color.Black, 100);
                store = ProjectStore.Create(path);
                ProjectImage image = store.ImportFiles(new[] { imagePath })[0];
                AnnotationSet set = store.CreateAnnotationSet("regions", AnnotationSetType.Seg);
                AnnotationClass annotationClass = store.GetClasses(set.Id).Single();
                SegRegion rectangle = store.AddSegRegion(set.Id, image.Id, annotationClass.Id, SegRegionType.Rect, 20, 30, 20, 10);
                SegRegion ellipse = store.AddSegRegion(set.Id, image.Id, annotationClass.Id, SegRegionType.Ellipse, 70, 40, 6, 10);
                string setId = set.Id;
                string imageId = image.Id;
                SegRegion[] original = { rectangle, ellipse };
                using (var canvas = new ImageCanvasControl { Size = new Size(100, 100), SegEnabled = true, SegTool = ImageCanvasControl.SegToolKind.Select })
                {
                    canvas.CreateControl();
                    canvas.LoadImage(imagePath);
                    WaitForImage(canvas, imagePath);
                    canvas.SetViewState(new ImageCanvasControl.CanvasViewState(1d, PointF.Empty, false));
                    canvas.SegClasses = new Dictionary<string, AnnotationClass> { { annotationClass.Id, annotationClass } };
                    canvas.SegRegions = original;
                    canvas.SelectAllSeg();
                    canvas.SegRegionsChanged += value =>
                    {
                        store.UpdateSegRegions(value);
                        canvas.SegRegions = store.GetSegRegions(setId, imageId);
                    };

                    RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 30, 6, 0));
                    RaiseMouse(canvas, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, 58, 35, 0));
                    RaiseMouse(canvas, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 0, 58, 35, 0));

                    SegRegion[] rotated = store.GetSegRegions(setId, imageId).ToArray();
                    Assert.AreEqual(2, rotated.Length);
                    Assert.IsTrue(rotated.All(region => region.Type == SegRegionType.PolyGon));
                    Assert.IsTrue(store.Undo(setId));
                    CollectionAssert.AreEqual(new[] { SegRegionType.Rect, SegRegionType.Ellipse }, store.GetSegRegions(setId, imageId).Select(region => region.Type).ToArray());
                    Assert.IsTrue(store.Redo(setId));
                    CollectionAssert.AreEqual(rotated.Select(region => region.Id).ToArray(), store.GetSegRegions(setId, imageId).Select(region => region.Id).ToArray());
                    foreach (SegRegion expected in rotated)
                    {
                        SegRegion actual = store.GetSegRegions(setId, imageId).Single(region => region.Id == expected.Id);
                        CollectionAssert.AreEqual(expected.Points.Select(point => new Point(point.X, point.Y)).ToArray(), actual.Points.Select(point => new Point(point.X, point.Y)).ToArray());
                    }
                }
                store.Dispose();
                store = ProjectStore.Open(path);
                Assert.AreEqual(2, store.GetSegRegions(setId, imageId).Count);
                Assert.IsTrue(store.GetSegRegions(setId, imageId).All(region => region.Type == SegRegionType.PolyGon));
            }
            finally
            {
                store?.Dispose();
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
        public void Z_and_x_adjust_seg_width_with_bounds_and_leave_ctrl_undo_shortcuts_unchanged()
        {
            string directory = Path.Combine(Path.GetTempPath(), "Neurocle.Tests", Guid.NewGuid().ToString("N"));
            string path = Path.Combine(directory, "project.neurocle");
            Directory.CreateDirectory(directory);
            ProjectStore store = null;
            try
            {
                store = ProjectStore.Create(path);
                using (var form = new MainForm(new ProjectLifecycle()))
                {
                    AnnotationSet seg = store.CreateAnnotationSet("regions", AnnotationSetType.Seg);
                    Invoke(form, "ReplaceProject", store);
                    TabControl pages = form.Controls.OfType<TabControl>().Single();
                    pages.SelectedIndex = 1;
                    Field<ComboBox>(form, "annotationSets").SelectedItem = AnnotationSet(Field<ComboBox>(form, "annotationSets"), seg.Id);
                    form.AnnotationCanvas.SegTool = ImageCanvasControl.SegToolKind.Brush;
                    form.Show();
                    System.Windows.Forms.Application.DoEvents();
                    form.AnnotationCanvas.Focus();
                    Assert.IsFalse((bool)typeof(MainForm).GetMethod("HasTextInputFocus", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, null));
                    Assert.AreEqual(seg.Id, ((AnnotationSet)Field<ComboBox>(form, "annotationSets").SelectedItem).Id);
                    NumericUpDown width = Field<NumericUpDown>(form, "segStrokeWidth");

                    var decrease = new KeyEventArgs(Keys.Z);
                    RaiseKey(form, decrease);
                    Assert.IsTrue(decrease.Handled);
                    Assert.AreEqual(14, width.Value);
                    Assert.AreEqual(14, Field<TrackBar>(form, "segStrokeWidthSlider").Value);
                    Assert.AreEqual(14, form.AnnotationCanvas.SegStrokeWidth);
                    RaiseKey(form, new KeyEventArgs(Keys.X));
                    Assert.AreEqual(15, width.Value);

                    width.Value = 1;
                    RaiseKey(form, new KeyEventArgs(Keys.Z));
                    Assert.AreEqual(1, width.Value, "Z should clamp at the minimum width.");
                    width.Value = 100;
                    RaiseKey(form, new KeyEventArgs(Keys.X));
                    Assert.AreEqual(100, width.Value, "X should clamp at the maximum width.");

                    width.Value = 40;
                    RaiseKey(form, new KeyEventArgs(Keys.Control | Keys.Z));
                    Assert.AreEqual(40, width.Value, "Ctrl+Z must remain the undo shortcut and must not change stroke width.");
                    RaiseKey(form, new KeyEventArgs(Keys.Control | Keys.Shift | Keys.Z));
                    Assert.AreEqual(40, width.Value, "Ctrl+Shift+Z must remain the redo shortcut.");
                    RaiseKey(form, new KeyEventArgs(Keys.Control | Keys.Y));
                    Assert.AreEqual(40, width.Value, "Ctrl+Y must remain the redo shortcut.");
                    width.Focus();
                    System.Windows.Forms.Application.DoEvents();
                    RaiseKey(form, new KeyEventArgs(Keys.Z));
                    Assert.AreEqual(40, width.Value, "Typing in the width field must not trigger the Z shortcut.");
                }
            }
            finally
            {
                store?.Dispose();
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
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
                Assert.AreEqual(40, opacity.Value);
                Assert.AreEqual(40, opacitySlider.Value);
                Assert.AreEqual(0.4f, form.AnnotationCanvas.ObdOpacity);
                Assert.AreEqual(0.4f, form.AnnotationCanvas.SegOpacity);
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
                    canvas.SegStrokeWidth = 12;
                    canvas.SegTool = ImageCanvasControl.SegToolKind.Brush;
                    RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 20, 40, 0));
                    RaiseMouse(canvas, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, 40, 40, 0));
                    canvas.SegStrokeWidth = 33;
                    RaiseMouse(canvas, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 0, 40, 40, 0));

                    canvas.SegTool = ImageCanvasControl.SegToolKind.Line;
                    canvas.SegStrokeWidth = 18;
                    RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 10, 50, 0));
                    canvas.SegStrokeWidth = 44;
                    RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 30, 50, 0));
                    CollectionAssert.AreEqual(new[] { 1, 1, 1, 15, 15, 15, 100, 100, 100, 12, 18 }, submitted);
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
                    canvas.SegEnabled = true;
                    canvas.SegTool = ImageCanvasControl.SegToolKind.Brush;
                    Assert.AreEqual(Cursors.Arrow, canvas.Cursor, "The system pointer should remain visible without an image.");
                    canvas.SegEnabled = false;
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
                    CollapseThumbnail(canvas, new Size(100, 100));
                    Assert.AreEqual(Color.White.ToArgb(), RenderPixel(canvas, 38, 50).ToArgb(), "Cursor ring should scale with canvas zoom.");

                    var symbolPixelCounts = new List<int>();
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
                        symbolPixelCounts.Add(CountNonBlackPixels(canvas, new Rectangle(57, 57, 16, 16)));
                    }
                    Assert.AreEqual(symbolPixelCounts[0], symbolPixelCounts[1], "Brush and line should use the same + cursor symbol.");
                    Assert.AreNotEqual(symbolPixelCounts[0], symbolPixelCounts[2], "Eraser should use the − cursor symbol.");

                    canvas.SegTool = ImageCanvasControl.SegToolKind.Brush;
                    RaiseMouse(canvas, "OnMouseMove", new MouseEventArgs(MouseButtons.None, 0, 0, 50, 0));
                    Assert.AreEqual(Color.White.ToArgb(), RenderPixel(canvas, 0, 50).ToArgb(), "Brush cursor ring should reach the image edge after its center is clamped inward.");
                    canvas.SegStrokeWidth = 30;
                    Assert.AreEqual(Color.White.ToArgb(), RenderPixel(canvas, 0, 50).ToArgb(), "Changing width with the pointer stationary should recalculate the edge-clamped center.");

                    RaiseMouse(canvas, "OnMouseMove", new MouseEventArgs(MouseButtons.None, 0, 50, 50, 0));
                    Assert.AreEqual(Color.White.ToArgb(), RenderPixel(canvas, 35, 50).ToArgb());
                    canvas.LoadImage(imagePath);
                    WaitForImage(canvas, imagePath);
                    Assert.AreEqual(Color.White.ToArgb(), RenderPixel(canvas, 35, 50).ToArgb(), "Switching images under the pointer should restore the cursor at its current location.");

                    canvas.SetZoomPercent(100);
                    canvas.SegTool = ImageCanvasControl.SegToolKind.Rect;
                    RaiseMouse(canvas, "OnMouseMove", new MouseEventArgs(MouseButtons.None, 0, 50, 50, 0));
                    Assert.AreEqual(Color.White.ToArgb(), RenderPixel(canvas, 44, 50).ToArgb());
                    RaiseEvent(canvas, "OnMouseLeave", EventArgs.Empty);
                    Assert.AreEqual(Cursors.Arrow, canvas.Cursor, "Leaving the canvas should restore the system pointer.");
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
        public void Annotation_cursor_and_thumbnail_follow_pointer_and_gesture_state()
        {
            string directory = Path.Combine(Path.GetTempPath(), "Neurocle.Tests", Guid.NewGuid().ToString("N"));
            string imagePath = Path.Combine(directory, "source.png");
            Directory.CreateDirectory(directory);
            try
            {
                using (var source = new Bitmap(400, 400))
                {
                    using (Graphics graphics = Graphics.FromImage(source)) graphics.Clear(Color.Black);
                    source.Save(imagePath, ImageFormat.Png);
                }
                using (var canvas = new ImageCanvasControl
                {
                    Size = new Size(200, 200),
                    SegEnabled = true,
                    SegTool = ImageCanvasControl.SegToolKind.Line
                })
                {
                    canvas.CreateControl();
                    canvas.LoadImage(imagePath);
                    WaitForImage(canvas, imagePath);
                    canvas.SetZoomPercent(100);
                    ImageCanvasControl.CanvasThumbnailLayout layout;
                    Assert.IsTrue(ImageCanvasControl.CanvasThumbnailLayout.TryCreate(new Size(400, 400), canvas.ClientSize, canvas.Zoom, canvas.ViewState.Origin, canvas.DeviceDpi / 96f, out layout));

                    RaiseMouse(canvas, "OnMouseMove", new MouseEventArgs(MouseButtons.None, 0, (int)layout.ImageBounds.Left + 5, (int)layout.ImageBounds.Top + 5, 0));
                    Assert.AreEqual(Cursors.Arrow, canvas.Cursor, "The system pointer should remain visible over the thumbnail.");

                    bool created = false;
                    canvas.SegPathCreated += (type, points, width) => created = true;
                    RaiseMouse(canvas, "OnMouseMove", new MouseEventArgs(MouseButtons.None, 0, 10, 10, 0));
                    RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 10, 10, 0));
                    Assert.IsTrue(Field<bool>(canvas, "thumbnailHiddenForGesture"), "Starting a line gesture should hide the thumbnail.");
                    Point thumbnailPoint = Point.Round(new PointF(layout.ImageBounds.Left + layout.ImageBounds.Width / 2f, layout.ImageBounds.Top + layout.ImageBounds.Height / 2f));
                    RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, thumbnailPoint.X, thumbnailPoint.Y, 0));
                    Assert.IsTrue(created, "A second line click over the former thumbnail must still complete the annotation.");
                    Assert.IsFalse(Field<bool>(canvas, "thumbnailHiddenForGesture"), "Completing a line gesture should restore the thumbnail.");

                    foreach (ImageCanvasControl.SegToolKind tool in new[]
                    {
                        ImageCanvasControl.SegToolKind.Rect,
                        ImageCanvasControl.SegToolKind.Ellipse,
                        ImageCanvasControl.SegToolKind.Polygon,
                        ImageCanvasControl.SegToolKind.Brush,
                        ImageCanvasControl.SegToolKind.Line,
                        ImageCanvasControl.SegToolKind.Eraser
                    })
                    {
                        canvas.DiscardUnfinishedShape();
                        canvas.SegTool = tool;
                        RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 10, 10, 0));
                        Assert.IsTrue(Field<bool>(canvas, "thumbnailHiddenForGesture"), "Starting {0} should hide the thumbnail.", tool);
                        canvas.DiscardUnfinishedShape();
                        Assert.IsFalse(Field<bool>(canvas, "thumbnailHiddenForGesture"), "Cancelling {0} should restore the thumbnail.", tool);
                    }

                    canvas.SegTool = ImageCanvasControl.SegToolKind.Brush;
                    int brushCommits = 0;
                    canvas.SegPathCreated += (type, points, width) => brushCommits++;
                    foreach (Point point in new[] { new Point(20, 20), new Point(40, 40), new Point(60, 60) })
                    {
                        RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, point.X, point.Y, 0));
                        Assert.IsTrue(Field<bool>(canvas, "thumbnailHiddenForGesture"));
                        RaiseMouse(canvas, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 0, point.X, point.Y, 0));
                        Assert.IsFalse(Field<bool>(canvas, "thumbnailHiddenForGesture"), "Completing a brush stroke must restore the thumbnail.");
                    }
                    Assert.AreEqual(3, brushCommits, "Each released brush stroke is one completed annotation.");

                    canvas.SegTool = ImageCanvasControl.SegToolKind.Line;
                    RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 10, 10, 0));
                    canvas.AnnotationsVisible = false;
                    Assert.IsFalse(Field<bool>(canvas, "thumbnailHiddenForGesture"), "Cancelling through annotation visibility must restore the thumbnail.");
                    Assert.IsNull(Field<object>(canvas, "segLineStart"), "Cancelling through annotation visibility must discard the unfinished line.");
                    canvas.AnnotationsVisible = true;

                    canvas.SegEnabled = false;
                    canvas.ObdEnabled = true;
                    canvas.ObdSelectMode = false;
                    RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 10, 10, 0));
                    Assert.IsTrue(Field<bool>(canvas, "thumbnailHiddenForGesture"), "Starting an OBD drawing gesture should hide the thumbnail.");
                    canvas.DiscardUnfinishedShape();
                    Assert.IsFalse(Field<bool>(canvas, "thumbnailHiddenForGesture"));
                }

                using (var source = new Bitmap(100, 100))
                {
                    using (Graphics graphics = Graphics.FromImage(source)) graphics.Clear(Color.Black);
                    source.Save(imagePath, ImageFormat.Png);
                }
                using (var canvas = new ImageCanvasControl
                {
                    Size = new Size(200, 200),
                    SegEnabled = true,
                    SegTool = ImageCanvasControl.SegToolKind.Rect
                })
                {
                    canvas.CreateControl();
                    canvas.LoadImage(imagePath);
                    WaitForImage(canvas, imagePath);
                    canvas.SetZoomPercent(100);
                    foreach (Point outside in new[] { new Point(0, 0), new Point(199, 0), new Point(0, 199), new Point(199, 199) })
                    {
                        RaiseMouse(canvas, "OnMouseMove", new MouseEventArgs(MouseButtons.None, 0, outside.X, outside.Y, 0));
                        Assert.AreEqual(Cursors.Arrow, canvas.Cursor, "The system pointer should remain visible outside the image at {0}.", outside);
                    }
                    Rectangle? created = null;
                    canvas.SegRegionCreated += (type, rectangle) => created = rectangle;
                    RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0));
                    RaiseMouse(canvas, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, 150, 150, 0));
                    RaiseMouse(canvas, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 0, 150, 150, 0));
                    Assert.IsFalse(created.HasValue, "A left click outside the image must not start an annotation.");

                    RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 100, 100, 0));
                    RaiseMouse(canvas, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, 199, 199, 0));
                    RaiseMouse(canvas, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 0, 199, 199, 0));
                    Assert.IsTrue(created.HasValue, "A gesture started inside the image must continue when dragged outside.");
                    Assert.IsTrue(created.Value.Left >= 0 && created.Value.Top >= 0 && created.Value.Right <= 100 && created.Value.Bottom <= 100,
                        "The continued gesture must remain clamped to the image boundary.");

                    SegPoint[] brushPath = null;
                    canvas.SegTool = ImageCanvasControl.SegToolKind.Brush;
                    canvas.SegPathCreated += (type, points, width) => brushPath = points.ToArray();
                    RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 100, 100, 0));
                    RaiseMouse(canvas, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, 199, 199, 0));
                    RaiseMouse(canvas, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 0, 199, 199, 0));
                    Assert.IsNotNull(brushPath, "A brush stroke started inside the image must continue outside it.");
                    Assert.IsTrue(brushPath.All(point => point.X >= 0 && point.Y >= 0 && point.X < 100 && point.Y < 100),
                        "Every continued brush point must stay inside the image boundary.");
                }
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [STATestMethod]
        public void Drawing_tools_ignore_new_left_presses_outside_the_image()
        {
            string directory = Path.Combine(Path.GetTempPath(), "Neurocle.Tests", Guid.NewGuid().ToString("N"));
            string imagePath = Path.Combine(directory, "source.png");
            Directory.CreateDirectory(directory);
            try
            {
                CreateFilledImage(imagePath, Color.Black, 100);
                using (var canvas = new ImageCanvasControl { Size = new Size(200, 200), SegEnabled = true })
                {
                    canvas.CreateControl();
                    canvas.LoadImage(imagePath);
                    WaitForImage(canvas, imagePath);
                    canvas.SetZoomPercent(100);
                    int submissions = 0;
                    canvas.SegRegionCreated += (type, rectangle) => submissions++;
                    canvas.SegPathCreated += (type, points, width) => submissions++;
                    canvas.SegEraseRequested += (points, width) => submissions++;
                    canvas.SegGeometryEraseRequested += (type, rectangle, points, width) => submissions++;

                    foreach (ImageCanvasControl.SegToolKind tool in new[]
                    {
                        ImageCanvasControl.SegToolKind.Rect,
                        ImageCanvasControl.SegToolKind.Ellipse,
                        ImageCanvasControl.SegToolKind.Polygon,
                        ImageCanvasControl.SegToolKind.Brush,
                        ImageCanvasControl.SegToolKind.Line,
                        ImageCanvasControl.SegToolKind.Eraser
                    })
                    {
                        canvas.DiscardUnfinishedShape();
                        canvas.SegTool = tool;
                        int before = submissions;
                        RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0));
                        RaiseMouse(canvas, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, 100, 100, 0));
                        RaiseMouse(canvas, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 0, 100, 100, 0));
                        Assert.AreEqual(before, submissions, "{0} must ignore a new left press outside the image.", tool);
                        Assert.IsFalse(Field<bool>(canvas, "thumbnailHiddenForGesture"), "{0} must not start a hidden-thumbnail gesture outside the image.", tool);
                        Assert.IsFalse(canvas.Capture, "{0} must not capture the mouse outside the image.", tool);
                    }

                    foreach (ImageCanvasControl.SegToolKind tool in new[] { ImageCanvasControl.SegToolKind.Line, ImageCanvasControl.SegToolKind.Polygon })
                    {
                        canvas.DiscardUnfinishedShape();
                        canvas.SegTool = tool;
                        int before = submissions;
                        RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 100, 100, 0));
                        Assert.IsTrue(Field<bool>(canvas, "thumbnailHiddenForGesture"));
                        RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0));
                        Assert.AreEqual(before, submissions, "{0} must ignore a later discrete click outside the image.", tool);
                        Assert.IsTrue(Field<bool>(canvas, "thumbnailHiddenForGesture"), "Ignoring the outside click must preserve the existing {0} gesture.", tool);
                        if (tool == ImageCanvasControl.SegToolKind.Line) Assert.IsNotNull(Field<object>(canvas, "segLineStart"));
                        else Assert.AreEqual(1, Field<List<PointF>>(canvas, "segPath").Count);
                    }

                    canvas.DiscardUnfinishedShape();
                    canvas.SegEnabled = false;
                    canvas.ObdEnabled = true;
                    canvas.ObdSelectMode = false;
                    int obdCreated = 0;
                    canvas.ObdRectangleCreated += rectangle => obdCreated++;
                    RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0));
                    RaiseMouse(canvas, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, 100, 100, 0));
                    RaiseMouse(canvas, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 0, 100, 100, 0));
                    Assert.AreEqual(0, obdCreated, "OBD rectangle drawing must ignore a new left press outside the image.");
                    Assert.IsFalse(Field<bool>(canvas, "thumbnailHiddenForGesture"));
                    Assert.IsFalse(canvas.Capture);
                }
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [STATestMethod]
        public void Continuous_drawing_gestures_started_inside_clamp_to_every_image_edge_at_multiple_zooms()
        {
            string directory = Path.Combine(Path.GetTempPath(), "Neurocle.Tests", Guid.NewGuid().ToString("N"));
            string imagePath = Path.Combine(directory, "source.png");
            Directory.CreateDirectory(directory);
            try
            {
                CreateFilledImage(imagePath, Color.Black, 100);
                using (var canvas = new ImageCanvasControl { Size = new Size(200, 200) })
                {
                    canvas.CreateControl();
                    canvas.LoadImage(imagePath);
                    WaitForImage(canvas, imagePath);
                    Rectangle? obdRectangle = null;
                    Rectangle? segRectangle = null;
                    SegPoint[] segPath = null;
                    SegPoint[] erasedPath = null;
                    canvas.ObdRectangleCreated += value => obdRectangle = value;
                    canvas.SegRegionCreated += (type, value) => segRectangle = value;
                    canvas.SegPathCreated += (type, points, width) => segPath = points.ToArray();
                    canvas.SegEraseRequested += (points, width) => erasedPath = points.ToArray();
                    PointF[] outsideImagePoints =
                    {
                        new PointF(120, 75), new PointF(-20, 25), new PointF(25, -20), new PointF(75, 120),
                        new PointF(-20, -20), new PointF(120, -20), new PointF(-20, 120), new PointF(120, 120)
                    };

                    foreach (int zoom in new[] { 100, 200 })
                    {
                        canvas.SetZoomPercent(zoom);
                        float scale = (float)canvas.Zoom;
                        PointF origin = canvas.ViewState.Origin;
                        Point start = Point.Round(new PointF(origin.X + 50 * scale, origin.Y + 50 * scale));
                        foreach (PointF outsideImage in outsideImagePoints)
                        {
                            Point outside = Point.Round(new PointF(origin.X + outsideImage.X * scale, origin.Y + outsideImage.Y * scale));
                            canvas.DiscardUnfinishedShape();
                            canvas.SegEnabled = false;
                            canvas.ObdEnabled = true;
                            canvas.ObdSelectMode = false;
                            obdRectangle = null;
                            RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, start.X, start.Y, 0));
                            Assert.AreEqual("Create", Field<object>(canvas, "obdDrag").ToString(), "OBD must begin inside the image at zoom {0}.", zoom);
                            RaiseMouse(canvas, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, outside.X, outside.Y, 0));
                            Assert.IsTrue(Field<RectangleF?>(canvas, "obdPreview").Value.Width >= 1f, "OBD preview must reach the clamped edge.");
                            RaiseMouse(canvas, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 0, outside.X, outside.Y, 0));
                            Assert.IsTrue(obdRectangle.HasValue, "OBD must continue at zoom {0} toward {1}.", zoom, outsideImage);
                            AssertRectangleInsideImage(obdRectangle.Value, zoom, outsideImage, "OBD");

                            canvas.ObdEnabled = false;
                            canvas.SegEnabled = true;
                            foreach (ImageCanvasControl.SegToolKind tool in new[] { ImageCanvasControl.SegToolKind.Rect, ImageCanvasControl.SegToolKind.Ellipse, ImageCanvasControl.SegToolKind.Brush, ImageCanvasControl.SegToolKind.Eraser })
                            {
                                canvas.DiscardUnfinishedShape();
                                canvas.SegTool = tool;
                                segRectangle = null;
                                segPath = null;
                                erasedPath = null;
                                RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, start.X, start.Y, 0));
                                RaiseMouse(canvas, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, outside.X, outside.Y, 0));
                                RaiseMouse(canvas, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 0, outside.X, outside.Y, 0));
                                if (tool == ImageCanvasControl.SegToolKind.Rect || tool == ImageCanvasControl.SegToolKind.Ellipse)
                                {
                                    Assert.IsTrue(segRectangle.HasValue, "{0} must continue at zoom {1} toward {2}.", tool, zoom, outsideImage);
                                    AssertRectangleInsideImage(segRectangle.Value, zoom, outsideImage, tool.ToString());
                                }
                                else
                                {
                                    SegPoint[] points = tool == ImageCanvasControl.SegToolKind.Eraser ? erasedPath : segPath;
                                    Assert.IsNotNull(points, "{0} must continue at zoom {1} toward {2}.", tool, zoom, outsideImage);
                                    Assert.IsTrue(points.All(point => point.X >= 0 && point.Y >= 0 && point.X < 100 && point.Y < 100),
                                        "{0} points must remain inside the image at zoom {1} toward {2}.", tool, zoom, outsideImage);
                                }
                            }

                            canvas.DiscardUnfinishedShape();
                            canvas.SegTool = ImageCanvasControl.SegToolKind.Line;
                            segPath = null;
                            RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, start.X, start.Y, 0));
                            RaiseMouse(canvas, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, outside.X, outside.Y, 0));
                            int lineInset = (canvas.SegStrokeWidth + 1) / 2;
                            PointF lineEnd = new PointF(
                                Math.Max(lineInset, Math.Min(100 - lineInset, outsideImage.X)),
                                Math.Max(lineInset, Math.Min(100 - lineInset, outsideImage.Y)));
                            Point lineEndScreen = Point.Round(new PointF(origin.X + lineEnd.X * scale, origin.Y + lineEnd.Y * scale));
                            Assert.IsTrue(CountNonBlackPixels(canvas, new Rectangle(lineEndScreen.X - 5, lineEndScreen.Y - 5, 11, 11)) > 0,
                                "Line preview must reach the clamped edge at zoom {0} toward {1}.", zoom, outsideImage);
                            PointF lineMidpoint = new PointF((50 + lineEnd.X) / 2f, (50 + lineEnd.Y) / 2f);
                            Point lineMidpointScreen = Point.Round(new PointF(origin.X + lineMidpoint.X * scale, origin.Y + lineMidpoint.Y * scale));
                            Assert.IsTrue(CountNonBlackPixels(canvas, new Rectangle(lineMidpointScreen.X - 5, lineMidpointScreen.Y - 5, 11, 11)) > 0,
                                "Line preview must reach the clamped edge at zoom {0} toward {1}.", zoom, outsideImage);
                            RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, lineEndScreen.X, lineEndScreen.Y, 0));
                            Assert.IsNotNull(segPath, "Line must complete at zoom {0} toward {1}.", zoom, outsideImage);
                            Assert.AreEqual((int)Math.Round(lineEnd.X, MidpointRounding.AwayFromZero), segPath[1].X, "Line must clamp its endpoint X at zoom {0} toward {1}.", zoom, outsideImage);
                            Assert.AreEqual((int)Math.Round(lineEnd.Y, MidpointRounding.AwayFromZero), segPath[1].Y, "Line must clamp its endpoint Y at zoom {0} toward {1}.", zoom, outsideImage);

                            canvas.DiscardUnfinishedShape();
                            canvas.SegTool = ImageCanvasControl.SegToolKind.Polygon;
                            segPath = null;
                            Point polygonStart = Point.Round(new PointF(origin.X + 30 * scale, origin.Y + 30 * scale));
                            Point polygonSecond = Point.Round(new PointF(origin.X + 70 * scale, origin.Y + 30 * scale));
                            Point polygonThird = Point.Round(new PointF(origin.X + 50 * scale, origin.Y + 70 * scale));
                            RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, polygonStart.X, polygonStart.Y, 0));
                            RaiseMouse(canvas, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, outside.X, outside.Y, 0));
                            RaiseMouse(canvas, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 0, outside.X, outside.Y, 0));
                            RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, polygonSecond.X, polygonSecond.Y, 0));
                            RaiseMouse(canvas, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 0, polygonSecond.X, polygonSecond.Y, 0));
                            RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, polygonThird.X, polygonThird.Y, 0));
                            RaiseMouse(canvas, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 0, polygonThird.X, polygonThird.Y, 0));
                            RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, polygonStart.X, polygonStart.Y, 0));
                            Assert.IsNotNull(segPath, "Polygon must complete at zoom {0} toward {1}.", zoom, outsideImage);
                            Assert.IsTrue(segPath.All(point => point.X >= 0 && point.Y >= 0 && point.X <= 100 && point.Y <= 100),
                                "Polygon points must remain inside the image at zoom {0} toward {1}.", zoom, outsideImage);
                            Assert.AreEqual((int)Math.Round(Math.Max(0, Math.Min(100, outsideImage.X)), MidpointRounding.AwayFromZero), segPath[1].X,
                                "Polygon must clamp its dragged vertex X at zoom {0} toward {1}.", zoom, outsideImage);
                            Assert.AreEqual((int)Math.Round(Math.Max(0, Math.Min(100, outsideImage.Y)), MidpointRounding.AwayFromZero), segPath[1].Y,
                                "Polygon must clamp its dragged vertex Y at zoom {0} toward {1}.", zoom, outsideImage);
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
        public void Ctrl_cursor_minus_is_shown_for_seg_erase_but_not_obd_rectangle()
        {
            string directory = Path.Combine(Path.GetTempPath(), "Neurocle.Tests", Guid.NewGuid().ToString("N"));
            string imagePath = Path.Combine(directory, "source.png");
            Directory.CreateDirectory(directory);
            try
            {
                CreateFilledImage(imagePath, Color.Black, 100);
                using (var canvas = new ImageCanvasControl { Size = new Size(200, 200), ObdEnabled = true })
                {
                    canvas.CreateControl();
                    canvas.LoadImage(imagePath);
                    WaitForImage(canvas, imagePath);
                    canvas.ObdSelectMode = false;
                    canvas.ModifierKeysProvider = () => Keys.Control;
                    RaiseMouse(canvas, "OnMouseMove", new MouseEventArgs(MouseButtons.None, 0, 50, 50, 0));
                    Assert.AreEqual(0, CountNonBlackPixels(canvas, new Rectangle(57, 57, 16, 16)), "Ctrl+OBD rectangle must not show an erase minus.");

                    canvas.ObdEnabled = false;
                    canvas.SegEnabled = true;
                    canvas.SegTool = ImageCanvasControl.SegToolKind.Rect;
                    RaiseMouse(canvas, "OnMouseMove", new MouseEventArgs(MouseButtons.None, 0, 50, 50, 0));
                    Assert.IsTrue(CountNonBlackPixels(canvas, new Rectangle(57, 57, 16, 16)) > 0, "Ctrl+SEG rectangle must retain its erase minus.");
                }
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [STATestMethod]
        public void Annotation_cursor_invalidates_its_old_pixels_when_pointer_leaves_image_or_enters_thumbnail()
        {
            string directory = Path.Combine(Path.GetTempPath(), "Neurocle.Tests", Guid.NewGuid().ToString("N"));
            string imagePath = Path.Combine(directory, "source.png");
            Directory.CreateDirectory(directory);
            try
            {
                CreateFilledImage(imagePath, Color.Black, 400);
                using (var canvas = new ImageCanvasControl
                {
                    Size = new Size(200, 200),
                    SegEnabled = true,
                    SegTool = ImageCanvasControl.SegToolKind.Rect
                })
                {
                    canvas.CreateControl();
                    canvas.LoadImage(imagePath);
                    WaitForImage(canvas, imagePath);
                    canvas.SetZoomPercent(100);
                    ImageCanvasControl.CanvasThumbnailLayout layout;
                    Assert.IsTrue(ImageCanvasControl.CanvasThumbnailLayout.TryCreate(new Size(400, 400), canvas.ClientSize, canvas.Zoom, canvas.ViewState.Origin, canvas.DeviceDpi / 96f, out layout));

                    Point oldCursor = new Point(20, 180);
                    RaiseMouse(canvas, "OnMouseMove", new MouseEventArgs(MouseButtons.None, 0, oldCursor.X, oldCursor.Y, 0));
                    var invalidated = new List<Rectangle>();
                    canvas.Invalidated += (sender, args) => invalidated.Add(args.InvalidRect);

                    Point thumbnail = Point.Round(new PointF(layout.ImageBounds.Left + layout.ImageBounds.Width / 2f, layout.ImageBounds.Top + layout.ImageBounds.Height / 2f));
                    RaiseMouse(canvas, "OnMouseMove", new MouseEventArgs(MouseButtons.None, 0, thumbnail.X, thumbnail.Y, 0));

                    Assert.AreEqual(Cursors.Arrow, canvas.Cursor);
                    Assert.IsTrue(invalidated.Any(area => area.Contains(oldCursor)), "Entering the thumbnail must erase the previously drawn annotation cursor.");
                }

                CreateFilledImage(imagePath, Color.Black, 100);
                using (var canvas = new ImageCanvasControl
                {
                    Size = new Size(200, 200),
                    SegEnabled = true,
                    SegTool = ImageCanvasControl.SegToolKind.Rect
                })
                {
                    canvas.CreateControl();
                    canvas.LoadImage(imagePath);
                    WaitForImage(canvas, imagePath);
                    canvas.SetZoomPercent(100);

                    Point oldCursor = new Point(100, 100);
                    RaiseMouse(canvas, "OnMouseMove", new MouseEventArgs(MouseButtons.None, 0, oldCursor.X, oldCursor.Y, 0));
                    var invalidated = new List<Rectangle>();
                    canvas.Invalidated += (sender, args) => invalidated.Add(args.InvalidRect);
                    RaiseMouse(canvas, "OnMouseMove", new MouseEventArgs(MouseButtons.None, 0, 10, 10, 0));

                    Assert.AreEqual(Cursors.Arrow, canvas.Cursor);
                    Assert.IsTrue(invalidated.Any(area => area.Contains(oldCursor)), "Leaving the image must erase the previously drawn annotation cursor.");
                }
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [STATestMethod]
        public void Obd_rectangle_and_seg_rectangle_ellipse_previews_fill_at_fixed_forty_percent()
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
                using (var canvas = new ImageCanvasControl { Size = new Size(100, 100), PreviewColor = Color.FromArgb(200, 80, 40) })
                {
                    canvas.CreateControl();
                    canvas.LoadImage(imagePath);
                    WaitForImage(canvas, imagePath);
                    foreach (ImageCanvasControl.SegToolKind? tool in new ImageCanvasControl.SegToolKind?[] { null, ImageCanvasControl.SegToolKind.Rect, ImageCanvasControl.SegToolKind.Ellipse })
                    {
                        Color? baseline = null;
                        foreach (float resultOpacity in new[] { 0f, 0.4f, 1f })
                        {
                            canvas.ObdEnabled = !tool.HasValue;
                            canvas.ObdSelectMode = false;
                            canvas.SegEnabled = tool.HasValue;
                            canvas.SegTool = tool;
                            canvas.ObdOpacity = resultOpacity;
                            canvas.SegOpacity = resultOpacity;
                            RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 20, 20, 0));
                            RaiseMouse(canvas, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, 60, 60, 0));

                            Color fill = RenderPixel(canvas, 40, 40);
                            AssertFortyPercentBlend(fill, canvas.PreviewColor);
                            Point edge = tool == ImageCanvasControl.SegToolKind.Ellipse ? new Point(40, 20) : new Point(20, 40);
                            Assert.IsTrue(RenderPixel(canvas, edge.X, edge.Y).R > 0, "{0} should show a visible outline.", tool.HasValue ? tool.ToString() : "OBD rectangle");
                            if (baseline.HasValue) Assert.AreEqual(baseline.Value.ToArgb(), fill.ToArgb(), "Preview fill should not follow result opacity for {0}.", tool.HasValue ? tool.ToString() : "OBD rectangle");
                            else baseline = fill;

                            RaiseMouse(canvas, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, 40, 40, 0));
                            AssertFortyPercentBlend(RenderPixel(canvas, 30, 30), canvas.PreviewColor);
                            Assert.AreEqual(Color.Black.ToArgb(), RenderPixel(canvas, 50, 50).ToArgb(), "Preview should follow the pointer for {0}.", tool.HasValue ? tool.ToString() : "OBD rectangle");

                            RaiseMouse(canvas, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 0, 40, 40, 0));
                            Assert.AreEqual(Color.Black.ToArgb(), RenderPixel(canvas, 40, 40).ToArgb(), "Preview should disappear after release.");
                        }
                    }

                    foreach (ImageCanvasControl.SegToolKind? tool in new ImageCanvasControl.SegToolKind?[] { null, ImageCanvasControl.SegToolKind.Rect, ImageCanvasControl.SegToolKind.Ellipse })
                    {
                        canvas.ObdEnabled = !tool.HasValue;
                        canvas.SegEnabled = tool.HasValue;
                        canvas.SegTool = tool;
                        RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 20, 20, 0));
                        RaiseMouse(canvas, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, 60, 60, 0));
                        AssertFortyPercentBlend(RenderPixel(canvas, 40, 40), canvas.PreviewColor);
                        canvas.DiscardUnfinishedShape();
                        Assert.AreEqual(Color.Black.ToArgb(), RenderPixel(canvas, 40, 40).ToArgb(), "Preview should disappear after cancellation for {0}.", tool.HasValue ? tool.ToString() : "OBD rectangle");
                    }
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
        public void Line_preview_uses_round_caps_like_the_completed_stroke()
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
                using (var canvas = new ImageCanvasControl
                {
                    Size = new Size(100, 100),
                    SegEnabled = true,
                    SegTool = ImageCanvasControl.SegToolKind.Line,
                    SegStrokeWidth = 20,
                    PreviewColor = Color.White
                })
                {
                    canvas.CreateControl();
                    canvas.LoadImage(imagePath);
                    WaitForImage(canvas, imagePath);
                    foreach (int width in new[] { 10, 20, 30 })
                    foreach (int zoomPercent in new[] { 100, 150, 200 })
                    foreach (Keys modifiers in new[] { Keys.None, Keys.Control })
                    {
                        canvas.DiscardUnfinishedShape();
                        canvas.SegStrokeWidth = width;
                        canvas.ModifierKeysProvider = () => modifiers;
                        canvas.SetZoomPercent(zoomPercent);
                        double zoom = zoomPercent / 100d;
                        int startX = (int)Math.Round(50d + (40d - 50d) * zoom);
                        int endX = (int)Math.Round(50d + (60d - 50d) * zoom);
                        RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, startX, 50, 0));
                        RaiseMouse(canvas, "OnMouseMove", new MouseEventArgs(MouseButtons.None, 0, endX, 50, 0));
                        SetField(canvas, "hasToolCursor", false);

                        using (var rendered = new Bitmap(canvas.Width, canvas.Height))
                        {
                            canvas.DrawToBitmap(rendered, canvas.ClientRectangle);
                            int left = Enumerable.Range(0, rendered.Width).First(x => rendered.GetPixel(x, 50).ToArgb() != Color.Black.ToArgb());
                            int right = Enumerable.Range(0, rendered.Width).Reverse().First(x => rendered.GetPixel(x, 50).ToArgb() != Color.Black.ToArgb());
                            Assert.IsTrue(left < startX, "The line preview must extend with a round start cap for width {0}, zoom {1}%, modifiers {2} (left={3}, start={4}).", width, zoomPercent, modifiers, left, startX);
                            Assert.IsTrue(right > endX, "The line preview must extend with a round end cap for width {0}, zoom {1}%, modifiers {2} (right={3}, end={4}).", width, zoomPercent, modifiers, right, endX);
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
        public void Seg_polygon_preview_uses_three_pixel_track_and_eight_pixel_solid_start_at_each_zoom()
        {
            string directory = Path.Combine(Path.GetTempPath(), "Neurocle.Tests", Guid.NewGuid().ToString("N"));
            string imagePath = Path.Combine(directory, "source.png");
            Directory.CreateDirectory(directory);
            try
            {
                using (var source = new Bitmap(300, 300))
                {
                    using (Graphics graphics = Graphics.FromImage(source)) graphics.Clear(Color.Black);
                    source.Save(imagePath, ImageFormat.Png);
                }
                using (var canvas = new ImageCanvasControl { Size = new Size(300, 300), SegEnabled = true, SegTool = ImageCanvasControl.SegToolKind.Polygon, PreviewColor = Color.White })
                {
                    canvas.CreateControl();
                    canvas.LoadImage(imagePath);
                    WaitForImage(canvas, imagePath);
                    foreach (double zoom in new[] { 100d, 200d })
                    {
                        canvas.DiscardUnfinishedShape();
                        canvas.SetZoomPercent(zoom);
                        Point start = ImagePointToScreen(canvas, 80, 150);
                        Point end = ImagePointToScreen(canvas, 220, 150);
                        Point third = ImagePointToScreen(canvas, 220, 220);
                        RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, start.X, start.Y, 0));
                        Assert.IsTrue(Enumerable.Range(start.Y - 6, 13).Any(y => RenderPixel(canvas, start.X, y).R > 80), "The start marker should appear immediately after the first click at {0}% zoom.", zoom);
                        RaiseMouse(canvas, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, end.X, end.Y, 0));
                        RaiseMouse(canvas, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, third.X, third.Y, 0));

                        using (var rendered = new Bitmap(canvas.Width, canvas.Height))
                        {
                            canvas.DrawToBitmap(rendered, canvas.ClientRectangle);
                            int strokePixels = Enumerable.Range(Math.Max(0, end.Y - 5), 11).Count(y => rendered.GetPixel(canvas.Width / 2, y).R > 80);
                            int startPixels = Enumerable.Range(start.Y - 6, 13).Count(y => rendered.GetPixel(start.X, y).R > 80);
                            Assert.IsTrue(Math.Abs(strokePixels - 3) <= 1, "Expected a 3px preview track at {0}% zoom, received {1}px.", zoom, strokePixels);
                            Assert.IsTrue(startPixels >= 6 && startPixels <= 9, "Expected an approximately 8px solid start marker at {0}% zoom, received {1}px.", zoom, startPixels);
                        }

                        RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, start.X, start.Y, 0));
                        RaiseMouse(canvas, "OnMouseMove", new MouseEventArgs(MouseButtons.None, 0, 10, 10, 0));
                        Assert.AreEqual(Color.Black.ToArgb(), RenderPixel(canvas, start.X, start.Y - 4).ToArgb(), "The start marker should disappear when polygon annotation is completed.");
                    }
                }
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [STATestMethod]
        public void Brush_returning_to_start_submits_a_rounded_polyline()
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
                    int submittedWidth = 0;
                    canvas.SegPathCreated += (type, points, width) => { submittedType = type; submittedPoints = points; submittedWidth = width; };

                    RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 20, 20, 0));
                    RaiseMouse(canvas, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, 70, 20, 0));
                    RaiseMouse(canvas, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, 70, 70, 0));
                    RaiseMouse(canvas, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, 20, 70, 0));
                    RaiseMouse(canvas, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, 20, 20, 0));
                    RaiseMouse(canvas, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 0, 20, 20, 0));

                    Assert.AreEqual(SegRegionType.PolyLine, submittedType);
                    Assert.IsNotNull(submittedPoints);
                    Assert.AreEqual(15, submittedWidth);
                    Assert.IsTrue(submittedPoints.Count >= 4);
                }
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [STATestMethod]
        public void Polygon_preview_follows_cursor_after_release_and_marks_start_and_closure()
        {
            string directory = Path.Combine(Path.GetTempPath(), "Neurocle.Tests", Guid.NewGuid().ToString("N"));
            string imagePath = Path.Combine(directory, "source.png");
            Directory.CreateDirectory(directory);
            try
            {
                CreateSolidImage(imagePath, Color.Red, 100);
                using (var canvas = new ImageCanvasControl { Size = new Size(100, 100), SegEnabled = true, SegTool = ImageCanvasControl.SegToolKind.Polygon, PreviewColor = Color.Blue })
                {
                    canvas.CreateControl();
                    canvas.LoadImage(imagePath);
                    WaitForImage(canvas, imagePath);
                    Point start = ImagePointToScreen(canvas, 20, 20);
                    Point next = ImagePointToScreen(canvas, 50, 20);
                    Point cursor = ImagePointToScreen(canvas, 50, 40);

                    RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, start.X, start.Y, 0));
                    RaiseMouse(canvas, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 0, start.X, start.Y, 0));
                    using (var rendered = new Bitmap(canvas.Width, canvas.Height))
                    {
                        canvas.DrawToBitmap(rendered, canvas.ClientRectangle);
                        Assert.AreEqual(Color.White.ToArgb(), rendered.GetPixel(start.X, start.Y).ToArgb(), "The polygon start marker should be solid white.");
                        Assert.AreEqual(Color.Black.ToArgb(), rendered.GetPixel(start.X + 4, start.Y).ToArgb(), "The polygon start marker should have a black outline.");
                    }

                    RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, next.X, next.Y, 0));
                    RaiseMouse(canvas, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 0, next.X, next.Y, 0));
                    RaiseMouse(canvas, "OnMouseMove", new MouseEventArgs(MouseButtons.None, 0, cursor.X, cursor.Y, 0));
                    using (var rendered = new Bitmap(canvas.Width, canvas.Height))
                    {
                        canvas.DrawToBitmap(rendered, canvas.ClientRectangle);
                        Color previewPixel = rendered.GetPixel(cursor.X, cursor.Y - 10);
                        Assert.IsTrue(previewPixel.B > 0, "The unfinished polygon edge should follow the cursor after the left button is released.");
                    }

                    Point third = ImagePointToScreen(canvas, 50, 50);
                    RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, third.X, third.Y, 0));
                    RaiseMouse(canvas, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 0, third.X, third.Y, 0));
                    Point closeCursor = ImagePointToScreen(canvas, 21, 20);
                    RaiseMouse(canvas, "OnMouseMove", new MouseEventArgs(MouseButtons.None, 0, closeCursor.X, closeCursor.Y, 0));
                    using (var rendered = new Bitmap(canvas.Width, canvas.Height))
                    {
                        canvas.DrawToBitmap(rendered, canvas.ClientRectangle);
                        Assert.AreEqual(Color.Blue.ToArgb(), rendered.GetPixel(start.X, start.Y).ToArgb(), "The closure-ready marker should use the annotation class color.");
                        Assert.AreEqual(Color.Blue.ToArgb(), rendered.GetPixel(start.X + 3, start.Y + 3).ToArgb(), "The closure-ready marker should grow to about 10 screen pixels.");
                    }
                }
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [STATestMethod]
        public void Brush_and_eraser_clicks_submit_single_point_round_paths()
        {
            string directory = Path.Combine(Path.GetTempPath(), "Neurocle.Tests", Guid.NewGuid().ToString("N"));
            string imagePath = Path.Combine(directory, "source.png");
            Directory.CreateDirectory(directory);
            try
            {
                CreateSolidImage(imagePath, Color.Red, 100);
                using (var canvas = new ImageCanvasControl { Size = new Size(100, 100), SegEnabled = true, SegTool = ImageCanvasControl.SegToolKind.Brush, SegStrokeWidth = 12 })
                {
                    canvas.CreateControl();
                    canvas.LoadImage(imagePath);
                    WaitForImage(canvas, imagePath);
                    SegRegionType? submittedType = null;
                    IReadOnlyList<SegPoint> submittedPoints = null;
                    int submittedWidth = 0;
                    IReadOnlyList<SegPoint> erasedPoints = null;
                    int erasedWidth = 0;
                    canvas.SegPathCreated += (type, points, width) => { submittedType = type; submittedPoints = points; submittedWidth = width; };
                    canvas.SegEraseRequested += (points, width) => { erasedPoints = points; erasedWidth = width; };

                    RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 40, 40, 0));
                    RaiseMouse(canvas, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 0, 40, 40, 0));
                    Assert.AreEqual(SegRegionType.PolyLine, submittedType);
                    Assert.AreEqual(1, submittedPoints.Count);
                    Assert.AreEqual(40, submittedPoints[0].X);
                    Assert.AreEqual(40, submittedPoints[0].Y);
                    Assert.AreEqual(12, submittedWidth);

                    RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 0, 50, 0));
                    RaiseMouse(canvas, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 0, 0, 50, 0));
                    Assert.AreEqual(6, submittedPoints[0].X, "A full-width brush dot should be clamped inside the image edge.");

                    RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 30, 30, 0));
                    RaiseMouse(canvas, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 0, 50, 30, 0));
                    Assert.AreEqual(2, submittedPoints.Count, "A quick drag should include the release point even when no mouse-move event was delivered.");
                    Assert.AreEqual(50, submittedPoints[1].X);
                    Assert.AreEqual(30, submittedPoints[1].Y);

                    canvas.SegTool = ImageCanvasControl.SegToolKind.Eraser;
                    RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 50, 50, 0));
                    RaiseMouse(canvas, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 0, 50, 50, 0));
                    Assert.AreEqual(1, erasedPoints.Count);
                    Assert.AreEqual(50, erasedPoints[0].X);
                    Assert.AreEqual(50, erasedPoints[0].Y);
                    Assert.AreEqual(12, erasedWidth);

                    canvas.SegRegions = new[]
                    {
                        new SegRegion("dot", "set", "image", "class", SegRegionType.PolyLine, 0, 0, 0, 0, 0, new[] { new SegPoint(40, 40) }, 12)
                    };
                    canvas.SegTool = ImageCanvasControl.SegToolKind.Select;
                    RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 40, 40, 0));
                    CollectionAssert.AreEqual(new[] { "dot" }, canvas.SelectedSegIds.ToArray(), "A clicked brush dot should remain selectable by its round stroke area.");
                }
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [STATestMethod]
        public void Ctrl_is_evaluated_only_when_a_SEG_shape_is_completed()
        {
            string directory = Path.Combine(Path.GetTempPath(), "Neurocle.Tests", Guid.NewGuid().ToString("N"));
            string imagePath = Path.Combine(directory, "source.png");
            Directory.CreateDirectory(directory);
            try
            {
                CreateSolidImage(imagePath, Color.Red, 100);
                Keys modifiers = Keys.None;
                using (var canvas = new ImageCanvasControl { Size = new Size(100, 100), SegEnabled = true, SegTool = ImageCanvasControl.SegToolKind.Rect })
                {
                    canvas.ModifierKeysProvider = () => modifiers;
                    canvas.CreateControl();
                    canvas.LoadImage(imagePath);
                    WaitForImage(canvas, imagePath);
                    int created = 0;
                    int erased = 0;
                    canvas.SegRegionCreated += (type, rectangle) => created++;
                    canvas.SegGeometryEraseRequested += (type, rectangle, points, width) => erased++;
                    RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 20, 20, 0));
                    RaiseMouse(canvas, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, 60, 60, 0));
                    modifiers = Keys.Control;
                    RaiseMouse(canvas, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 0, 60, 60, 0));
                    Assert.AreEqual(0, created);
                    Assert.AreEqual(1, erased);
                }
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [STATestMethod]
        public void Ctrl_brush_and_line_completion_submit_one_geometry_erase_with_locked_width()
        {
            string directory = Path.Combine(Path.GetTempPath(), "Neurocle.Tests", Guid.NewGuid().ToString("N"));
            string imagePath = Path.Combine(directory, "source.png");
            Directory.CreateDirectory(directory);
            try
            {
                CreateSolidImage(imagePath, Color.Red, 100);
                Keys modifiers = Keys.None;
                using (var canvas = new ImageCanvasControl { Size = new Size(100, 100), SegEnabled = true, SegStrokeWidth = 14 })
                {
                    canvas.ModifierKeysProvider = () => modifiers;
                    canvas.CreateControl();
                    canvas.LoadImage(imagePath);
                    WaitForImage(canvas, imagePath);
                    var erases = new List<Tuple<SegRegionType, int>>();
                    canvas.SegGeometryEraseRequested += (type, rectangle, points, width) => erases.Add(Tuple.Create(type, width));
                    canvas.SegTool = ImageCanvasControl.SegToolKind.Brush;
                    RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 20, 20, 0));
                    RaiseMouse(canvas, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, 50, 20, 0));
                    canvas.SegStrokeWidth = 30;
                    modifiers = Keys.Control;
                    RaiseMouse(canvas, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 0, 50, 20, 0));
                    canvas.SegTool = ImageCanvasControl.SegToolKind.Line;
                    modifiers = Keys.None;
                    canvas.SegStrokeWidth = 14;
                    RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 20, 40, 0));
                    canvas.SegStrokeWidth = 30;
                    modifiers = Keys.Control;
                    RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 50, 40, 0));
                    CollectionAssert.AreEqual(new[] { SegRegionType.PolyLine, SegRegionType.PolyLine }, erases.Select(value => value.Item1).ToArray());
                    CollectionAssert.AreEqual(new[] { 14, 14 }, erases.Select(value => value.Item2).ToArray());
                }
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [STATestMethod]
        public void Brush_returning_to_start_keeps_enclosed_area_empty_through_main_form()
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
                        ImagePointToScreen(canvas, 90, 10),
                        ImagePointToScreen(canvas, 10, 10)
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
                    SegRegion brush = store.GetSegRegions(set.Id, image.Id).Single(value => value.ClassId == annotationClass.Id);
                    Assert.AreEqual(SegRegionType.PolyLine, brush.Type);
                    Paths64 brushArea = SegGeometryPrototype.Stroke(new Path64(brush.Points.Select(point => new Point64(point.X, point.Y)).ToList()), brush.StrokeWidth);
                    Paths64 center = Clipper.Intersect(brushArea, new Paths64 { SegGeometryPrototype.Rectangle(49, 49, 51, 51) }, FillRule.NonZero);
                    Assert.AreEqual(0d, SegGeometryPrototype.Area(center), "Returning to the start should leave the unbrushed center empty.");
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

        private static void AssertRectangleInsideImage(Rectangle rectangle, int zoom, PointF outside, string tool)
        {
            Assert.IsTrue(rectangle.Left >= 0 && rectangle.Top >= 0 && rectangle.Right <= 100 && rectangle.Bottom <= 100,
                "{0} must remain inside the image at zoom {1} toward {2}; received {3}.", tool, zoom, outside, rectangle);
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
        public void Ctrl_c_and_ctrl_v_duplicate_the_selected_obd_snapshot_and_select_the_copies()
        {
            string directory = Path.Combine(Path.GetTempPath(), "Neurocle.Tests", Guid.NewGuid().ToString("N"));
            string path = Path.Combine(directory, "project.neurocle");
            string imagePath = Path.Combine(directory, "source.png");
            ProjectStore store = null;
            Directory.CreateDirectory(directory);
            try
            {
                CreateSolidImage(imagePath, Color.Red, 100);
                store = ProjectStore.Create(path);
                AnnotationSet set = store.CreateAnnotationSet("boxes", AnnotationSetType.Obd);
                AnnotationClass firstClass = store.GetClasses(set.Id).Single();
                AnnotationClass secondClass = store.AddClass(set.Id, "other", "#123456");
                ProjectImage image = store.ImportFiles(new[] { imagePath })[0];
                ObdAnnotation first = store.AddObdAnnotation(set.Id, image.Id, firstClass.Id, 10, 12, 20, 18);
                ObdAnnotation second = store.AddObdAnnotation(set.Id, image.Id, secondClass.Id, 40, 35, 15, 10);
                using (var form = new MainForm(new ProjectLifecycle()))
                {
                    Invoke(form, "ReplaceProject", store);
                    form.Show();
                    System.Windows.Forms.Application.DoEvents();
                    TabControl pages = form.Controls.OfType<TabControl>().Single();
                    pages.SelectedIndex = 1;
                    ComboBox sets = Field<ComboBox>(form, "annotationSets");
                    sets.SelectedItem = AnnotationSet(sets, set.Id);
                    form.ImageBrowser.SelectImage(image.Id);
                    System.Windows.Forms.Application.DoEvents();
                    Invoke(form, "ApplyImageSelection", new ImageSelectedEventArgs(image, Path.Combine(store.ImagesDirectory, image.ManagedFileName)));
                    form.AnnotationCanvas.SelectAllObd();

                    RaiseKey(form, new KeyEventArgs(Keys.Control | Keys.C));
                    store.UpdateObdAnnotation(first.Id, firstClass.Id, 5, 6, 7, 8);
                    store.DeleteObdAnnotation(second.Id);
                    Invoke(form, "RefreshAnnotationClasses");
                    form.AnnotationCanvas.SelectObdAnnotations(new string[0]);
                    RaiseKey(form, new KeyEventArgs(Keys.Control | Keys.C));
                    RaiseKey(form, new KeyEventArgs(Keys.Control | Keys.V));

                    ObdAnnotation[] afterPaste = store.GetObdAnnotations(set.Id, image.Id).ToArray();
                    Assert.AreEqual(3, afterPaste.Length);
                    ObdAnnotation[] copies = afterPaste.Where(value => value.Id != first.Id && value.Id != second.Id).OrderBy(value => value.ZOrder).ToArray();
                    Assert.AreEqual(2, copies.Length);
                    Assert.AreNotEqual(first.Id, copies[0].Id);
                    CollectionAssert.AreEqual(new[] { firstClass.Id, secondClass.Id }, copies.Select(value => value.ClassId).ToArray());
                    CollectionAssert.AreEqual(new[] { new Rectangle(10, 12, 20, 18), new Rectangle(40, 35, 15, 10) }, copies.Select(value => new Rectangle(value.X, value.Y, value.Width, value.Height)).ToArray());
                    CollectionAssert.AreEqual(copies.Select(value => value.Id).ToArray(), form.AnnotationCanvas.SelectedObdIds.OrderBy(id => copies.Single(value => value.Id == id).ZOrder).ToArray());

                    Assert.IsTrue(store.Undo(set.Id), "Pasting two boxes must be one undo step.");
                    CollectionAssert.AreEqual(new[] { first.Id }, store.GetObdAnnotations(set.Id, image.Id).Select(value => value.Id).ToArray());
                    Assert.IsTrue(store.Redo(set.Id));
                    Assert.AreEqual(3, store.GetObdAnnotations(set.Id, image.Id).Count);
                }
            }
            finally
            {
                store?.Dispose();
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [STATestMethod]
        public void Ctrl_v_pastes_obd_copies_to_selected_images_skips_out_of_bounds_and_keeps_current_image()
        {
            string directory = Path.Combine(Path.GetTempPath(), "Neurocle.Tests", Guid.NewGuid().ToString("N"));
            string path = Path.Combine(directory, "project.neurocle");
            string sourcePath = Path.Combine(directory, "source.png");
            string smallPath = Path.Combine(directory, "small.png");
            ProjectStore store = null;
            Directory.CreateDirectory(directory);
            try
            {
                CreateSolidImage(sourcePath, Color.Red, 100);
                CreateSolidImage(smallPath, Color.Blue, 30);
                store = ProjectStore.Create(path);
                AnnotationSet set = store.CreateAnnotationSet("boxes", AnnotationSetType.Obd);
                AnnotationSet otherSet = store.CreateAnnotationSet("other boxes", AnnotationSetType.Obd);
                AnnotationClass firstClass = store.GetClasses(set.Id).Single();
                AnnotationClass secondClass = store.AddClass(set.Id, "other", "#123456");
                ProjectImage[] images = store.ImportFiles(new[] { sourcePath, smallPath }).ToArray();
                ProjectImage sourceImage = images.Single(image => image.OriginalFileName == "source.png");
                ProjectImage smallImage = images.Single(image => image.OriginalFileName == "small.png");
                store.AddObdAnnotation(set.Id, sourceImage.Id, firstClass.Id, 10, 12, 20, 18);
                store.AddObdAnnotation(set.Id, sourceImage.Id, secondClass.Id, 40, 35, 15, 10);

                using (var form = new MainForm(new ProjectLifecycle()))
                {
                    Invoke(form, "ReplaceProject", store);
                    form.Show();
                    System.Windows.Forms.Application.DoEvents();
                    form.Controls.OfType<TabControl>().Single().SelectedIndex = 1;
                    ComboBox sets = Field<ComboBox>(form, "annotationSets");
                    sets.SelectedItem = AnnotationSet(sets, set.Id);
                    ListView imageList = Field<ListView>(form.ImageBrowser, "list");
                    int sourceIndex = form.ImageBrowser.Rows.Select((row, index) => new { row, index }).Single(item => item.row.Image.Id == sourceImage.Id).index;
                    int smallIndex = form.ImageBrowser.Rows.Select((row, index) => new { row, index }).Single(item => item.row.Image.Id == smallImage.Id).index;
                    imageList.SelectedIndices.Clear();
                    imageList.SelectedIndices.Add(sourceIndex);
                    imageList.SelectedIndices.Add(smallIndex);
                    System.Windows.Forms.Application.DoEvents();
                    Invoke(form, "ApplyImageSelection", new ImageSelectedEventArgs(sourceImage, Path.Combine(store.ImagesDirectory, sourceImage.ManagedFileName)));
                    form.AnnotationCanvas.SelectAllObd();

                    RaiseKey(form, new KeyEventArgs(Keys.Control | Keys.C));
                    RaiseKey(form, new KeyEventArgs(Keys.Control | Keys.V));

                    ObdAnnotation[] sourceResults = store.GetObdAnnotations(set.Id, sourceImage.Id).ToArray();
                    ObdAnnotation[] smallResults = store.GetObdAnnotations(set.Id, smallImage.Id).ToArray();
                    Assert.AreEqual(4, sourceResults.Length, "The source image should be included when selected as a paste target.");
                    Assert.AreEqual(1, smallResults.Length, "Only the source box that fits inside the small target should be pasted.");
                    Assert.AreEqual(firstClass.Id, smallResults[0].ClassId);
                    Assert.AreEqual(sourceImage.Id, Field<string>(form, "selectedImageId"), "Pasting must not switch the current image.");
                    CollectionAssert.AreEquivalent(new[] { sourceImage.Id, smallImage.Id }, form.ImageBrowser.SelectedImageIds.ToArray());
                    Assert.AreEqual(2, form.AnnotationCanvas.SelectedObdIds.Count, "Copies on the currently displayed image should be selected.");

                    Assert.IsTrue(store.Undo(set.Id), "All cross-image copies should be undone together.");
                    Assert.AreEqual(2, store.GetObdAnnotations(set.Id, sourceImage.Id).Count);
                    Assert.AreEqual(0, store.GetObdAnnotations(set.Id, smallImage.Id).Count);
                    Assert.IsTrue(store.Redo(set.Id));
                    Assert.AreEqual(4, store.GetObdAnnotations(set.Id, sourceImage.Id).Count);
                    Assert.AreEqual(1, store.GetObdAnnotations(set.Id, smallImage.Id).Count);

                    imageList.SelectedIndices.Clear();
                    RaiseKey(form, new KeyEventArgs(Keys.Control | Keys.V));
                    Assert.AreEqual(4, store.GetObdAnnotations(set.Id, sourceImage.Id).Count);
                    Assert.AreEqual(1, store.GetObdAnnotations(set.Id, smallImage.Id).Count);

                    imageList.SelectedIndices.Add(sourceIndex);
                    imageList.SelectedIndices.Add(smallIndex);
                    sets.SelectedItem = AnnotationSet(sets, otherSet.Id);
                    RaiseKey(form, new KeyEventArgs(Keys.Control | Keys.V));
                    Assert.AreEqual(0, store.GetObdAnnotations(otherSet.Id, sourceImage.Id).Count);
                    Assert.AreEqual(0, store.GetObdAnnotations(otherSet.Id, smallImage.Id).Count);
                }
            }
            finally
            {
                store?.Dispose();
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [STATestMethod]
        public void Ctrl_c_and_ctrl_v_copy_seg_regions_to_selected_images_as_independent_regions()
        {
            string directory = Path.Combine(Path.GetTempPath(), "Neurocle.Tests", Guid.NewGuid().ToString("N"));
            string path = Path.Combine(directory, "project.neurocle");
            string sourcePath = Path.Combine(directory, "source.png");
            string targetPath = Path.Combine(directory, "target.png");
            ProjectStore store = null;
            Directory.CreateDirectory(directory);
            try
            {
                CreateSolidImage(sourcePath, Color.Red, 100);
                CreateSolidImage(targetPath, Color.Blue, 100);
                store = ProjectStore.Create(path);
                AnnotationSet set = store.CreateAnnotationSet("regions", AnnotationSetType.Seg);
                AnnotationClass annotationClass = store.GetClasses(set.Id).Single();
                ProjectImage[] images = store.ImportFiles(new[] { sourcePath, targetPath }).ToArray();
                ProjectImage sourceImage = images.Single(image => image.OriginalFileName == "source.png");
                ProjectImage targetImage = images.Single(image => image.OriginalFileName == "target.png");
                SegRegion first = store.AddSegRegion(set.Id, sourceImage.Id, annotationClass.Id, SegRegionType.Rect, 10, 12, 20, 18);
                SegRegion second = store.AddSegPath(set.Id, sourceImage.Id, annotationClass.Id, SegRegionType.PolyGon, new[] { new SegPoint(40, 40), new SegPoint(60, 40), new SegPoint(50, 60) }, 0);

                using (var form = new MainForm(new ProjectLifecycle()))
                {
                    Invoke(form, "ReplaceProject", store);
                    form.Show();
                    System.Windows.Forms.Application.DoEvents();
                    form.Controls.OfType<TabControl>().Single().SelectedIndex = 1;
                    ComboBox sets = Field<ComboBox>(form, "annotationSets");
                    sets.SelectedItem = AnnotationSet(sets, set.Id);
                    ListView imageList = Field<ListView>(form.ImageBrowser, "list");
                    int sourceIndex = form.ImageBrowser.Rows.Select((row, index) => new { row, index }).Single(item => item.row.Image.Id == sourceImage.Id).index;
                    int targetIndex = form.ImageBrowser.Rows.Select((row, index) => new { row, index }).Single(item => item.row.Image.Id == targetImage.Id).index;
                    imageList.SelectedIndices.Clear();
                    imageList.SelectedIndices.Add(sourceIndex);
                    imageList.SelectedIndices.Add(targetIndex);
                    System.Windows.Forms.Application.DoEvents();
                    Invoke(form, "ApplyImageSelection", new ImageSelectedEventArgs(sourceImage, Path.Combine(store.ImagesDirectory, sourceImage.ManagedFileName)));
                    Field<Button>(form, "segSelect").PerformClick();
                    form.AnnotationCanvas.SelectAllSeg();

                    RaiseKey(form, new KeyEventArgs(Keys.Control | Keys.C));
                    RaiseKey(form, new KeyEventArgs(Keys.Control | Keys.V));

                    SegRegion[] sourceResults = store.GetSegRegions(set.Id, sourceImage.Id).ToArray();
                    SegRegion[] targetResults = store.GetSegRegions(set.Id, targetImage.Id).ToArray();
                    Assert.AreEqual(4, sourceResults.Length, "Same-class copies exactly overlapping their originals must remain separate.");
                    Assert.AreEqual(2, targetResults.Length);
                    SegRegion[] sourceCopies = sourceResults.Where(region => region.Id != first.Id && region.Id != second.Id).ToArray();
                    Assert.AreEqual(2, sourceCopies.Length);
                    Assert.AreNotEqual(first.Id, sourceCopies.Single(region => region.Type == SegRegionType.Rect).Id);
                    CollectionAssert.AreEqual(new[] { SegRegionType.Rect, SegRegionType.PolyGon }, targetResults.Select(region => region.Type).ToArray());
                    Assert.AreEqual(sourceImage.Id, Field<string>(form, "selectedImageId"), "Pasting must not switch the current image.");
                    CollectionAssert.AreEquivalent(new[] { sourceImage.Id, targetImage.Id }, form.ImageBrowser.SelectedImageIds.ToArray());
                    Assert.AreEqual(2, form.AnnotationCanvas.SelectedSegIds.Count, "Copies on the current image should be selected.");

                    Assert.IsTrue(store.Undo(set.Id));
                    CollectionAssert.AreEquivalent(new[] { first.Id, second.Id }, store.GetSegRegions(set.Id, sourceImage.Id).Select(region => region.Id).ToArray());
                    Assert.AreEqual(0, store.GetSegRegions(set.Id, targetImage.Id).Count);
                    Assert.IsTrue(store.Redo(set.Id));
                    Assert.AreEqual(4, store.GetSegRegions(set.Id, sourceImage.Id).Count);
                    Assert.AreEqual(2, store.GetSegRegions(set.Id, targetImage.Id).Count);
                }
            }
            finally
            {
                store?.Dispose();
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [STATestMethod]
        public void Ctrl_v_shows_toast_and_changes_nothing_when_a_copied_seg_class_was_deleted()
        {
            string directory = Path.Combine(Path.GetTempPath(), "Neurocle.Tests", Guid.NewGuid().ToString("N"));
            string path = Path.Combine(directory, "project.neurocle");
            string imagePath = Path.Combine(directory, "source.png");
            ProjectStore store = null;
            Directory.CreateDirectory(directory);
            try
            {
                CreateSolidImage(imagePath, Color.Red, 100);
                store = ProjectStore.Create(path);
                AnnotationSet set = store.CreateAnnotationSet("regions", AnnotationSetType.Seg);
                AnnotationClass copiedClass = store.GetClasses(set.Id).Single();
                store.AddClass(set.Id, "remaining", "#123456");
                ProjectImage image = store.ImportFiles(new[] { imagePath })[0];
                store.AddSegRegion(set.Id, image.Id, copiedClass.Id, SegRegionType.Rect, 10, 12, 20, 18);
                using (var form = new MainForm(new ProjectLifecycle()))
                {
                    Invoke(form, "ReplaceProject", store);
                    form.Show();
                    System.Windows.Forms.Application.DoEvents();
                    form.Controls.OfType<TabControl>().Single().SelectedIndex = 1;
                    ComboBox sets = Field<ComboBox>(form, "annotationSets");
                    sets.SelectedItem = AnnotationSet(sets, set.Id);
                    form.ImageBrowser.SelectImage(image.Id);
                    System.Windows.Forms.Application.DoEvents();
                    Invoke(form, "ApplyImageSelection", new ImageSelectedEventArgs(image, Path.Combine(store.ImagesDirectory, image.ManagedFileName)));
                    Field<Button>(form, "segSelect").PerformClick();
                    form.AnnotationCanvas.SelectAllSeg();
                    RaiseKey(form, new KeyEventArgs(Keys.Control | Keys.C));

                    store.DeleteClass(copiedClass.Id);
                    Invoke(form, "RefreshAnnotationClasses");
                    bool canUndoBeforePaste = store.CanUndo(set.Id);
                    RaiseKey(form, new KeyEventArgs(Keys.Control | Keys.V));

                    Assert.AreEqual(0, store.GetSegRegions(set.Id, image.Id).Count);
                    Assert.IsTrue(Field<Label>(form, "toastMessage").Visible);
                    Assert.AreEqual("複製標註所屬類別已刪除，無法貼上。", Field<Label>(form, "toastMessage").Text);
                    Assert.AreEqual(canUndoBeforePaste, store.CanUndo(set.Id), "A rejected paste must not create an undo record.");
                }
            }
            finally
            {
                store?.Dispose();
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [STATestMethod]
        public void Ctrl_v_shows_toast_and_changes_nothing_when_a_copied_obd_class_was_deleted()
        {
            string directory = Path.Combine(Path.GetTempPath(), "Neurocle.Tests", Guid.NewGuid().ToString("N"));
            string path = Path.Combine(directory, "project.neurocle");
            string imagePath = Path.Combine(directory, "source.png");
            ProjectStore store = null;
            Directory.CreateDirectory(directory);
            try
            {
                CreateSolidImage(imagePath, Color.Red, 100);
                store = ProjectStore.Create(path);
                AnnotationSet set = store.CreateAnnotationSet("boxes", AnnotationSetType.Obd);
                AnnotationClass copiedClass = store.GetClasses(set.Id).Single();
                store.AddClass(set.Id, "remaining", "#123456");
                ProjectImage image = store.ImportFiles(new[] { imagePath })[0];
                store.AddObdAnnotation(set.Id, image.Id, copiedClass.Id, 10, 12, 20, 18);
                using (var form = new MainForm(new ProjectLifecycle()))
                {
                    Invoke(form, "ReplaceProject", store);
                    form.Show();
                    System.Windows.Forms.Application.DoEvents();
                    form.Controls.OfType<TabControl>().Single().SelectedIndex = 1;
                    ComboBox sets = Field<ComboBox>(form, "annotationSets");
                    sets.SelectedItem = AnnotationSet(sets, set.Id);
                    form.ImageBrowser.SelectImage(image.Id);
                    System.Windows.Forms.Application.DoEvents();
                    Invoke(form, "ApplyImageSelection", new ImageSelectedEventArgs(image, Path.Combine(store.ImagesDirectory, image.ManagedFileName)));
                    form.AnnotationCanvas.SelectAllObd();
                    RaiseKey(form, new KeyEventArgs(Keys.Control | Keys.C));

                    store.DeleteClass(copiedClass.Id);
                    Invoke(form, "RefreshAnnotationClasses");
                    bool canUndoBeforePaste = store.CanUndo(set.Id);
                    RaiseKey(form, new KeyEventArgs(Keys.Control | Keys.V));

                    Assert.AreEqual(0, store.GetObdAnnotations(set.Id, image.Id).Count);
                    Assert.IsTrue(Field<Label>(form, "toastMessage").Visible);
                    Assert.AreEqual("複製標註所屬類別已刪除，無法貼上。", Field<Label>(form, "toastMessage").Text);
                    Assert.AreEqual(canUndoBeforePaste, store.CanUndo(set.Id), "A rejected paste must not create an undo record.");
                }
            }
            finally
            {
                store?.Dispose();
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [STATestMethod]
        public void Ctrl_c_replaces_the_previous_copied_annotation_type()
        {
            string directory = Path.Combine(Path.GetTempPath(), "Neurocle.Tests", Guid.NewGuid().ToString("N"));
            string path = Path.Combine(directory, "project.neurocle");
            string imagePath = Path.Combine(directory, "image.png");
            ProjectStore store = null;
            Directory.CreateDirectory(directory);
            try
            {
                CreateSolidImage(imagePath, Color.Red, 100);
                store = ProjectStore.Create(path);
                AnnotationSet obdSet = store.CreateAnnotationSet("boxes", AnnotationSetType.Obd);
                AnnotationClass obdClass = store.GetClasses(obdSet.Id).Single();
                AnnotationSet segSet = store.CreateAnnotationSet("regions", AnnotationSetType.Seg);
                AnnotationClass segClass = store.GetClasses(segSet.Id).Single();
                ProjectImage image = store.ImportFiles(new[] { imagePath })[0];
                store.AddObdAnnotation(obdSet.Id, image.Id, obdClass.Id, 10, 12, 20, 18);
                store.AddSegRegion(segSet.Id, image.Id, segClass.Id, SegRegionType.Rect, 40, 42, 20, 18);

                using (var form = new MainForm(new ProjectLifecycle()))
                {
                    Invoke(form, "ReplaceProject", store);
                    form.Show();
                    System.Windows.Forms.Application.DoEvents();
                    form.Controls.OfType<TabControl>().Single().SelectedIndex = 1;
                    ComboBox sets = Field<ComboBox>(form, "annotationSets");
                    sets.SelectedItem = AnnotationSet(sets, obdSet.Id);
                    form.ImageBrowser.SelectImage(image.Id);
                    System.Windows.Forms.Application.DoEvents();
                    Invoke(form, "ApplyImageSelection", new ImageSelectedEventArgs(image, Path.Combine(store.ImagesDirectory, image.ManagedFileName)));
                    form.AnnotationCanvas.SelectAllObd();
                    RaiseKey(form, new KeyEventArgs(Keys.Control | Keys.C));

                    sets.SelectedItem = AnnotationSet(sets, segSet.Id);
                    System.Windows.Forms.Application.DoEvents();
                    Field<Button>(form, "segSelect").PerformClick();
                    form.AnnotationCanvas.SelectAllSeg();
                    RaiseKey(form, new KeyEventArgs(Keys.Control | Keys.C));

                    sets.SelectedItem = AnnotationSet(sets, obdSet.Id);
                    System.Windows.Forms.Application.DoEvents();
                    RaiseKey(form, new KeyEventArgs(Keys.Control | Keys.V));
                    Assert.AreEqual(1, store.GetObdAnnotations(obdSet.Id, image.Id).Count, "The earlier OBD snapshot must be replaced by the later SEG copy.");

                    sets.SelectedItem = AnnotationSet(sets, segSet.Id);
                    System.Windows.Forms.Application.DoEvents();
                    RaiseKey(form, new KeyEventArgs(Keys.Control | Keys.V));
                    Assert.AreEqual(2, store.GetSegRegions(segSet.Id, image.Id).Count, "The most recently copied SEG snapshot should still paste in its own set.");
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
        public void Adding_annotation_class_uses_next_exact_class_number_without_prompt_and_persists()
        {
            string directory = Path.Combine(Path.GetTempPath(), "Neurocle.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            ProjectStore store = ProjectStore.Create(Path.Combine(directory, "project.neurocle"));
            try
            {
                AnnotationSet set = store.CreateAnnotationSet("labels", AnnotationSetType.Obd);
                using (var form = new MainForm(new ProjectLifecycle()))
                {
                    Invoke(form, "ReplaceProject", store);
                    form.Show();
                    Field<ComboBox>(form, "annotationSets").SelectedItem = AnnotationSet(Field<ComboBox>(form, "annotationSets"), set.Id);
                    store.RenameClass(store.GetClasses(set.Id).Single().Id, "renamed default");
                    Invoke(form, "AddAnnotationClass");
                    Assert.AreEqual("class1", store.GetClasses(set.Id).Last().Name);
                    store.DeleteClass(store.GetClasses(set.Id).Single(value => value.Name == "class1").Id);
                    Invoke(form, "AddAnnotationClass");
                    Assert.AreEqual("class1", store.GetClasses(set.Id).Last().Name);
                    store.AddClass(set.Id, "class5", null);
                    store.AddClass(set.Id, "Class8", null);
                    store.AddClass(set.Id, "class08", null);
                    store.AddClass(set.Id, "class9x", null);
                    Invoke(form, "AddAnnotationClass");
                    Assert.AreEqual("class6", store.GetClasses(set.Id).Last().Name);
                    store.DeleteClass(store.GetClasses(set.Id).Single(value => value.Name == "class6").Id);
                    Invoke(form, "AddAnnotationClass");
                    Assert.AreEqual("class6", store.GetClasses(set.Id).Last().Name);
                    store.AddClass(set.Id, "class2147483648", null);
                    Invoke(form, "AddAnnotationClass");
                    Assert.AreEqual("class2147483649", store.GetClasses(set.Id).Last().Name);
                    string setId = set.Id;
                    store.Dispose();
                    store = ProjectStore.Open(Path.Combine(directory, "project.neurocle"));
                    Assert.AreEqual("class2147483649", store.GetClasses(setId).Last().Name);
                }
            }
            finally { store?.Dispose(); Directory.Delete(directory, true); }
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

        [STATestMethod]
        public void Selecting_an_annotation_class_updates_existing_buttons_without_rebuilding_the_sidebar()
        {
            string directory = Path.Combine(Path.GetTempPath(), "Neurocle.Tests", Guid.NewGuid().ToString("N"));
            string projectPath = Path.Combine(directory, "project.neurocle");
            ProjectStore store = null;
            Directory.CreateDirectory(directory);
            try
            {
                store = ProjectStore.Create(projectPath);
                AnnotationSet set = store.CreateAnnotationSet("regions", AnnotationSetType.Seg);
                AnnotationClass other = store.AddClass(set.Id, "other", "#00ff00");
                using (var form = new MainForm(new ProjectLifecycle()))
                {
                    Invoke(form, "ReplaceProject", store);
                    ComboBox sets = Field<ComboBox>(form, "annotationSets");
                    sets.SelectedItem = AnnotationSet(sets, set.Id);
                    FlowLayoutPanel classes = Field<FlowLayoutPanel>(form, "annotationClasses");
                    Button[] originalButtons = classes.Controls.OfType<Button>().ToArray();
                    int removed = 0;
                    classes.ControlRemoved += (sender, args) => removed++;

                    Button target = originalButtons.Single(button => button.Text == other.Name);
                    typeof(Button).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, new object[] { EventArgs.Empty });

                    Assert.AreEqual(0, removed, "Changing the active class must not clear and rebuild the visible class list.");
                    CollectionAssert.AreEqual(originalButtons, classes.Controls.OfType<Button>().ToArray(), "Stable controls avoid a visible sidebar flash.");
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
        public void Drawing_previews_invalidate_only_the_changed_canvas_area()
        {
            string directory = Path.Combine(Path.GetTempPath(), "Neurocle.Tests", Guid.NewGuid().ToString("N"));
            string imagePath = Path.Combine(directory, "source.png");
            Directory.CreateDirectory(directory);
            try
            {
                CreateSolidImage(imagePath, Color.Black, 300);
                using (var canvas = new ImageCanvasControl { Size = new Size(300, 300), SegEnabled = true, SegTool = ImageCanvasControl.SegToolKind.Brush, SegStrokeWidth = 15 })
                {
                    canvas.CreateControl();
                    canvas.LoadImage(imagePath);
                    WaitForImage(canvas, imagePath);
                    var invalidated = new List<Rectangle>();
                    canvas.Invalidated += (sender, args) => invalidated.Add(args.InvalidRect);
                    Action<MouseButtons> moveAndAssertPartial = buttons =>
                    {
                        invalidated.Clear();
                        RaiseMouse(canvas, "OnMouseMove", new MouseEventArgs(buttons, 0, 30, 20, 0));
                        Assert.IsTrue(invalidated.Count > 0);
                        Assert.IsTrue(invalidated.All(bounds => bounds != canvas.ClientRectangle && bounds.Width * bounds.Height < canvas.ClientSize.Width * canvas.ClientSize.Height),
                            "A short drawing preview must not schedule a repaint of the entire image and every stored annotation.");
                    };

                    RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 20, 20, 0));
                    moveAndAssertPartial(MouseButtons.Left);
                    RaiseMouse(canvas, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 0, 30, 20, 0));

                    foreach (ImageCanvasControl.SegToolKind tool in new[] { ImageCanvasControl.SegToolKind.Rect, ImageCanvasControl.SegToolKind.Ellipse })
                    {
                        canvas.SegTool = tool;
                        RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 20, 20, 0));
                        moveAndAssertPartial(MouseButtons.Left);
                        RaiseMouse(canvas, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 0, 30, 20, 0));
                    }

                    foreach (ImageCanvasControl.SegToolKind tool in new[] { ImageCanvasControl.SegToolKind.Line, ImageCanvasControl.SegToolKind.Polygon })
                    {
                        canvas.SegTool = tool;
                        RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 20, 20, 0));
                        moveAndAssertPartial(MouseButtons.None);
                        canvas.DiscardUnfinishedShape();
                    }

                    canvas.SegEnabled = false;
                    canvas.ObdEnabled = true;
                    canvas.ObdSelectMode = false;
                    RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 20, 20, 0));
                    moveAndAssertPartial(MouseButtons.Left);
                }
            }
            finally
            {
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
                Assert.AreEqual("矩形 R", toolTips.GetToolTip(Field<Control>(form, "segRectangle")));
            }
        }

        [STATestMethod]
        public void Annotation_tool_shortcuts_match_type_use_embedded_icons_and_cancel_previews()
        {
            string directory = Path.Combine(Path.GetTempPath(), "Neurocle.Tests", Guid.NewGuid().ToString("N"));
            string projectPath = Path.Combine(directory, "project.neurocle");
            string imagePath = Path.Combine(directory, "source.png");
            ProjectStore store = null;
            Directory.CreateDirectory(directory);
            try
            {
                CreateFilledImage(imagePath, Color.Black, 100);
                store = ProjectStore.Create(projectPath);
                ProjectImage image = store.ImportFiles(new[] { imagePath }).Single();
                AnnotationSet claSet = store.CreateAnnotationSet("classes", AnnotationSetType.Cla);
                AnnotationSet obdSet = store.CreateAnnotationSet("boxes", AnnotationSetType.Obd);
                AnnotationSet segSet = store.CreateAnnotationSet("regions", AnnotationSetType.Seg);
                using (var form = new MainForm(new ProjectLifecycle()))
                {
                    Invoke(form, "ReplaceProject", store);
                    form.Show();
                    System.Windows.Forms.Application.DoEvents();
                    form.Controls.OfType<TabControl>().Single().SelectedIndex = 1;
                    form.ImageBrowser.SelectImage(image.Id);
                    Invoke(form, "ApplyImageSelection", new ImageSelectedEventArgs(image, Path.Combine(store.ImagesDirectory, image.ManagedFileName)));
                    WaitForImage(form.AnnotationCanvas, Path.Combine(store.ImagesDirectory, image.ManagedFileName));
                    ComboBox sets = Field<ComboBox>(form, "annotationSets");
                    ToolTip toolTips = Field<ToolTip>(form, "annotationToolTips");
                    Button[] toolButtons = { Field<Button>(form, "segSelect"), Field<Button>(form, "segBrush"), Field<Button>(form, "segPolygon"), Field<Button>(form, "segEllipse"), Field<Button>(form, "segRectangle"), Field<Button>(form, "segEraser"), Field<Button>(form, "segLine"), Field<Button>(form, "obdSelect"), Field<Button>(form, "obdRectangle") };
                    foreach (Button button in toolButtons) Assert.IsNotNull(button.Image, "{0} should display its embedded tool icon.", button.Name);
                    var expectedTooltips = new Dictionary<string, string>
                    {
                        { "segSelect", "選取 V" }, { "segBrush", "筆刷 B" }, { "segPolygon", "畫筆 P" }, { "segEllipse", "圓形 C" },
                        { "segRectangle", "矩形 R" }, { "segEraser", "橡皮擦 E" }, { "segLine", "線形 L" },
                        { "obdSelect", "選取 V" }, { "obdRectangle", "矩形 R" }
                    };
                    foreach (Button button in toolButtons) Assert.AreEqual(expectedTooltips[button.Name], toolTips.GetToolTip(button));
                    string[] iconResources = { "01_cursor.png", "02_eraser.png", "03_brush.png", "04_pencil.png", "05_line.png", "06_square.png", "07_circle.png" };
                    foreach (string resource in iconResources) Assert.IsTrue(typeof(MainForm).Assembly.GetManifestResourceNames().Contains("Neurocle.WinForms.Label_tool." + resource), "{0} should be embedded in the executable assembly.", resource);

                    sets.SelectedItem = AnnotationSet(sets, segSet.Id);
                    ImageCanvasControl canvas = form.AnnotationCanvas;
                    var expectedTools = new Dictionary<Keys, ImageCanvasControl.SegToolKind>
                    {
                        { Keys.V, ImageCanvasControl.SegToolKind.Select }, { Keys.B, ImageCanvasControl.SegToolKind.Brush },
                        { Keys.P, ImageCanvasControl.SegToolKind.Polygon }, { Keys.C, ImageCanvasControl.SegToolKind.Ellipse },
                        { Keys.R, ImageCanvasControl.SegToolKind.Rect }, { Keys.E, ImageCanvasControl.SegToolKind.Eraser },
                        { Keys.L, ImageCanvasControl.SegToolKind.Line }
                    };
                    foreach (var expected in expectedTools)
                    {
                        RaiseKey(form, new KeyEventArgs(expected.Key));
                        Assert.AreEqual(expected.Value, canvas.SegTool);
                    }

                    int submittedPaths = 0;
                    canvas.SegPathCreated += (type, points, width) => submittedPaths++;
                    RaiseKey(form, new KeyEventArgs(Keys.B));
                    Point start = ImagePointToScreen(canvas, 20, 20);
                    RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, start.X, start.Y, 0));
                    RaiseKey(form, new KeyEventArgs(Keys.R));
                    RaiseMouse(canvas, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 0, start.X, start.Y, 0));
                    Assert.AreEqual(0, submittedPaths, "Switching tools should discard an unfinished path instead of submitting it.");

                    sets.SelectedItem = AnnotationSet(sets, obdSet.Id);
                    RaiseKey(form, new KeyEventArgs(Keys.R));
                    Assert.IsFalse(canvas.ObdSelectMode);
                    RaiseKey(form, new KeyEventArgs(Keys.V));
                    Assert.IsTrue(canvas.ObdSelectMode);
                    foreach (Keys unsupported in new[] { Keys.B, Keys.P, Keys.C, Keys.E, Keys.L }) RaiseKey(form, new KeyEventArgs(unsupported));
                    Assert.IsTrue(canvas.ObdSelectMode, "OBD should ignore SEG-only tool shortcuts.");

                    sets.SelectedItem = AnnotationSet(sets, claSet.Id);
                    bool priorObdMode = canvas.ObdSelectMode;
                    ImageCanvasControl.SegToolKind? priorSegTool = canvas.SegTool;
                    foreach (Keys unsupported in expectedTools.Keys) RaiseKey(form, new KeyEventArgs(unsupported));
                    Assert.AreEqual(priorObdMode, canvas.ObdSelectMode);
                    Assert.AreEqual(priorSegTool, canvas.SegTool, "CLA should ignore every annotation tool shortcut.");

                    sets.SelectedItem = AnnotationSet(sets, segSet.Id);
                    RaiseKey(form, new KeyEventArgs(Keys.R));
                    var textInput = new TextBox();
                    form.Controls.Add(textInput);
                    textInput.BringToFront();
                    textInput.Focus();
                    RaiseKey(form, new KeyEventArgs(Keys.B));
                    Assert.AreEqual(ImageCanvasControl.SegToolKind.Rect, canvas.SegTool, "Tool shortcuts should not intercept text input.");
                    textInput.Dispose();
                    form.Close();
                    store = null;
                }
            }
            finally
            {
                store?.Dispose();
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [STATestMethod]
        public void Each_released_seg_brush_path_is_one_ctrl_z_history_step()
        {
            string directory = Path.Combine(Path.GetTempPath(), "Neurocle.Tests", Guid.NewGuid().ToString("N"));
            string projectPath = Path.Combine(directory, "project.neurocle");
            string imagePath = Path.Combine(directory, "source.png");
            string secondImagePath = Path.Combine(directory, "second.png");
            ProjectStore store = null;
            Directory.CreateDirectory(directory);
            try
            {
                CreateFilledImage(imagePath, Color.Black, 100);
                CreateFilledImage(secondImagePath, Color.White, 100);
                store = ProjectStore.Create(projectPath);
                ProjectImage[] images = store.ImportFiles(new[] { imagePath, secondImagePath }).ToArray();
                ProjectImage image = images[0];
                ProjectImage secondImage = images[1];
                AnnotationSet set = store.CreateAnnotationSet("regions", AnnotationSetType.Seg);
                ProjectStore activeStore = store;
                using (var form = new MainForm(new ProjectLifecycle()))
                {
                    Invoke(form, "ReplaceProject", store);
                    store = null;
                    form.Show();
                    System.Windows.Forms.Application.DoEvents();
                    form.Controls.OfType<TabControl>().Single().SelectedIndex = 1;
                    form.ImageBrowser.SelectImage(image.Id);
                    Invoke(form, "ApplyImageSelection", new ImageSelectedEventArgs(image, Path.Combine(activeStore.ImagesDirectory, image.ManagedFileName)));
                    ImageCanvasControl canvas = form.AnnotationCanvas;
                    WaitForImage(canvas, Path.Combine(activeStore.ImagesDirectory, image.ManagedFileName));
                    Field<ComboBox>(form, "annotationSets").SelectedItem = AnnotationSet(Field<ComboBox>(form, "annotationSets"), set.Id);
                    Field<Button>(form, "segBrush").PerformClick();

                    int submittedPaths = 0;
                    canvas.SegPathCreated += (type, points, width) => submittedPaths++;
                    int expectedCount = 0;
                    foreach (int coordinate in new[] { 20, 40, 60 })
                    {
                        expectedCount++;
                        Point screenPoint = ImagePointToScreen(canvas, coordinate, coordinate);
                        RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, screenPoint.X, screenPoint.Y, 0));
                        RaiseMouse(canvas, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 0, screenPoint.X, screenPoint.Y, 0));
                        Assert.AreEqual(expectedCount, submittedPaths, "Each release must submit one brush path.");
                        DateTime timeout = DateTime.UtcNow.AddSeconds(5);
                        while (Field<bool>(form, "annotationOperationInProgress") && DateTime.UtcNow < timeout)
                        {
                            System.Windows.Forms.Application.DoEvents();
                            Thread.Sleep(1);
                        }
                        Assert.IsFalse(Field<bool>(form, "annotationOperationInProgress"), "The brush save did not finish.");
                        Assert.AreEqual(expectedCount, activeStore.GetSegRegions(set.Id, image.Id).Count, "Each released brush path should be saved separately.");
                    }

                    Assert.AreEqual(3, activeStore.GetSegRegions(set.Id, image.Id).Count);
                    RaiseKey(form, new KeyEventArgs(Keys.Control | Keys.Z));
                    CollectionAssert.AreEqual(new[] { 20, 40 }, activeStore.GetSegRegions(set.Id, image.Id).Select(region => region.Points[0].X).ToArray());
                    Assert.IsTrue(activeStore.CanRedo(set.Id));
                    RaiseKey(form, new KeyEventArgs(Keys.Control | Keys.Y));
                    CollectionAssert.AreEqual(new[] { 20, 40, 60 }, activeStore.GetSegRegions(set.Id, image.Id).Select(region => region.Points[0].X).ToArray());

                    Point unfinishedPoint = ImagePointToScreen(canvas, 80, 80);
                    RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, unfinishedPoint.X, unfinishedPoint.Y, 0));
                    Assert.IsTrue(Field<bool>(canvas, "thumbnailHiddenForGesture"));
                    RaiseEvent(canvas, "OnKeyDown", new KeyEventArgs(Keys.Escape));
                    Assert.IsFalse(Field<bool>(canvas, "thumbnailHiddenForGesture"), "Esc should restore the thumbnail while cancelling a brush stroke.");
                    Assert.AreEqual(3, activeStore.GetSegRegions(set.Id, image.Id).Count, "Esc must not submit a partially drawn brush stroke.");

                    RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, unfinishedPoint.X, unfinishedPoint.Y, 0));
                    Assert.IsTrue(Field<bool>(canvas, "thumbnailHiddenForGesture"));
                    Invoke(form, "ApplyImageSelection", new ImageSelectedEventArgs(secondImage, Path.Combine(activeStore.ImagesDirectory, secondImage.ManagedFileName)));
                    Assert.IsFalse(Field<bool>(canvas, "thumbnailHiddenForGesture"), "Switching images should restore the thumbnail while cancelling a brush stroke.");
                    Assert.AreEqual(3, activeStore.GetSegRegions(set.Id, image.Id).Count, "Switching images must not submit a partially drawn brush stroke.");
                    Assert.AreEqual(secondImage.Id, Field<string>(form, "selectedImageId"));
                    form.Close();
                }
            }
            finally
            {
                store?.Dispose();
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [STATestMethod]
        public void New_annotation_set_dialog_requires_a_type_and_name_and_creates_only_the_selected_type()
        {
            string directory = Path.Combine(Path.GetTempPath(), "Neurocle.Tests", Guid.NewGuid().ToString("N"));
            string projectPath = Path.Combine(directory, "project.neurocle");
            ProjectStore store = null;
            Directory.CreateDirectory(directory);
            try
            {
                store = ProjectStore.Create(projectPath);
                using (var form = new MainForm(new ProjectLifecycle()))
                {
                    Invoke(form, "ReplaceProject", store);
                    int initialCount = store.GetAnnotationSets().Count;
                    RunAnnotationSetDialogAction(form, dialog =>
                    {
                        Assert.IsFalse(dialog.AnnotationSetType.HasValue, "Opening the dialog should not preselect a type.");
                        Assert.IsFalse(Field<Button>(dialog, "createButton").Enabled);
                        Assert.AreEqual(1, GetDescendants(dialog).OfType<TextBox>().Count(), "The form should only request the annotation set name.");
                        Panel footer = Field<Panel>(dialog, "footerPanel");
                        foreach (Button button in new[] { Field<Button>(dialog, "cancelButton"), Field<Button>(dialog, "createButton") })
                        {
                            Assert.IsTrue(button.Visible, button.Name + " should be visible.");
                            Assert.IsTrue(footer.ClientRectangle.Contains(button.Bounds), button.Name + " should fit inside the visible footer.");
                        }
                        Button[] cards = { Field<Button>(dialog, "claCard"), Field<Button>(dialog, "obdCard"), Field<Button>(dialog, "segCard") };
                        Assert.AreEqual(3, cards.Length);
                        foreach (Button card in cards)
                        {
                            Assert.IsNotNull(card.Image, "{0} should display its embedded model type icon.", card.Name);
                            Assert.IsTrue(card.Width > 200 && card.Height > 100, "{0} should be visible as a full type card in the Designer layout.", card.Name);
                        }
                        Field<Button>(dialog, "claCard").PerformClick();
                        Assert.AreEqual(AnnotationSetType.Cla, dialog.AnnotationSetType);
                        Assert.IsFalse(Field<Button>(dialog, "createButton").Enabled, "Choosing a type without a name should keep Create disabled.");
                        Field<TextBox>(dialog, "name").Text = "   ";
                        Assert.IsFalse(Field<Button>(dialog, "createButton").Enabled, "Whitespace-only names should keep Create disabled.");
                        Field<Button>(dialog, "cancelButton").PerformClick();
                    });
                    Assert.AreEqual(initialCount, store.GetAnnotationSets().Count, "Canceling must not create an annotation set.");

                    var expectedTypes = new[] { AnnotationSetType.Cla, AnnotationSetType.Obd, AnnotationSetType.Seg };
                    foreach (AnnotationSetType expected in expectedTypes)
                    {
                        string setName = "new-" + expected.ToString().ToLowerInvariant();
                        RunAnnotationSetDialogAction(form, dialog =>
                        {
                            Button card = Field<Button>(dialog, expected == AnnotationSetType.Cla ? "claCard" : expected == AnnotationSetType.Obd ? "obdCard" : "segCard");
                            card.PerformClick();
                            Field<TextBox>(dialog, "name").Text = setName;
                            Assert.IsTrue(Field<Button>(dialog, "createButton").Enabled);
                            Field<Button>(dialog, "createButton").PerformClick();
                        });
                        AnnotationSet created = store.GetAnnotationSets().Single(value => value.Name == setName);
                        Assert.AreEqual(expected, created.Type);
                    }
                    form.Close();
                    store = null;
                }
            }
            finally
            {
                store?.Dispose();
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
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
                    canvas.SegOpacity = 0f;
                    Assert.AreEqual(Color.White.ToArgb(), RenderPixel(canvas, 20, 50).ToArgb(), "SEG closed-region outline must remain visible at 0% opacity.");

                    canvas.SegRegions = new[] { new SegRegion("line", "set", "image", "class", SegRegionType.PolyLine, 0, 0, 0, 0, 0, new[] { new SegPoint(10, 50), new SegPoint(90, 50) }, 10) };
                    AssertOpacity(canvas, value => canvas.SegOpacity = value, 50, 50);
                    canvas.SegOpacity = 0f;
                    Assert.AreEqual(Color.Black.ToArgb(), RenderPixel(canvas, 50, 50).ToArgb(), "Unselected SEG line may disappear at 0% opacity.");
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
                    Assert.AreEqual(70, opacity.Value);
                    Assert.AreEqual(70, opacityValue.Value);
                    Assert.AreEqual(0.7f, form.AnnotationCanvas.ObdOpacity);
                    Assert.AreEqual(0.7f, form.AnnotationCanvas.SegOpacity);

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

        private static void AssertRectangle(RectangleF expected, RectangleF actual)
        {
            Assert.AreEqual(expected.X, actual.X, 0.01f);
            Assert.AreEqual(expected.Y, actual.Y, 0.01f);
            Assert.AreEqual(expected.Width, actual.Width, 0.01f);
            Assert.AreEqual(expected.Height, actual.Height, 0.01f);
        }

        private static void CollapseThumbnail(ImageCanvasControl canvas, Size imageSize)
        {
            if (canvas.IsThumbnailCollapsed) return;
            ImageCanvasControl.CanvasThumbnailLayout layout;
            Assert.IsTrue(ImageCanvasControl.CanvasThumbnailLayout.TryCreate(imageSize, canvas.ClientSize, canvas.Zoom, canvas.ViewState.Origin, canvas.DeviceDpi / 96f, out layout));
            Point toggle = Point.Round(new PointF(layout.ToggleBounds.Left + layout.ToggleBounds.Width / 2f, layout.ToggleBounds.Top + layout.ToggleBounds.Height / 2f));
            RaiseMouse(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, toggle.X, toggle.Y, 0));
            RaiseMouse(canvas, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 0, toggle.X, toggle.Y, 0));
            Assert.IsTrue(canvas.IsThumbnailCollapsed);
        }

        private static Color RenderPixel(ImageCanvasControl canvas, int x, int y)
        {
            using (var rendered = new Bitmap(canvas.Width, canvas.Height))
            {
                canvas.DrawToBitmap(rendered, canvas.ClientRectangle);
                return rendered.GetPixel(x, y);
            }
        }

        private static Bitmap RenderCanvas(ImageCanvasControl canvas)
        {
            var rendered = new Bitmap(canvas.Width, canvas.Height);
            canvas.DrawToBitmap(rendered, canvas.ClientRectangle);
            return rendered;
        }

        private static void AssertBitmapEquals(Bitmap expected, Bitmap actual, string message)
        {
            Assert.AreEqual(expected.Size, actual.Size);
            for (int y = 0; y < expected.Height; y++)
                for (int x = 0; x < expected.Width; x++)
                    Assert.AreEqual(expected.GetPixel(x, y).ToArgb(), actual.GetPixel(x, y).ToArgb(), "{0} First differing pixel: ({1}, {2}).", message, x, y);
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

        private static void AssertWhiteRotatedHandleCenters(ImageCanvasControl canvas, RectangleF bounds, PointF center, double angle)
        {
            PointF[] points =
            {
                new PointF(bounds.Left, bounds.Top), new PointF(bounds.Left + bounds.Width / 2f, bounds.Top), new PointF(bounds.Right, bounds.Top),
                new PointF(bounds.Right, bounds.Top + bounds.Height / 2f), new PointF(bounds.Right, bounds.Bottom),
                new PointF(bounds.Left + bounds.Width / 2f, bounds.Bottom), new PointF(bounds.Left, bounds.Bottom),
                new PointF(bounds.Left, bounds.Top + bounds.Height / 2f)
            };
            foreach (PointF point in points)
            {
                PointF rotated = RotateTestPoint(point, center, angle);
                AssertWhitePixelsAt(canvas, new Point((int)Math.Round(rotated.X), (int)Math.Round(rotated.Y)), 5, "Expected a selection handle on the rotated bounding box.");
            }
            PointF top = RotateTestPoint(points[1], center, angle);
            PointF rotationHandle = new PointF(top.X + (float)Math.Sin(angle) * 24f, top.Y - (float)Math.Cos(angle) * 24f);
            AssertWhitePixelsAt(canvas, new Point((int)Math.Round(rotationHandle.X), (int)Math.Round(rotationHandle.Y)), 5, "Expected the rotation handle to follow the rotated bounding box.");
        }

        private static PointF RotateTestPoint(PointF point, PointF center, double angle)
        {
            double cosine = Math.Cos(angle), sine = Math.Sin(angle);
            return new PointF((float)(center.X + (point.X - center.X) * cosine - (point.Y - center.Y) * sine), (float)(center.Y + (point.X - center.X) * sine + (point.Y - center.Y) * cosine));
        }

        private static void AssertWhitePixelsAt(ImageCanvasControl canvas, Point center, int radius, string message)
        {
            using (var rendered = new Bitmap(canvas.Width, canvas.Height))
            {
                canvas.DrawToBitmap(rendered, canvas.ClientRectangle);
                int whitePixels = 0;
                for (int x = Math.Max(0, center.X - radius); x <= Math.Min(rendered.Width - 1, center.X + radius); x++)
                    for (int y = Math.Max(0, center.Y - radius); y <= Math.Min(rendered.Height - 1, center.Y + radius); y++)
                        if (rendered.GetPixel(x, y).ToArgb() == Color.White.ToArgb()) whitePixels++;
                Assert.IsTrue(whitePixels >= 12, "{0} Center: {1}.", message, center);
            }
        }

        private static RectangleF GetTestSegBounds(SegRegion region)
        {
            if (region.Type == SegRegionType.Rect) return new RectangleF(region.X, region.Y, region.Width, region.Height);
            if (region.Type == SegRegionType.Ellipse) return new RectangleF(region.X - region.Width, region.Y - region.Height, region.Width * 2, region.Height * 2);
            float inset = region.Type == SegRegionType.PolyLine ? region.StrokeWidth / 2f : 0f;
            return RectangleF.FromLTRB(region.Points.Min(point => point.X) - inset, region.Points.Min(point => point.Y) - inset, region.Points.Max(point => point.X) + inset, region.Points.Max(point => point.Y) + inset);
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

        private static void CreateFilledImage(string path, Color color, int size)
        {
            using (var bitmap = new Bitmap(size, size))
            {
                using (Graphics graphics = Graphics.FromImage(bitmap)) graphics.Clear(color);
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

        private static void RaiseScroll(ScrollBar scrollBar, ScrollEventArgs args)
        {
            typeof(ScrollBar).GetMethod("OnScroll", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(scrollBar, new object[] { args });
        }

        private static void RaiseKey(Control control, KeyEventArgs args)
        {
            typeof(Control).GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(control, new object[] { args });
        }

        private static void RaiseKeyUp(Control control, KeyEventArgs args)
        {
            typeof(Control).GetMethod("OnKeyUp", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(control, new object[] { args });
        }

        private static Point ImagePointToScreen(ImageCanvasControl canvas, int x, int y)
        {
            ImageCanvasControl.CanvasViewState state = canvas.ViewState;
            return Point.Round(new PointF((float)(state.Origin.X + x * state.Zoom), (float)(state.Origin.Y + y * state.Zoom)));
        }


        private static object Invoke(object target, string methodName, params object[] arguments)
        {
            MethodInfo method = target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
                .Single(candidate => candidate.Name == methodName && candidate.GetParameters().Length == arguments.Length);
            return method.Invoke(target, arguments);
        }

        private static void RunAnnotationSetDialogAction(MainForm owner, Action<AnnotationSetCreationDialog> action)
        {
            Exception failure = null;
            bool foundDialog = false;
            using (var timer = new System.Windows.Forms.Timer { Interval = 25 })
            {
                timer.Tick += (sender, args) =>
                {
                    AnnotationSetCreationDialog dialog = System.Windows.Forms.Application.OpenForms.OfType<AnnotationSetCreationDialog>().FirstOrDefault();
                    if (dialog == null) return;
                    foundDialog = true;
                    timer.Stop();
                    try { action(dialog); }
                    catch (Exception error)
                    {
                        failure = error;
                        dialog.DialogResult = DialogResult.Cancel;
                    }
                };
                timer.Start();
                Invoke(owner, "CreateAnnotationSet");
                timer.Stop();
            }
            Assert.IsTrue(foundDialog, "The annotation set dialog did not open.");
            if (failure != null) throw failure;
        }

        private static IEnumerable<Control> GetDescendants(Control parent)
        {
            foreach (Control child in parent.Controls)
            {
                yield return child;
                foreach (Control descendant in GetDescendants(child)) yield return descendant;
            }
        }

        private static string FindRepositoryRoot()
        {
            DirectoryInfo directory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Neurocle.sln"))) directory = directory.Parent;
            Assert.IsNotNull(directory, "Could not locate the repository root from the test output directory.");
            return directory.FullName;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ScrollInfo
        {
            public uint Size;
            public uint Mask;
            public int Minimum;
            public int Maximum;
            public uint Page;
            public int Position;
            public int TrackPosition;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetScrollInfo(IntPtr handle, int bar, ref ScrollInfo info);

        [DllImport("user32.dll", EntryPoint = "SendMessage", CharSet = CharSet.Auto)]
        private static extern IntPtr SendWheelMessage(IntPtr handle, int message, IntPtr wParam, IntPtr lParam);

        private static ScrollInfo ReadScrollInfo(Control control, int bar)
        {
            var info = new ScrollInfo { Size = (uint)Marshal.SizeOf(typeof(ScrollInfo)), Mask = 0x0017 };
            Assert.IsTrue(GetScrollInfo(control.Handle, bar, ref info), "The control must expose the requested scrollbar information.");
            return info;
        }

        private static bool HasScrollRange(ScrollInfo info)
        {
            long maximumPosition = (long)info.Maximum - Math.Max(0L, (long)info.Page - 1L);
            return maximumPosition > info.Minimum;
        }

        private static void SendMouseWheel(Control control, int delta, bool shift)
        {
            const int mouseWheelMessage = 0x020A;
            const int shiftKey = 0x0004;
            long keyState = shift ? shiftKey : 0;
            long wheelParameter = keyState | ((long)unchecked((ushort)(short)delta) << 16);
            SendWheelMessage(control.Handle, mouseWheelMessage, new IntPtr(wheelParameter), IntPtr.Zero);
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
            Control host = CanvasHost(canvas);
            Assert.IsTrue(ReferenceEquals(page, host.Parent), "The canvas host is not on the expected page.");
            Assert.IsTrue(host.Width > 0 && host.Height > 0, "The canvas host has no usable area.");
            Assert.IsTrue(page.ClientRectangle.Contains(host.Bounds), "The canvas host exceeds its page.");
            Assert.IsTrue(canvas.Parent.ClientRectangle.Contains(canvas.Bounds), "The canvas exceeds its layout area.");
        }

        private static CanvasScrollHost CanvasHost(Control canvas)
        {
            return (CanvasScrollHost)canvas.Parent.Parent;
        }

        private static IEnumerable<T> Descendants<T>(Control control) where T : Control
        {
            return control.Controls.Cast<Control>().SelectMany(Descendants<T>).Concat(control is T ? new[] { (T)control } : Enumerable.Empty<T>());
        }
    }
}

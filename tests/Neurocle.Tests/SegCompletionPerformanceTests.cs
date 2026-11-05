using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Neurocle.Application.Projects;
using Neurocle.Infrastructure.Persistence;
using Neurocle.WinForms;

namespace Neurocle.Tests
{
    [TestClass]
    public sealed class SegCompletionPerformanceTests
    {
        [STATestMethod]
        [TestCategory("ManualPerformance")]
        public void Seventeen_thousand_point_seg_path_persists_and_renders_within_one_second()
        {
            string path = TemporaryProjectPath();
            string source = Path.Combine(Path.GetDirectoryName(path), "source.png");
            try
            {
                CreateBitmap(source, Color.Red, 1000, 1000);
                using (var store = ProjectStore.Create(path))
                using (var canvas = new ImageCanvasControl { Size = new Size(800, 800), SegEnabled = true })
                {
                    ProjectImage image = store.ImportFiles(new[] { source })[0];
                    AnnotationSet set = store.CreateAnnotationSet("render performance", AnnotationSetType.Seg);
                    AnnotationClass annotationClass = store.GetClasses(set.Id).Single();
                    canvas.CreateControl();
                    canvas.LoadImage(source);
                    var loadTimeout = Stopwatch.StartNew();
                    while (Math.Abs(canvas.Zoom - 0.8d) > 0.001d && loadTimeout.Elapsed < TimeSpan.FromSeconds(5))
                    {
                        System.Windows.Forms.Application.DoEvents();
                        Thread.Sleep(1);
                    }

                    Assert.AreEqual(0.8d, canvas.Zoom, 0.001d, "Canvas image did not finish loading.");
                    canvas.SegClasses = new Dictionary<string, AnnotationClass> { { annotationClass.Id, annotationClass } };

                    var stopwatch = Stopwatch.StartNew();
                    store.AddSegPath(set.Id, image.Id, annotationClass.Id, SegRegionType.PolyGon,
                        SegPerformanceFixture.CreateSeventeenThousandPointEllipse(), 0);
                    canvas.SegRegions = store.GetSegRegions(set.Id, image.Id);
                    using (var rendered = new Bitmap(canvas.Width, canvas.Height))
                        canvas.DrawToBitmap(rendered, canvas.ClientRectangle);
                    stopwatch.Stop();

                    Console.WriteLine("17,000-point persist and render: {0:F3} ms", stopwatch.Elapsed.TotalMilliseconds);
                    Assert.IsTrue(stopwatch.Elapsed <= TimeSpan.FromSeconds(1),
                        "17,000-point persist and render took {0:F3} ms.", stopwatch.Elapsed.TotalMilliseconds);
                }
            }
            finally
            {
                DeleteTemporaryProject(path);
                if (File.Exists(source)) File.Delete(source);
            }
        }

        [STATestMethod]
        [TestCategory("ManualPerformance")]
        public void Ctrl_erase_canvas_frames_keep_a_17k_point_annotation_responsive()
        {
            string path = TemporaryProjectPath();
            string source = Path.Combine(Path.GetDirectoryName(path), "source.png");
            try
            {
                CreateBitmap(source, Color.Red, 1000, 1000);
                using (var store = ProjectStore.Create(path))
                using (var canvas = new ImageCanvasControl { Size = new Size(800, 800), SegEnabled = true, SegTool = ImageCanvasControl.SegToolKind.Brush, SegStrokeWidth = 12 })
                {
                    ProjectImage image = store.ImportFiles(new[] { source })[0];
                    AnnotationSet set = store.CreateAnnotationSet("erase performance", AnnotationSetType.Seg);
                    AnnotationClass annotationClass = store.GetClasses(set.Id).Single();
                    canvas.CreateControl();
                    canvas.LoadImage(source);
                    var loadTimeout = Stopwatch.StartNew();
                    while (Math.Abs(canvas.Zoom - 0.8d) > 0.001d && loadTimeout.Elapsed < TimeSpan.FromSeconds(5))
                    {
                        System.Windows.Forms.Application.DoEvents();
                        Thread.Sleep(1);
                    }

                    canvas.SegClasses = new Dictionary<string, AnnotationClass> { { annotationClass.Id, annotationClass } };
                    canvas.SegRegions = new[] { new SegRegion("large", set.Id, image.Id, annotationClass.Id, SegRegionType.PolyLine, 0, 0, 1000, 1000, 0, SegPerformanceFixture.CreateSeventeenThousandPointEllipse(), 12) };
                    foreach (ImageCanvasControl.SegToolKind tool in new[]
                    {
                        ImageCanvasControl.SegToolKind.Rect,
                        ImageCanvasControl.SegToolKind.Ellipse,
                        ImageCanvasControl.SegToolKind.Polygon,
                        ImageCanvasControl.SegToolKind.Brush,
                        ImageCanvasControl.SegToolKind.Line
                    })
                    {
                        FrameMeasurement normal = MeasureGestureFrames(canvas, tool, Keys.None);
                        FrameMeasurement erase = MeasureGestureFrames(canvas, tool, Keys.Control);
                        Console.WriteLine("17,000-point {0} frames: normal={1:F3} ms avg/{2:F3} ms p95/{3:F3} ms max, Ctrl erase={4:F3} ms avg/{5:F3} ms p95/{6:F3} ms max", tool, normal.Average, normal.P95, normal.Maximum, erase.Average, erase.P95, erase.Maximum);
                        AssertFrameBudget(normal, tool + " normal");
                        AssertFrameBudget(erase, tool + " Ctrl erase");
                    }

                    FrameMeasurement eraser = MeasureGestureFrames(canvas, ImageCanvasControl.SegToolKind.Eraser, Keys.None);
                    Console.WriteLine("17,000-point standalone eraser frames: {0:F3} ms avg/{1:F3} ms p95/{2:F3} ms max", eraser.Average, eraser.P95, eraser.Maximum);
                    AssertFrameBudget(eraser, "standalone eraser");
                }
            }
            finally
            {
                DeleteTemporaryProject(path);
                if (File.Exists(source)) File.Delete(source);
            }
        }

        [STATestMethod]
        public void Repeated_ctrl_key_events_only_invalidate_the_annotation_cursor_area()
        {
            string source = Path.Combine(Path.GetTempPath(), "Neurocle.Tests", Guid.NewGuid().ToString("N"), "source.png");
            try
            {
                CreateBitmap(source, Color.Red, 1000, 1000);
                using (var canvas = new ImageCanvasControl { Size = new Size(800, 800), SegEnabled = true, SegTool = ImageCanvasControl.SegToolKind.Brush, SegStrokeWidth = 12 })
                {
                    canvas.CreateControl();
                    canvas.LoadImage(source);
                    var loadTimeout = Stopwatch.StartNew();
                    while (Math.Abs(canvas.Zoom - 0.8d) > 0.001d && loadTimeout.Elapsed < TimeSpan.FromSeconds(5))
                    {
                        System.Windows.Forms.Application.DoEvents();
                        Thread.Sleep(1);
                    }

                    canvas.SegRegions = new[]
                    {
                        new SegRegion("large", "set", "image", "class", SegRegionType.PolyLine, 0, 0, 1000, 1000, 0, SegPerformanceFixture.CreateSeventeenThousandPointEllipse(), 12)
                    };
                    var invalidated = new List<Rectangle>();
                    canvas.Invalidated += (sender, args) => invalidated.Add(args.InvalidRect);
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
                        canvas.SegTool = tool;
                        canvas.ModifierKeysProvider = () => Keys.None;
                        typeof(ImageCanvasControl).GetMethod("OnMouseMove", BindingFlags.Instance | BindingFlags.NonPublic)
                            .Invoke(canvas, new object[] { new MouseEventArgs(MouseButtons.None, 0, 400, 400, 0) });
                        invalidated.Clear();
                        canvas.ModifierKeysProvider = () => Keys.Control;
                        for (int index = 0; index < 5; index++)
                        {
                            typeof(ImageCanvasControl).GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic)
                                .Invoke(canvas, new object[] { new KeyEventArgs(Keys.Control | Keys.ControlKey) });
                        }

                        Assert.AreEqual(1, invalidated.Count, "Only the Ctrl state transition should request one {0} cursor repaint; key-repeat events must do nothing.", tool);
                        Assert.IsTrue(invalidated.All(area => area.Width * area.Height < canvas.ClientSize.Width * canvas.ClientSize.Height / 10),
                            "Holding Ctrl with {0} must not repaint the entire 17,000-point canvas.", tool);

                        canvas.ModifierKeysProvider = () => Keys.None;
                        typeof(ImageCanvasControl).GetMethod("OnKeyUp", BindingFlags.Instance | BindingFlags.NonPublic)
                            .Invoke(canvas, new object[] { new KeyEventArgs(Keys.ControlKey) });
                        Assert.AreEqual(2, invalidated.Count, "Releasing Ctrl should repaint the {0} cursor exactly once.", tool);
                        Assert.IsTrue(invalidated.All(area => area.Width * area.Height < canvas.ClientSize.Width * canvas.ClientSize.Height / 10));
                    }

                    canvas.SegTool = ImageCanvasControl.SegToolKind.Brush;
                    canvas.ModifierKeysProvider = () => Keys.Control;
                    invalidated.Clear();
                    typeof(ImageCanvasControl).GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic)
                        .Invoke(canvas, new object[] { new KeyEventArgs(Keys.Control | Keys.ControlKey) });
                    typeof(ImageCanvasControl).GetMethod("OnLostFocus", BindingFlags.Instance | BindingFlags.NonPublic)
                        .Invoke(canvas, new object[] { EventArgs.Empty });
                    invalidated.Clear();
                    typeof(ImageCanvasControl).GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic)
                        .Invoke(canvas, new object[] { new KeyEventArgs(Keys.Control | Keys.ControlKey) });
                    Assert.AreEqual(1, invalidated.Count, "Losing focus must reset Ctrl repeat tracking so the next press repaints the cursor.");
                }
            }
            finally
            {
                string directory = Path.GetDirectoryName(source);
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        private static FrameMeasurement MeasureGestureFrames(ImageCanvasControl canvas, ImageCanvasControl.SegToolKind tool, Keys modifierKeys)
        {
            canvas.DiscardUnfinishedShape();
            canvas.SegTool = tool;
            canvas.ModifierKeysProvider = () => modifierKeys;
            MethodInfo onPaint = typeof(ImageCanvasControl).GetMethod("OnPaint", BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo onMouseDown = typeof(ImageCanvasControl).GetMethod("OnMouseDown", BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo onMouseMove = typeof(ImageCanvasControl).GetMethod("OnMouseMove", BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo onKeyDown = typeof(ImageCanvasControl).GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo onKeyUp = typeof(ImageCanvasControl).GetMethod("OnKeyUp", BindingFlags.Instance | BindingFlags.NonPublic);
            var invalidated = new List<Rectangle>();
            canvas.Invalidated += CaptureInvalidated;
            using (var bitmap = new Bitmap(canvas.Width, canvas.Height))
            {
                try
                {
                    onMouseMove.Invoke(canvas, new object[] { new MouseEventArgs(MouseButtons.None, 0, 200, 300, 0) });
                    if ((modifierKeys & Keys.Control) == Keys.Control)
                        onKeyDown.Invoke(canvas, new object[] { new KeyEventArgs(Keys.Control | Keys.ControlKey) });
                    onMouseDown.Invoke(canvas, new object[] { new MouseEventArgs(MouseButtons.Left, 1, 200, 300, 0) });
                    invalidated.Clear();

                    var samples = new List<double>();
                    for (int index = 0; index < 70; index++)
                    {
                        invalidated.Clear();
                        var stopwatch = Stopwatch.StartNew();
                        if ((modifierKeys & Keys.Control) == Keys.Control)
                            onKeyDown.Invoke(canvas, new object[] { new KeyEventArgs(Keys.Control | Keys.ControlKey) });
                        int x = 204 + index * 5;
                        int y = 300 + (int)Math.Round(40d * Math.Sin(index / 6d));
                        MouseButtons buttons = tool == ImageCanvasControl.SegToolKind.Rect || tool == ImageCanvasControl.SegToolKind.Ellipse || tool == ImageCanvasControl.SegToolKind.Brush || tool == ImageCanvasControl.SegToolKind.Eraser
                            ? MouseButtons.Left
                            : MouseButtons.None;
                        onMouseMove.Invoke(canvas, new object[] { new MouseEventArgs(buttons, 0, x, y, 0) });
                        Rectangle clip = invalidated.Aggregate(Rectangle.Empty, (current, area) => current.IsEmpty ? area : Rectangle.Union(current, area));
                        Assert.IsFalse(clip.IsEmpty, "{0} did not invalidate a frame at sample {1}.", tool, index);
                        using (Graphics graphics = Graphics.FromImage(bitmap))
                            onPaint.Invoke(canvas, new object[] { new PaintEventArgs(graphics, clip) });
                        stopwatch.Stop();
                        if (index >= 10) samples.Add(stopwatch.Elapsed.TotalMilliseconds);
                    }
                    samples.Sort();
                    return new FrameMeasurement(samples.Average(), samples[(int)Math.Ceiling(samples.Count * 0.95d) - 1], samples[samples.Count - 1]);
                }
                finally
                {
                    if ((modifierKeys & Keys.Control) == Keys.Control)
                    {
                        canvas.ModifierKeysProvider = () => Keys.None;
                        onKeyUp.Invoke(canvas, new object[] { new KeyEventArgs(Keys.ControlKey) });
                    }
                    canvas.Invalidated -= CaptureInvalidated;
                    canvas.DiscardUnfinishedShape();
                }
            }

            void CaptureInvalidated(object sender, InvalidateEventArgs args)
            {
                invalidated.Add(args.InvalidRect);
            }
        }

        private static void AssertFrameBudget(FrameMeasurement measurement, string scenario)
        {
            Assert.IsTrue(measurement.Average <= 1000d / 60d && measurement.P95 <= 33.3d && measurement.Maximum <= 100d,
                "{0} frames exceeded the budget: avg={1:F3}, p95={2:F3}, max={3:F3} ms.", scenario, measurement.Average, measurement.P95, measurement.Maximum);
        }

        private sealed class FrameMeasurement
        {
            internal FrameMeasurement(double average, double p95, double maximum)
            {
                Average = average;
                P95 = p95;
                Maximum = maximum;
            }

            internal double Average { get; private set; }
            internal double P95 { get; private set; }
            internal double Maximum { get; private set; }
        }

        private static string TemporaryProjectPath()
        {
            return Path.Combine(Path.GetTempPath(), "Neurocle.Tests", Guid.NewGuid().ToString("N"), "project.neurocle");
        }

        private static void DeleteTemporaryProject(string path)
        {
            string directory = Path.GetDirectoryName(path);
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }

        private static void CreateBitmap(string path, Color color, int width, int height)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using (var bitmap = new Bitmap(width, height))
            {
                bitmap.SetPixel(0, 0, color);
                bitmap.Save(path, ImageFormat.Png);
            }
        }
    }

    internal static class SegPerformanceFixture
    {
        internal static SegPoint[] CreateSeventeenThousandPointEllipse()
        {
            return Enumerable.Range(0, 17000)
                .Select(index => new SegPoint(
                    (int)Math.Round(500 + 400 * Math.Cos(index * 2d * Math.PI / 17000)),
                    (int)Math.Round(500 + 300 * Math.Sin(index * 2d * Math.PI / 17000))))
                .ToArray();
        }
    }
}

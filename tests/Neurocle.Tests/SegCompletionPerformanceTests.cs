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
                    MethodInfo onPaint = typeof(ImageCanvasControl).GetMethod("OnPaint", BindingFlags.Instance | BindingFlags.NonPublic);
                    Rectangle clip = new Rectangle(350, 350, 24, 24);
                    FrameMeasurement normal = MeasureFrames(canvas, onPaint, clip, Keys.None);
                    FrameMeasurement erase = MeasureFrames(canvas, onPaint, clip, Keys.Control);

                    Console.WriteLine("17,000-point canvas frames: normal={0:F3} ms avg/{1:F3} ms p95/{2:F3} ms max, Ctrl erase={3:F3} ms avg/{4:F3} ms p95/{5:F3} ms max", normal.Average, normal.P95, normal.Maximum, erase.Average, erase.P95, erase.Maximum);
                    Assert.IsTrue(erase.Average <= 33.3d && erase.P95 <= 33.3d && erase.Maximum <= 100d,
                        "Ctrl erase frames exceeded the budget: avg={0:F3}, p95={1:F3}, max={2:F3} ms.", erase.Average, erase.P95, erase.Maximum);
                }
            }
            finally
            {
                DeleteTemporaryProject(path);
                if (File.Exists(source)) File.Delete(source);
            }
        }

        private static FrameMeasurement MeasureFrames(ImageCanvasControl canvas, MethodInfo onPaint, Rectangle clip, Keys modifierKeys)
        {
            canvas.ModifierKeysProvider = () => modifierKeys;
            using (var bitmap = new Bitmap(canvas.Width, canvas.Height))
            {
                for (int index = 0; index < 10; index++)
                {
                    using (Graphics graphics = Graphics.FromImage(bitmap))
                        onPaint.Invoke(canvas, new object[] { new System.Windows.Forms.PaintEventArgs(graphics, clip) });
                }

                var samples = new List<double>();
                for (int index = 0; index < 60; index++)
                {
                    var stopwatch = Stopwatch.StartNew();
                    using (Graphics graphics = Graphics.FromImage(bitmap))
                        onPaint.Invoke(canvas, new object[] { new System.Windows.Forms.PaintEventArgs(graphics, clip) });
                    stopwatch.Stop();
                    samples.Add(stopwatch.Elapsed.TotalMilliseconds);
                }
                samples.Sort();
                return new FrameMeasurement(samples.Average(), samples[(int)Math.Ceiling(samples.Count * 0.95d) - 1], samples[samples.Count - 1]);
            }
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

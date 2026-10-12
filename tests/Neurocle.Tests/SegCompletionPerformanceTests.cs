using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
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

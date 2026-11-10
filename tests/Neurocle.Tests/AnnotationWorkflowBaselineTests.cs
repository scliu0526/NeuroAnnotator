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
using Neurocle.Domain.Segmentation;
using Neurocle.Infrastructure.Persistence;
using Neurocle.WinForms;

namespace Neurocle.Tests
{
    [TestClass]
    public sealed class AnnotationWorkflowBaselineTests
    {
        [STATestMethod]
        [TestCategory("ManualPerformance")]
        public void Seg_move_after_erasing_hole_preserves_bridge_and_undoes_as_one_operation()
        {
            string path = TemporaryProjectPath();
            string source = Path.Combine(Path.GetDirectoryName(path), "source.png");
            SegPoint[] original =
            {
                new SegPoint(10, 10), new SegPoint(60, 10), new SegPoint(60, 20), new SegPoint(80, 20),
                new SegPoint(80, 30), new SegPoint(90, 30), new SegPoint(90, 20), new SegPoint(80, 20),
                new SegPoint(60, 20), new SegPoint(60, 60), new SegPoint(10, 60)
            };
            try
            {
                CreateBitmap(source, ImageFormat.Png, 100, 100);
                using (var store = ProjectStore.Create(path))
                {
                    ProjectImage image = store.ImportFiles(new[] { source })[0];
                    AnnotationSet set = store.CreateAnnotationSet("baseline", AnnotationSetType.Seg);
                    AnnotationClass annotationClass = store.GetClasses(set.Id).Single();
                    SegRegion region = store.AddSegPath(set.Id, image.Id, annotationClass.Id, SegRegionType.PolyGon, original, 0);
                    SegPoint[] moved = original.Select(point => new SegPoint(point.X + 1, point.Y + 1)).ToArray();
                    store.UpdateSegRegions(new[] { new SegRegion(region.Id, set.Id, image.Id, annotationClass.Id, SegRegionType.PolyGon, 0, 0, 0, 0, region.ZOrder, moved) });
                    CollectionAssert.AreEqual(moved.Select(PointSignature).ToArray(), store.GetSegRegions(set.Id, image.Id).Single().Points.Select(PointSignature).ToArray());
                    Assert.IsTrue(store.Undo(set.Id));
                    CollectionAssert.AreEqual(original.Select(PointSignature).ToArray(), store.GetSegRegions(set.Id, image.Id).Single().Points.Select(PointSignature).ToArray());
                }
            }
            finally { DeleteTemporaryProject(path); if (File.Exists(source)) File.Delete(source); }
        }

        [STATestMethod]
        [TestCategory("ManualPerformance")]
        public void Ten_seventeen_thousand_point_regions_record_drag_and_commit_baseline()
        {
            string directory = Path.Combine(Path.GetTempPath(), "Neurocle.Tests", Guid.NewGuid().ToString("N"));
            string source = Path.Combine(directory, "source.png");
            try
            {
                CreateBitmap(source, ImageFormat.Png, 5000, 3300);
                using (var canvas = new ImageCanvasControl { Size = new Size(1000, 800), SegEnabled = true, SegTool = ImageCanvasControl.SegToolKind.Select })
                {
                    canvas.CreateControl();
                    canvas.LoadImage(source);
                    WaitForImage(canvas, source);
                    var regions = new List<SegRegion>();
                    SegPoint[] basePoints = SegPerformanceFixture.CreateSeventeenThousandPointEllipse();
                    for (int index = 0; index < 10; index++)
                    {
                        int offsetX = (index % 5) * 1000;
                        int offsetY = (index / 5) * 1100;
                        regions.Add(new SegRegion(index.ToString(), "set", "image", "class", SegRegionType.PolyLine, 0, 0, 1000, 1000, index,
                            basePoints.Select(point => new SegPoint(point.X + offsetX, point.Y + offsetY)).ToArray(), 12));
                    }
                    SegPoint originalFirstPoint = regions[0].Points[0];
                    canvas.SegRegions = regions;
                    canvas.SelectAllSeg();
                    int changed = 0;
                    IReadOnlyList<SegRegion> committed = null;
                    canvas.SegRegionsChanged += updated => { changed++; committed = updated; };
                    MethodInfo mouseDown = typeof(ImageCanvasControl).GetMethod("OnMouseDown", BindingFlags.Instance | BindingFlags.NonPublic);
                    MethodInfo mouseMove = typeof(ImageCanvasControl).GetMethod("OnMouseMove", BindingFlags.Instance | BindingFlags.NonPublic);
                    var frameTimes = new List<double>();
                    var moveTimes = new List<double>();
                    var paintTimes = new List<double>();
                    Point dragStart = ImagePointToScreen(canvas, 1900, 500);
                    mouseDown.Invoke(canvas, new object[] { new MouseEventArgs(MouseButtons.Left, 1, dragStart.X, dragStart.Y, 0) });
                    mouseMove.Invoke(canvas, new object[] { new MouseEventArgs(MouseButtons.Left, 0, dragStart.X, dragStart.Y, 0) });
                    for (int index = 0; index < 30; index++)
                    {
                        var frame = Stopwatch.StartNew();
                        var move = Stopwatch.StartNew();
                        mouseMove.Invoke(canvas, new object[] { new MouseEventArgs(MouseButtons.Left, 0, dragStart.X + index, dragStart.Y + index, 0) });
                        move.Stop();
                        var paint = Stopwatch.StartNew();
                        using (var bitmap = new Bitmap(canvas.Width, canvas.Height))
                            canvas.DrawToBitmap(bitmap, canvas.ClientRectangle);
                        paint.Stop();
                        frame.Stop();
                        frameTimes.Add(frame.Elapsed.TotalMilliseconds);
                        moveTimes.Add(move.Elapsed.TotalMilliseconds);
                        paintTimes.Add(paint.Elapsed.TotalMilliseconds);
                    }
                    mouseMove.Invoke(canvas, new object[] { new MouseEventArgs(MouseButtons.Left, 0, dragStart.X + 30, dragStart.Y + 30, 0) });
                    MethodInfo mouseUp = typeof(ImageCanvasControl).GetMethod("OnMouseUp", BindingFlags.Instance | BindingFlags.NonPublic);
                    var commit = Stopwatch.StartNew();
                    mouseUp.Invoke(canvas, new object[] { new MouseEventArgs(MouseButtons.Left, 1, dragStart.X + 30, dragStart.Y + 30, 0) });
                    commit.Stop();
                    double average = moveTimes.Average();
                    double p95 = moveTimes.OrderBy(value => value).ElementAt((int)Math.Ceiling(moveTimes.Count * 0.95d) - 1);
                    double maximum = moveTimes.Max();
                    Console.WriteLine("170K-point drag baseline: moveAvg={0:F3} ms/moveP95={1:F3} ms/moveMax={2:F3} ms/paintAvg={3:F3} ms/commit={4:F3} ms", average, p95, maximum, paintTimes.Average(), commit.Elapsed.TotalMilliseconds);
                    Assert.AreEqual(30, frameTimes.Count);
                    Assert.IsTrue(average <= 16.7d, "The measured drag handler average must stay within one 60 FPS frame.");
                    Assert.IsTrue(p95 <= 16.7d, "The measured drag handler p95 must stay within one 60 FPS frame.");
                    Assert.IsTrue(maximum <= 50d, "The measured drag handler maximum must stay below 50 ms.");
                    Assert.IsTrue(commit.Elapsed.TotalMilliseconds <= 500d, "Mouse-up persistence must complete within 500 ms.");
                    Assert.AreEqual(1, changed, "The drag baseline must exercise the canvas commit event.");
                    Assert.IsNotNull(committed);
                    Assert.IsTrue(committed[0].Points[0].X != originalFirstPoint.X || committed[0].Points[0].Y != originalFirstPoint.Y,
                        "The drag baseline must move the selected SEG regions.");
                }
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }

        [STATestMethod]
        [TestCategory("ManualPerformance")]
        public void Image_switch_baseline_records_cold_warm_rapid_and_viewport_cases()
        {
            string directory = Path.Combine(Path.GetTempPath(), "Neurocle.Tests", Guid.NewGuid().ToString("N"));
            string first = Path.Combine(directory, "first.png");
            string second = Path.Combine(directory, "second.jpg");
            string third = Path.Combine(directory, "third.png");
            try
            {
                CreateBitmap(first, ImageFormat.Png, 1920, 1080);
                CreateBitmap(second, ImageFormat.Jpeg, 5120, 2880);
                CreateBitmap(third, ImageFormat.Png, 1920, 1080);
                using (var canvas = new ImageCanvasControl { Size = new Size(1000, 800) })
                {
                    canvas.CreateControl();
                    double cold = LoadAndMeasure(canvas, first);
                    canvas.SetViewState(new ImageCanvasControl.CanvasViewState(1.25d, new PointF(-120f, -80f), false));
                    ImageCanvasControl.CanvasViewState sameSizeBefore = canvas.ViewState;
                    double differentSize = LoadAndMeasure(canvas, second);
                    ImageCanvasControl.CanvasViewState differentSizeState = canvas.ViewState;
                    double sameSize = LoadAndMeasure(canvas, third);
                    ImageCanvasControl.CanvasViewState sameSizeAfter = canvas.ViewState;
                    var warm = new List<double>();
                    foreach (string path in new[] { first, second, third }) warm.Add(LoadAndMeasure(canvas, path));
                    var rapid = Stopwatch.StartNew();
                    canvas.LoadImage(first);
                    canvas.LoadImage(second);
                    canvas.LoadImage(third);
                    WaitForImage(canvas, third);
                    rapid.Stop();
                    var browse = Stopwatch.StartNew();
                    for (int index = 0; index < 100; index++) canvas.LoadImage(index % 3 == 0 ? first : (index % 3 == 1 ? second : third));
                    WaitForImage(canvas, first);
                    browse.Stop();
                    Console.WriteLine("Image switch baseline: cold={0:F3} ms/different-size={1:F3} ms/same-size={2:F3} ms/warm3={3}/rapid-last-wins={4:F3} ms/browse100={5:F3} ms/same-before={6}/{7}/{8}/different={9}/{10}/{11}/same-after={12}/{13}/{14}",
                        cold, differentSize, sameSize, string.Join(",", warm.Select(value => value.ToString("F3"))), rapid.Elapsed.TotalMilliseconds,
                        browse.Elapsed.TotalMilliseconds, sameSizeBefore.Zoom, sameSizeBefore.Origin.X, sameSizeBefore.Origin.Y,
                        differentSizeState.Zoom, differentSizeState.Origin.X, differentSizeState.Origin.Y,
                        sameSizeAfter.Zoom, sameSizeAfter.Origin.X, sameSizeAfter.Origin.Y);
                }
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }

        private static double LoadAndMeasure(ImageCanvasControl canvas, string path)
        {
            var stopwatch = Stopwatch.StartNew();
            canvas.LoadImage(path);
            WaitForImage(canvas, path);
            stopwatch.Stop();
            return stopwatch.Elapsed.TotalMilliseconds;
        }

        private static void WaitForImage(ImageCanvasControl canvas, string path)
        {
            DateTime timeout = DateTime.UtcNow.AddSeconds(10);
            while (!string.Equals(canvas.LoadedImagePath, path, StringComparison.OrdinalIgnoreCase) && DateTime.UtcNow < timeout)
            {
                System.Windows.Forms.Application.DoEvents();
                Thread.Sleep(1);
            }
            Assert.AreEqual(path, canvas.LoadedImagePath);
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

        private static void CreateBitmap(string path, ImageFormat format, int width, int height)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using (var bitmap = new Bitmap(width, height))
            {
                using (Graphics graphics = Graphics.FromImage(bitmap)) graphics.Clear(Color.Black);
                bitmap.Save(path, format);
            }
        }

        private static string PointSignature(SegPoint point) { return point.X + "," + point.Y; }

        private static Point ImagePointToScreen(ImageCanvasControl canvas, int x, int y)
        {
            ImageCanvasControl.CanvasViewState state = canvas.ViewState;
            return Point.Round(new PointF(state.Origin.X + x * (float)state.Zoom, state.Origin.Y + y * (float)state.Zoom));
        }
    }
}

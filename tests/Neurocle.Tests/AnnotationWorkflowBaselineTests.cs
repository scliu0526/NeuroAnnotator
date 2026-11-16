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
using Clipper2Lib;
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
                    store.TranslateSegRegions(new[] { region.Id }, 1, 1);
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
            string projectPath = Path.Combine(directory, "performance.neurocle");
            try
            {
                CreateBitmap(source, ImageFormat.Png, 5000, 3300);
                using (var store = ProjectStore.Create(projectPath))
                using (var host = new Form { ClientSize = new Size(1000, 800) })
                using (var canvas = new ImageCanvasControl { Dock = DockStyle.Fill, SegEnabled = true, SegTool = ImageCanvasControl.SegToolKind.Select })
                {
                    ProjectImage projectImage = store.ImportFiles(new[] { source })[0];
                    AnnotationSet set = store.CreateAnnotationSet("170K translation", AnnotationSetType.Seg);
                    AnnotationClass annotationClass = store.GetClasses(set.Id).Single();
                    SegPoint[] basePoints = SegPerformanceFixture.CreateSeventeenThousandPointEllipse();
                    for (int index = 0; index < 10; index++)
                    {
                        int offsetX = (index % 5) * 1000;
                        int offsetY = (index / 5) * 1100;
                        store.AddSegPath(set.Id, projectImage.Id, annotationClass.Id, SegRegionType.PolyLine,
                            basePoints.Select(point => new SegPoint(point.X + offsetX, point.Y + offsetY)).ToArray(), 12);
                    }
                    host.Controls.Add(canvas);
                    host.Show();
                    System.Windows.Forms.Application.DoEvents();
                    canvas.LoadImage(source);
                    WaitForImage(canvas, source);
                    var baselinePaintTimes = new List<double>();
                    for (int index = 0; index < 5; index++)
                    {
                        var baselinePaint = Stopwatch.StartNew();
                        canvas.Invalidate();
                        canvas.Update();
                        baselinePaint.Stop();
                        baselinePaintTimes.Add(baselinePaint.Elapsed.TotalMilliseconds);
                    }
                    SegRegion[] regions = store.GetSegRegions(set.Id, projectImage.Id).ToArray();
                    SegPoint originalFirstPoint = regions[0].Points[0];
                    canvas.SegRegions = regions;
                    canvas.SelectAllSeg();
                    int translated = 0;
                    double committedPaintMilliseconds = 0d;
                    canvas.SegRegionsTranslated += (ids, offsetX, offsetY) =>
                    {
                        MainForm.TranslateSegRegions(store, canvas, ids, offsetX, offsetY);
                        var committedPaint = Stopwatch.StartNew();
                        canvas.Refresh();
                        committedPaint.Stop();
                        committedPaintMilliseconds = committedPaint.Elapsed.TotalMilliseconds;
                        translated++;
                    };
                    MethodInfo mouseDown = typeof(ImageCanvasControl).GetMethod("OnMouseDown", BindingFlags.Instance | BindingFlags.NonPublic);
                    MethodInfo mouseMove = typeof(ImageCanvasControl).GetMethod("OnMouseMove", BindingFlags.Instance | BindingFlags.NonPublic);
                    var frameTimes = new List<double>();
                    var moveTimes = new List<double>();
                    var paintTimes = new List<double>();
                    SegPoint dragPoint = basePoints[1000];
                    Point dragStart = ImagePointToScreen(canvas, dragPoint.X + 1000, dragPoint.Y);
                    mouseDown.Invoke(canvas, new object[] { new MouseEventArgs(MouseButtons.Left, 1, dragStart.X, dragStart.Y, 0) });
                    mouseMove.Invoke(canvas, new object[] { new MouseEventArgs(MouseButtons.Left, 0, dragStart.X, dragStart.Y, 0) });
                    for (int index = 0; index < 30; index++)
                    {
                        var frame = Stopwatch.StartNew();
                        var move = Stopwatch.StartNew();
                        mouseMove.Invoke(canvas, new object[] { new MouseEventArgs(MouseButtons.Left, 0, dragStart.X + index, dragStart.Y + index, 0) });
                        move.Stop();
                        var paint = Stopwatch.StartNew();
                        canvas.Update();
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
                    double frameAverage = frameTimes.Average();
                    double frameP95 = frameTimes.OrderBy(value => value).ElementAt((int)Math.Ceiling(frameTimes.Count * 0.95d) - 1);
                    double frameMaximum = frameTimes.Max();
                    Console.WriteLine("170K-point drag baseline: moveAvg={0:F3} ms/moveP95={1:F3} ms/moveMax={2:F3} ms/frameAvg={3:F3} ms/frameP95={4:F3} ms/frameMax={5:F3} ms/paintAvg={6:F3} ms/commit={7:F3} ms/committedPaint={8:F3} ms/idlePaintAvg={9:F3} ms", average, p95, maximum, frameAverage, frameP95, frameMaximum, paintTimes.Average(), commit.Elapsed.TotalMilliseconds, committedPaintMilliseconds, baselinePaintTimes.Average());
                    Assert.AreEqual(30, frameTimes.Count);
                    Assert.IsTrue(frameAverage <= 16.7d, "The complete visible drag frame average must stay within one 60 FPS frame.");
                    Assert.IsTrue(frameP95 <= 16.7d, "The complete visible drag frame p95 must stay within one 60 FPS frame.");
                    Assert.IsTrue(frameMaximum <= 50d, "The complete visible drag frame maximum must stay below 50 ms.");
                    Assert.IsTrue(commit.Elapsed.TotalMilliseconds <= 500d, "Mouse-up persistence must complete within 500 ms.");
                    Assert.AreEqual(1, translated, "The drag baseline must exercise the incremental translation event.");
                    SegRegion[] afterDrag = store.GetSegRegions(set.Id, projectImage.Id).ToArray();
                    Assert.AreEqual(170000, afterDrag.Sum(region => region.Points.Count));
                    Assert.IsTrue(afterDrag[0].Points[0].X != originalFirstPoint.X || afterDrag[0].Points[0].Y != originalFirstPoint.Y,
                        "The drag baseline must move the selected SEG regions.");

                    canvas.SelectAllSeg();
                    Assert.IsTrue(canvas.NudgeSelectedAnnotations(0, 1));
                    var keyboardCommit = Stopwatch.StartNew();
                    Assert.IsTrue(canvas.CommitKeyboardNudge());
                    keyboardCommit.Stop();
                    Console.WriteLine("170K-point keyboard commit: {0:F3} ms", keyboardCommit.Elapsed.TotalMilliseconds);
                    Assert.IsTrue(keyboardCommit.Elapsed.TotalMilliseconds <= 500d, "Keyboard persistence must complete within 500 ms.");
                    Assert.AreEqual(2, translated);
                    Assert.IsTrue(store.Undo(set.Id));
                    Assert.IsTrue(store.Undo(set.Id));
                    SegRegion[] undone = store.GetSegRegions(set.Id, projectImage.Id).ToArray();
                    Assert.AreEqual(170000, undone.Sum(region => region.Points.Count));
                    Assert.AreEqual(originalFirstPoint.X, undone[0].Points[0].X);
                    Assert.AreEqual(originalFirstPoint.Y, undone[0].Points[0].Y);
                    Assert.IsTrue(store.Redo(set.Id));
                    Assert.IsTrue(store.Redo(set.Id));
                }
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }

        [STATestMethod]
        [TestCategory("ManualPerformance")]
        public void Many_holes_match_a_similar_point_count_for_move_rotate_and_scale()
        {
            Path64 manyHoles = CreateManyHolePath(64);
            Path64 denseBlock = CreateDenseBlock(manyHoles.Count);
            InteractionMetrics block = MeasureTransformInteractions(denseBlock, "dense-block", 0);
            InteractionMetrics holes = MeasureTransformInteractions(manyHoles, "many-holes", 64);

            Console.WriteLine("Hole comparison ({0} vs {1} points): block={2}; holes={3}", denseBlock.Count, manyHoles.Count, block, holes);
            AssertInteractionThresholds(block);
            AssertInteractionThresholds(holes);
            AssertComparable(block.Move, holes.Move, "move");
            AssertComparable(block.Rotate, holes.Rotate, "rotate");
            AssertComparable(block.Scale, holes.Scale, "scale");
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
                    ImageCanvasControl.CanvasViewState differentToSameState = canvas.ViewState;
                    LoadAndMeasure(canvas, first);
                    ImageCanvasControl.CanvasViewState sameSizeAfter = canvas.ViewState;
                    Assert.AreNotEqual(sameSizeBefore.Zoom, differentSizeState.Zoom, 0.001d);
                    Assert.AreEqual(differentToSameState.Zoom, sameSizeAfter.Zoom, 0.001d);
                    Assert.AreEqual(differentToSameState.Origin, sameSizeAfter.Origin);
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

        [STATestMethod]
        public void Image_decode_cache_shares_decodes_and_evicts_after_three_images()
        {
            string directory = Path.Combine(Path.GetTempPath(), "Neurocle.Tests", Guid.NewGuid().ToString("N"));
            string[] paths = Enumerable.Range(0, 4).Select(index => Path.Combine(directory, "image" + index + ".png")).ToArray();
            try
            {
                foreach (string path in paths) CreateBitmap(path, ImageFormat.Png, 1920, 1080);
                ImageDecodeCache.Clear();
                using (var first = new ImageCanvasControl { Size = new Size(400, 300) })
                using (var second = new ImageCanvasControl { Size = new Size(400, 300) })
                {
                    first.CreateControl();
                    second.CreateControl();
                    first.LoadImage(paths[0]);
                    second.LoadImage(paths[0]);
                    WaitForImage(first, paths[0]);
                    WaitForImage(second, paths[0]);
                    Assert.AreEqual(1, ImageDecodeCache.DecodeCount);
                    foreach (string path in paths.Skip(1))
                    {
                        first.LoadImage(path);
                        WaitForImage(first, path);
                    }
                    Assert.AreEqual(3, ImageDecodeCache.Count);
                }
            }
            finally { ImageDecodeCache.Clear(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }

        [STATestMethod]
        public void Image_render_cache_falls_back_at_large_zoom_without_allocating_a_huge_bitmap()
        {
            string directory = Path.Combine(Path.GetTempPath(), "Neurocle.Tests", Guid.NewGuid().ToString("N"));
            string source = Path.Combine(directory, "large.png");
            try
            {
                CreateBitmap(source, ImageFormat.Png, 5000, 3300);
                using (var canvas = new ImageCanvasControl { Size = new Size(1000, 800) })
                {
                    canvas.CreateControl();
                    canvas.LoadImage(source);
                    WaitForImage(canvas, source);
                    canvas.SetZoomPercent(400);
                    using (var bitmap = new Bitmap(canvas.Width, canvas.Height)) canvas.DrawToBitmap(bitmap, canvas.ClientRectangle);
                    FieldInfo renderedImage = typeof(ImageCanvasControl).GetField("renderedImage", BindingFlags.Instance | BindingFlags.NonPublic);
                    Assert.IsNull(renderedImage.GetValue(canvas));
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

        private sealed class InteractionSample
        {
            internal double Average;
            internal double P95;
            internal double Maximum;
            internal double Commit;
            public override string ToString() { return string.Format("avg={0:F3}/p95={1:F3}/max={2:F3}/commit={3:F3}ms", Average, P95, Maximum, Commit); }
        }

        private sealed class InteractionMetrics
        {
            internal string Name;
            internal InteractionSample Move;
            internal InteractionSample Rotate;
            internal InteractionSample Scale;
            public override string ToString() { return Name + " move[" + Move + "] rotate[" + Rotate + "] scale[" + Scale + "]"; }
        }

        private static InteractionMetrics MeasureTransformInteractions(Path64 path, string name, int expectedHoles)
        {
            string directory = Path.Combine(Path.GetTempPath(), "Neurocle.Tests", Guid.NewGuid().ToString("N"));
            string projectPath = Path.Combine(directory, "project.neurocle");
            string imagePath = Path.Combine(directory, "source.png");
            string setId = null, imageId = null;
            InteractionMetrics result;
            try
            {
                CreateBitmap(imagePath, ImageFormat.Png, 1000, 1000);
                using (var store = ProjectStore.Create(projectPath))
                using (var canvas = new ImageCanvasControl { Size = new Size(1000, 1000), SegEnabled = true, SegTool = ImageCanvasControl.SegToolKind.Select })
                {
                    ProjectImage image = store.ImportFiles(new[] { imagePath })[0];
                    imageId = image.Id;
                    string pointsJson = string.Join(",", path.Select(point => "[" + point.X + "," + point.Y + "]"));
                    string json = "{\"label_type\":\"seg\",\"source\":\"labelset\",\"time\":\"2026-11-12T00:00:00Z\",\"version\":\"4.5.2.2\",\"classes\":[{\"name\":\"shape\",\"color\":\"#00FF00\"}],\"data\":[{\"fileName\":\"source.png\",\"set\":\"\",\"classLabel\":\"\",\"regionLabel\":[{\"className\":\"shape\",\"type\":\"PolyGon\",\"points\":[" + pointsJson + "]}],\"retestset\":0,\"rotation_angle\":0,\"width\":1000,\"height\":1000}]}";
                    AnnotationSet set = store.ImportAnnotationJson(json, name, false);
                    setId = set.Id;
                    AnnotationClass annotationClass = store.GetClasses(set.Id).Single();
                    canvas.CreateControl();
                    canvas.LoadImage(imagePath);
                    WaitForImage(canvas, imagePath);
                    canvas.SetViewState(new ImageCanvasControl.CanvasViewState(1d, PointF.Empty, false));
                    canvas.SegClasses = new Dictionary<string, AnnotationClass> { { annotationClass.Id, annotationClass } };
                    canvas.SegRegions = store.GetSegRegions(set.Id, image.Id);
                    canvas.SelectAllSeg();
                    int translations = 0, transformations = 0;
                    canvas.SegRegionsTranslated += (ids, x, y) => { translations++; MainForm.TranslateSegRegions(store, canvas, ids, x, y); };
                    canvas.SegRegionsTransformed += regions =>
                    {
                        transformations++;
                        MainForm.TransformSegRegions(store, canvas, regions);
                    };

                    SegRegion beforeMove = store.GetSegRegions(set.Id, image.Id).Single();
                    InteractionSample move = MeasureGesture(canvas, new Point(320, 150), Enumerable.Range(1, 30).Select(index => new Point(320 + index / 6, 150 + index / 6)).ToArray());
                    Assert.AreEqual(1, translations, name + " move gesture did not submit a translation.");
                    SegRegion afterMove = store.GetSegRegions(set.Id, image.Id).Single();
                    AssertTranslatedExactly(beforeMove, afterMove, name + " move");
                    AssertHoleTopology(afterMove, expectedHoles, name + " move");
                    RectangleF rotatedSource = Bounds(canvas.SegRegions.Single());
                    PointF center = new PointF(rotatedSource.Left + rotatedSource.Width / 2f, rotatedSource.Top + rotatedSource.Height / 2f);
                    PointF rotationStart = new PointF(center.X, rotatedSource.Top - 24f);
                    Point[] rotationMoves = Enumerable.Range(1, 30).Select(index =>
                    {
                        double angle = 5d * Math.PI / 180d * index / 30d;
                        float dx = rotationStart.X - center.X, dy = rotationStart.Y - center.Y;
                        return Point.Round(new PointF(center.X + (float)(dx * Math.Cos(angle) - dy * Math.Sin(angle)), center.Y + (float)(dx * Math.Sin(angle) + dy * Math.Cos(angle))));
                    }).ToArray();
                    InteractionSample rotate = MeasureGesture(canvas, Point.Round(rotationStart), rotationMoves);
                    Assert.AreEqual(1, transformations, name + " rotation gesture did not submit transformed geometry.");
                    SegRegion afterRotate = store.GetSegRegions(set.Id, image.Id).Single();
                    AssertHoleTopology(afterRotate, expectedHoles, name + " rotate");
                    RectangleF scaledSource = Bounds(canvas.SegRegions.Single());
                    Point scaleStart = Point.Round(new PointF(scaledSource.Right, scaledSource.Bottom));
                    Point[] scaleMoves = Enumerable.Range(1, 30).Select(index => new Point(scaleStart.X - index, scaleStart.Y - index)).ToArray();
                    InteractionSample scale = MeasureGesture(canvas, scaleStart, scaleMoves);
                    Assert.AreEqual(2, transformations, name + " scale gesture did not submit transformed geometry.");
                    SegRegion afterScale = store.GetSegRegions(set.Id, image.Id).Single();
                    AssertHoleTopology(afterScale, expectedHoles, name + " scale");
                    Assert.IsTrue(store.Undo(set.Id));
                    AssertHoleTopology(store.GetSegRegions(set.Id, image.Id).Single(), expectedHoles, name + " undo scale");
                    Assert.IsTrue(store.Redo(set.Id));
                    AssertHoleTopology(store.GetSegRegions(set.Id, image.Id).Single(), expectedHoles, name + " redo scale");
                    result = new InteractionMetrics { Name = name, Move = move, Rotate = rotate, Scale = scale };
                }
                using (var reopened = ProjectStore.Open(projectPath)) AssertHoleTopology(reopened.GetSegRegions(setId, imageId).Single(), expectedHoles, name + " reopen");
                return result;
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }

        private static void AssertTranslatedExactly(SegRegion before, SegRegion after, string operation)
        {
            Assert.AreEqual(before.Points.Count, after.Points.Count, operation + " changed the point count.");
            int offsetX = after.Points[0].X - before.Points[0].X;
            int offsetY = after.Points[0].Y - before.Points[0].Y;
            Assert.IsTrue(offsetX != 0 || offsetY != 0, operation + " did not move the region.");
            for (int index = 0; index < before.Points.Count; index++)
            {
                Assert.AreEqual(before.Points[index].X + offsetX, after.Points[index].X, operation + " changed point order or X geometry at " + index + ".");
                Assert.AreEqual(before.Points[index].Y + offsetY, after.Points[index].Y, operation + " changed point order or Y geometry at " + index + ".");
            }
        }

        private static void AssertHoleTopology(SegRegion region, int expectedHoles, string operation)
        {
            Path64 path = new Path64(region.Points.Select(point => new Point64(point.X, point.Y)));
            Paths64 contours = Clipper.Union(new Paths64 { path }, FillRule.NonZero);
            Assert.AreEqual(expectedHoles + 1, contours.Count, operation + " changed the outer/hole contour count.");
            Assert.AreEqual(expectedHoles, CountBridgeEdges(path), operation + " changed the bridge count.");
        }

        private static int CountBridgeEdges(Path64 path)
        {
            int count = 0;
            for (int first = 0; first < path.Count; first++)
            {
                Point64 firstStart = path[first], firstEnd = path[(first + 1) % path.Count];
                if (firstStart == firstEnd) continue;
                for (int second = first + 1; second < path.Count; second++)
                {
                    Point64 secondStart = path[second], secondEnd = path[(second + 1) % path.Count];
                    if (firstStart == secondEnd && firstEnd == secondStart) { count++; break; }
                }
            }
            return count;
        }

        private static InteractionSample MeasureGesture(ImageCanvasControl canvas, Point start, IReadOnlyList<Point> moves)
        {
            MethodInfo mouseDown = typeof(ImageCanvasControl).GetMethod("OnMouseDown", BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo mouseMove = typeof(ImageCanvasControl).GetMethod("OnMouseMove", BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo mouseUp = typeof(ImageCanvasControl).GetMethod("OnMouseUp", BindingFlags.Instance | BindingFlags.NonPublic);
            mouseDown.Invoke(canvas, new object[] { new MouseEventArgs(MouseButtons.Left, 1, start.X, start.Y, 0) });
            var frames = new List<double>();
            foreach (Point move in moves)
            {
                var frame = Stopwatch.StartNew();
                mouseMove.Invoke(canvas, new object[] { new MouseEventArgs(MouseButtons.Left, 0, move.X, move.Y, 0) });
                canvas.Refresh();
                frame.Stop();
                frames.Add(frame.Elapsed.TotalMilliseconds);
            }
            Point end = moves[moves.Count - 1];
            var commit = Stopwatch.StartNew();
            mouseUp.Invoke(canvas, new object[] { new MouseEventArgs(MouseButtons.Left, 0, end.X, end.Y, 0) });
            canvas.Refresh();
            commit.Stop();
            double[] ordered = frames.OrderBy(value => value).ToArray();
            return new InteractionSample { Average = frames.Average(), P95 = ordered[(int)Math.Ceiling(ordered.Length * 0.95d) - 1], Maximum = frames.Max(), Commit = commit.Elapsed.TotalMilliseconds };
        }

        private static void AssertInteractionThresholds(InteractionMetrics metrics)
        {
            foreach (InteractionSample sample in new[] { metrics.Move, metrics.Rotate, metrics.Scale })
            {
                Assert.IsTrue(sample.Average <= 16.7d, metrics.Name + " average frame time exceeded 16.7 ms.");
                Assert.IsTrue(sample.P95 <= 16.7d, metrics.Name + " p95 frame time exceeded 16.7 ms.");
                Assert.IsTrue(sample.Maximum <= 50d, metrics.Name + " maximum frame time exceeded 50 ms.");
                Assert.IsTrue(sample.Commit <= 500d, metrics.Name + " commit exceeded 500 ms.");
            }
        }

        private static void AssertComparable(InteractionSample block, InteractionSample holes, string operation)
        {
            Assert.IsTrue(holes.P95 <= block.P95 * 2d + 5d, operation + " preview degrades disproportionately with many holes.");
            Assert.IsTrue(holes.Commit <= block.Commit * 2d + 50d, operation + " commit degrades disproportionately with many holes.");
        }

        private static Path64 CreateManyHolePath(int count)
        {
            var holes = new List<Path64>();
            for (int index = 0; index < count; index++)
            {
                int column = index % 8, row = index / 8;
                int left = 190 + column * 80, top = 190 + row * 80;
                holes.Add(SegGeometryPrototype.Rectangle(left, top, left + 28, top + 28));
            }
            return SegGeometryPrototype.BridgeHoles(SegGeometryPrototype.Rectangle(100, 100, 900, 900), holes);
        }

        private static Path64 CreateDenseBlock(int count)
        {
            var result = new Path64();
            for (int index = 0; index < count; index++)
            {
                double angle = 2d * Math.PI * index / count;
                result.Add(new Point64((long)Math.Round(500d + 400d * Math.Cos(angle)), (long)Math.Round(500d + 400d * Math.Sin(angle))));
            }
            return result;
        }

        private static RectangleF Bounds(SegRegion region)
        {
            return RectangleF.FromLTRB(region.Points.Min(point => point.X), region.Points.Min(point => point.Y), region.Points.Max(point => point.X), region.Points.Max(point => point.Y));
        }
    }
}

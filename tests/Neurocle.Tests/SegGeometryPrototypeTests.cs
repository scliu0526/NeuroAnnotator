using Clipper2Lib;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Neurocle.Domain.Segmentation;
using System;
using System.Diagnostics;

namespace Neurocle.Tests
{
    [TestClass]
    public sealed class SegGeometryPrototypeTests
    {
        [TestMethod]
        public void OverlappingNewRegionsUnionIntoOneArea()
        {
            Paths64 result = SegGeometryPrototype.Union(Paths(Rectangle(0, 0, 10, 10)), Paths(Rectangle(5, 0, 15, 10)));

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(150d, SegGeometryPrototype.Area(result));
        }

        [TestMethod]
        public void EdgeOrCornerContactDoesNotMergeNewRegions()
        {
            Paths64 edge = SegGeometryPrototype.Union(Paths(Rectangle(0, 0, 10, 10)), Paths(Rectangle(10, 0, 20, 10)));
            Paths64 corner = SegGeometryPrototype.Union(Paths(Rectangle(0, 0, 10, 10)), Paths(Rectangle(10, 10, 20, 20)));

            Assert.AreEqual(2, edge.Count);
            Assert.AreEqual(2, corner.Count);
        }

        [TestMethod]
        public void EraserCreatesHoleAndKeepsRemainingArea()
        {
            Paths64 result = SegGeometryPrototype.Difference(Paths(Rectangle(0, 0, 20, 20)), Paths(Rectangle(5, 5, 15, 15)));
            Path64 bridge = SegGeometryPrototype.BridgeHole(result[0], result[1]);

            Assert.AreEqual(2, result.Count);
            Assert.AreEqual(300d, SegGeometryPrototype.Area(result));
            Assert.AreEqual(300d, Math.Abs(Clipper.Area(bridge)));
        }

        [TestMethod]
        public void EraserCanSplitOneRegionIntoTwoComponents()
        {
            Paths64 result = SegGeometryPrototype.Difference(Paths(Rectangle(0, 0, 20, 10)), Paths(Rectangle(9, 0, 11, 10)));

            Assert.AreEqual(2, result.Count);
            Assert.AreEqual(180d, SegGeometryPrototype.Area(result));
        }

        [TestMethod]
        public void StrokeExpandsPolylineToAnArea()
        {
            Paths64 result = SegGeometryPrototype.Stroke(new Path64 { new Point64(0, 0), new Point64(20, 0) }, 4);

            Assert.IsTrue(SegGeometryPrototype.Area(result) > 80d);
        }

        [TestMethod]
        public void RoundingAndBoundsFollowAcceptedContract()
        {
            Point64 rounded = SegGeometryPrototype.RoundAndClamp(4.5, 4.5, 10, 10);
            Point64 clamped = SegGeometryPrototype.RoundAndClamp(10.5, -0.5, 10, 10);

            Assert.AreEqual(new Point64(5, 5), rounded);
            Assert.AreEqual(new Point64(9, 0), clamped);
        }

        [TestMethod]
        public void NonZeroWindingAcceptsSelfIntersectionAndRepeatedPoints()
        {
            var figureEight = new Path64
            {
                new Point64(0, 0), new Point64(10, 10), new Point64(0, 10),
                new Point64(10, 0), new Point64(0, 0), new Point64(0, 0)
            };

            Paths64 result = SegGeometryPrototype.Union(Paths(figureEight), new Paths64());

            Assert.IsTrue(SegGeometryPrototype.Area(result) > 0d);
        }

        [TestMethod]
        public void NonZeroWindingPreservesAreaForMultipleLoops()
        {
            var twoLoops = new Path64
            {
                new Point64(0, 0), new Point64(10, 0), new Point64(10, 10), new Point64(0, 10), new Point64(0, 0),
                new Point64(10, 0), new Point64(10, 10), new Point64(0, 10), new Point64(0, 0)
            };

            Paths64 result = SegGeometryPrototype.Union(Paths(twoLoops), new Paths64());

            Assert.AreEqual(100d, SegGeometryPrototype.Area(result));
        }

        [TestMethod]
        public void RectangleEllipseAndRotationProduceIntegerPaths()
        {
            Path64 rectangle = SegGeometryPrototype.Rectangle(0, 0, 20, 10);
            Path64 ellipse = SegGeometryPrototype.Ellipse(10, 10, 8, 4, 32);
            Path64 rotated = SegGeometryPrototype.RotateWithinBounds(rectangle, 90d, 10d, 10d, 30, 30);

            Assert.AreEqual(200d, Math.Abs(Clipper.Area(rectangle)));
            Assert.IsTrue(Math.Abs(Clipper.Area(ellipse)) > 90d);
            CollectionAssert.AreEqual(new Path64
            {
                new Point64(20, 0), new Point64(20, 20), new Point64(10, 20), new Point64(10, 0)
            }, rotated);
        }

        [TestMethod]
        [TestCategory("ManualPerformance")]
        public void SeventeenThousandPointRotationMaintainsThirtyFramesPerSecondAndUndoFitsLimit()
        {
            const int pointCount = 17000;
            const int frames = 60;
            Path64 path = SegGeometryPrototype.Ellipse(5000, 5000, 4000, 3000, pointCount);
            var stopwatch = Stopwatch.StartNew();
            for (var frame = 0; frame < frames; frame++)
            {
                path = SegGeometryPrototype.RotateWithinBounds(path, 0.25d, 5000d, 5000d, 10001, 10001);
            }

            stopwatch.Stop();
            Assert.IsTrue(stopwatch.Elapsed.TotalMilliseconds / frames < 1000d / 30d,
                "17,000-point rotation must stay at or above 30 FPS.");

            GC.Collect();
            long before = GC.GetTotalMemory(true);
            var undoSnapshots = new Path64[100];
            for (var index = 0; index < undoSnapshots.Length; index++)
            {
                undoSnapshots[index] = new Path64(path);
            }

            long used = GC.GetTotalMemory(true) - before;
            GC.KeepAlive(undoSnapshots);
            Assert.IsTrue(used < 512L * 1024L * 1024L,
                "100 prototype undo snapshots must remain below the 512 MiB policy limit.");
        }

        private static Paths64 Paths(Path64 path)
        {
            return new Paths64 { path };
        }

        private static Path64 Rectangle(long left, long top, long right, long bottom)
        {
            return SegGeometryPrototype.Rectangle(left, top, right, bottom);
        }
    }
}

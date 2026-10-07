using Clipper2Lib;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Neurocle.Domain.Segmentation;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

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
        public void Hole_bridge_uses_stable_nearest_mid_edge_endpoints()
        {
            Path64 outer = Rectangle(0, 0, 100, 100);
            Path64 hole = Rectangle(40, 40, 60, 60);

            Path64 first = SegGeometryPrototype.BridgeHole(outer, hole);
            Path64 second = SegGeometryPrototype.BridgeHole(outer, hole);
            BridgeLink link = BridgeLinks(first).Single();

            CollectionAssert.AreEqual(first, second);
            Assert.AreEqual(new Point64(0, 40), link.Outer);
            Assert.AreEqual(new Point64(40, 40), link.Hole);
            Assert.AreEqual(9600d, Math.Abs(Clipper.Area(first)));
        }

        [TestMethod]
        public void Hole_bridges_stay_in_concave_fill_and_route_around_other_holes()
        {
            Path64 concaveOuter = new Path64
            {
                new Point64(0, 0), new Point64(100, 0), new Point64(100, 100), new Point64(70, 100),
                new Point64(70, 30), new Point64(30, 30), new Point64(30, 100), new Point64(0, 100)
            };
            Path64 concaveHole = Rectangle(5, 45, 15, 65);
            BridgeLink concaveLink = BridgeLinks(SegGeometryPrototype.BridgeHole(concaveOuter, concaveHole)).Single();
            Assert.IsTrue(PointsOnSegment(concaveLink.Outer, concaveLink.Hole).All(point => PointInPath(point, concaveOuter)));

            Path64 outer = Rectangle(0, 0, 100, 100);
            Path64 target = Rectangle(40, 40, 60, 60);
            Path64 blocker = Rectangle(45, 15, 55, 35);
            Path64 bridged = SegGeometryPrototype.BridgeHoles(outer, new[] { target, blocker });
            BridgeLink targetLink = BridgeLinks(bridged).Single(link => PointOnPath(link.Hole, target));

            Assert.AreEqual(new Point64(0, 40), targetLink.Outer);
            Assert.AreEqual(new Point64(40, 40), targetLink.Hole);
            Assert.AreEqual(9400d, Math.Abs(Clipper.Area(bridged)));
        }

        [TestMethod]
        public void Hole_bridge_inserts_a_nearest_inner_edge_endpoint()
        {
            Path64 outer = new Path64
            {
                new Point64(0, 0), new Point64(100, 0), new Point64(100, 100), new Point64(0, 100),
                new Point64(0, 70), new Point64(20, 70), new Point64(20, 50), new Point64(0, 50)
            };
            Path64 hole = Rectangle(40, 45, 60, 65);

            Path64 bridged = SegGeometryPrototype.BridgeHole(outer, hole);
            BridgeLink link = BridgeLinks(bridged).Single();

            Assert.AreEqual(new Point64(20, 50), link.Outer);
            Assert.AreEqual(new Point64(40, 50), link.Hole);
            Assert.AreEqual(9200d, Math.Abs(Clipper.Area(bridged)));
        }

        [TestMethod]
        public void Hole_bridge_searches_past_blocked_nearest_edge_points()
        {
            Path64 outer = new Path64
            {
                new Point64(-100, -100), new Point64(200, -100), new Point64(200, 200), new Point64(-100, 200),
                new Point64(-100, 100), new Point64(20, 50), new Point64(-100, 0)
            };
            Path64 target = Rectangle(80, 0, 100, 100);
            Path64 blocker = Rectangle(30, 45, 70, 55);

            Path64 bridged = SegGeometryPrototype.BridgeHoles(outer, new[] { target, blocker });
            BridgeLink targetLink = BridgeLinks(bridged).Single(link => PointOnPath(link.Hole, target));

            Assert.AreEqual(new Point64(20, 50), targetLink.Outer);
            Assert.AreEqual(new Point64(80, 19), targetLink.Hole);
        }

        [TestMethod]
        public void Hole_bridge_searches_around_a_blocking_corner()
        {
            Path64 outer = Rectangle(0, 0, 200, 200);
            Path64 target = new Path64 { new Point64(100, 40), new Point64(130, 80), new Point64(70, 80) };
            Path64 blocker = Rectangle(92, 10, 108, 48);

            Path64 bridged = SegGeometryPrototype.BridgeHoles(outer, new[] { target, blocker });
            BridgeLink targetLink = BridgeLinks(bridged).Single(link => PointOnPath(link.Hole, target));

            Assert.AreEqual(new Point64(91, 0), targetLink.Outer);
            Assert.AreEqual(new Point64(91, 52), targetLink.Hole);
        }

        [TestMethod]
        [TestCategory("ManualPerformance")]
        public void Seventeen_thousand_point_hole_bridge_completes_within_one_second()
        {
            Path64 outer = SegGeometryPrototype.Ellipse(5000, 5000, 4500, 4500, 17000);
            Path64 hole = Rectangle(4900, 4900, 5100, 5100);
            var stopwatch = Stopwatch.StartNew();

            Path64 bridged = SegGeometryPrototype.BridgeHole(outer, hole);

            stopwatch.Stop();
            Assert.IsTrue(stopwatch.Elapsed <= TimeSpan.FromSeconds(1), "17,000-point hole bridge took {0:F3} ms.", stopwatch.Elapsed.TotalMilliseconds);
            Assert.AreEqual(Math.Abs(Clipper.Area(outer)) - 40000d, Math.Abs(Clipper.Area(bridged)));
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

        private static IReadOnlyList<BridgeLink> BridgeLinks(Path64 path)
        {
            var result = new List<BridgeLink>();
            for (int index = 0; index + 3 < path.Count; index++)
            {
                Point64 outer = path[index];
                Point64 hole = path[index + 1];
                for (int end = index + 2; end + 1 < path.Count; end++)
                {
                    if (path[end] == hole && path[end + 1] == outer)
                    {
                        result.Add(new BridgeLink(outer, hole));
                        index = end;
                        break;
                    }
                }
            }
            return result;
        }

        private static IEnumerable<Point64> PointsOnSegment(Point64 first, Point64 second)
        {
            long count = GreatestCommonDivisor(Math.Abs(second.X - first.X), Math.Abs(second.Y - first.Y));
            for (long index = 0; index <= count; index++) yield return new Point64(first.X + (second.X - first.X) / count * index, first.Y + (second.Y - first.Y) / count * index);
        }

        private static bool PointOnPath(Point64 point, Path64 path)
        {
            return path.Any(value => value == point) || path.Select((value, index) => new { Start = value, End = path[(index + 1) % path.Count] }).Any(edge => Cross(edge.Start, edge.End, point) == 0 && point.X >= Math.Min(edge.Start.X, edge.End.X) && point.X <= Math.Max(edge.Start.X, edge.End.X) && point.Y >= Math.Min(edge.Start.Y, edge.End.Y) && point.Y <= Math.Max(edge.Start.Y, edge.End.Y));
        }

        private static bool PointInPath(Point64 point, Path64 path)
        {
            if (PointOnPath(point, path)) return true;
            bool inside = false;
            for (int current = 0, previous = path.Count - 1; current < path.Count; previous = current++)
            {
                Point64 a = path[current]; Point64 b = path[previous];
                if ((a.Y > point.Y) != (b.Y > point.Y) && point.X < (b.X - a.X) * (point.Y - a.Y) / (double)(b.Y - a.Y) + a.X) inside = !inside;
            }
            return inside;
        }

        private static long Cross(Point64 first, Point64 second, Point64 third)
        {
            return (second.X - first.X) * (third.Y - first.Y) - (second.Y - first.Y) * (third.X - first.X);
        }

        private static long GreatestCommonDivisor(long first, long second)
        {
            while (second != 0) { long remainder = first % second; first = second; second = remainder; }
            return first;
        }

        private sealed class BridgeLink
        {
            public BridgeLink(Point64 outer, Point64 hole) { Outer = outer; Hole = hole; }
            public Point64 Outer { get; private set; }
            public Point64 Hole { get; private set; }
        }
    }
}

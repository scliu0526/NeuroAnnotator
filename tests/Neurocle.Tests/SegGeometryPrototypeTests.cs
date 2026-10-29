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
        public void Equal_length_bridge_prefers_a_still_legal_existing_connection()
        {
            Path64 outer = Rectangle(0, 0, 100, 100);
            Path64 hole = Rectangle(40, 40, 60, 60);
            var existing = new SegGeometryPrototype.BridgeConnection(0, new Point64(40, 0), 1, new Point64(40, 40));

            Path64 bridged = SegGeometryPrototype.BridgeHoles(outer, new[] { hole }, new[] { existing });
            BridgeLink link = BridgeLinks(bridged).Single();

            Assert.AreEqual(new Point64(40, 0), link.Outer);
            Assert.AreEqual(new Point64(40, 40), link.Hole);
            Assert.AreEqual(9600d, Math.Abs(Clipper.Area(bridged)));
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
            Assert.AreEqual(2, BridgeLinks(bridged).Count, "Two holes require two links in the minimum spanning network.");
            AssertBridgeNetworkIsLegal(bridged, outer, target, blocker);
            AssertFilledMaskMatchesContours(bridged, outer, target, blocker);
            Assert.AreEqual(9400d, Math.Abs(Clipper.Area(bridged)));
        }

        [TestMethod]
        public void Multiple_holes_use_the_shortest_connected_bridge_network()
        {
            Path64 outer = Rectangle(0, 0, 100, 100);
            Path64 firstHole = Rectangle(20, 20, 40, 40);
            Path64 secondHole = Rectangle(45, 20, 65, 40);

            Path64 bridged = SegGeometryPrototype.BridgeHoles(outer, new[] { firstHole, secondHole });
            BridgeLink[] links = BridgeLinks(bridged).ToArray();

            Assert.AreEqual(2, links.Length, string.Join(";", bridged.Select(point => point.X + "," + point.Y)));
            Assert.IsTrue(links.Any(link => link.Outer == new Point64(0, 20) && link.Hole == new Point64(20, 20)));
            Assert.IsTrue(links.Any(link => link.Outer == new Point64(40, 20) && link.Hole == new Point64(45, 20)));
            AssertBridgeNetworkIsLegal(bridged, outer, firstHole, secondHole);
            AssertFilledMaskMatchesContours(bridged, outer, firstHole, secondHole);
            Assert.AreEqual(25d, links.Sum(link => Math.Sqrt(SquaredDistance(link.Outer, link.Hole))));
            Assert.AreEqual(9200d, Math.Abs(Clipper.Area(bridged)));
        }

        [TestMethod]
        public void One_pixel_gap_between_holes_keeps_the_actual_shortest_bridge()
        {
            Path64 outer = Rectangle(0, 0, 100, 100);
            Path64 firstHole = Rectangle(20, 20, 40, 40);
            Path64 secondHole = Rectangle(41, 20, 61, 40);

            Path64 bridged = SegGeometryPrototype.BridgeHoles(outer, new[] { firstHole, secondHole });
            BridgeLink link = BridgeLinks(bridged).First(edge =>
                (edge.Outer == new Point64(40, 20) && edge.Hole == new Point64(41, 20)) ||
                (edge.Outer == new Point64(41, 20) && edge.Hole == new Point64(40, 20)));

            Assert.IsNotNull(link);
            Assert.AreEqual(9200d, Math.Abs(Clipper.Area(bridged)));
        }

        [TestMethod]
        public void Three_holes_can_form_a_chain_that_remains_connected_to_the_outer_boundary()
        {
            Path64 outer = Rectangle(0, 0, 200, 100);
            Path64 firstHole = Rectangle(20, 20, 40, 40);
            Path64 secondHole = Rectangle(45, 20, 65, 40);
            Path64 thirdHole = Rectangle(70, 20, 90, 40);

            Path64 bridged = SegGeometryPrototype.BridgeHoles(outer, new[] { firstHole, secondHole, thirdHole });
            BridgeLink[] links = BridgeLinks(bridged).ToArray();

            Assert.AreEqual(3, links.Length);
            Assert.IsTrue(links.Any(link => link.Outer == new Point64(0, 20) && link.Hole == new Point64(20, 20)));
            Assert.IsTrue(links.Any(link => link.Outer == new Point64(40, 20) && link.Hole == new Point64(45, 20)));
            Assert.IsTrue(links.Any(link => link.Outer == new Point64(65, 20) && link.Hole == new Point64(70, 20)));
            AssertBridgeNetworkIsLegal(bridged, outer, firstHole, secondHole, thirdHole);
            AssertFilledMaskMatchesContours(bridged, outer, firstHole, secondHole, thirdHole);
            Assert.AreEqual(30d, links.Sum(link => Math.Sqrt(SquaredDistance(link.Outer, link.Hole))));
            Assert.AreEqual(18800d, Math.Abs(Clipper.Area(bridged)));
        }

        [TestMethod]
        public void Equal_geometry_produces_the_same_network_when_hole_input_order_changes()
        {
            Path64 outer = Rectangle(0, 0, 200, 100);
            Path64 firstHole = Rectangle(20, 20, 40, 40);
            Path64 secondHole = Rectangle(45, 20, 65, 40);
            Path64 thirdHole = Rectangle(70, 20, 90, 40);

            Path64 forward = SegGeometryPrototype.BridgeHoles(outer, new[] { firstHole, secondHole, thirdHole });
            Path64 reverse = SegGeometryPrototype.BridgeHoles(outer, new[] { thirdHole, secondHole, firstHole });

            CollectionAssert.AreEqual(forward, reverse);
        }

        [TestMethod]
        public void Equal_distance_hole_cycle_uses_geometry_order_independent_of_input_order()
        {
            Path64 outer = Rectangle(0, 0, 100, 100);
            Path64 a = Rectangle(20, 20, 30, 30);
            Path64 b = Rectangle(40, 20, 50, 30);
            Path64 c = Rectangle(20, 40, 30, 50);
            Path64 d = Rectangle(40, 40, 50, 50);

            Path64 first = SegGeometryPrototype.BridgeHoles(outer, new[] { a, b, c, d });
            Path64 reordered = SegGeometryPrototype.BridgeHoles(outer, new[] { b, a, d, c });

            CollectionAssert.AreEqual(first, reordered);
            AssertBridgeNetworkIsLegal(first, outer, a, b, c, d);
        }

        [TestMethod]
        public void Bridge_network_fails_when_a_hole_cannot_connect_to_the_outer_fill()
        {
            Path64 outer = Rectangle(0, 0, 100, 100);
            Path64 outsideHole = Rectangle(120, 20, 140, 40);

            Assert.ThrowsException<InvalidOperationException>(() => SegGeometryPrototype.BridgeHoles(outer, new[] { outsideHole }));
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
            Assert.AreEqual(2, BridgeLinks(bridged).Count);
            AssertBridgeNetworkIsLegal(bridged, outer, target, blocker);
            AssertFilledMaskMatchesContours(bridged, outer, target, blocker);
        }

        [TestMethod]
        public void Hole_bridge_searches_around_a_blocking_corner()
        {
            Path64 outer = Rectangle(0, 0, 200, 200);
            Path64 target = new Path64 { new Point64(100, 40), new Point64(130, 80), new Point64(70, 80) };
            Path64 blocker = Rectangle(92, 10, 108, 38);

            Path64 bridged = SegGeometryPrototype.BridgeHoles(outer, new[] { target, blocker });
            Assert.AreEqual(2, BridgeLinks(bridged).Count);
            AssertBridgeNetworkIsLegal(bridged, outer, target, blocker);
            AssertFilledMaskMatchesContours(bridged, outer, target, blocker);
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
        [TestCategory("ManualPerformance")]
        public void Ten_hole_bridge_network_with_seventeen_thousand_total_points_completes_within_two_seconds()
        {
            Path64 outer = SegGeometryPrototype.Ellipse(5000, 5000, 4500, 4500, 16960);
            Path64[] holes = Enumerable.Range(0, 10).Select(index =>
            {
                double radians = 2d * Math.PI * index / 10;
                long centerX = (long)Math.Round(5000 + 2200 * Math.Cos(radians));
                long centerY = (long)Math.Round(5000 + 2200 * Math.Sin(radians));
                return Rectangle(centerX - 20, centerY - 20, centerX + 20, centerY + 20);
            }).ToArray();
            var stopwatch = Stopwatch.StartNew();

            Path64 bridged = SegGeometryPrototype.BridgeHoles(outer, holes);

            stopwatch.Stop();
            Assert.IsTrue(stopwatch.Elapsed <= TimeSpan.FromSeconds(2), "17,000-point, ten-hole bridge network took {0:F3} ms.", stopwatch.Elapsed.TotalMilliseconds);
            Assert.AreEqual(10, BridgeLinks(bridged).Count);
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
        public void StrokeTurnsOnePointIntoARoundDotAtTheRequestedWidth()
        {
            Paths64 result = SegGeometryPrototype.Stroke(new Path64 { new Point64(20, 20) }, 10);

            Assert.AreEqual(1, result.Count);
            Assert.IsTrue(SegGeometryPrototype.Area(result) > 70d && SegGeometryPrototype.Area(result) < 90d);
            Assert.AreEqual(15, result[0].Min(point => point.X));
            Assert.AreEqual(25, result[0].Max(point => point.X));
            Assert.AreEqual(15, result[0].Min(point => point.Y));
            Assert.AreEqual(25, result[0].Max(point => point.Y));

            Paths64 edgeDot = SegGeometryPrototype.Stroke(new Path64 { new Point64(5, 50) }, 10);
            Assert.AreEqual(0, edgeDot[0].Min(point => point.X), "Clamping the center to half the width keeps the dot inside the image edge.");
            Assert.AreEqual(10, edgeDot[0].Max(point => point.X));
        }

        [TestMethod]
        public void StrokeFoldbackDoesNotFillTheUnbrushedAreaBetweenPasses()
        {
            Paths64 result = SegGeometryPrototype.Stroke(new Path64
            {
                new Point64(20, 20), new Point64(80, 20), new Point64(20, 20)
            }, 10);
            Paths64 unbrushedCenter = Clipper.Intersect(result, new Paths64 { Rectangle(45, 27, 55, 33) }, FillRule.NonZero);

            Assert.AreEqual(0d, SegGeometryPrototype.Area(unbrushedCenter));
            Assert.IsTrue(SegGeometryPrototype.Area(result) > 600d && SegGeometryPrototype.Area(result) < 700d);
        }

        [TestMethod]
        public void StrokeSelfIntersectionRemainsARoundUnionOfBrushPasses()
        {
            Paths64 result = SegGeometryPrototype.Stroke(new Path64
            {
                new Point64(20, 20), new Point64(80, 80), new Point64(20, 80), new Point64(80, 20)
            }, 10);
            Paths64 crossing = Clipper.Intersect(result, new Paths64 { Rectangle(47, 47, 53, 53) }, FillRule.NonZero);

            Assert.IsTrue(SegGeometryPrototype.Area(result) > 0d);
            Assert.IsTrue(SegGeometryPrototype.Area(crossing) > 0d);
        }

        [TestMethod]
        public void StrokeUsesRoundJoinsInsteadOfMiteredCorners()
        {
            Paths64 result = SegGeometryPrototype.Stroke(new Path64
            {
                new Point64(20, 20), new Point64(40, 20), new Point64(40, 40)
            }, 10);

            double furthestCornerDistance = result.SelectMany(path => path)
                .Where(point => point.X >= 40 && point.Y <= 20)
                .Max(point => Math.Sqrt(Math.Pow(point.X - 40, 2) + Math.Pow(point.Y - 20, 2)));
            Assert.IsTrue(furthestCornerDistance <= 6d, "A round join should not extend to a square miter corner.");
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
                        break;
                    }
                }
            }
            return result;
        }

        private static void AssertBridgeNetworkIsLegal(Path64 bridgePath, params Path64[] contours)
        {
            BridgeLink[] links = BridgeLinks(bridgePath).ToArray();
            Assert.AreEqual(contours.Length - 1, links.Length, "The bridge network must be a tree with one fewer edge than contours.");
            var connected = new HashSet<int> { 0 };
            bool changed;
            do
            {
                changed = false;
                foreach (BridgeLink link in links)
                {
                    int first = Array.FindIndex(contours, contour => PointOnPath(link.Outer, contour));
                    int second = Array.FindIndex(contours, contour => PointOnPath(link.Hole, contour));
                    Assert.IsTrue(first >= 0 && second >= 0 && first != second, "Every bridge must join two distinct contour boundaries.");
                    if (connected.Contains(first) && connected.Add(second)) changed = true;
                    if (connected.Contains(second) && connected.Add(first)) changed = true;
                    for (int step = 1; step < 100; step++)
                    {
                        double fraction = step / 100d;
                        double x = link.Outer.X + (link.Hole.X - link.Outer.X) * fraction;
                        double y = link.Outer.Y + (link.Hole.Y - link.Outer.Y) * fraction;
                        Assert.IsTrue(PointInPath(x, y, contours[0]), "A bridge segment must remain inside the outer boundary.");
                        for (int holeIndex = 1; holeIndex < contours.Length; holeIndex++)
                        {
                            Assert.IsFalse(PointInPath(x, y, contours[holeIndex], false), "A bridge segment must not cross a hole interior.");
                            if (holeIndex != first && holeIndex != second)
                            {
                                Assert.IsFalse(SegmentIntersectsPath(link.Outer, link.Hole, contours[holeIndex]), "A bridge segment must not touch another hole.");
                            }
                        }
                    }
                }
            } while (changed);
            Assert.AreEqual(contours.Length, connected.Count, "Every hole must be connected to the outer contour.");
        }

        private static void AssertFilledMaskMatchesContours(Path64 bridgePath, params Path64[] contours)
        {
            Path64 outer = contours[0];
            Paths64 expectedMask = Clipper.Difference(new Paths64 { outer }, new Paths64(contours.Skip(1)), FillRule.NonZero);
            Paths64 actualMask = Clipper.Union(new Paths64 { bridgePath }, FillRule.NonZero);
            Paths64 mismatch = Clipper.Union(
                Clipper.Difference(expectedMask, actualMask, FillRule.NonZero),
                Clipper.Difference(actualMask, expectedMask, FillRule.NonZero),
                FillRule.NonZero);
            Assert.AreEqual(0d, SegGeometryPrototype.Area(mismatch), "The Non-Zero filled mask must remain unchanged.");
            for (long y = outer.Min(point => point.Y); y < outer.Max(point => point.Y); y++)
            for (long x = outer.Min(point => point.X); x < outer.Max(point => point.X); x++)
            {
                double sampleX = x + 0.5d;
                double sampleY = y + 0.5d;
                bool expected = PointInPath(sampleX, sampleY, outer) && !contours.Skip(1).Any(hole => PointInPath(sampleX, sampleY, hole, false));
                Assert.AreEqual(expected, PointInPath(sampleX, sampleY, bridgePath), "Bridge traversal must preserve the original filled mask at ({0}, {1}); path: {2}.", sampleX, sampleY, string.Join(";", bridgePath.Select(point => point.X + "," + point.Y)));
            }
        }

        private static bool PointInPath(double x, double y, Path64 path, bool includeBoundary = true)
        {
            bool inside = false;
            for (int current = 0, previous = path.Count - 1; current < path.Count; previous = current++)
            {
                Point64 a = path[current];
                Point64 b = path[previous];
                double cross = (x - a.X) * (b.Y - a.Y) - (y - a.Y) * (b.X - a.X);
                if (Math.Abs(cross) < 1e-9 && x >= Math.Min(a.X, b.X) && x <= Math.Max(a.X, b.X) && y >= Math.Min(a.Y, b.Y) && y <= Math.Max(a.Y, b.Y)) return includeBoundary;
                if ((a.Y > y) != (b.Y > y) && x < (b.X - a.X) * (y - a.Y) / (double)(b.Y - a.Y) + a.X) inside = !inside;
            }
            return inside;
        }

        private static bool SegmentIntersectsPath(Point64 first, Point64 second, Path64 path)
        {
            for (int index = 0; index < path.Count; index++)
            {
                Point64 start = path[index];
                Point64 end = path[(index + 1) % path.Count];
                long a = Cross(first, second, start);
                long b = Cross(first, second, end);
                long c = Cross(start, end, first);
                long d = Cross(start, end, second);
                if ((a > 0 && b < 0 || a < 0 && b > 0) && (c > 0 && d < 0 || c < 0 && d > 0)) return true;
                if (a == 0 && PointOnPath(start, new Path64 { first, second }) || b == 0 && PointOnPath(end, new Path64 { first, second }) || c == 0 && PointOnPath(first, new Path64 { start, end }) || d == 0 && PointOnPath(second, new Path64 { start, end })) return true;
            }
            return false;
        }

        private static double SquaredDistance(Point64 first, Point64 second)
        {
            double x = first.X - second.X;
            double y = first.Y - second.Y;
            return x * x + y * y;
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

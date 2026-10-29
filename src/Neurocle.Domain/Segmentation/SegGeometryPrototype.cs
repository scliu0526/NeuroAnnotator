using System;
using System.Collections.Generic;
using System.Linq;
using Clipper2Lib;

namespace Neurocle.Domain.Segmentation
{
    /// <summary>
    /// Throwaway contract prototype for the accepted SEG integer geometry model.
    /// It deliberately exposes only operations that later SEG use-cases need.
    /// </summary>
    public static class SegGeometryPrototype
    {
        public static Paths64 Union(Paths64 subject, Paths64 clip)
        {
            // Clipper's raw union also joins contours that merely touch. The
            // product rule is stricter: only a positive-area overlap merges.
            if (subject.Count == 0 || clip.Count == 0)
            {
                return Clipper.Union(Combine(subject, clip), FillRule.NonZero);
            }

            if (Area(Clipper.Intersect(subject, clip, FillRule.NonZero)) == 0)
            {
                return Combine(subject, clip);
            }

            return Clipper.Union(Combine(subject, clip), FillRule.NonZero);
        }

        public static Paths64 Difference(Paths64 subject, Paths64 eraser)
        {
            return Clipper.Difference(subject, eraser, FillRule.NonZero);
        }

        public static Paths64 Stroke(Path64 centerLine, long width)
        {
            if (width < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(width));
            }

            return Clipper.InflatePaths(new Paths64 { centerLine }, width / 2.0, JoinType.Round, EndType.Round);
        }

        public static Path64 Rectangle(long left, long top, long right, long bottom)
        {
            return new Path64
            {
                new Point64(left, top), new Point64(right, top), new Point64(right, bottom), new Point64(left, bottom)
            };
        }

        public static Path64 Ellipse(long centerX, long centerY, long radiusX, long radiusY, int segments)
        {
            if (radiusX < 1 || radiusY < 1 || segments < 8)
            {
                throw new ArgumentOutOfRangeException();
            }

            var result = new Path64(segments);
            for (var index = 0; index < segments; index++)
            {
                double radians = 2d * Math.PI * index / segments;
                result.Add(new Point64(
                    (long)Math.Round(centerX + radiusX * Math.Cos(radians), MidpointRounding.AwayFromZero),
                    (long)Math.Round(centerY + radiusY * Math.Sin(radians), MidpointRounding.AwayFromZero)));
            }

            return result;
        }

        public static Path64 Rotate(Path64 path, double degrees, double centerX, double centerY)
        {
            double radians = degrees * Math.PI / 180d;
            double cosine = Math.Cos(radians);
            double sine = Math.Sin(radians);
            var result = new Path64(path.Count);
            foreach (Point64 point in path)
            {
                double x = point.X - centerX;
                double y = point.Y - centerY;
                result.Add(new Point64(
                    (long)Math.Round(centerX + x * cosine - y * sine, MidpointRounding.AwayFromZero),
                    (long)Math.Round(centerY + x * sine + y * cosine, MidpointRounding.AwayFromZero)));
            }

            return result;
        }

        public static Path64 RotateWithinBounds(Path64 path, double degrees, double centerX, double centerY, long width, long height)
        {
            Path64 rotated = Rotate(path, degrees, centerX, centerY);
            foreach (Point64 point in rotated)
            {
                if (point.X < 0 || point.Y < 0 || point.X >= width || point.Y >= height)
                {
                    return new Path64(path);
                }
            }

            return rotated;
        }

        public static Path64 BridgeHole(Path64 outer, Path64 hole)
        {
            return BridgeHoles(outer, new[] { hole });
        }

        public static Path64 BridgeHoles(Path64 outer, IEnumerable<Path64> holes)
        {
            return BridgeHoles(outer, holes, null);
        }

        public static Path64 BridgeHoles(Path64 outer, IEnumerable<Path64> holes, IEnumerable<BridgeConnection> existingConnections)
        {
            if (outer == null || outer.Count < 3)
            {
                throw new ArgumentException("Both contours must be closed regions.");
            }

            Path64 outerPath = new Path64(outer);
            Path64[] holePaths = (holes ?? throw new ArgumentNullException(nameof(holes))).Select(path => new Path64(path)).ToArray();
            if (holePaths.Any(path => path.Count < 3)) throw new ArgumentException("Both contours must be closed regions.", nameof(holes));
            int outerOrientation = Math.Sign(Clipper.Area(outerPath));
            if (outerOrientation == 0) throw new ArgumentException("The outer contour must have area.", nameof(outer));
            foreach (Path64 holePath in holePaths)
            {
                if (Math.Sign(Clipper.Area(holePath)) == outerOrientation) holePath.Reverse();
            }

            Path64[] contours = new[] { outerPath }.Concat(holePaths).ToArray();
            BridgeConnection[] existing = (existingConnections ?? Enumerable.Empty<BridgeConnection>()).ToArray();
            if (existing.Any(link => link == null || link.FirstContourIndex < 0 || link.SecondContourIndex < 0 || link.FirstContourIndex >= contours.Length || link.SecondContourIndex >= contours.Length || link.FirstContourIndex == link.SecondContourIndex))
            {
                throw new ArgumentException("Existing bridge endpoints must refer to two distinct contours.", nameof(existingConnections));
            }
            var candidates = new List<BridgeSelection>();
            for (int first = 0; first < contours.Length; first++)
            for (int second = first + 1; second < contours.Length; second++)
            {
                Path64[] blockers = holePaths.Where((_, index) => index + 1 != first && index + 1 != second).ToArray();
                BridgeSelection candidate = FindShortestLegalBridge(contours[first], contours[second], outerPath, holePaths, blockers, first, second);
                if (candidate != null) candidates.Add(candidate);
                foreach (BridgeConnection connection in existing.Where(link => link.FirstContourIndex == first && link.SecondContourIndex == second))
                {
                    if (IsLegalBridge(connection.FirstPoint, connection.SecondPoint, contours[first], contours[second], outerPath, holePaths, blockers))
                    {
                        candidates.Add(new BridgeSelection(connection.FirstPoint, connection.SecondPoint, first, second, true));
                    }
                }
            }
            candidates.Sort();
            var parent = Enumerable.Range(0, contours.Length).ToArray();
            var selected = new List<BridgeSelection>();
            foreach (BridgeSelection candidate in candidates)
            {
                int firstRoot = FindRoot(parent, candidate.FirstContourIndex);
                int secondRoot = FindRoot(parent, candidate.SecondContourIndex);
                if (firstRoot == secondRoot) continue;
                parent[firstRoot] = secondRoot;
                selected.Add(candidate);
                if (selected.Count == contours.Length - 1) break;
            }
            if (selected.Count != contours.Length - 1) throw new BridgeNetworkException();

            for (int index = 0; index < contours.Length; index++)
            {
                Point64[] endpoints = selected.SelectMany(edge => new[]
                {
                    edge.FirstContourIndex == index ? edge.FirstPoint : (Point64?)null,
                    edge.SecondContourIndex == index ? edge.SecondPoint : (Point64?)null
                }).Where(point => point.HasValue).Select(point => point.Value).ToArray();
                contours[index] = InsertBridgePoints(contours[index], endpoints);
            }
            return BuildBridgeNetwork(contours, selected);
        }

        public static Path64 Rebridge(Path64 polygon)
        {
            if (polygon == null) throw new ArgumentNullException(nameof(polygon));
            List<UndirectedEdge> bridges = FindBridgeSegments(polygon);
            if (bridges.Count == 0) return new Path64(polygon);
            Paths64 contours = ExtractBridgeContours(polygon, bridges);
            if (contours == null) throw new BridgeNetworkException();
            if (contours.Count < 2) throw new BridgeNetworkException();
            Path64 outer = contours.OrderByDescending(path => Math.Abs(Clipper.Area(path))).First();
            Path64[] holes = contours.Where(path => !ReferenceEquals(path, outer)).ToArray();
            if (holes.Any(path => Math.Sign(Clipper.Area(path)) == Math.Sign(Clipper.Area(outer)) || !IsPointInside(outer, path[0]))) throw new BridgeNetworkException();
            return BridgeHoles(outer, holes, MapBridgeSegments(bridges, new[] { outer }.Concat(holes).ToArray()));
        }

        public static IReadOnlyList<BridgeConnection> FindExistingBridgeConnections(Path64 polygon, Path64 outer, IReadOnlyList<Path64> holes)
        {
            if (polygon == null) throw new ArgumentNullException(nameof(polygon));
            if (outer == null) throw new ArgumentNullException(nameof(outer));
            if (holes == null) throw new ArgumentNullException(nameof(holes));
            var contours = new[] { outer }.Concat(holes).ToArray();
            return MapBridgeSegments(FindBridgeSegments(polygon), contours);
        }

        private static IReadOnlyList<BridgeConnection> MapBridgeSegments(IEnumerable<UndirectedEdge> bridges, params Path64[] contours)
        {
            if (contours == null) throw new ArgumentNullException(nameof(contours));
            if (contours.Length == 0) return new BridgeConnection[0];
            var result = new List<BridgeConnection>();
            foreach (UndirectedEdge bridge in bridges)
            {
                int firstContour = FindContainingContour(contours, bridge.First);
                int secondContour = FindContainingContour(contours, bridge.Second);
                if (firstContour < 0 || secondContour < 0 || firstContour == secondContour) continue;
                result.Add(new BridgeConnection(firstContour, bridge.First, secondContour, bridge.Second));
            }
            return result;
        }

        private static List<UndirectedEdge> FindBridgeSegments(Path64 polygon)
        {
            var traversals = new Dictionary<UndirectedEdge, EdgeTraversal>();
            for (int index = 0; index < polygon.Count; index++)
            {
                Point64 start = polygon[index];
                Point64 end = polygon[(index + 1) % polygon.Count];
                if (start == end) continue;
                bool forward;
                var edge = new UndirectedEdge(start, end, out forward);
                EdgeTraversal traversal;
                if (!traversals.TryGetValue(edge, out traversal))
                {
                    traversal = new EdgeTraversal();
                    traversals.Add(edge, traversal);
                }
                if (forward) traversal.Forward++;
                else traversal.Reverse++;
            }

            var result = new List<UndirectedEdge>();
            foreach (var item in traversals)
            {
                if (item.Value.Forward == 0 || item.Value.Reverse == 0) continue;
                result.Add(item.Key);
            }
            return result;
        }

        private static Paths64 ExtractBridgeContours(Path64 polygon, IEnumerable<UndirectedEdge> bridges)
        {
            var bridgeSet = new HashSet<UndirectedEdge>(bridges);
            var boundaryEdges = new Dictionary<Point64, Point64>();
            for (int index = 0; index < polygon.Count; index++)
            {
                Point64 start = polygon[index];
                Point64 end = polygon[(index + 1) % polygon.Count];
                if (start == end || bridgeSet.Contains(new UndirectedEdge(start, end, out _))) continue;
                if (boundaryEdges.ContainsKey(start)) return null;
                boundaryEdges.Add(start, end);
            }

            var contours = new Paths64();
            while (boundaryEdges.Count > 0)
            {
                Point64 start = boundaryEdges.Keys.First();
                Point64 current = start;
                var contour = new Path64();
                do
                {
                    Point64 next;
                    if (!boundaryEdges.TryGetValue(current, out next)) return null;
                    boundaryEdges.Remove(current);
                    contour.Add(current);
                    current = next;
                } while (current != start && contour.Count <= polygon.Count);
                if (current != start || contour.Count < 3 || Math.Abs(Clipper.Area(contour)) == 0) return null;
                contours.Add(contour);
            }
            return contours;
        }

        private static int FindContainingContour(IReadOnlyList<Path64> contours, Point64 point)
        {
            for (int index = 0; index < contours.Count; index++)
            {
                Path64 contour = contours[index];
                for (int edge = 0; edge < contour.Count; edge++)
                {
                    if (PointOnSegment(point, contour[edge], contour[(edge + 1) % contour.Count])) return index;
                }
            }
            return -1;
        }

        public static bool IsPointInside(Path64 contour, Point64 point)
        {
            bool inside = false;
            for (int current = 0, previous = contour.Count - 1; current < contour.Count; previous = current++)
            {
                Point64 first = contour[current];
                Point64 second = contour[previous];
                if ((first.Y > point.Y) != (second.Y > point.Y) && point.X < (second.X - first.X) * (point.Y - first.Y) / (double)(second.Y - first.Y) + first.X) inside = !inside;
            }
            return inside;
        }

        private static int CompareCoordinates(Point64 first, Point64 second)
        {
            int comparison = first.X.CompareTo(second.X);
            return comparison != 0 ? comparison : first.Y.CompareTo(second.Y);
        }

        private struct UndirectedEdge : IEquatable<UndirectedEdge>
        {
            public UndirectedEdge(Point64 start, Point64 end, out bool forward)
            {
                forward = CompareCoordinates(start, end) <= 0;
                First = forward ? start : end;
                Second = forward ? end : start;
            }

            public Point64 First { get; private set; }
            public Point64 Second { get; private set; }

            public bool Equals(UndirectedEdge other) { return First == other.First && Second == other.Second; }
            public override bool Equals(object value) { return value is UndirectedEdge && Equals((UndirectedEdge)value); }
            public override int GetHashCode() { return (First.GetHashCode() * 397) ^ Second.GetHashCode(); }
        }

        private sealed class EdgeTraversal
        {
            public int Forward;
            public int Reverse;
        }

        private static int FindRoot(int[] parents, int index)
        {
            while (parents[index] != index)
            {
                parents[index] = parents[parents[index]];
                index = parents[index];
            }
            return index;
        }

        private static Path64 BuildBridgeNetwork(Path64[] contours, IEnumerable<BridgeSelection> edges)
        {
            BridgeSelection[] links = edges.ToArray();
            var result = new Path64(contours.Sum(path => path.Count) + links.Length * 2);
            AppendNetworkContour(0, contours[0][0], -1, contours, links, result);
            return result;
        }

        private static void AppendNetworkContour(int contourIndex, Point64 startPoint, int parentIndex, Path64[] contours, BridgeSelection[] edges, Path64 result)
        {
            Path64 path = RotateTo(contours[contourIndex], startPoint);
            if (result.Count == 0 || result[result.Count - 1] != startPoint) result.Add(startPoint);
            for (int offset = 0; offset < path.Count; offset++)
            {
                Point64 point = path[offset];
                if (offset > 0) result.Add(point);
                foreach (BridgeSelection edge in edges.Where(link =>
                    (link.FirstContourIndex == contourIndex && link.SecondContourIndex != parentIndex && link.FirstPoint == point) ||
                    (link.SecondContourIndex == contourIndex && link.FirstContourIndex != parentIndex && link.SecondPoint == point)))
                {
                    int childIndex = edge.FirstContourIndex == contourIndex ? edge.SecondContourIndex : edge.FirstContourIndex;
                    Point64 childPoint = edge.FirstContourIndex == contourIndex ? edge.SecondPoint : edge.FirstPoint;
                    result.Add(childPoint);
                    AppendNetworkContour(childIndex, childPoint, contourIndex, contours, edges, result);
                    result.Add(point);
                }
            }
            if (contourIndex != 0) result.Add(startPoint);
        }

        private static BridgeSelection FindShortestLegalBridge(Path64 firstContour, Path64 secondContour, Path64 outer, Path64[] holes, IEnumerable<Path64> blockers, int firstIndex, int secondIndex)
        {
            BridgeSelection best = null;
            Path64[] blockerPaths = blockers.ToArray();
            var pairs = new List<EdgePair>();
            for (int outerIndex = 0; outerIndex < firstContour.Count; outerIndex++)
            {
                Point64 outerStart = firstContour[outerIndex];
                Point64 outerEnd = firstContour[(outerIndex + 1) % firstContour.Count];
                for (int holeIndex = 0; holeIndex < secondContour.Count; holeIndex++)
                {
                    Point64 holeStart = secondContour[holeIndex];
                    Point64 holeEnd = secondContour[(holeIndex + 1) % secondContour.Count];
                    ClosestSegmentParameters(outerStart, outerEnd, holeStart, holeEnd, out double outerParameter, out double holeParameter);
                    pairs.Add(new EdgePair(outerStart, outerEnd, holeStart, holeEnd, outerParameter, holeParameter, outerIndex, holeIndex));
                }
            }
            pairs.Sort();
            foreach (EdgePair pair in pairs)
            {
                if (best != null && pair.LowerBound > SquaredDistance(best.FirstPoint, best.SecondPoint)) break;
                List<BridgeSelection> candidates = BridgeCandidates(pair, blockerPaths, firstIndex, secondIndex);
                candidates.Sort();
                foreach (BridgeSelection candidate in candidates)
                {
                    if (best != null && SquaredDistance(candidate.FirstPoint, candidate.SecondPoint) > SquaredDistance(best.FirstPoint, best.SecondPoint)) break;
                    if (!IsLegalBridge(candidate.FirstPoint, candidate.SecondPoint, firstContour, secondContour, outer, holes, blockerPaths)) continue;
                    if (best == null || candidate.CompareTo(best) < 0) best = candidate;
                }
            }
            return best;
        }

        private static List<BridgeSelection> BridgeCandidates(EdgePair pair, IEnumerable<Path64> blockers, int firstIndex, int secondIndex)
        {
            var outerPoints = new HashSet<Point64>(NearbyLatticePoints(pair.OuterStart, pair.OuterEnd, pair.OuterParameter));
            var holePoints = new HashSet<Point64>(NearbyLatticePoints(pair.HoleStart, pair.HoleEnd, pair.HoleParameter));
            Point64[] blockerPoints = blockers.SelectMany(path => path).ToArray();
            foreach (Point64 point in blockerPoints)
            {
                AddNearby(outerPoints, pair.OuterStart, pair.OuterEnd, ProjectParameter(point, pair.OuterStart, pair.OuterEnd));
                AddNearby(holePoints, pair.HoleStart, pair.HoleEnd, ProjectParameter(point, pair.HoleStart, pair.HoleEnd));
            }
            foreach (Point64 outerPoint in outerPoints.ToArray()) AddNearby(holePoints, pair.HoleStart, pair.HoleEnd, ProjectParameter(outerPoint, pair.HoleStart, pair.HoleEnd));
            foreach (Point64 holePoint in holePoints.ToArray()) AddNearby(outerPoints, pair.OuterStart, pair.OuterEnd, ProjectParameter(holePoint, pair.OuterStart, pair.OuterEnd));
            foreach (Point64 outerPoint in outerPoints.ToArray())
            foreach (Point64 blockerPoint in blockerPoints)
            {
                double parameter;
                if (RayIntersectsSegment(outerPoint, blockerPoint, pair.HoleStart, pair.HoleEnd, out parameter)) AddNearby(holePoints, pair.HoleStart, pair.HoleEnd, parameter);
            }
            foreach (Point64 holePoint in holePoints.ToArray())
            foreach (Point64 blockerPoint in blockerPoints)
            {
                double parameter;
                if (RayIntersectsSegment(holePoint, blockerPoint, pair.OuterStart, pair.OuterEnd, out parameter)) AddNearby(outerPoints, pair.OuterStart, pair.OuterEnd, parameter);
            }
            foreach (Path64 blocker in blockers)
            {
                for (int index = 0; index < blocker.Count; index++)
                {
                    Point64 start = blocker[index]; Point64 end = blocker[(index + 1) % blocker.Count];
                    long x = end.X - start.X; long y = end.Y - start.Y;
                    long divisor = GreatestCommonDivisor(Math.Abs(x), Math.Abs(y));
                    if (divisor == 0) continue;
                    foreach (long direction in new[] { -1L, 1L })
                    {
                        var origin = new Point64(start.X + direction * -y / divisor, start.Y + direction * x / divisor);
                        var through = new Point64(origin.X + x, origin.Y + y);
                        double parameter;
                        if (LineIntersectsSegment(origin, through, pair.OuterStart, pair.OuterEnd, out parameter)) AddNearby(outerPoints, pair.OuterStart, pair.OuterEnd, parameter);
                        if (LineIntersectsSegment(origin, through, pair.HoleStart, pair.HoleEnd, out parameter)) AddNearby(holePoints, pair.HoleStart, pair.HoleEnd, parameter);
                    }
                }
            }
            var result = new List<BridgeSelection>(outerPoints.Count * holePoints.Count);
            foreach (Point64 outerPoint in outerPoints)
            foreach (Point64 holePoint in holePoints) result.Add(new BridgeSelection(outerPoint, holePoint, firstIndex, secondIndex));
            return result;
        }

        private static void AddNearby(ISet<Point64> points, Point64 start, Point64 end, double parameter)
        {
            foreach (Point64 point in NearbyLatticePoints(start, end, parameter)) points.Add(point);
        }

        private static bool RayIntersectsSegment(Point64 origin, Point64 through, Point64 start, Point64 end, out double segmentParameter)
        {
            double directionX = through.X - origin.X; double directionY = through.Y - origin.Y;
            double edgeX = end.X - start.X; double edgeY = end.Y - start.Y;
            double denominator = directionX * edgeY - directionY * edgeX;
            if (Math.Abs(denominator) < double.Epsilon) { segmentParameter = 0d; return false; }
            double offsetX = start.X - origin.X; double offsetY = start.Y - origin.Y;
            double rayParameter = (offsetX * edgeY - offsetY * edgeX) / denominator;
            segmentParameter = (offsetX * directionY - offsetY * directionX) / denominator;
            return rayParameter >= 1d && segmentParameter >= 0d && segmentParameter <= 1d;
        }

        private static bool LineIntersectsSegment(Point64 origin, Point64 through, Point64 start, Point64 end, out double segmentParameter)
        {
            double directionX = through.X - origin.X; double directionY = through.Y - origin.Y;
            double edgeX = end.X - start.X; double edgeY = end.Y - start.Y;
            double denominator = directionX * edgeY - directionY * edgeX;
            if (Math.Abs(denominator) < double.Epsilon) { segmentParameter = 0d; return false; }
            double offsetX = start.X - origin.X; double offsetY = start.Y - origin.Y;
            segmentParameter = (offsetX * directionY - offsetY * directionX) / denominator;
            return segmentParameter >= 0d && segmentParameter <= 1d;
        }

        private static IEnumerable<Point64> NearbyLatticePoints(Point64 start, Point64 end, double parameter)
        {
            long deltaX = end.X - start.X;
            long deltaY = end.Y - start.Y;
            long count = GreatestCommonDivisor(Math.Abs(deltaX), Math.Abs(deltaY));
            if (count == 0) return new[] { start };
            long nearest = Math.Max(0, Math.Min(count, (long)Math.Round(parameter * count, MidpointRounding.AwayFromZero)));
            long[] positions = new[] { 0L, count, nearest - 1, nearest, nearest + 1 }.Where(value => value >= 0 && value <= count).Distinct().OrderBy(value => value).ToArray();
            return positions.Select(value => new Point64(start.X + deltaX / count * value, start.Y + deltaY / count * value));
        }

        private static void ClosestSegmentParameters(Point64 firstStart, Point64 firstEnd, Point64 secondStart, Point64 secondEnd, out double firstParameter, out double secondParameter)
        {
            var values = new[]
            {
                new SegmentParameters(0d, ProjectParameter(firstStart, secondStart, secondEnd)),
                new SegmentParameters(1d, ProjectParameter(firstEnd, secondStart, secondEnd)),
                new SegmentParameters(ProjectParameter(secondStart, firstStart, firstEnd), 0d),
                new SegmentParameters(ProjectParameter(secondEnd, firstStart, firstEnd), 1d)
            };
            SegmentParameters closest = values.OrderBy(value => DistanceSquared(Interpolate(firstStart, firstEnd, value.First), Interpolate(secondStart, secondEnd, value.Second))).ThenBy(value => value.First).ThenBy(value => value.Second).First();
            firstParameter = closest.First;
            secondParameter = closest.Second;
        }

        private static double ProjectParameter(Point64 point, Point64 start, Point64 end)
        {
            double x = end.X - start.X; double y = end.Y - start.Y;
            double length = x * x + y * y;
            return length == 0d ? 0d : Clamp(((point.X - start.X) * x + (point.Y - start.Y) * y) / length, 0d, 1d);
        }

        private static Point64 Interpolate(Point64 start, Point64 end, double parameter)
        {
            return new Point64((long)Math.Round(start.X + (end.X - start.X) * parameter, MidpointRounding.AwayFromZero), (long)Math.Round(start.Y + (end.Y - start.Y) * parameter, MidpointRounding.AwayFromZero));
        }

        private static double DistanceSquared(Point64 first, Point64 second)
        {
            double x = first.X - second.X; double y = first.Y - second.Y;
            return x * x + y * y;
        }

        private static double DistanceSquared(Point64 firstStart, Point64 firstEnd, double firstParameter, Point64 secondStart, Point64 secondEnd, double secondParameter)
        {
            double firstX = firstStart.X + (firstEnd.X - firstStart.X) * firstParameter;
            double firstY = firstStart.Y + (firstEnd.Y - firstStart.Y) * firstParameter;
            double secondX = secondStart.X + (secondEnd.X - secondStart.X) * secondParameter;
            double secondY = secondStart.Y + (secondEnd.Y - secondStart.Y) * secondParameter;
            double x = firstX - secondX; double y = firstY - secondY;
            return x * x + y * y;
        }

        private static bool IsLegalBridge(Point64 firstPoint, Point64 secondPoint, Path64 firstContour, Path64 secondContour, Path64 outer, IEnumerable<Path64> holes, IEnumerable<Path64> blockers)
        {
            if (firstPoint == secondPoint) return false;
            if (!PointOnPath(firstPoint, firstContour) || !PointOnPath(secondPoint, secondContour)) return false;
            if (firstContour == outer)
            {
                if (!BridgeOnlyTouchesAt(firstPoint, secondPoint, firstPoint, outer)) return false;
            }
            else if (SegmentIntersectsPath(firstPoint, secondPoint, outer)) return false;
            if (!BridgeOnlyTouchesAt(firstPoint, secondPoint, firstPoint, firstContour)) return false;
            if (!BridgeOnlyTouchesAt(firstPoint, secondPoint, secondPoint, secondContour)) return false;
            foreach (Path64 blocker in blockers)
            {
                if (SegmentIntersectsPath(firstPoint, secondPoint, blocker)) return false;
            }
            double midpointX = (firstPoint.X + (double)secondPoint.X) / 2d;
            double midpointY = (firstPoint.Y + (double)secondPoint.Y) / 2d;
            if (!PointInside(midpointX, midpointY, outer) || holes.Any(hole => PointInside(midpointX, midpointY, hole))) return false;
            return true;
        }

        private static bool PointOnPath(Point64 point, Path64 path)
        {
            for (int index = 0; index < path.Count; index++) if (PointOnSegment(point, path[index], path[(index + 1) % path.Count])) return true;
            return false;
        }

        private static bool BridgeOnlyTouchesAt(Point64 first, Point64 second, Point64 allowed, Path64 path)
        {
            bool touched = false;
            for (int index = 0; index < path.Count; index++)
            {
                Point64 start = path[index];
                Point64 end = path[(index + 1) % path.Count];
                if (!SegmentsIntersect(first, second, start, end)) continue;
                if (!PointOnSegment(allowed, start, end) || PointOnSegment(second == allowed ? first : second, start, end)) return false;
                if (IsCollinear(first, second, start, end) && ((start != allowed && PointOnSegment(start, first, second)) || (end != allowed && PointOnSegment(end, first, second)))) return false;
                touched = true;
            }
            return touched;
        }

        private static bool SegmentIntersectsPath(Point64 first, Point64 second, Path64 path)
        {
            for (int index = 0; index < path.Count; index++) if (SegmentsIntersect(first, second, path[index], path[(index + 1) % path.Count])) return true;
            return false;
        }

        private static bool PointInside(Point64 point, Path64 path)
        {
            for (int index = 0; index < path.Count; index++) if (PointOnSegment(point, path[index], path[(index + 1) % path.Count])) return true;
            bool inside = false;
            for (int current = 0, previous = path.Count - 1; current < path.Count; previous = current++)
            {
                Point64 a = path[current]; Point64 b = path[previous];
                if ((a.Y > point.Y) != (b.Y > point.Y) && point.X < (b.X - a.X) * (point.Y - a.Y) / (double)(b.Y - a.Y) + a.X) inside = !inside;
            }
            return inside;
        }

        private static bool PointInside(double x, double y, Path64 path)
        {
            bool inside = false;
            for (int current = 0, previous = path.Count - 1; current < path.Count; previous = current++)
            {
                Point64 a = path[current];
                Point64 b = path[previous];
                double cross = (x - a.X) * (b.Y - a.Y) - (y - a.Y) * (b.X - a.X);
                if (Math.Abs(cross) < 1e-9 && x >= Math.Min(a.X, b.X) && x <= Math.Max(a.X, b.X) && y >= Math.Min(a.Y, b.Y) && y <= Math.Max(a.Y, b.Y)) return false;
                if ((a.Y > y) != (b.Y > y) && x < (b.X - a.X) * (y - a.Y) / (double)(b.Y - a.Y) + a.X) inside = !inside;
            }
            return inside;
        }

        private static bool SegmentsIntersect(Point64 firstStart, Point64 firstEnd, Point64 secondStart, Point64 secondEnd)
        {
            long first = Cross(firstStart, firstEnd, secondStart);
            long second = Cross(firstStart, firstEnd, secondEnd);
            long third = Cross(secondStart, secondEnd, firstStart);
            long fourth = Cross(secondStart, secondEnd, firstEnd);
            if ((first > 0 && second < 0 || first < 0 && second > 0) && (third > 0 && fourth < 0 || third < 0 && fourth > 0)) return true;
            return first == 0 && PointOnSegment(secondStart, firstStart, firstEnd) || second == 0 && PointOnSegment(secondEnd, firstStart, firstEnd) || third == 0 && PointOnSegment(firstStart, secondStart, secondEnd) || fourth == 0 && PointOnSegment(firstEnd, secondStart, secondEnd);
        }

        private static bool PointOnSegment(Point64 point, Point64 start, Point64 end)
        {
            return Cross(start, end, point) == 0 && point.X >= Math.Min(start.X, end.X) && point.X <= Math.Max(start.X, end.X) && point.Y >= Math.Min(start.Y, end.Y) && point.Y <= Math.Max(start.Y, end.Y);
        }

        private static bool IsCollinear(Point64 first, Point64 second, Point64 pathStart, Point64 pathEnd)
        {
            return Cross(first, second, pathStart) == 0 && Cross(first, second, pathEnd) == 0;
        }

        private static long Cross(Point64 first, Point64 second, Point64 third)
        {
            return (second.X - first.X) * (third.Y - first.Y) - (second.Y - first.Y) * (third.X - first.X);
        }

        private static Path64 InsertBridgePoints(Path64 path, IEnumerable<Point64> points)
        {
            Point64[] values = points.Distinct().ToArray();
            var result = new Path64(path.Count + values.Length);
            for (int index = 0; index < path.Count; index++)
            {
                Point64 start = path[index];
                Point64 end = path[(index + 1) % path.Count];
                result.Add(start);
                foreach (Point64 point in values.Where(value => value != start && value != end && PointOnSegment(value, start, end)).OrderBy(value => SquaredDistance(start, value))) result.Add(point);
            }
            return result;
        }

        private static Path64 RotateTo(Path64 path, Point64 point)
        {
            int index = path.FindIndex(value => value == point);
            if (index < 0) throw new ArgumentException("The bridge endpoint must be on the contour.", nameof(point));
            var result = new Path64(path.Count);
            for (int offset = 0; offset < path.Count; offset++) result.Add(path[(index + offset) % path.Count]);
            return result;
        }

        private static long SquaredDistance(Point64 first, Point64 second)
        {
            long x = first.X - second.X; long y = first.Y - second.Y;
            return x * x + y * y;
        }

        private static long GreatestCommonDivisor(long first, long second)
        {
            while (second != 0) { long remainder = first % second; first = second; second = remainder; }
            return first;
        }

        private static double Clamp(double value, double minimum, double maximum)
        {
            return Math.Max(minimum, Math.Min(maximum, value));
        }

        private sealed class EdgePair : IComparable<EdgePair>
        {
            public EdgePair(Point64 outerStart, Point64 outerEnd, Point64 holeStart, Point64 holeEnd, double outerParameter, double holeParameter, int outerIndex, int holeIndex)
            {
                OuterStart = outerStart; OuterEnd = outerEnd; HoleStart = holeStart; HoleEnd = holeEnd;
                OuterParameter = outerParameter; HoleParameter = holeParameter;
                OuterIndex = outerIndex; HoleIndex = holeIndex;
                LowerBound = DistanceSquared(outerStart, outerEnd, outerParameter, holeStart, holeEnd, holeParameter);
            }
            public Point64 OuterStart { get; private set; }
            public Point64 OuterEnd { get; private set; }
            public Point64 HoleStart { get; private set; }
            public Point64 HoleEnd { get; private set; }
            public double OuterParameter { get; private set; }
            public double HoleParameter { get; private set; }
            public int OuterIndex { get; private set; }
            public int HoleIndex { get; private set; }
            public double LowerBound { get; private set; }
            public int CompareTo(EdgePair other)
            {
                int comparison = LowerBound.CompareTo(other.LowerBound);
                if (comparison != 0) return comparison;
                comparison = OuterIndex.CompareTo(other.OuterIndex);
                return comparison != 0 ? comparison : HoleIndex.CompareTo(other.HoleIndex);
            }
        }

        private sealed class SegmentParameters
        {
            public SegmentParameters(double first, double second) { First = first; Second = second; }
            public double First { get; private set; }
            public double Second { get; private set; }
        }

        private sealed class BridgeSelection : IComparable<BridgeSelection>
        {
            public BridgeSelection(Point64 firstPoint, Point64 secondPoint, int firstContourIndex, int secondContourIndex, bool isExisting = false)
            {
                FirstPoint = firstPoint; SecondPoint = secondPoint; FirstContourIndex = firstContourIndex; SecondContourIndex = secondContourIndex; IsExisting = isExisting;
            }
            public Point64 FirstPoint { get; private set; }
            public Point64 SecondPoint { get; private set; }
            public int FirstContourIndex { get; private set; }
            public int SecondContourIndex { get; private set; }
            public bool IsExisting { get; private set; }
            public int CompareTo(BridgeSelection other)
            {
                long distance = SquaredDistance(FirstPoint, SecondPoint);
                long otherDistance = SquaredDistance(other.FirstPoint, other.SecondPoint);
                if (distance != otherDistance) return distance < otherDistance ? -1 : 1;
                if (IsExisting != other.IsExisting) return IsExisting ? -1 : 1;
                Point64 firstLow = ComparePoint(FirstPoint, SecondPoint) <= 0 ? FirstPoint : SecondPoint;
                Point64 firstHigh = firstLow == FirstPoint ? SecondPoint : FirstPoint;
                Point64 secondLow = ComparePoint(other.FirstPoint, other.SecondPoint) <= 0 ? other.FirstPoint : other.SecondPoint;
                Point64 secondHigh = secondLow == other.FirstPoint ? other.SecondPoint : other.FirstPoint;
                int comparison = ComparePoint(firstLow, secondLow);
                if (comparison != 0) return comparison;
                comparison = ComparePoint(firstHigh, secondHigh);
                if (comparison != 0) return comparison;
                comparison = FirstContourIndex.CompareTo(other.FirstContourIndex);
                return comparison != 0 ? comparison : SecondContourIndex.CompareTo(other.SecondContourIndex);
            }

            private static int ComparePoint(Point64 first, Point64 second)
            {
                int comparison = first.X.CompareTo(second.X);
                return comparison != 0 ? comparison : first.Y.CompareTo(second.Y);
            }
        }

        public sealed class BridgeConnection
        {
            public BridgeConnection(int firstContourIndex, Point64 firstPoint, int secondContourIndex, Point64 secondPoint)
            {
                if (firstContourIndex <= secondContourIndex)
                {
                    FirstContourIndex = firstContourIndex;
                    FirstPoint = firstPoint;
                    SecondContourIndex = secondContourIndex;
                    SecondPoint = secondPoint;
                }
                else
                {
                    FirstContourIndex = secondContourIndex;
                    FirstPoint = secondPoint;
                    SecondContourIndex = firstContourIndex;
                    SecondPoint = firstPoint;
                }
            }

            public int FirstContourIndex { get; private set; }
            public Point64 FirstPoint { get; private set; }
            public int SecondContourIndex { get; private set; }
            public Point64 SecondPoint { get; private set; }
        }

        public sealed class BridgeNetworkException : InvalidOperationException
        {
            public BridgeNetworkException() : base("A legal hole bridge network could not be found.") { }
        }

        public static Point64 RoundAndClamp(double x, double y, long width, long height)
        {
            if (width < 1 || height < 1)
            {
                throw new ArgumentOutOfRangeException(width < 1 ? nameof(width) : nameof(height));
            }

            return new Point64(
                Math.Max(0, Math.Min(width - 1, (long)Math.Round(x, MidpointRounding.AwayFromZero))),
                Math.Max(0, Math.Min(height - 1, (long)Math.Round(y, MidpointRounding.AwayFromZero))));
        }

        public static double Area(Paths64 paths)
        {
            double area = 0;
            foreach (Path64 path in paths)
            {
                area += Clipper.Area(path);
            }

            return Math.Abs(area);
        }

        private static Paths64 Combine(Paths64 first, Paths64 second)
        {
            var result = new Paths64(first.Count + second.Count);
            result.AddRange(first);
            result.AddRange(second);
            return result;
        }
    }
}

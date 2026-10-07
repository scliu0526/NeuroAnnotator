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

            BridgeSelection[] selections = holePaths.Select((holePath, index) => FindShortestLegalBridge(outerPath, holePath, holePaths.Where((_, otherIndex) => otherIndex != index))).ToArray();
            Array.Sort(selections);
            outerPath = InsertBridgePoints(outerPath, selections.Select(selection => selection.OuterPoint));
            foreach (BridgeSelection selection in selections) selection.InsertHolePoint();
            var result = new Path64(outerPath.Count + holePaths.Sum(path => path.Count + 3));
            for (int index = 0; index < outerPath.Count; index++)
            {
                Point64 outerPoint = outerPath[index];
                result.Add(outerPoint);
                foreach (BridgeSelection selection in selections.Where(value => value.OuterPoint == outerPoint))
                {
                    Path64 holePath = RotateTo(selection.Hole, selection.HolePoint);
                    result.Add(holePath[0]);
                    for (int holeIndex = 1; holeIndex < holePath.Count; holeIndex++) result.Add(holePath[holeIndex]);
                    result.Add(holePath[0]);
                    result.Add(outerPoint);
                }
            }
            return result;
        }

        private static BridgeSelection FindShortestLegalBridge(Path64 outer, Path64 hole, IEnumerable<Path64> blockers)
        {
            BridgeSelection best = null;
            Path64[] blockerPaths = blockers.ToArray();
            var pairs = new List<EdgePair>();
            for (int outerIndex = 0; outerIndex < outer.Count; outerIndex++)
            {
                Point64 outerStart = outer[outerIndex];
                Point64 outerEnd = outer[(outerIndex + 1) % outer.Count];
                for (int holeIndex = 0; holeIndex < hole.Count; holeIndex++)
                {
                    Point64 holeStart = hole[holeIndex];
                    Point64 holeEnd = hole[(holeIndex + 1) % hole.Count];
                    ClosestSegmentParameters(outerStart, outerEnd, holeStart, holeEnd, out double outerParameter, out double holeParameter);
                    pairs.Add(new EdgePair(outerStart, outerEnd, holeStart, holeEnd, outerParameter, holeParameter, outerIndex, holeIndex));
                }
            }
            pairs.Sort();
            foreach (EdgePair pair in pairs)
            {
                if (best != null && pair.LowerBound > SquaredDistance(best.OuterPoint, best.HolePoint)) break;
                List<BridgeSelection> candidates = BridgeCandidates(pair, blockerPaths, hole);
                candidates.Sort();
                foreach (BridgeSelection candidate in candidates)
                {
                    if (best != null && SquaredDistance(candidate.OuterPoint, candidate.HolePoint) > SquaredDistance(best.OuterPoint, best.HolePoint)) break;
                    if (!IsLegalBridge(candidate.OuterPoint, candidate.HolePoint, outer, hole, blockerPaths)) continue;
                    if (best == null || candidate.CompareTo(best) < 0) best = candidate;
                }
            }
            if (best == null) throw new InvalidOperationException("A legal hole bridge could not be found.");
            return best;
        }

        private static List<BridgeSelection> BridgeCandidates(EdgePair pair, IEnumerable<Path64> blockers, Path64 hole)
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
            foreach (Point64 holePoint in holePoints) result.Add(new BridgeSelection(outerPoint, holePoint, hole));
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

        private static bool IsLegalBridge(Point64 outerPoint, Point64 holePoint, Path64 outer, Path64 hole, IEnumerable<Path64> blockers)
        {
            if (outerPoint == holePoint) return false;
            if (!PointInside(outerPoint, outer) || !PointInside(holePoint, hole)) return false;
            if (!BridgeOnlyTouchesAt(outerPoint, holePoint, outerPoint, outer)) return false;
            if (!BridgeOnlyTouchesAt(outerPoint, holePoint, holePoint, hole)) return false;
            foreach (Path64 blocker in blockers)
            {
                if (SegmentIntersectsPath(outerPoint, holePoint, blocker)) return false;
                if (PointInside(Midpoint(outerPoint, holePoint), blocker)) return false;
            }
            if (!PointInside(Midpoint(outerPoint, holePoint), outer) || PointInside(Midpoint(outerPoint, holePoint), hole)) return false;
            return true;
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

        private static Point64 Midpoint(Point64 first, Point64 second)
        {
            return new Point64((first.X + second.X) / 2, (first.Y + second.Y) / 2);
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
            public BridgeSelection(Point64 outerPoint, Point64 holePoint, Path64 hole)
            {
                OuterPoint = outerPoint; HolePoint = holePoint; Hole = hole;
            }
            public Point64 OuterPoint { get; private set; }
            public Point64 HolePoint { get; private set; }
            public Path64 Hole { get; private set; }
            public void InsertHolePoint() { Hole = InsertBridgePoints(Hole, new[] { HolePoint }); }
            public int CompareTo(BridgeSelection other)
            {
                long distance = SquaredDistance(OuterPoint, HolePoint);
                long otherDistance = SquaredDistance(other.OuterPoint, other.HolePoint);
                if (distance != otherDistance) return distance < otherDistance ? -1 : 1;
                int comparison = OuterPoint.X.CompareTo(other.OuterPoint.X);
                if (comparison != 0) return comparison;
                comparison = OuterPoint.Y.CompareTo(other.OuterPoint.Y);
                if (comparison != 0) return comparison;
                comparison = HolePoint.X.CompareTo(other.HolePoint.X);
                return comparison != 0 ? comparison : HolePoint.Y.CompareTo(other.HolePoint.Y);
            }
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

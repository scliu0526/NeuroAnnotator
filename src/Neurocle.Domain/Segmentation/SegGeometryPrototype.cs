using System;
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
            if (outer.Count < 3 || hole.Count < 3)
            {
                throw new ArgumentException("Both contours must be closed regions.");
            }

            var holePath = new Path64(hole);
            if (Math.Sign(Clipper.Area(outer)) == Math.Sign(Clipper.Area(holePath)))
            {
                holePath.Reverse();
            }

            var result = new Path64(outer.Count + holePath.Count + 4);
            result.AddRange(outer);
            result.Add(outer[0]);
            result.Add(holePath[0]);
            result.AddRange(holePath);
            result.Add(holePath[0]);
            result.Add(outer[0]);
            return result;
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

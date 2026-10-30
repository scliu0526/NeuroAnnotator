using System;
using System.Collections.Generic;
using System.Linq;
using Clipper2Lib;

namespace Neurocle.Domain.Segmentation
{
    // 以 Clipper2 整數路徑執行 SEG 幾何運算與空洞橋接網路建立。
    public static class SegGeometryPrototype
    {
        /// <summary>聯集兩組路徑；只有重疊面積大於零時才合併互相接觸的區域。</summary>
        /// <param name="subject">主要區域路徑。</param>
        /// <param name="clip">要聯集的另一組路徑。</param>
        /// <returns>依 Non-Zero 規則正規化後的聯集路徑。</returns>
        public static Paths64 Union(Paths64 subject, Paths64 clip)
        {
            // Clipper 會合併僅接觸的輪廓；產品規則只在有正面積重疊時合併。
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

        /// <summary>從主要路徑中扣除橡皮擦路徑所覆蓋的區域。</summary>
        /// <param name="subject">待保留的主要路徑。</param>
        /// <param name="eraser">要扣除的橡皮擦路徑。</param>
        /// <returns>依 Non-Zero 規則計算的差集路徑。</returns>
        public static Paths64 Difference(Paths64 subject, Paths64 eraser)
        {
            return Clipper.Difference(subject, eraser, FillRule.NonZero);
        }

        /// <summary>將中心線膨脹為具有圓角接合與端點的封閉筆畫區域。</summary>
        /// <param name="centerLine">筆畫中心線。</param>
        /// <param name="width">正整數筆畫寬度。</param>
        /// <returns>代表筆畫覆蓋範圍的多邊形路徑。</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="width"/> 小於 1。</exception>
        public static Paths64 Stroke(Path64 centerLine, long width)
        {
            if (width < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(width));
            }

            return Clipper.InflatePaths(new Paths64 { centerLine }, width / 2.0, JoinType.Round, EndType.Round);
        }

        /// <summary>依左、上、右、下座標建立矩形路徑。</summary>
        /// <param name="left">左側 X 座標。</param>
        /// <param name="top">上側 Y 座標。</param>
        /// <param name="right">右側 X 座標。</param>
        /// <param name="bottom">下側 Y 座標。</param>
        /// <returns>由四個角點依序組成的路徑。</returns>
        public static Path64 Rectangle(long left, long top, long right, long bottom)
        {
            return new Path64
            {
                new Point64(left, top), new Point64(right, top), new Point64(right, bottom), new Point64(left, bottom)
            };
        }

        /// <summary>以指定中心、半徑及線段數近似建立橢圓路徑。</summary>
        /// <param name="centerX">中心 X 座標。</param>
        /// <param name="centerY">中心 Y 座標。</param>
        /// <param name="radiusX">正整數水平半徑。</param>
        /// <param name="radiusY">正整數垂直半徑。</param>
        /// <param name="segments">至少為 8 的近似線段數。</param>
        /// <returns>以整數座標近似的橢圓路徑。</returns>
        /// <exception cref="ArgumentOutOfRangeException">半徑小於 1 或線段數少於 8。</exception>
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

        /// <summary>繞指定中心旋轉路徑，並將結果四捨五入至整數座標。</summary>
        /// <param name="path">待旋轉路徑。</param>
        /// <param name="degrees">以度為單位的旋轉角度。</param>
        /// <param name="centerX">旋轉中心 X 座標。</param>
        /// <param name="centerY">旋轉中心 Y 座標。</param>
        /// <returns>旋轉並整數化後的新路徑。</returns>
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

        /// <summary>旋轉路徑；若任一點超出圖片範圍則回傳原路徑複本。</summary>
        /// <param name="path">待旋轉路徑。</param>
        /// <param name="degrees">以度為單位的旋轉角度。</param>
        /// <param name="centerX">旋轉中心 X 座標。</param>
        /// <param name="centerY">旋轉中心 Y 座標。</param>
        /// <param name="width">圖片寬度。</param>
        /// <param name="height">圖片高度。</param>
        /// <returns>完全位於圖片內的旋轉路徑，或越界時的原路徑複本。</returns>
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

        /// <summary>將單一空洞以最短合法橋接連至外圈。</summary>
        /// <param name="outer">外圈輪廓。</param>
        /// <param name="hole">空洞輪廓。</param>
        /// <returns>包含外圈、空洞及重合橋接往返段的路徑。</returns>
        public static Path64 BridgeHole(Path64 outer, Path64 hole)
        {
            return BridgeHoles(outer, new[] { hole });
        }

        /// <summary>建立外圈與全部空洞的最短合法連通橋接網路。</summary>
        /// <param name="outer">外圈輪廓。</param>
        /// <param name="holes">空洞輪廓集合。</param>
        /// <returns>表示所有空洞均連通外圈且不含多餘循環的路徑。</returns>
        public static Path64 BridgeHoles(Path64 outer, IEnumerable<Path64> holes)
        {
            return BridgeHoles(outer, holes, null);
        }

        /// <summary>建立最短合法橋接網路，等距時優先保留仍合法的既有橋接。</summary>
        /// <param name="outer">外圈輪廓。</param>
        /// <param name="holes">空洞輪廓集合。</param>
        /// <param name="existingConnections">可優先保留的既有橋接；可為 null。</param>
        /// <returns>連通外圈與所有空洞的整數路徑。</returns>
        /// <exception cref="ArgumentException">外圈或空洞輪廓無效，或既有橋接索引不合法。</exception>
        /// <exception cref="BridgeNetworkException">找不到連通全部輪廓的合法橋接網路。</exception>
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
                // 空洞方向需與外圈相反，才能符合 Non-Zero 填色語意。
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
                    // 只把仍符合目前幾何限制的舊端點納入候選，供等距排序優先保留。
                    if (IsLegalBridge(connection.FirstPoint, connection.SecondPoint, contours[first], contours[second], outerPath, holePaths, blockers))
                    {
                        candidates.Add(new BridgeSelection(connection.FirstPoint, connection.SecondPoint, first, second, true));
                    }
                }
            }
            candidates.Sort();
            // 依最短合法候選逐一合併不相連集合，形成不含循環的最小生成樹。
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

        /// <summary>重新分析包含往返橋接段的路徑，並依目前最短網路規則重建橋接。</summary>
        /// <param name="polygon">含外圈、空洞及既有橋接的單一路徑。</param>
        /// <returns>保留輪廓形狀並重新建立橋接後的路徑。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="polygon"/> 為 null。</exception>
        /// <exception cref="BridgeNetworkException">無法從路徑還原有效輪廓或橋接網路。</exception>
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

        /// <summary>從既有路徑中解析每條橋接的兩端輪廓與實際端點。</summary>
        /// <param name="polygon">含橋接往返段的路徑。</param>
        /// <param name="outer">已解析的外圈輪廓。</param>
        /// <param name="holes">已解析的空洞輪廓。</param>
        /// <returns>依輪廓索引標示的既有橋接。</returns>
        /// <exception cref="ArgumentNullException">任一輸入為 null。</exception>
        public static IReadOnlyList<BridgeConnection> FindExistingBridgeConnections(Path64 polygon, Path64 outer, IReadOnlyList<Path64> holes)
        {
            if (polygon == null) throw new ArgumentNullException(nameof(polygon));
            if (outer == null) throw new ArgumentNullException(nameof(outer));
            if (holes == null) throw new ArgumentNullException(nameof(holes));
            var contours = new[] { outer }.Concat(holes).ToArray();
            return MapBridgeSegments(FindBridgeSegments(polygon), contours);
        }

        /// <summary>將橋接端點對應至包含端點的輪廓索引。</summary>
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

        /// <summary>找出路徑中以相反方向重複出現、因此代表橋接的無向線段。</summary>
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

        /// <summary>移除橋接線段並還原各自封閉的輪廓；遇到無法形成輪廓的拓樸時回傳 null。</summary>
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

        /// <summary>尋找包含指定邊界點的第一個輪廓索引。</summary>
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

        /// <summary>以射線奇偶規則判斷整數點是否位於輪廓內部。</summary>
        /// <param name="contour">封閉輪廓。</param>
        /// <param name="point">待判斷的整數座標。</param>
        /// <returns>點位於輪廓內部時回傳 true。</returns>
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

        /// <summary>依 X 再依 Y 的字典順序比較座標。</summary>
        private static int CompareCoordinates(Point64 first, Point64 second)
        {
            int comparison = first.X.CompareTo(second.X);
            return comparison != 0 ? comparison : first.Y.CompareTo(second.Y);
        }

        // 以座標排序後的端點表示不分方向的線段。
        private struct UndirectedEdge : IEquatable<UndirectedEdge>
        {
            /// <summary>建立端點固定排序的無向邊，並回報輸入方向是否為正向。</summary>
            /// <param name="start">輸入線段起點。</param>
            /// <param name="end">輸入線段終點。</param>
            /// <param name="forward">輸入方向與端點排序方向相同時為 true。</param>
            public UndirectedEdge(Point64 start, Point64 end, out bool forward)
            {
                forward = CompareCoordinates(start, end) <= 0;
                First = forward ? start : end;
                Second = forward ? end : start;
            }

            // 取得依座標順序排列的第一個端點。
            public Point64 First { get; private set; }
            // 取得依座標順序排列的第二個端點。
            public Point64 Second { get; private set; }

            /// <summary>依兩個端點判斷無向邊是否相等。</summary>
            /// <param name="other">待比較的無向邊。</param>
            /// <returns>兩個排序後端點皆相同時回傳 true。</returns>
            public bool Equals(UndirectedEdge other) { return First == other.First && Second == other.Second; }
            /// <summary>判斷物件是否為端點相同的無向邊。</summary>
            /// <param name="value">待比較的物件。</param>
            /// <returns>物件是端點相同的無向邊時回傳 true。</returns>
            public override bool Equals(object value) { return value is UndirectedEdge && Equals((UndirectedEdge)value); }
            /// <summary>依端點計算無向邊雜湊碼。</summary>
            /// <returns>由兩端點雜湊值組成的整數。</returns>
            public override int GetHashCode() { return (First.GetHashCode() * 397) ^ Second.GetHashCode(); }
        }

        // 累計路徑邊以正向與反向出現的次數。
        private sealed class EdgeTraversal
        {
            // 記錄邊以排序方向出現的次數。
            public int Forward;
            // 記錄邊以反排序方向出現的次數。
            public int Reverse;
        }

        /// <summary>沿父節點陣列尋找集合根節點，並壓縮查找路徑。</summary>
        private static int FindRoot(int[] parents, int index)
        {
            while (parents[index] != index)
            {
                parents[index] = parents[parents[index]];
                index = parents[index];
            }
            return index;
        }

        /// <summary>依選取的無循環橋接邊遞迴輸出所有輪廓與往返線段。</summary>
        private static Path64 BuildBridgeNetwork(Path64[] contours, IEnumerable<BridgeSelection> edges)
        {
            BridgeSelection[] links = edges.ToArray();
            var result = new Path64(contours.Sum(path => path.Count) + links.Length * 2);
            AppendNetworkContour(0, contours[0][0], -1, contours, links, result);
            return result;
        }

        /// <summary>從指定輪廓端點開始輸出輪廓，並遞迴加入其子輪廓橋接。</summary>
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
                    // 子輪廓完成後沿同一橋接返回，保持單一路徑且不增加填色面積。
                    result.Add(childPoint);
                    AppendNetworkContour(childIndex, childPoint, contourIndex, contours, edges, result);
                    result.Add(point);
                }
            }
            if (contourIndex != 0) result.Add(startPoint);
        }

        /// <summary>以輪廓邊最近距離為下界搜尋兩輪廓間最短的合法整數橋接。</summary>
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
                // 最近線段距離下界已大於最佳整數橋接時，可停止搜尋後續邊對。
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

        /// <summary>為一對候選輪廓邊產生可能的整數橋接端點組合。</summary>
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

        /// <summary>加入線段指定比例附近可用的整數端點。</summary>
        private static void AddNearby(ISet<Point64> points, Point64 start, Point64 end, double parameter)
        {
            foreach (Point64 point in NearbyLatticePoints(start, end, parameter)) points.Add(point);
        }

        /// <summary>測試由原點朝指定點延伸的射線是否與線段相交，並回報線段比例。</summary>
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

        /// <summary>測試兩條有限線段的交點，並回報第二條線段上的比例。</summary>
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

        /// <summary>產生最接近線段指定投影位置的周邊整數格點。</summary>
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

        /// <summary>計算兩線段彼此最近位置在各自線段上的參數。</summary>
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

        /// <summary>將點正交投影至線段並回傳限制於線段範圍內的比例。</summary>
        private static double ProjectParameter(Point64 point, Point64 start, Point64 end)
        {
            double x = end.X - start.X; double y = end.Y - start.Y;
            double length = x * x + y * y;
            return length == 0d ? 0d : Clamp(((point.X - start.X) * x + (point.Y - start.Y) * y) / length, 0d, 1d);
        }

        /// <summary>依線段比例內插並四捨五入至整數座標。</summary>
        private static Point64 Interpolate(Point64 start, Point64 end, double parameter)
        {
            return new Point64((long)Math.Round(start.X + (end.X - start.X) * parameter, MidpointRounding.AwayFromZero), (long)Math.Round(start.Y + (end.Y - start.Y) * parameter, MidpointRounding.AwayFromZero));
        }

        /// <summary>計算兩個整數座標點之間的平方距離。</summary>
        private static double DistanceSquared(Point64 first, Point64 second)
        {
            double x = first.X - second.X; double y = first.Y - second.Y;
            return x * x + y * y;
        }

        /// <summary>計算兩線段指定比例位置之間的平方距離。</summary>
        private static double DistanceSquared(Point64 firstStart, Point64 firstEnd, double firstParameter, Point64 secondStart, Point64 secondEnd, double secondParameter)
        {
            double firstX = firstStart.X + (firstEnd.X - firstStart.X) * firstParameter;
            double firstY = firstStart.Y + (firstEnd.Y - firstStart.Y) * firstParameter;
            double secondX = secondStart.X + (secondEnd.X - secondStart.X) * secondParameter;
            double secondY = secondStart.Y + (secondEnd.Y - secondStart.Y) * secondParameter;
            double x = firstX - secondX; double y = firstY - secondY;
            return x * x + y * y;
        }

        /// <summary>驗證橋接端點位於來源輪廓，且線段不穿越外圈、空洞或阻擋輪廓。</summary>
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
                // 其他空洞是阻擋輪廓，橋接不得穿越其邊界。
                if (SegmentIntersectsPath(firstPoint, secondPoint, blocker)) return false;
            }
            double midpointX = (firstPoint.X + (double)secondPoint.X) / 2d;
            double midpointY = (firstPoint.Y + (double)secondPoint.Y) / 2d;
            // 邊界相交檢查搭配中點包含測試，排除穿出外圈或進入空洞內部的線段。
            if (!PointInside(midpointX, midpointY, outer) || holes.Any(hole => PointInside(midpointX, midpointY, hole))) return false;
            return true;
        }

        /// <summary>判斷點是否位於路徑的任一線段上。</summary>
        private static bool PointOnPath(Point64 point, Path64 path)
        {
            for (int index = 0; index < path.Count; index++) if (PointOnSegment(point, path[index], path[(index + 1) % path.Count])) return true;
            return false;
        }

        /// <summary>確認橋接與輪廓只在允許的端點相接，且沒有沿輪廓重疊。</summary>
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

        /// <summary>判斷線段是否與封閉路徑任一邊相交。</summary>
        private static bool SegmentIntersectsPath(Point64 first, Point64 second, Path64 path)
        {
            for (int index = 0; index < path.Count; index++) if (SegmentsIntersect(first, second, path[index], path[(index + 1) % path.Count])) return true;
            return false;
        }

        /// <summary>將邊界視為內部後，判斷整數座標是否位於路徑內或其上。</summary>
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

        /// <summary>以浮點射線奇偶規則判斷點是否位於路徑嚴格內部。</summary>
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

        /// <summary>判斷兩線段相交，包含共線重疊與端點接觸。</summary>
        private static bool SegmentsIntersect(Point64 firstStart, Point64 firstEnd, Point64 secondStart, Point64 secondEnd)
        {
            long first = Cross(firstStart, firstEnd, secondStart);
            long second = Cross(firstStart, firstEnd, secondEnd);
            long third = Cross(secondStart, secondEnd, firstStart);
            long fourth = Cross(secondStart, secondEnd, firstEnd);
            if ((first > 0 && second < 0 || first < 0 && second > 0) && (third > 0 && fourth < 0 || third < 0 && fourth > 0)) return true;
            return first == 0 && PointOnSegment(secondStart, firstStart, firstEnd) || second == 0 && PointOnSegment(secondEnd, firstStart, firstEnd) || third == 0 && PointOnSegment(firstStart, secondStart, secondEnd) || fourth == 0 && PointOnSegment(firstEnd, secondStart, secondEnd);
        }

        /// <summary>判斷整數點是否位於線段範圍內。</summary>
        private static bool PointOnSegment(Point64 point, Point64 start, Point64 end)
        {
            return Cross(start, end, point) == 0 && point.X >= Math.Min(start.X, end.X) && point.X <= Math.Max(start.X, end.X) && point.Y >= Math.Min(start.Y, end.Y) && point.Y <= Math.Max(start.Y, end.Y);
        }

        /// <summary>判斷兩條非退化線段是否位於同一直線。</summary>
        private static bool IsCollinear(Point64 first, Point64 second, Point64 pathStart, Point64 pathEnd)
        {
            return Cross(first, second, pathStart) == 0 && Cross(first, second, pathEnd) == 0;
        }

        /// <summary>計算兩向量的二維叉積，用於整數方向與共線判斷。</summary>
        private static long Cross(Point64 first, Point64 second, Point64 third)
        {
            return (second.X - first.X) * (third.Y - first.Y) - (second.Y - first.Y) * (third.X - first.X);
        }

        /// <summary>將位於既有輪廓邊上的橋接端點插入輪廓順序。</summary>
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

        /// <summary>以指定端點為首點循環旋轉輪廓頂點順序。</summary>
        private static Path64 RotateTo(Path64 path, Point64 point)
        {
            int index = path.FindIndex(value => value == point);
            if (index < 0) throw new ArgumentException("The bridge endpoint must be on the contour.", nameof(point));
            var result = new Path64(path.Count);
            for (int offset = 0; offset < path.Count; offset++) result.Add(path[(index + offset) % path.Count]);
            return result;
        }

        /// <summary>計算兩個整數座標點之間的 64 位元平方距離。</summary>
        private static long SquaredDistance(Point64 first, Point64 second)
        {
            long x = first.X - second.X; long y = first.Y - second.Y;
            return x * x + y * y;
        }

        /// <summary>以歐幾里得算法計算兩個整數的最大公因數。</summary>
        private static long GreatestCommonDivisor(long first, long second)
        {
            while (second != 0) { long remainder = first % second; first = second; second = remainder; }
            return first;
        }

        /// <summary>將數值限制於指定的上下界。</summary>
        private static double Clamp(double value, double minimum, double maximum)
        {
            return Math.Max(minimum, Math.Min(maximum, value));
        }

        // 保存兩條候選輪廓邊、最近投影位置及搜尋下界。
        private sealed class EdgePair : IComparable<EdgePair>
        {
            /// <summary>建立兩邊最近點搜尋資料並計算距離下界。</summary>
            public EdgePair(Point64 outerStart, Point64 outerEnd, Point64 holeStart, Point64 holeEnd, double outerParameter, double holeParameter, int outerIndex, int holeIndex)
            {
                OuterStart = outerStart; OuterEnd = outerEnd; HoleStart = holeStart; HoleEnd = holeEnd;
                OuterParameter = outerParameter; HoleParameter = holeParameter;
                OuterIndex = outerIndex; HoleIndex = holeIndex;
                LowerBound = DistanceSquared(outerStart, outerEnd, outerParameter, holeStart, holeEnd, holeParameter);
            }
            // 取得第一條候選輪廓邊的起點。
            public Point64 OuterStart { get; private set; }
            // 取得第一條候選輪廓邊的終點。
            public Point64 OuterEnd { get; private set; }
            // 取得第二條候選輪廓邊的起點。
            public Point64 HoleStart { get; private set; }
            // 取得第二條候選輪廓邊的終點。
            public Point64 HoleEnd { get; private set; }
            // 取得最近投影點在線段一上的比例。
            public double OuterParameter { get; private set; }
            // 取得最近投影點在線段二上的比例。
            public double HoleParameter { get; private set; }
            // 取得第一條邊在輪廓中的索引。
            public int OuterIndex { get; private set; }
            // 取得第二條邊在輪廓中的索引。
            public int HoleIndex { get; private set; }
            // 取得此邊對間距的平方距離下界。
            public double LowerBound { get; private set; }
            /// <summary>依距離下界及輪廓邊索引建立穩定排序。</summary>
            /// <param name="other">待比較的候選邊對。</param>
            /// <returns>依排序先後回傳負數、零或正數。</returns>
            public int CompareTo(EdgePair other)
            {
                int comparison = LowerBound.CompareTo(other.LowerBound);
                if (comparison != 0) return comparison;
                comparison = OuterIndex.CompareTo(other.OuterIndex);
                return comparison != 0 ? comparison : HoleIndex.CompareTo(other.HoleIndex);
            }
        }

        // 保存兩條線段最近點各自的投影比例。
        private sealed class SegmentParameters
        {
            /// <summary>建立兩個線段比例的結果物件。</summary>
            public SegmentParameters(double first, double second) { First = first; Second = second; }
            // 取得第一條線段的比例。
            public double First { get; private set; }
            // 取得第二條線段的比例。
            public double Second { get; private set; }
        }

        // 表示候選橋接及其端點所屬輪廓，並保留既有連線標記。
        private sealed class BridgeSelection : IComparable<BridgeSelection>
        {
            /// <summary>建立候選橋接資料。</summary>
            public BridgeSelection(Point64 firstPoint, Point64 secondPoint, int firstContourIndex, int secondContourIndex, bool isExisting = false)
            {
                FirstPoint = firstPoint; SecondPoint = secondPoint; FirstContourIndex = firstContourIndex; SecondContourIndex = secondContourIndex; IsExisting = isExisting;
            }
            // 取得橋接第一端點。
            public Point64 FirstPoint { get; private set; }
            // 取得橋接第二端點。
            public Point64 SecondPoint { get; private set; }
            // 取得第一端點所屬輪廓索引。
            public int FirstContourIndex { get; private set; }
            // 取得第二端點所屬輪廓索引。
            public int SecondContourIndex { get; private set; }
            // 指出此候選是否來自先前已存在的橋接。
            public bool IsExisting { get; private set; }
            /// <summary>依橋接長度、既有狀態與固定端點順序比較候選。</summary>
            /// <param name="other">待比較的橋接候選。</param>
            /// <returns>依排序先後回傳負數、零或正數。</returns>
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

            /// <summary>依 X 再依 Y 的順序比較兩個整數座標。</summary>
            private static int ComparePoint(Point64 first, Point64 second)
            {
                int comparison = first.X.CompareTo(second.X);
                return comparison != 0 ? comparison : first.Y.CompareTo(second.Y);
            }
        }

        // 表示兩個不同輪廓間既有橋接的輪廓索引與實際端點。
        public sealed class BridgeConnection
        {
            /// <summary>建立既有橋接；輪廓索引較小者會固定存為第一端。</summary>
            /// <param name="firstContourIndex">輸入第一端所屬輪廓索引。</param>
            /// <param name="firstPoint">輸入第一端的整數座標。</param>
            /// <param name="secondContourIndex">輸入第二端所屬輪廓索引。</param>
            /// <param name="secondPoint">輸入第二端的整數座標。</param>
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

            // 取得索引較小的第一個輪廓索引。
            public int FirstContourIndex { get; private set; }
            // 取得第一個輪廓上的橋接端點。
            public Point64 FirstPoint { get; private set; }
            // 取得索引較大的第二個輪廓索引。
            public int SecondContourIndex { get; private set; }
            // 取得第二個輪廓上的橋接端點。
            public Point64 SecondPoint { get; private set; }
        }

        // 表示無法建立涵蓋全部輪廓的合法空洞橋接網路。
        public sealed class BridgeNetworkException : InvalidOperationException
        {
            /// <summary>建立橋接網路無法連通的例外。</summary>
            public BridgeNetworkException() : base("A legal hole bridge network could not be found.") { }
        }

        /// <summary>將浮點座標四捨五入並限制在圖片的有效整數像素範圍。</summary>
        /// <param name="x">浮點 X 座標。</param>
        /// <param name="y">浮點 Y 座標。</param>
        /// <param name="width">正整數圖片寬度。</param>
        /// <param name="height">正整數圖片高度。</param>
        /// <returns>位於 0 至寬高減一範圍內的整數座標。</returns>
        /// <exception cref="ArgumentOutOfRangeException">任一圖片尺寸小於 1。</exception>
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

        /// <summary>計算多個路徑的絕對有向面積總和。</summary>
        /// <param name="paths">要計算面積的整數路徑集合。</param>
        /// <returns>路徑面積總和的絕對值。</returns>
        public static double Area(Paths64 paths)
        {
            double area = 0;
            foreach (Path64 path in paths)
            {
                area += Clipper.Area(path);
            }

            return Math.Abs(area);
        }

        /// <summary>建立包含兩組路徑的串接集合，不執行幾何正規化。</summary>
        private static Paths64 Combine(Paths64 first, Paths64 second)
        {
            var result = new Paths64(first.Count + second.Count);
            result.AddRange(first);
            result.AddRange(second);
            return result;
        }
    }
}

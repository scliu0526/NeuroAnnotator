using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Clipper2Lib;
using Neurocle.Application.Projects;

namespace Neurocle.WinForms
{
    internal sealed class ImageCanvasControl : Control
    {
        private const float HandleSize = 8f;
        private readonly CanvasViewport viewport = new CanvasViewport(Size.Empty, Size.Empty);
        private readonly SemaphoreSlim imageLoadGate = new SemaphoreSlim(1, 1);
        private Image image;
        private CancellationTokenSource imageLoad;
        private bool panning;
        private Point previousPoint;
        private PointF obdStart;
        private RectangleF? obdPreview;
        private IReadOnlyDictionary<string, RectangleF> obdPreviews = new Dictionary<string, RectangleF>();
        private IReadOnlyList<ObdAnnotation> obdAnnotations = new List<ObdAnnotation>();
        private IReadOnlyDictionary<string, AnnotationClass> obdClasses = new Dictionary<string, AnnotationClass>();
        private ObdAnnotation activeObd;
        private ObdDragKind obdDrag;
        private RectangleF obdOriginal;
        private PointF obdDragStart;
        private readonly HashSet<string> selectedObdIds = new HashSet<string>(StringComparer.Ordinal);
        private string selectedObdId;
        private PointF segStart;
        private RectangleF? segPreview;
        private readonly List<PointF> segPath = new List<PointF>();
        private bool segPathDragging;
        private PointF? segLineStart;
        private PointF segCursor;
        private IReadOnlyList<SegRegion> segRegions = new List<SegRegion>();
        private IReadOnlyDictionary<string, AnnotationClass> segClasses = new Dictionary<string, AnnotationClass>();
        private readonly HashSet<string> selectedSegIds = new HashSet<string>(StringComparer.Ordinal);
        private string selectedSegId;
        private IReadOnlyDictionary<string, SegRegion> segPreviews = new Dictionary<string, SegRegion>();
        private PointF segDragStart;
        private SegDragKind segDrag;
        private SegRegion segOriginal;
        private RectangleF segOriginalBounds;

        public bool ObdEnabled { get; set; }
        public bool SegEnabled { get; set; }
        public SegToolKind? SegTool { get; set; }
        public int BrushWidth { get; set; } = 15;
        public int LineWidth { get; set; } = 15;
        public float SegOpacity { get; set; } = 0.4f;
        public string SelectedSegId { get { return selectedSegId; } }
        public IReadOnlyCollection<string> SelectedSegIds { get { return selectedSegIds; } }
        public string SelectedObdId { get { return selectedObdId; } }
        public IReadOnlyCollection<string> SelectedObdIds { get { return selectedObdIds; } }
        public IReadOnlyList<ObdAnnotation> ObdAnnotations
        {
            get { return obdAnnotations; }
            set
            {
                obdAnnotations = value ?? new List<ObdAnnotation>();
                selectedObdIds.RemoveWhere(id => !obdAnnotations.Any(annotation => annotation.Id == id));
                if (!selectedObdIds.Contains(selectedObdId)) selectedObdId = selectedObdIds.FirstOrDefault();
                Invalidate();
            }
        }
        public IReadOnlyDictionary<string, AnnotationClass> ObdClasses { get { return obdClasses; } set { obdClasses = value ?? new Dictionary<string, AnnotationClass>(); Invalidate(); } }
        public IReadOnlyList<SegRegion> SegRegions
        {
            get { return segRegions; }
            set
            {
                segRegions = value ?? new List<SegRegion>();
                selectedSegIds.RemoveWhere(id => !segRegions.Any(region => region.Id == id));
                if (!selectedSegIds.Contains(selectedSegId)) selectedSegId = selectedSegIds.FirstOrDefault();
                Invalidate();
            }
        }
        public IReadOnlyDictionary<string, AnnotationClass> SegClasses { get { return segClasses; } set { segClasses = value ?? new Dictionary<string, AnnotationClass>(); Invalidate(); } }
        public event Action<Rectangle> ObdRectangleCreated;
        public event Action<IReadOnlyDictionary<string, Rectangle>> ObdRectanglesChanged;
        public event Action<IReadOnlyCollection<string>> ObdAnnotationsDeleteRequested;
        public event Action<SegRegionType, Rectangle> SegRegionCreated;
        public event Action<SegRegionType, IReadOnlyList<SegPoint>, int> SegPathCreated;
        public event Action<IReadOnlyList<SegRegion>> SegRegionsChanged;
        public event Action<IReadOnlyCollection<string>> SegRegionsDeleteRequested;

        public ImageCanvasControl()
        {
            DoubleBuffered = true;
            BackColor = Color.FromArgb(45, 45, 48);
            TabStop = true;
            Resize += (sender, args) => viewport.SetViewportSize(ClientSize);
        }

        public double Zoom { get { return viewport.Zoom; } }
        public event EventHandler ZoomChanged;

        public void LoadImage(string path)
        {
            DiscardUnfinishedShape();
            CancelImageLoad();
            ClearImage();
            if (path != null) LoadImageAsync(path);
        }

        private async void LoadImageAsync(string path)
        {
            var cancellation = new CancellationTokenSource();
            imageLoad = cancellation;
            try
            {
                CanvasImage loaded = await Task.Run(() => LoadCanvasImage(path, cancellation.Token), cancellation.Token);
                if (cancellation.IsCancellationRequested) { loaded.Preview.Dispose(); return; }
                image = loaded.Preview;
                viewport.SetImageSize(loaded.OriginalSize);
                Invalidate();
                ZoomChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (OperationCanceledException) { }
            catch { if (!cancellation.IsCancellationRequested) ClearImage(); }
            finally
            {
                if (ReferenceEquals(imageLoad, cancellation)) imageLoad = null;
                cancellation.Dispose();
            }
        }

        private CanvasImage LoadCanvasImage(string path, CancellationToken cancellation)
        {
            bool acquired = false;
            try
            {
                imageLoadGate.Wait(cancellation);
                acquired = true;
                using (var source = Image.FromFile(path))
                {
                    cancellation.ThrowIfCancellationRequested();
                    var preview = new Bitmap(source);
                    try { cancellation.ThrowIfCancellationRequested(); return new CanvasImage(preview, source.Size); }
                    catch { preview.Dispose(); throw; }
                }
            }
            finally { if (acquired) imageLoadGate.Release(); }
        }

        private void ClearImage()
        {
            Image previous = image;
            image = null;
            previous?.Dispose();
            viewport.SetImageSize(Size.Empty);
            Invalidate();
            ZoomChanged?.Invoke(this, EventArgs.Empty);
        }

        private void CancelImageLoad() { imageLoad?.Cancel(); imageLoad = null; }
        public void FitToWindow() { viewport.FitToWindow(); Invalidate(); ZoomChanged?.Invoke(this, EventArgs.Empty); }
        public void SetZoomPercent(double percent)
        {
            viewport.SetZoomAt(percent / 100d, new PointF(ClientSize.Width / 2f, ClientSize.Height / 2f));
            Invalidate();
            ZoomChanged?.Invoke(this, EventArgs.Empty);
        }

        public void DiscardUnfinishedShape()
        {
            obdPreview = null;
            obdPreviews = new Dictionary<string, RectangleF>();
            segPreview = null;
            segPath.Clear();
            segPathDragging = false;
            segLineStart = null;
            selectedSegIds.Clear();
            selectedSegId = null;
            segPreviews = new Dictionary<string, SegRegion>();
            segDrag = SegDragKind.None;
            activeObd = null;
            obdDrag = ObdDragKind.None;
            Capture = false;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs args)
        {
            base.OnPaint(args);
            if (image == null) return;
            PointF origin = viewport.Origin;
            args.Graphics.DrawImage(image, new RectangleF(origin.X, origin.Y, (float)(viewport.ImageSize.Width * viewport.Zoom), (float)(viewport.ImageSize.Height * viewport.Zoom)));
            foreach (ObdAnnotation annotation in obdAnnotations.Where(item => !selectedObdIds.Contains(item.Id)).Concat(obdAnnotations.Where(item => selectedObdIds.Contains(item.Id))))
            {
                RectangleF imageRectangle;
                if (!obdPreviews.TryGetValue(annotation.Id, out imageRectangle)) imageRectangle = new RectangleF(annotation.X, annotation.Y, annotation.Width, annotation.Height);
                RectangleF rectangle = ToScreenRectangle(imageRectangle.X, imageRectangle.Y, imageRectangle.Width, imageRectangle.Height);
                AnnotationClass annotationClass;
                obdClasses.TryGetValue(annotation.ClassId, out annotationClass);
                Color color = annotationClass == null ? Color.Red : ColorTranslator.FromHtml(annotationClass.Color);
                using (var fill = new SolidBrush(Color.FromArgb(102, color))) args.Graphics.FillRectangle(fill, rectangle);
                using (var pen = new Pen(color, selectedObdIds.Contains(annotation.Id) ? 3f : 2f)) args.Graphics.DrawRectangle(pen, rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);
                if (annotationClass != null) using (var text = new SolidBrush(color)) args.Graphics.DrawString(annotationClass.Name, Font, text, rectangle.Left + 2f, rectangle.Top + 2f);
                if (annotation.Id == selectedObdId) DrawHandles(args.Graphics, rectangle);
            }
            if (obdPreview.HasValue)
            {
                RectangleF rectangle = ToScreenRectangle(obdPreview.Value.X, obdPreview.Value.Y, obdPreview.Value.Width, obdPreview.Value.Height);
                using (var pen = new Pen(Color.White, 1f)) args.Graphics.DrawRectangle(pen, rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);
            }
            foreach (SegRegion stored in segRegions.Where(item => !selectedSegIds.Contains(item.Id)).Concat(segRegions.Where(item => selectedSegIds.Contains(item.Id))))
            {
                SegRegion region;
                if (!segPreviews.TryGetValue(stored.Id, out region)) region = stored;
                AnnotationClass annotationClass;
                segClasses.TryGetValue(region.ClassId, out annotationClass);
                Color color = annotationClass == null ? Color.Red : ColorTranslator.FromHtml(annotationClass.Color);
                using (var fill = new SolidBrush(Color.FromArgb(Math.Max(0, Math.Min(255, (int)Math.Round(255f * SegOpacity))), color)))
                using (var pen = new Pen(color, 2f))
                {
                    if (region.Type == SegRegionType.Rect)
                    {
                        RectangleF rectangle = ToScreenRectangle(region.X, region.Y, region.Width, region.Height);
                        args.Graphics.FillRectangle(fill, rectangle);
                        args.Graphics.DrawRectangle(pen, rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);
                    }
                    else if (region.Type == SegRegionType.Ellipse)
                    {
                        RectangleF ellipse = ToScreenRectangle(region.X - region.Width, region.Y - region.Height, region.Width * 2, region.Height * 2);
                        args.Graphics.FillEllipse(fill, ellipse);
                        args.Graphics.DrawEllipse(pen, ellipse);
                    }
                    else if (region.Points.Count > 1)
                    {
                        PointF[] points = region.Points.Select(point => viewport.ImageToScreen(new PointF(point.X, point.Y))).ToArray();
                        if (region.Type == SegRegionType.PolyGon && points.Length > 2)
                        {
                            args.Graphics.FillPolygon(fill, points);
                            args.Graphics.DrawPolygon(pen, points);
                        }
                        else
                        {
                            using (var stroke = new Pen(color, Math.Max(1f, (float)(region.StrokeWidth * viewport.Zoom)))) args.Graphics.DrawLines(stroke, points);
                        }
                    }
                }
                if (region.Id == selectedSegId) DrawSegHandles(args.Graphics, ToScreenRectangle(GetSegBounds(region)));
            }
            if (segPreview.HasValue)
            {
                RectangleF rectangle = ToScreenRectangle(segPreview.Value.X, segPreview.Value.Y, segPreview.Value.Width, segPreview.Value.Height);
                float width = SegTool == SegToolKind.Brush ? Math.Max(1f, (float)(BrushWidth * viewport.Zoom)) : 1f;
                using (var pen = new Pen(Color.White, width))
                {
                    if (SegTool == SegToolKind.Ellipse) args.Graphics.DrawEllipse(pen, rectangle);
                    else args.Graphics.DrawRectangle(pen, rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);
                }
            }
            if (segPath.Count > 0)
            {
                PointF[] points = segPath.Select(viewport.ImageToScreen).ToArray();
                using (var pen = new Pen(Color.White, 1f))
                {
                    if (points.Length > 1) args.Graphics.DrawLines(pen, points);
                    if (SegTool == SegToolKind.Polygon && points.Length > 2) args.Graphics.DrawLine(pen, points[points.Length - 1], points[0]);
                }
                if (SegTool == SegToolKind.Polygon && DistanceInScreenPixels(segPath[0], segCursor) <= 8f)
                {
                    PointF closureOrigin = points[0];
                    using (var pen = new Pen(Color.Lime, 2f)) args.Graphics.DrawEllipse(pen, closureOrigin.X - 5f, closureOrigin.Y - 5f, 10f, 10f);
                }
            }
            if (segLineStart.HasValue && SegTool == SegToolKind.Line)
            {
                using (var pen = new Pen(Color.White, Math.Max(1f, (float)(LineWidth * viewport.Zoom)))) args.Graphics.DrawLine(pen, viewport.ImageToScreen(segLineStart.Value), viewport.ImageToScreen(segCursor));
            }
            if (SegTool == SegToolKind.Brush)
            {
                PointF cursor = viewport.ImageToScreen(segCursor);
                float diameter = Math.Max(1f, (float)(BrushWidth * viewport.Zoom));
                using (var pen = new Pen(Color.White, 1f)) args.Graphics.DrawEllipse(pen, cursor.X - diameter / 2f, cursor.Y - diameter / 2f, diameter, diameter);
            }
        }

        private static void DrawHandles(Graphics graphics, RectangleF rectangle)
        {
            foreach (PointF point in GetHandlePoints(rectangle))
            {
                var handle = new RectangleF(point.X - HandleSize / 2f, point.Y - HandleSize / 2f, HandleSize, HandleSize);
                using (var fill = new SolidBrush(Color.White)) graphics.FillRectangle(fill, handle);
                using (var pen = new Pen(Color.Black)) graphics.DrawRectangle(pen, handle.X, handle.Y, handle.Width, handle.Height);
            }
        }

        private static void DrawSegHandles(Graphics graphics, RectangleF rectangle)
        {
            DrawHandles(graphics, rectangle);
            PointF rotation = new PointF(rectangle.Left + rectangle.Width / 2f, rectangle.Top - 24f);
            using (var pen = new Pen(Color.Black)) graphics.DrawLine(pen, rectangle.Left + rectangle.Width / 2f, rectangle.Top, rotation.X, rotation.Y);
            using (var fill = new SolidBrush(Color.White)) graphics.FillEllipse(fill, rotation.X - 5f, rotation.Y - 5f, 10f, 10f);
            using (var pen = new Pen(Color.Black)) graphics.DrawEllipse(pen, rotation.X - 5f, rotation.Y - 5f, 10f, 10f);
        }

        protected override void OnMouseWheel(MouseEventArgs args)
        {
            if ((ModifierKeys & Keys.Control) == Keys.Control) { viewport.SetZoomAt(viewport.Zoom * (args.Delta > 0 ? 1.1d : 1d / 1.1d), args.Location); ZoomChanged?.Invoke(this, EventArgs.Empty); }
            else if ((ModifierKeys & Keys.Shift) == Keys.Shift) viewport.Pan(args.Delta / 120f * 48f, 0f);
            else viewport.Pan(0f, args.Delta / 120f * 48f);
            Invalidate();
            base.OnMouseWheel(args);
        }

        protected override void OnMouseDown(MouseEventArgs args)
        {
            Focus();
            panning = args.Button == MouseButtons.Left && (ModifierKeys & Keys.Space) == Keys.Space;
            if (args.Button == MouseButtons.Left && ObdEnabled && !panning && image != null)
            {
                ObdDragKind handle = HitHandle(args.Location);
                PointF point = viewport.ScreenToImage(args.Location);
                if (handle != ObdDragKind.None)
                {
                    activeObd = obdAnnotations.FirstOrDefault(item => item.Id == selectedObdId);
                    if (activeObd != null) BeginObdDrag(handle, point, activeObd);
                }
                else
                {
                    ObdAnnotation hit = TopmostObdAt(point);
                    if (hit == null)
                    {
                        ClearObdSelection();
                        obdStart = ClampPoint(point);
                        obdPreview = new RectangleF(obdStart, SizeF.Empty);
                        obdDrag = ObdDragKind.Create;
                    }
                    else if ((ModifierKeys & Keys.Control) == Keys.Control)
                    {
                        if (!selectedObdIds.Add(hit.Id))
                        {
                            selectedObdIds.Remove(hit.Id);
                            if (selectedObdId == hit.Id) selectedObdId = selectedObdIds.FirstOrDefault();
                        }
                        else selectedObdId = hit.Id;
                        Invalidate();
                        base.OnMouseDown(args);
                        return;
                    }
                    else
                    {
                        if (!selectedObdIds.Contains(hit.Id))
                        {
                            ClearObdSelection();
                            selectedObdIds.Add(hit.Id);
                            selectedObdId = hit.Id;
                        }
                        BeginObdDrag(ObdDragKind.Move, point, hit);
                    }
                }
                Capture = true;
                Invalidate();
            }
            else if (args.Button == MouseButtons.Left && SegEnabled && SegTool.HasValue && !panning && image != null)
            {
                if (SegTool == SegToolKind.Select)
                {
                    PointF selectionPoint = ClampPoint(viewport.ScreenToImage(args.Location));
                    SegDragKind handle = HitSegHandle(args.Location);
                    if (handle != SegDragKind.None)
                    {
                        segOriginal = segRegions.FirstOrDefault(item => item.Id == selectedSegId);
                        if (segOriginal != null)
                        {
                            segOriginalBounds = GetSegBounds(segOriginal);
                            segDragStart = selectionPoint;
                            segDrag = handle;
                            Capture = true;
                            base.OnMouseDown(args);
                            return;
                        }
                    }
                    SegRegion hit = TopmostSegAt(selectionPoint);
                    if (hit == null) ClearSegSelection();
                    else if ((ModifierKeys & Keys.Control) == Keys.Control)
                    {
                        if (!selectedSegIds.Add(hit.Id)) selectedSegIds.Remove(hit.Id);
                        selectedSegId = selectedSegIds.Contains(hit.Id) ? hit.Id : selectedSegIds.FirstOrDefault();
                    }
                    else
                    {
                        if (!selectedSegIds.Contains(hit.Id)) { ClearSegSelection(); selectedSegIds.Add(hit.Id); selectedSegId = hit.Id; }
                        segDragStart = selectionPoint;
                        segDrag = SegDragKind.Move;
                        Capture = true;
                    }
                    Invalidate();
                    base.OnMouseDown(args);
                    return;
                }
                if ((SegTool == SegToolKind.Brush || SegTool == SegToolKind.Line) && !CanFitSegPath())
                {
                    DiscardUnfinishedShape();
                    base.OnMouseDown(args);
                    return;
                }
                PointF point = ClampSegPathPoint(viewport.ScreenToImage(args.Location));
                if (SegTool == SegToolKind.Rect || SegTool == SegToolKind.Ellipse)
                {
                    segStart = point;
                    segPreview = new RectangleF(segStart, SizeF.Empty);
                    Capture = true;
                }
                else if (SegTool == SegToolKind.Line)
                {
                    if (!segLineStart.HasValue) segLineStart = point;
                    else
                    {
                        SegPoint first = ToSegPoint(segLineStart.Value);
                        SegPoint second = ToSegPoint(point);
                        if (first.X != second.X || first.Y != second.Y) SegPathCreated?.Invoke(SegRegionType.PolyLine, new[] { first, second }, LineWidth);
                        segLineStart = null;
                    }
                }
                else
                {
                    if (SegTool == SegToolKind.Polygon && segPath.Count >= 3 && DistanceInScreenPixels(segPath[0], point) <= 8f)
                    {
                        SegPathCreated?.Invoke(SegRegionType.PolyGon, segPath.Select(ToSegPoint).ToArray(), 0);
                        segPath.Clear();
                        Invalidate();
                        base.OnMouseDown(args);
                        return;
                    }
                    if (segPath.Count == 0) segPath.Add(point);
                    else segPath.Add(point);
                    segPathDragging = false;
                    Capture = true;
                }
                Invalidate();
            }
            previousPoint = args.Location;
            base.OnMouseDown(args);
        }

        private void BeginObdDrag(ObdDragKind drag, PointF point, ObdAnnotation annotation)
        {
            activeObd = annotation;
            obdDrag = drag;
            obdOriginal = new RectangleF(annotation.X, annotation.Y, annotation.Width, annotation.Height);
            obdDragStart = point;
            obdPreview = null;
            obdPreviews = new Dictionary<string, RectangleF>();
        }

        protected override void OnMouseMove(MouseEventArgs args)
        {
            if (SegEnabled && image != null) segCursor = ClampPoint(viewport.ScreenToImage(args.Location));
            if (panning)
            {
                viewport.Pan(args.X - previousPoint.X, args.Y - previousPoint.Y);
                previousPoint = args.Location;
                Invalidate();
            }
            else if (obdDrag != ObdDragKind.None && (args.Button & MouseButtons.Left) == MouseButtons.Left)
            {
                PointF point = ClampPoint(viewport.ScreenToImage(args.Location));
                if (obdDrag == ObdDragKind.Create) obdPreview = Normalize(obdStart, point);
                else if (obdDrag == ObdDragKind.Move) obdPreviews = MoveRectangles(point);
                else
                {
                    obdPreviews = new Dictionary<string, RectangleF> { { activeObd.Id, ResizeRectangle(point) } };
                }
                Invalidate();
            }
            else if (segDrag != SegDragKind.None && (args.Button & MouseButtons.Left) == MouseButtons.Left)
            {
                PointF point = ClampPoint(viewport.ScreenToImage(args.Location));
                if (segDrag == SegDragKind.Move) segPreviews = MoveSegRegions(point);
                else if (segDrag == SegDragKind.Rotate) segPreviews = new Dictionary<string, SegRegion> { { segOriginal.Id, RotateSegRegion(segOriginal, point) } };
                else segPreviews = new Dictionary<string, SegRegion> { { segOriginal.Id, ResizeSegRegion(segOriginal, point) } };
                Invalidate();
            }
            else if (segPreview.HasValue && (args.Button & MouseButtons.Left) == MouseButtons.Left)
            {
                segPreview = Normalize(segStart, ClampPoint(viewport.ScreenToImage(args.Location)));
                Invalidate();
            }
            else if (SegEnabled && SegTool == SegToolKind.Line && segLineStart.HasValue)
            {
                segCursor = ClampSegPathPoint(viewport.ScreenToImage(args.Location));
                Invalidate();
            }
            else if (segPath.Count > 0 && (args.Button & MouseButtons.Left) == MouseButtons.Left && (SegTool == SegToolKind.Polygon || SegTool == SegToolKind.Brush))
            {
                if (DistanceInScreenPixels(segPath[segPath.Count - 1], viewport.ScreenToImage(args.Location)) >= 2f)
                {
                    segPath.Add(ClampSegPathPoint(viewport.ScreenToImage(args.Location)));
                    segPathDragging = true;
                }
                Invalidate();
            }
            base.OnMouseMove(args);
        }

        protected override void OnMouseUp(MouseEventArgs args)
        {
            if (args.Button == MouseButtons.Left && obdDrag != ObdDragKind.None)
            {
                ObdDragKind completed = obdDrag;
                var previews = obdPreviews.ToDictionary(item => item.Key, item => item.Value);
                if (completed == ObdDragKind.Create && obdPreview.HasValue) previews.Add(string.Empty, obdPreview.Value);
                obdPreview = null;
                obdPreviews = new Dictionary<string, RectangleF>();
                activeObd = null;
                obdDrag = ObdDragKind.None;
                Capture = false;
                if (completed == ObdDragKind.Create)
                {
                    Rectangle rectangle = ToIntegerRectangle(previews[string.Empty]);
                    if (rectangle.Width >= 1 && rectangle.Height >= 1) ObdRectangleCreated?.Invoke(rectangle);
                }
                else if (previews.Count > 0)
                {
                    var changed = new Dictionary<string, Rectangle>();
                    foreach (var preview in previews)
                    {
                        ObdAnnotation original = obdAnnotations.FirstOrDefault(item => item.Id == preview.Key);
                        if (original != null && ToIntegerRectangle(preview.Value) != new Rectangle(original.X, original.Y, original.Width, original.Height)) changed.Add(preview.Key, ToIntegerRectangle(preview.Value));
                    }
                    if (changed.Count > 0) ObdRectanglesChanged?.Invoke(changed);
                }
                Invalidate();
            }
            else if (args.Button == MouseButtons.Left && segDrag != SegDragKind.None)
            {
                SegRegion[] changed = segPreviews.Values.Where(value => !SegEquals(value, segRegions.First(item => item.Id == value.Id))).ToArray();
                segPreviews = new Dictionary<string, SegRegion>();
                segDrag = SegDragKind.None;
                Capture = false;
                if (changed.Length > 0) SegRegionsChanged?.Invoke(changed);
                Invalidate();
            }
            else if (args.Button == MouseButtons.Left && segPreview.HasValue)
            {
                Rectangle preview = ToIntegerRectangle(segPreview.Value);
                SegToolKind? tool = SegTool;
                segPreview = null;
                Capture = false;
                if (tool == SegToolKind.Rect && preview.Width >= 1 && preview.Height >= 1) SegRegionCreated?.Invoke(SegRegionType.Rect, preview);
                else if (tool == SegToolKind.Ellipse)
                {
                    int radiusX = preview.Width / 2;
                    int radiusY = preview.Height / 2;
                    if (radiusX >= 1 && radiusY >= 1) SegRegionCreated?.Invoke(SegRegionType.Ellipse, preview);
                }
                Invalidate();
            }
            else if (args.Button == MouseButtons.Left && Capture && (SegTool == SegToolKind.Polygon || SegTool == SegToolKind.Brush))
            {
                Capture = false;
                PointF point = ClampSegPathPoint(viewport.ScreenToImage(args.Location));
                if (segPathDragging && SegTool == SegToolKind.Polygon && segPath.Count >= 3 && DistanceInScreenPixels(segPath[0], point) <= 8f)
                {
                    SegPathCreated?.Invoke(SegRegionType.PolyGon, segPath.Select(ToSegPoint).ToArray(), 0);
                    segPath.Clear();
                }
                else if (SegTool == SegToolKind.Brush)
                {
                    if (segPath.Count >= 2) SegPathCreated?.Invoke(SegRegionType.PolyLine, segPath.Select(ToSegPoint).ToArray(), BrushWidth);
                    segPath.Clear();
                }
                segPathDragging = false;
                Invalidate();
            }
            panning = false;
            base.OnMouseUp(args);
        }

        protected override void OnKeyDown(KeyEventArgs args)
        {
            if (args.KeyCode == Keys.Escape) { DiscardUnfinishedShape(); args.Handled = true; }
            else if (args.KeyCode == Keys.Delete && ObdEnabled && selectedObdIds.Count > 0)
            {
                ObdAnnotationsDeleteRequested?.Invoke(selectedObdIds.ToArray());
                args.Handled = true;
                args.SuppressKeyPress = true;
            }
            else if (args.KeyCode == Keys.Delete && SegEnabled && selectedSegIds.Count > 0)
            {
                SegRegionsDeleteRequested?.Invoke(selectedSegIds.ToArray());
                args.Handled = true;
                args.SuppressKeyPress = true;
            }
            base.OnKeyDown(args);
        }

        private IReadOnlyDictionary<string, RectangleF> MoveRectangles(PointF point)
        {
            ObdAnnotation[] selected = obdAnnotations.Where(item => selectedObdIds.Contains(item.Id)).ToArray();
            float xOffset = point.X - obdDragStart.X;
            float yOffset = point.Y - obdDragStart.Y;
            xOffset = Math.Max(selected.Max(item => -item.X), Math.Min(xOffset, selected.Min(item => viewport.ImageSize.Width - item.X - item.Width)));
            yOffset = Math.Max(selected.Max(item => -item.Y), Math.Min(yOffset, selected.Min(item => viewport.ImageSize.Height - item.Y - item.Height)));
            return selected.ToDictionary(item => item.Id, item => new RectangleF(item.X + xOffset, item.Y + yOffset, item.Width, item.Height));
        }

        private IReadOnlyDictionary<string, SegRegion> MoveSegRegions(PointF point)
        {
            SegRegion[] selected = segRegions.Where(item => selectedSegIds.Contains(item.Id)).ToArray();
            float xOffset = point.X - segDragStart.X;
            float yOffset = point.Y - segDragStart.Y;
            xOffset = Math.Max(selected.Max(item => -GetStoredBounds(item).Left), Math.Min(xOffset, selected.Min(item => (item.Type == SegRegionType.PolyGon ? viewport.ImageSize.Width - 1 : viewport.ImageSize.Width) - GetStoredBounds(item).Right)));
            yOffset = Math.Max(selected.Max(item => -GetStoredBounds(item).Top), Math.Min(yOffset, selected.Min(item => (item.Type == SegRegionType.PolyGon ? viewport.ImageSize.Height - 1 : viewport.ImageSize.Height) - GetStoredBounds(item).Bottom)));
            return selected.ToDictionary(item => item.Id, item => TranslateSegRegion(item, xOffset, yOffset));
        }

        private static SegRegion TranslateSegRegion(SegRegion region, float xOffset, float yOffset)
        {
            if (region.Points.Count > 0)
            {
                return new SegRegion(region.Id, region.AnnotationSetId, region.ImageId, region.ClassId, region.Type, 0, 0, 0, 0, region.ZOrder, region.Points.Select(point => new SegPoint((int)Math.Round(point.X + xOffset, MidpointRounding.AwayFromZero), (int)Math.Round(point.Y + yOffset, MidpointRounding.AwayFromZero))).ToArray(), region.StrokeWidth);
            }
            return new SegRegion(region.Id, region.AnnotationSetId, region.ImageId, region.ClassId, region.Type, (int)Math.Round(region.X + xOffset, MidpointRounding.AwayFromZero), (int)Math.Round(region.Y + yOffset, MidpointRounding.AwayFromZero), region.Width, region.Height, region.ZOrder);
        }

        private SegRegion TopmostSegAt(PointF point)
        {
            return segRegions.Where(item => selectedSegIds.Contains(item.Id)).Reverse().Concat(segRegions.Where(item => !selectedSegIds.Contains(item.Id)).Reverse()).FirstOrDefault(item => ContainsSegPoint(item, point));
        }

        private static bool ContainsSegPoint(SegRegion region, PointF point)
        {
            if (region.Type == SegRegionType.Rect) return new RectangleF(region.X, region.Y, region.Width, region.Height).Contains(point);
            if (region.Type == SegRegionType.Ellipse)
            {
                float x = (point.X - region.X) / region.Width, y = (point.Y - region.Y) / region.Height;
                return x * x + y * y <= 1f;
            }
            if (region.Type == SegRegionType.PolyLine) return region.Points.Zip(region.Points.Skip(1), (first, second) => DistanceToSegment(point, first, second)).Any(distance => distance <= region.StrokeWidth / 2f);
            int winding = 0;
            for (int index = 0, previous = region.Points.Count - 1; index < region.Points.Count; previous = index++)
            {
                SegPoint a = region.Points[index], b = region.Points[previous];
                double cross = (b.X - a.X) * (point.Y - a.Y) - (point.X - a.X) * (b.Y - a.Y);
                if (a.Y <= point.Y && b.Y > point.Y && cross > 0d) winding++;
                else if (a.Y > point.Y && b.Y <= point.Y && cross < 0d) winding--;
            }
            return winding != 0;
        }

        private static float DistanceToSegment(PointF point, SegPoint first, SegPoint second)
        {
            float x = second.X - first.X, y = second.Y - first.Y;
            float length = x * x + y * y;
            float t = length == 0f ? 0f : Math.Max(0f, Math.Min(1f, ((point.X - first.X) * x + (point.Y - first.Y) * y) / length));
            float dx = point.X - (first.X + t * x), dy = point.Y - (first.Y + t * y);
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        private static RectangleF GetStoredBounds(SegRegion region)
        {
            if (region.Points.Count == 0) return GetSegBounds(region);
            int inset = region.Type == SegRegionType.PolyLine ? (region.StrokeWidth + 1) / 2 : 0;
            return RectangleF.FromLTRB(region.Points.Min(point => point.X) - inset, region.Points.Min(point => point.Y) - inset, region.Points.Max(point => point.X) + inset, region.Points.Max(point => point.Y) + inset);
        }

        private SegDragKind HitSegHandle(Point point)
        {
            if (selectedSegId == null) return SegDragKind.None;
            SegRegion region = segRegions.FirstOrDefault(item => item.Id == selectedSegId);
            if (region == null) return SegDragKind.None;
            RectangleF rectangle = ToScreenRectangle(GetSegBounds(region));
            PointF rotation = new PointF(rectangle.Left + rectangle.Width / 2f, rectangle.Top - 24f);
            if (Math.Abs(point.X - rotation.X) <= HandleSize && Math.Abs(point.Y - rotation.Y) <= HandleSize) return SegDragKind.Rotate;
            SegDragKind[] kinds = { SegDragKind.ResizeNw, SegDragKind.ResizeN, SegDragKind.ResizeNe, SegDragKind.ResizeE, SegDragKind.ResizeSe, SegDragKind.ResizeS, SegDragKind.ResizeSw, SegDragKind.ResizeW };
            PointF[] points = GetHandlePoints(rectangle);
            for (int index = 0; index < points.Length; index++) if (Math.Abs(point.X - points[index].X) <= HandleSize && Math.Abs(point.Y - points[index].Y) <= HandleSize) return kinds[index];
            return SegDragKind.None;
        }

        private SegRegion ResizeSegRegion(SegRegion region, PointF point)
        {
            float left = segOriginalBounds.Left, top = segOriginalBounds.Top, right = segOriginalBounds.Right, bottom = segOriginalBounds.Bottom;
            if (segDrag == SegDragKind.ResizeN || segDrag == SegDragKind.ResizeNw || segDrag == SegDragKind.ResizeNe) top = Math.Min(point.Y, bottom - 1f);
            if (segDrag == SegDragKind.ResizeS || segDrag == SegDragKind.ResizeSw || segDrag == SegDragKind.ResizeSe) bottom = Math.Max(point.Y, top + 1f);
            if (segDrag == SegDragKind.ResizeW || segDrag == SegDragKind.ResizeNw || segDrag == SegDragKind.ResizeSw) left = Math.Min(point.X, right - 1f);
            if (segDrag == SegDragKind.ResizeE || segDrag == SegDragKind.ResizeNe || segDrag == SegDragKind.ResizeSe) right = Math.Max(point.X, left + 1f);
            RectangleF target = Clamp(RectangleF.FromLTRB(left, top, right, bottom));
            if (region.Points.Count > 0)
            {
                int inset = region.Type == SegRegionType.PolyLine ? (region.StrokeWidth + 1) / 2 : 0;
                target = RectangleF.FromLTRB(Math.Max(inset, target.Left), Math.Max(inset, target.Top), Math.Min(viewport.ImageSize.Width - 1 - inset, target.Right), Math.Min(viewport.ImageSize.Height - 1 - inset, target.Bottom));
            }
            return ScaleSegRegion(region, segOriginalBounds, target);
        }

        private static SegRegion ScaleSegRegion(SegRegion region, RectangleF source, RectangleF target)
        {
            float scaleX = target.Width / source.Width, scaleY = target.Height / source.Height;
            Func<float, float, SegPoint> scale = (x, y) => new SegPoint((int)Math.Round(target.Left + (x - source.Left) * scaleX, MidpointRounding.AwayFromZero), (int)Math.Round(target.Top + (y - source.Top) * scaleY, MidpointRounding.AwayFromZero));
            if (region.Type == SegRegionType.Rect) return new SegRegion(region.Id, region.AnnotationSetId, region.ImageId, region.ClassId, region.Type, (int)Math.Round(target.Left), (int)Math.Round(target.Top), Math.Max(1, (int)Math.Round(target.Width)), Math.Max(1, (int)Math.Round(target.Height)), region.ZOrder);
            if (region.Type == SegRegionType.Ellipse)
            {
                SegPoint center = scale(region.X, region.Y);
                return new SegRegion(region.Id, region.AnnotationSetId, region.ImageId, region.ClassId, region.Type, center.X, center.Y, Math.Max(1, (int)Math.Round(region.Width * scaleX)), Math.Max(1, (int)Math.Round(region.Height * scaleY)), region.ZOrder);
            }
            if (region.Type == SegRegionType.PolyLine)
            {
                var line = new Path64(region.Points.Select(value => new Point64(value.X, value.Y)));
                SegPoint[] outline = Clipper.InflatePaths(new Paths64 { line }, region.StrokeWidth / 2d, JoinType.Round, EndType.Round).SelectMany(path => path).Select(value => scale(value.X, value.Y)).ToArray();
                return new SegRegion(region.Id, region.AnnotationSetId, region.ImageId, region.ClassId, SegRegionType.PolyGon, 0, 0, 0, 0, region.ZOrder, outline);
            }
            return new SegRegion(region.Id, region.AnnotationSetId, region.ImageId, region.ClassId, region.Type, 0, 0, 0, 0, region.ZOrder, region.Points.Select(value => scale(value.X, value.Y)).ToArray(), region.StrokeWidth);
        }


        private SegRegion RotateSegRegion(SegRegion region, PointF point)
        {
            PointF center = new PointF(segOriginalBounds.Left + segOriginalBounds.Width / 2f, segOriginalBounds.Top + segOriginalBounds.Height / 2f);
            double start = Math.Atan2(segDragStart.Y - center.Y, segDragStart.X - center.X);
            double current = Math.Atan2(point.Y - center.Y, point.X - center.X);
            double cosine = Math.Cos(current - start), sine = Math.Sin(current - start);
            IEnumerable<SegPoint> points = GetRotationPoints(region).Select(value => new SegPoint((int)Math.Round(center.X + (value.X - center.X) * cosine - (value.Y - center.Y) * sine, MidpointRounding.AwayFromZero), (int)Math.Round(center.Y + (value.X - center.X) * sine + (value.Y - center.Y) * cosine, MidpointRounding.AwayFromZero)));
            SegPoint[] transformed = points.ToArray();
            SegRegion rotated = new SegRegion(region.Id, region.AnnotationSetId, region.ImageId, region.ClassId, region.Type == SegRegionType.PolyLine ? SegRegionType.PolyLine : SegRegionType.PolyGon, 0, 0, 0, 0, region.ZOrder, transformed, region.StrokeWidth);
            RectangleF bounds = GetSegBounds(rotated);
            if (transformed.Any(value => value.X < 0 || value.Y < 0 || value.X >= viewport.ImageSize.Width || value.Y >= viewport.ImageSize.Height) || bounds.Left < 0 || bounds.Top < 0 || bounds.Right > viewport.ImageSize.Width || bounds.Bottom > viewport.ImageSize.Height) return region;
            return rotated;
        }

        private static IEnumerable<SegPoint> GetRotationPoints(SegRegion region)
        {
            if (region.Points.Count > 0) return region.Points;
            if (region.Type == SegRegionType.Rect) return new[] { new SegPoint(region.X, region.Y), new SegPoint(region.X + region.Width, region.Y), new SegPoint(region.X + region.Width, region.Y + region.Height), new SegPoint(region.X, region.Y + region.Height) };
            return Enumerable.Range(0, 32).Select(index => new SegPoint((int)Math.Round(region.X + region.Width * Math.Cos(2d * Math.PI * index / 32d)), (int)Math.Round(region.Y + region.Height * Math.Sin(2d * Math.PI * index / 32d))));
        }

        private void ClearSegSelection()
        {
            selectedSegIds.Clear();
            selectedSegId = null;
            segPreviews = new Dictionary<string, SegRegion>();
        }

        private static bool SegEquals(SegRegion first, SegRegion second)
        {
            return first.ClassId == second.ClassId && first.Type == second.Type && first.X == second.X && first.Y == second.Y && first.Width == second.Width && first.Height == second.Height && first.StrokeWidth == second.StrokeWidth && first.Points.Count == second.Points.Count && first.Points.Zip(second.Points, (left, right) => left.X == right.X && left.Y == right.Y).All(equal => equal);
        }

        private RectangleF ResizeRectangle(PointF point)
        {
            float left = obdOriginal.Left, top = obdOriginal.Top, right = obdOriginal.Right, bottom = obdOriginal.Bottom;
            if (obdDrag == ObdDragKind.ResizeN || obdDrag == ObdDragKind.ResizeNw || obdDrag == ObdDragKind.ResizeNe) top = Math.Min(point.Y, bottom - 1f);
            if (obdDrag == ObdDragKind.ResizeS || obdDrag == ObdDragKind.ResizeSw || obdDrag == ObdDragKind.ResizeSe) bottom = Math.Max(point.Y, top + 1f);
            if (obdDrag == ObdDragKind.ResizeW || obdDrag == ObdDragKind.ResizeNw || obdDrag == ObdDragKind.ResizeSw) left = Math.Min(point.X, right - 1f);
            if (obdDrag == ObdDragKind.ResizeE || obdDrag == ObdDragKind.ResizeNe || obdDrag == ObdDragKind.ResizeSe) right = Math.Max(point.X, left + 1f);
            return Clamp(RectangleF.FromLTRB(left, top, right, bottom));
        }

        private ObdDragKind HitHandle(Point point)
        {
            if (selectedObdId == null) return ObdDragKind.None;
            ObdAnnotation annotation = obdAnnotations.FirstOrDefault(item => item.Id == selectedObdId);
            if (annotation == null) return ObdDragKind.None;
            PointF[] points = GetHandlePoints(ToScreenRectangle(annotation.X, annotation.Y, annotation.Width, annotation.Height));
            ObdDragKind[] kinds = { ObdDragKind.ResizeNw, ObdDragKind.ResizeN, ObdDragKind.ResizeNe, ObdDragKind.ResizeE, ObdDragKind.ResizeSe, ObdDragKind.ResizeS, ObdDragKind.ResizeSw, ObdDragKind.ResizeW };
            for (int index = 0; index < points.Length; index++) if (Math.Abs(point.X - points[index].X) <= HandleSize && Math.Abs(point.Y - points[index].Y) <= HandleSize) return kinds[index];
            return ObdDragKind.None;
        }

        private ObdAnnotation TopmostObdAt(PointF point)
        {
            ObdAnnotation selected = obdAnnotations.Where(item => selectedObdIds.Contains(item.Id)).Reverse().FirstOrDefault(item => new RectangleF(item.X, item.Y, item.Width, item.Height).Contains(point));
            if (selected != null) return selected;
            return obdAnnotations.Reverse().FirstOrDefault(item => !selectedObdIds.Contains(item.Id) && new RectangleF(item.X, item.Y, item.Width, item.Height).Contains(point));
        }

        private void ClearObdSelection()
        {
            selectedObdIds.Clear();
            selectedObdId = null;
        }

        private static PointF[] GetHandlePoints(RectangleF rectangle)
        {
            return new[] { new PointF(rectangle.Left, rectangle.Top), new PointF(rectangle.Left + rectangle.Width / 2f, rectangle.Top), new PointF(rectangle.Right, rectangle.Top), new PointF(rectangle.Right, rectangle.Top + rectangle.Height / 2f), new PointF(rectangle.Right, rectangle.Bottom), new PointF(rectangle.Left + rectangle.Width / 2f, rectangle.Bottom), new PointF(rectangle.Left, rectangle.Bottom), new PointF(rectangle.Left, rectangle.Top + rectangle.Height / 2f) };
        }

        private RectangleF ToScreenRectangle(float x, float y, float width, float height)
        {
            PointF topLeft = viewport.ImageToScreen(new PointF(x, y));
            return new RectangleF(topLeft.X, topLeft.Y, (float)(width * viewport.Zoom), (float)(height * viewport.Zoom));
        }

        private RectangleF ToScreenRectangle(RectangleF rectangle) { return ToScreenRectangle(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height); }

        private static RectangleF GetSegBounds(SegRegion region)
        {
            if (region.Type == SegRegionType.Rect) return new RectangleF(region.X, region.Y, region.Width, region.Height);
            if (region.Type == SegRegionType.Ellipse) return new RectangleF(region.X - region.Width, region.Y - region.Height, region.Width * 2, region.Height * 2);
            int left = region.Points.Min(point => point.X), right = region.Points.Max(point => point.X), top = region.Points.Min(point => point.Y), bottom = region.Points.Max(point => point.Y);
            float inset = region.Type == SegRegionType.PolyLine ? region.StrokeWidth / 2f : 0f;
            return RectangleF.FromLTRB(left - inset, top - inset, right + inset, bottom + inset);
        }

        private PointF ClampPoint(PointF value) { return new PointF(Math.Max(0f, Math.Min(value.X, viewport.ImageSize.Width)), Math.Max(0f, Math.Min(value.Y, viewport.ImageSize.Height))); }
        private RectangleF Clamp(RectangleF value)
        {
            float left = Math.Max(0f, Math.Min(value.Left, viewport.ImageSize.Width));
            float top = Math.Max(0f, Math.Min(value.Top, viewport.ImageSize.Height));
            float right = Math.Max(left, Math.Min(value.Right, viewport.ImageSize.Width));
            float bottom = Math.Max(top, Math.Min(value.Bottom, viewport.ImageSize.Height));
            return RectangleF.FromLTRB(left, top, right, bottom);
        }
        private static RectangleF Normalize(PointF first, PointF second) { return RectangleF.FromLTRB(Math.Min(first.X, second.X), Math.Min(first.Y, second.Y), Math.Max(first.X, second.X), Math.Max(first.Y, second.Y)); }
        private static Rectangle ToIntegerRectangle(RectangleF rectangle) { return Rectangle.FromLTRB((int)Math.Round(rectangle.Left), (int)Math.Round(rectangle.Top), (int)Math.Round(rectangle.Right), (int)Math.Round(rectangle.Bottom)); }
        private PointF ClampSegPathPoint(PointF point)
        {
            int width = SegTool == SegToolKind.Brush ? BrushWidth : SegTool == SegToolKind.Line ? LineWidth : 0;
            int inset = width == 0 ? 0 : (width + 1) / 2;
            Size size = viewport.ImageSize;
            return new PointF(Math.Max(inset, Math.Min(size.Width - inset, point.X)), Math.Max(inset, Math.Min(size.Height - inset, point.Y)));
        }
        private bool CanFitSegPath()
        {
            int width = SegTool == SegToolKind.Brush ? BrushWidth : LineWidth;
            int inset = (width + 1) / 2;
            return viewport.ImageSize.Width >= inset * 2 && viewport.ImageSize.Height >= inset * 2;
        }
        private static SegPoint ToSegPoint(PointF point) { return new SegPoint((int)Math.Round(point.X, MidpointRounding.AwayFromZero), (int)Math.Round(point.Y, MidpointRounding.AwayFromZero)); }
        private float DistanceInScreenPixels(PointF first, PointF second) { PointF a = viewport.ImageToScreen(first); PointF b = viewport.ImageToScreen(second); float x = a.X - b.X, y = a.Y - b.Y; return (float)Math.Sqrt(x * x + y * y); }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { CancelImageLoad(); image?.Dispose(); }
            base.Dispose(disposing);
        }

        private enum ObdDragKind { None, Create, Move, ResizeN, ResizeS, ResizeW, ResizeE, ResizeNw, ResizeNe, ResizeSw, ResizeSe }
        private enum SegDragKind { None, Move, ResizeN, ResizeS, ResizeW, ResizeE, ResizeNw, ResizeNe, ResizeSw, ResizeSe, Rotate }
        public enum SegToolKind { Select, Rect, Ellipse, Polygon, Brush, Line }
        private sealed class CanvasImage
        {
            public CanvasImage(Image preview, Size originalSize) { Preview = preview; OriginalSize = originalSize; }
            public Image Preview { get; private set; }
            public Size OriginalSize { get; private set; }
        }
    }
}

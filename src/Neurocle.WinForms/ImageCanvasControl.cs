using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
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
        private IReadOnlyList<SegRegion> segRegions = new List<SegRegion>();
        private IReadOnlyDictionary<string, AnnotationClass> segClasses = new Dictionary<string, AnnotationClass>();

        public bool ObdEnabled { get; set; }
        public bool SegEnabled { get; set; }
        public SegRegionType? SegTool { get; set; }
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
        public IReadOnlyList<SegRegion> SegRegions { get { return segRegions; } set { segRegions = value ?? new List<SegRegion>(); Invalidate(); } }
        public IReadOnlyDictionary<string, AnnotationClass> SegClasses { get { return segClasses; } set { segClasses = value ?? new Dictionary<string, AnnotationClass>(); Invalidate(); } }
        public event Action<Rectangle> ObdRectangleCreated;
        public event Action<IReadOnlyDictionary<string, Rectangle>> ObdRectanglesChanged;
        public event Action<IReadOnlyCollection<string>> ObdAnnotationsDeleteRequested;
        public event Action<SegRegionType, Rectangle> SegRegionCreated;

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
            foreach (SegRegion region in segRegions)
            {
                AnnotationClass annotationClass;
                segClasses.TryGetValue(region.ClassId, out annotationClass);
                Color color = annotationClass == null ? Color.Red : ColorTranslator.FromHtml(annotationClass.Color);
                using (var fill = new SolidBrush(Color.FromArgb(102, color)))
                using (var pen = new Pen(color, 2f))
                {
                    if (region.Type == SegRegionType.Rect)
                    {
                        RectangleF rectangle = ToScreenRectangle(region.X, region.Y, region.Width, region.Height);
                        args.Graphics.FillRectangle(fill, rectangle);
                        args.Graphics.DrawRectangle(pen, rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);
                    }
                    else
                    {
                        RectangleF ellipse = ToScreenRectangle(region.X - region.Width, region.Y - region.Height, region.Width * 2, region.Height * 2);
                        args.Graphics.FillEllipse(fill, ellipse);
                        args.Graphics.DrawEllipse(pen, ellipse);
                    }
                }
            }
            if (segPreview.HasValue)
            {
                RectangleF rectangle = ToScreenRectangle(segPreview.Value.X, segPreview.Value.Y, segPreview.Value.Width, segPreview.Value.Height);
                using (var pen = new Pen(Color.White, 1f))
                {
                    if (SegTool == SegRegionType.Ellipse) args.Graphics.DrawEllipse(pen, rectangle);
                    else args.Graphics.DrawRectangle(pen, rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);
                }
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
                segStart = ClampPoint(viewport.ScreenToImage(args.Location));
                segPreview = new RectangleF(segStart, SizeF.Empty);
                Capture = true;
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
            else if (segPreview.HasValue && (args.Button & MouseButtons.Left) == MouseButtons.Left)
            {
                segPreview = Normalize(segStart, ClampPoint(viewport.ScreenToImage(args.Location)));
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
            else if (args.Button == MouseButtons.Left && segPreview.HasValue)
            {
                Rectangle preview = ToIntegerRectangle(segPreview.Value);
                SegRegionType? tool = SegTool;
                segPreview = null;
                Capture = false;
                if (tool == SegRegionType.Rect && preview.Width >= 1 && preview.Height >= 1) SegRegionCreated?.Invoke(SegRegionType.Rect, preview);
                else if (tool == SegRegionType.Ellipse)
                {
                    int radiusX = preview.Width / 2;
                    int radiusY = preview.Height / 2;
                    if (radiusX >= 1 && radiusY >= 1) SegRegionCreated?.Invoke(SegRegionType.Ellipse, preview);
                }
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

        protected override void Dispose(bool disposing)
        {
            if (disposing) { CancelImageLoad(); image?.Dispose(); }
            base.Dispose(disposing);
        }

        private enum ObdDragKind { None, Create, Move, ResizeN, ResizeS, ResizeW, ResizeE, ResizeNw, ResizeNe, ResizeSw, ResizeSe }
        private sealed class CanvasImage
        {
            public CanvasImage(Image preview, Size originalSize) { Preview = preview; OriginalSize = originalSize; }
            public Image Preview { get; private set; }
            public Size OriginalSize { get; private set; }
        }
    }
}

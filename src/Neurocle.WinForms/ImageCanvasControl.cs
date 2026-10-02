using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Neurocle.Application.Projects;

namespace Neurocle.WinForms
{
    internal sealed class ImageCanvasControl : Control
    {
        private readonly CanvasViewport viewport = new CanvasViewport(Size.Empty, Size.Empty);
        private readonly SemaphoreSlim imageLoadGate = new SemaphoreSlim(1, 1);
        private Image image;
        private CancellationTokenSource imageLoad;
        private bool panning;
        private Point previousPoint;

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
                if (cancellation.IsCancellationRequested)
                {
                    loaded.Preview.Dispose();
                    return;
                }
                image = loaded.Preview;
                viewport.SetImageSize(loaded.OriginalSize);
                Invalidate();
                ZoomChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (OperationCanceledException) { }
            catch
            {
                if (!cancellation.IsCancellationRequested) ClearImage();
            }
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
                    try
                    {
                        cancellation.ThrowIfCancellationRequested();
                        return new CanvasImage(preview, source.Size);
                    }
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

        private void CancelImageLoad()
        {
            imageLoad?.Cancel();
            imageLoad = null;
        }

        public void FitToWindow()
        {
            viewport.FitToWindow();
            Invalidate();
            ZoomChanged?.Invoke(this, EventArgs.Empty);
        }

        public void SetZoomPercent(double percent)
        {
            viewport.SetZoomAt(percent / 100d, new PointF(ClientSize.Width / 2f, ClientSize.Height / 2f));
            Invalidate();
            ZoomChanged?.Invoke(this, EventArgs.Empty);
        }

        public void DiscardUnfinishedShape()
        {
            // Formal annotation tools are added by later tickets; this is the page-switch seam.
        }

        protected override void OnPaint(PaintEventArgs args)
        {
            base.OnPaint(args);
            if (image == null) return;
            PointF origin = viewport.Origin;
            var target = new RectangleF(origin.X, origin.Y, (float)(viewport.ImageSize.Width * viewport.Zoom), (float)(viewport.ImageSize.Height * viewport.Zoom));
            args.Graphics.DrawImage(image, target);
        }

        protected override void OnMouseWheel(MouseEventArgs args)
        {
            if ((ModifierKeys & Keys.Control) == Keys.Control)
            {
                viewport.SetZoomAt(viewport.Zoom * (args.Delta > 0 ? 1.1d : 1d / 1.1d), args.Location);
                ZoomChanged?.Invoke(this, EventArgs.Empty);
            }
            else if ((ModifierKeys & Keys.Shift) == Keys.Shift) viewport.Pan(args.Delta / 120f * 48f, 0f);
            else viewport.Pan(0f, args.Delta / 120f * 48f);
            Invalidate();
            base.OnMouseWheel(args);
        }

        protected override void OnMouseDown(MouseEventArgs args)
        {
            Focus();
            panning = args.Button == MouseButtons.Left && (ModifierKeys & Keys.Space) == Keys.Space;
            previousPoint = args.Location;
            base.OnMouseDown(args);
        }

        protected override void OnMouseMove(MouseEventArgs args)
        {
            if (panning)
            {
                viewport.Pan(args.X - previousPoint.X, args.Y - previousPoint.Y);
                previousPoint = args.Location;
                Invalidate();
            }
            base.OnMouseMove(args);
        }

        protected override void OnMouseUp(MouseEventArgs args)
        {
            panning = false;
            base.OnMouseUp(args);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                CancelImageLoad();
                image?.Dispose();
            }
            base.Dispose(disposing);
        }

        private sealed class CanvasImage
        {
            public CanvasImage(Image preview, Size originalSize)
            {
                Preview = preview;
                OriginalSize = originalSize;
            }

            public Image Preview { get; private set; }
            public Size OriginalSize { get; private set; }
        }
    }
}

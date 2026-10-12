using System;
using System.Drawing;

namespace Neurocle.Application.Projects
{
    public sealed class CanvasViewport
    {
        private const double MinimumZoom = 0.01d;
        private const double MaximumZoom = 4d;
        private bool isFitted;

        public CanvasViewport(Size imageSize, Size viewportSize)
        {
            ImageSize = imageSize;
            ViewportSize = viewportSize;
            FitToWindow();
        }

        public Size ImageSize { get; private set; }
        public Size ViewportSize { get; private set; }
        public double Zoom { get; private set; }
        public PointF Origin { get; private set; }
        public bool IsFitted { get { return isFitted; } }

        public void SetImageSize(Size imageSize)
        {
            ImageSize = imageSize;
            FitToWindow();
        }

        public void SetViewportSize(Size viewportSize)
        {
            ViewportSize = viewportSize;
            if (isFitted) FitToWindow();
            else ClampOrigin();
        }

        public void FitToWindow()
        {
            isFitted = true;
            if (ImageSize.Width <= 0 || ImageSize.Height <= 0 || ViewportSize.Width <= 0 || ViewportSize.Height <= 0)
            {
                Zoom = 1d;
                Origin = PointF.Empty;
                return;
            }

            Zoom = Clamp(Math.Min((double)ViewportSize.Width / ImageSize.Width, (double)ViewportSize.Height / ImageSize.Height));
            Origin = new PointF((float)((ViewportSize.Width - ImageSize.Width * Zoom) / 2d), (float)((ViewportSize.Height - ImageSize.Height * Zoom) / 2d));
        }

        public void SetZoomAt(double zoom, PointF screenPoint)
        {
            isFitted = false;
            PointF imagePoint = ScreenToImage(screenPoint);
            Zoom = Clamp(zoom);
            Origin = new PointF((float)(screenPoint.X - imagePoint.X * Zoom), (float)(screenPoint.Y - imagePoint.Y * Zoom));
            ClampOrigin();
        }

        public void Pan(float horizontal, float vertical)
        {
            isFitted = false;
            Origin = new PointF(Origin.X + horizontal, Origin.Y + vertical);
            ClampOrigin();
        }

        public void SetView(double zoom, PointF origin, bool fitted)
        {
            isFitted = fitted;
            Zoom = Clamp(zoom);
            Origin = origin;
            ClampOrigin();
        }

        public PointF ScreenToImage(PointF screenPoint)
        {
            return new PointF((float)((screenPoint.X - Origin.X) / Zoom), (float)((screenPoint.Y - Origin.Y) / Zoom));
        }

        public PointF ImageToScreen(PointF imagePoint)
        {
            return new PointF((float)(Origin.X + imagePoint.X * Zoom), (float)(Origin.Y + imagePoint.Y * Zoom));
        }

        private static double Clamp(double zoom)
        {
            return Math.Max(MinimumZoom, Math.Min(MaximumZoom, zoom));
        }

        private void ClampOrigin()
        {
            Origin = new PointF(
                ClampOriginAxis(Origin.X, ImageSize.Width * Zoom, ViewportSize.Width),
                ClampOriginAxis(Origin.Y, ImageSize.Height * Zoom, ViewportSize.Height));
        }

        private static float ClampOriginAxis(float origin, double imageLength, int viewportLength)
        {
            if (imageLength <= viewportLength) return (float)((viewportLength - imageLength) / 2d);
            return Math.Max((float)(viewportLength - imageLength), Math.Min(0f, origin));
        }
    }
}

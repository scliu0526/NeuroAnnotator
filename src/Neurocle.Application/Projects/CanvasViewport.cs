using System;
using System.Drawing;

namespace Neurocle.Application.Projects
{
    // 維護原圖座標與畫布螢幕座標之間的縮放、平移及捲動狀態。
    public sealed class CanvasViewport
    {
        // 允許的最小縮放倍率。
        private const double MinimumZoom = 0.01d;
        // 允許的最大縮放倍率。
        private const double MaximumZoom = 4d;
        // 指出目前視角是否由 FitToWindow 自動計算。
        private bool isFitted;

        /// <summary>
        /// 建立視角並立即將原圖置中縮放至可視範圍。
        /// </summary>
        /// <param name="imageSize">原圖像素尺寸。</param>
        /// <param name="viewportSize">畫布可視區域的螢幕尺寸。</param>
        public CanvasViewport(Size imageSize, Size viewportSize)
        {
            ImageSize = imageSize;
            ViewportSize = viewportSize;
            FitToWindow();
        }

        // 目前原圖的像素尺寸。
        public Size ImageSize { get; private set; }
        // 目前畫布可視區域的螢幕尺寸。
        public Size ViewportSize { get; private set; }
        // 原圖像素轉換為螢幕像素所使用的倍率。
        public double Zoom { get; private set; }
        // 原圖左上角在畫布螢幕座標中的位置。
        public PointF Origin { get; private set; }
        // 取得視角是否仍由 FitToWindow 維持。
        public bool IsFitted { get { return isFitted; } }
        // 取得原圖水平超出畫布時是否可捲動。
        public bool CanScrollHorizontally { get { return HorizontalScrollMaximum > 0; } }
        // 取得原圖垂直超出畫布時是否可捲動。
        public bool CanScrollVertically { get { return VerticalScrollMaximum > 0; } }
        // 取得水平捲動位置的最大原圖螢幕像素值。
        public int HorizontalScrollMaximum { get { return GetScrollMaximum(ImageSize.Width * Zoom, ViewportSize.Width); } }
        // 取得垂直捲動位置的最大原圖螢幕像素值。
        public int VerticalScrollMaximum { get { return GetScrollMaximum(ImageSize.Height * Zoom, ViewportSize.Height); } }
        // 取得目前水平捲動位置；不可捲動時為零。
        public int HorizontalScrollPosition { get { return CanScrollHorizontally ? (int)Math.Round(-Origin.X) : 0; } }
        // 取得目前垂直捲動位置；不可捲動時為零。
        public int VerticalScrollPosition { get { return CanScrollVertically ? (int)Math.Round(-Origin.Y) : 0; } }

        /// <summary>
        /// 更新原圖尺寸，並重新以 FitToWindow 設定視角。
        /// </summary>
        /// <param name="imageSize">新的原圖像素尺寸。</param>
        public void SetImageSize(Size imageSize)
        {
            ImageSize = imageSize;
            FitToWindow();
        }

        /// <summary>
        /// 更新畫布尺寸；自動適配時重算縮放，否則限制目前原圖位置。
        /// </summary>
        /// <param name="viewportSize">新的畫布可視區域尺寸。</param>
        public void SetViewportSize(Size viewportSize)
        {
            ViewportSize = viewportSize;
            if (isFitted) FitToWindow();
            else ClampOrigin();
        }

        /// <summary>
        /// 以等比例縮放將整張原圖置中顯示，並恢復自動適配狀態。
        /// </summary>
        public void FitToWindow()
        {
            isFitted = true;
            if (ImageSize.Width <= 0 || ImageSize.Height <= 0 || ViewportSize.Width <= 0 || ViewportSize.Height <= 0)
            {
                Zoom = 1d;
                Origin = PointF.Empty;
                return;
            }

            // 以較小軸向倍率完整容納圖片，並將剩餘空間置中分配。
            Zoom = Clamp(Math.Min((double)ViewportSize.Width / ImageSize.Width, (double)ViewportSize.Height / ImageSize.Height));
            Origin = new PointF((float)((ViewportSize.Width - ImageSize.Width * Zoom) / 2d), (float)((ViewportSize.Height - ImageSize.Height * Zoom) / 2d));
        }

        /// <summary>
        /// 以指定螢幕座標為縮放錨點調整倍率，並限制原圖不超出可視邊界。
        /// </summary>
        /// <param name="zoom">目標縮放倍率，會限制在支援範圍內。</param>
        /// <param name="screenPoint">縮放時保持對應原圖位置不變的畫布座標。</param>
        public void SetZoomAt(double zoom, PointF screenPoint)
        {
            isFitted = false;
            // 先保存游標下的原圖座標，再重算原點，讓縮放錨點保持不動。
            PointF imagePoint = ScreenToImage(screenPoint);
            Zoom = Clamp(zoom);
            Origin = new PointF((float)(screenPoint.X - imagePoint.X * Zoom), (float)(screenPoint.Y - imagePoint.Y * Zoom));
            ClampOrigin();
        }

        /// <summary>
        /// 依螢幕像素位移平移原圖，並限制平移範圍。
        /// </summary>
        /// <param name="horizontal">水平螢幕像素位移。</param>
        /// <param name="vertical">垂直螢幕像素位移。</param>
        public void Pan(float horizontal, float vertical)
        {
            isFitted = false;
            Origin = new PointF(Origin.X + horizontal, Origin.Y + vertical);
            ClampOrigin();
        }

        /// <summary>
        /// 設定水平與垂直捲動位置，超出範圍的值會被限制。
        /// </summary>
        /// <param name="horizontal">水平捲動位置。</param>
        /// <param name="vertical">垂直捲動位置。</param>
        public void SetScrollPosition(int horizontal, int vertical)
        {
            isFitted = false;
            Origin = new PointF(
                -Math.Max(0, Math.Min(horizontal, HorizontalScrollMaximum)),
                -Math.Max(0, Math.Min(vertical, VerticalScrollMaximum)));
            ClampOrigin();
        }

        /// <summary>
        /// 直接設定倍率與原圖位置，並依旗標記錄是否仍處於自動適配狀態。
        /// </summary>
        /// <param name="zoom">目標縮放倍率。</param>
        /// <param name="origin">原圖左上角的畫布座標。</param>
        /// <param name="fitted">視角是否視為自動適配狀態。</param>
        public void SetView(double zoom, PointF origin, bool fitted)
        {
            isFitted = fitted;
            Zoom = Clamp(zoom);
            Origin = origin;
            ClampOrigin();
        }

        /// <summary>
        /// 將畫布螢幕座標轉換為原圖座標。
        /// </summary>
        /// <param name="screenPoint">畫布中的螢幕座標。</param>
        /// <returns>對應的原圖像素座標。</returns>
        public PointF ScreenToImage(PointF screenPoint)
        {
            return new PointF((float)((screenPoint.X - Origin.X) / Zoom), (float)((screenPoint.Y - Origin.Y) / Zoom));
        }

        /// <summary>
        /// 將原圖座標轉換為畫布螢幕座標。
        /// </summary>
        /// <param name="imagePoint">原圖中的像素座標。</param>
        /// <returns>對應的畫布座標。</returns>
        public PointF ImageToScreen(PointF imagePoint)
        {
            return new PointF((float)(Origin.X + imagePoint.X * Zoom), (float)(Origin.Y + imagePoint.Y * Zoom));
        }

        /// <summary>
        /// 將縮放倍率限制在視角支援範圍內。
        /// </summary>
        /// <param name="zoom">畫布縮放倍率。</param>
        /// <returns>回傳限制在指定範圍內的數值。</returns>
        private static double Clamp(double zoom)
        {
            return Math.Max(MinimumZoom, Math.Min(MaximumZoom, zoom));
        }

        /// <summary>
        /// 計算影像超出可視區域時可移動的整數捲動範圍。
        /// </summary>
        /// <param name="imageLength">圖片在該軸向的長度。</param>
        /// <param name="viewportLength">檢視區在該軸向的長度。</param>
        /// <returns>回傳圖片超出檢視區時所需的最大捲動量。</returns>
        private static int GetScrollMaximum(double imageLength, int viewportLength)
        {
            double overflow = imageLength - viewportLength;
            return overflow <= 0d ? 0 : (int)Math.Ceiling(overflow);
        }

        /// <summary>
        /// 限制原圖原點，使影像不會露出空白邊界。
        /// </summary>
        private void ClampOrigin()
        {
            Origin = new PointF(
                ClampOriginAxis(Origin.X, ImageSize.Width * Zoom, ViewportSize.Width),
                ClampOriginAxis(Origin.Y, ImageSize.Height * Zoom, ViewportSize.Height));
        }

        /// <summary>
        /// 限制單一軸向原點；影像較小時置中，較大時限制於可捲動區間。
        /// </summary>
        /// <param name="origin">座標或視埠的原點。</param>
        /// <param name="imageLength">圖片在該軸向的長度。</param>
        /// <param name="viewportLength">檢視區在該軸向的長度。</param>
        /// <returns>回傳限制在可見圖片範圍內的原點座標。</returns>
        private static float ClampOriginAxis(float origin, double imageLength, int viewportLength)
        {
            // 原圖短於視窗時置中；超出時則限制原點落在可捲動區間。
            if (imageLength <= viewportLength) return (float)((viewportLength - imageLength) / 2d);
            return Math.Max((float)(viewportLength - imageLength), Math.Min(0f, origin));
        }
    }
}

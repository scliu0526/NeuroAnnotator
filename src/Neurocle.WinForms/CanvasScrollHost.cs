using System;
using System.Drawing;
using System.Windows.Forms;

namespace Neurocle.WinForms
{
    // 將畫布與依視角同步的水平、垂直捲軸組合成單一控制項。
    internal sealed partial class CanvasScrollHost : UserControl
    {
        // 此容器中的互動畫布。
        private ImageCanvasControl canvas;
        // 用於水平調整原圖視角的捲軸。
        private HScrollBar horizontal;
        // 用於垂直調整原圖視角的捲軸。
        private VScrollBar vertical;
        // 配置畫布及兩個捲軸的設計器版面。
        private TableLayoutPanel layout;
        // 避免更新捲軸值時重入捲動事件。
        private bool updatingScrollBars;

        // 取得水平捲軸，供畫布與測試使用。
        internal HScrollBar HorizontalScrollBar { get { return horizontal; } }
        // 取得垂直捲軸，供畫布與測試使用。
        internal VScrollBar VerticalScrollBar { get { return vertical; } }

        /// <summary>初始化捲軸容器並接線尺寸與捲動事件。</summary>
        public CanvasScrollHost()
        {
            InitializeComponent();
            horizontal.Scroll += (sender, args) => ApplyScrollPosition(args.NewValue, vertical.Value);
            vertical.Scroll += (sender, args) => ApplyScrollPosition(horizontal.Value, args.NewValue);
            Resize += (sender, args) => UpdateScrollBars();
            HandleCreated += (sender, args) => UpdateScrollBars();
        }

        /// <summary>將指定畫布放入容器並依畫布視角初始化捲軸。</summary>
        internal CanvasScrollHost(ImageCanvasControl canvas)
            : this()
        {
            this.canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
            canvas.Dock = DockStyle.Fill;
            canvas.TabIndex = 0;
            canvas.Resize += (sender, args) => UpdateScrollBars();
            canvas.ViewChanged += (sender, args) => UpdateScrollBars();
            layout.Controls.Add(canvas, 0, 0);
            UpdateScrollBars();
        }

        /// <summary>將捲軸位置轉換為畫布原圖座標的捲動位置。</summary>
        private void ApplyScrollPosition(int horizontalPosition, int verticalPosition)
        {
            if (updatingScrollBars || canvas == null) return;
            canvas.SetScrollPosition(horizontalPosition, verticalPosition);
        }

        /// <summary>依畫布視角、尺寸及可捲動範圍同步兩個捲軸。</summary>
        private void UpdateScrollBars()
        {
            if (updatingScrollBars || canvas == null || canvas.IsDisposed) return;
            updatingScrollBars = true;
            try
            {
                PointF origin = canvas.ViewState.Origin;
                Configure(horizontal, canvas.HorizontalScrollMaximum, canvas.ClientSize.Width, -origin.X);
                Configure(vertical, canvas.VerticalScrollMaximum, canvas.ClientSize.Height, -origin.Y);
            }
            finally { updatingScrollBars = false; }
        }

        /// <summary>設定單一捲軸的範圍、步進、位置及可見狀態。</summary>
        private static void Configure(ScrollBar scrollBar, int maximum, int viewportLength, double position)
        {
            scrollBar.Minimum = 0;
            scrollBar.LargeChange = Math.Max(1, viewportLength);
            scrollBar.SmallChange = Math.Max(1, viewportLength / 10);
            scrollBar.Maximum = maximum + scrollBar.LargeChange - 1;
            scrollBar.Value = Math.Max(0, Math.Min(maximum, (int)Math.Round(position)));
            scrollBar.Enabled = maximum > 0;
            scrollBar.Visible = maximum > 0;
        }
    }
}

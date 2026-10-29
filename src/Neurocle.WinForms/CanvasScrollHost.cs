using System;
using System.Drawing;
using System.Windows.Forms;

namespace Neurocle.WinForms
{
    internal sealed partial class CanvasScrollHost : UserControl
    {
        private ImageCanvasControl canvas;
        private HScrollBar horizontal;
        private VScrollBar vertical;
        private TableLayoutPanel layout;
        private bool updatingScrollBars;

        internal HScrollBar HorizontalScrollBar { get { return horizontal; } }
        internal VScrollBar VerticalScrollBar { get { return vertical; } }

        public CanvasScrollHost()
        {
            InitializeComponent();
            horizontal.Scroll += (sender, args) => ApplyScrollPosition(args.NewValue, vertical.Value);
            vertical.Scroll += (sender, args) => ApplyScrollPosition(horizontal.Value, args.NewValue);
            Resize += (sender, args) => UpdateScrollBars();
            HandleCreated += (sender, args) => UpdateScrollBars();
        }

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

        private void ApplyScrollPosition(int horizontalPosition, int verticalPosition)
        {
            if (updatingScrollBars || canvas == null) return;
            canvas.SetScrollPosition(horizontalPosition, verticalPosition);
        }

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

using System;
using System.Drawing;
using System.Windows.Forms;

namespace Neurocle.WinForms
{
    internal sealed class CanvasScrollHost : UserControl
    {
        private readonly ImageCanvasControl canvas;
        private readonly HScrollBar horizontal;
        private readonly VScrollBar vertical;
        private bool updatingScrollBars;

        internal HScrollBar HorizontalScrollBar { get { return horizontal; } }
        internal VScrollBar VerticalScrollBar { get { return vertical; } }

        internal CanvasScrollHost(ImageCanvasControl canvas)
        {
            this.canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
            Dock = DockStyle.Fill;
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 2,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, SystemInformation.VerticalScrollBarWidth));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, SystemInformation.HorizontalScrollBarHeight));
            horizontal = new HScrollBar { Dock = DockStyle.Fill, Name = "horizontalCanvasScroll" };
            vertical = new VScrollBar { Dock = DockStyle.Fill, Name = "verticalCanvasScroll" };
            horizontal.Scroll += (sender, args) => ApplyScrollPosition(args.NewValue, vertical.Value);
            vertical.Scroll += (sender, args) => ApplyScrollPosition(horizontal.Value, args.NewValue);
            canvas.Dock = DockStyle.Fill;
            canvas.Resize += (sender, args) => UpdateScrollBars();
            canvas.ViewChanged += (sender, args) => UpdateScrollBars();
            layout.Controls.Add(canvas, 0, 0);
            layout.Controls.Add(horizontal, 0, 1);
            layout.Controls.Add(vertical, 1, 0);
            Controls.Add(layout);
            Resize += (sender, args) => UpdateScrollBars();
            HandleCreated += (sender, args) => UpdateScrollBars();
        }

        private void ApplyScrollPosition(int horizontalPosition, int verticalPosition)
        {
            if (updatingScrollBars) return;
            canvas.SetScrollPosition(horizontalPosition, verticalPosition);
        }

        private void UpdateScrollBars()
        {
            if (updatingScrollBars || canvas.IsDisposed) return;
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

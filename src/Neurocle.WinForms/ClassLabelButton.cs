using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Neurocle.WinForms
{
    /// <summary>以圓角色塊與右側名稱呈現標註類別的可點擊按鈕。</summary>
    internal sealed class ClassLabelButton : Button
    {
        public Color ClassColor { get; set; }
        public bool IsActive { get; set; }

        protected override void OnPaint(PaintEventArgs args)
        {
            Graphics graphics = args.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle bounds = new Rectangle(1, 1, Width - 2, Height - 2);
            using (var background = new SolidBrush(IsActive ? Color.FromArgb(32, SystemColors.Highlight) : BackColor)) graphics.FillRectangle(background, bounds);
            if (IsActive)
            {
                using (var accent = new SolidBrush(SystemColors.Highlight)) graphics.FillRectangle(accent, 0, 0, 3, Height);
            }
            Rectangle colorBounds = new Rectangle(10, 7, 28, Math.Max(8, Height - 14));
            using (var path = RoundedRectangle(colorBounds, 6))
            using (var fill = new SolidBrush(ClassColor)) graphics.FillPath(fill, path);
            TextRenderer.DrawText(graphics, Text, Font, new Rectangle(48, 0, Math.Max(0, Width - 56), Height), ForeColor, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            using (var pen = new Pen(IsActive ? SystemColors.Highlight : SystemColors.ControlDark, IsActive ? 2f : 1f)) graphics.DrawRectangle(pen, bounds);
        }

        private static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
        {
            int diameter = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}

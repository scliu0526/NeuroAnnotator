namespace Neurocle.WinForms
{
    partial class CanvasScrollHost
    {
        private void InitializeComponent()
        {
            this.layout = new System.Windows.Forms.TableLayoutPanel();
            this.horizontal = new System.Windows.Forms.HScrollBar();
            this.vertical = new System.Windows.Forms.VScrollBar();
            this.layout.SuspendLayout();
            this.SuspendLayout();
            //
            // layout
            //
            this.layout.ColumnCount = 2;
            this.layout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.layout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, System.Windows.Forms.SystemInformation.VerticalScrollBarWidth));
            this.layout.RowCount = 2;
            this.layout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.layout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, System.Windows.Forms.SystemInformation.HorizontalScrollBarHeight));
            this.layout.Controls.Add(this.horizontal, 0, 1);
            this.layout.Controls.Add(this.vertical, 1, 0);
            this.layout.Dock = System.Windows.Forms.DockStyle.Fill;
            this.layout.Margin = System.Windows.Forms.Padding.Empty;
            this.layout.Name = "layout";
            this.layout.Padding = System.Windows.Forms.Padding.Empty;
            this.layout.TabIndex = 0;
            //
            // horizontal
            //
            this.horizontal.Dock = System.Windows.Forms.DockStyle.Fill;
            this.horizontal.Name = "horizontalCanvasScroll";
            this.horizontal.TabIndex = 1;
            //
            // vertical
            //
            this.vertical.Dock = System.Windows.Forms.DockStyle.Fill;
            this.vertical.Name = "verticalCanvasScroll";
            this.vertical.TabIndex = 2;
            //
            // CanvasScrollHost
            //
            this.Controls.Add(this.layout);
            this.Dock = System.Windows.Forms.DockStyle.Fill;
            this.Name = "CanvasScrollHost";
            this.layout.ResumeLayout(false);
            this.ResumeLayout(false);
        }
    }
}

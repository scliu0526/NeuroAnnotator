namespace Neurocle.WinForms
{
    partial class KeyboardShortcutsForm
    {
        private System.ComponentModel.IContainer components = null;
        private System.Windows.Forms.ListView shortcutsList;

        protected override void Dispose(bool disposing)
        {
            if (disposing && components != null) components.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.shortcutsList = new System.Windows.Forms.ListView();
            this.SuspendLayout();
            //
            // Shortcuts
            //
            this.shortcutsList.Dock = System.Windows.Forms.DockStyle.Fill;
            this.shortcutsList.FullRowSelect = true;
            this.shortcutsList.GridLines = true;
            this.shortcutsList.HideSelection = false;
            this.shortcutsList.Name = "Shortcuts";
            this.shortcutsList.TabIndex = 0;
            this.shortcutsList.UseCompatibleStateImageBehavior = false;
            this.shortcutsList.View = System.Windows.Forms.View.Details;
            this.shortcutsList.Columns.Add("按鍵", 180);
            this.shortcutsList.Columns.Add("功能", 250);
            this.shortcutsList.Columns.Add("適用範圍與條件", 350);
            //
            // KeyboardShortcutsForm
            //
            this.Controls.Add(this.shortcutsList);
            this.MinimumSize = new System.Drawing.Size(680, 360);
            this.Name = "KeyboardShortcutsForm";
            this.ShowInTaskbar = false;
            this.Size = new System.Drawing.Size(820, 460);
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "快捷鍵";
            this.ResumeLayout(false);
        }
    }
}

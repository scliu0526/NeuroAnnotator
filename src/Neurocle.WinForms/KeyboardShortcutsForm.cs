using System.Drawing;
using System.Windows.Forms;

namespace Neurocle.WinForms
{
    internal sealed class KeyboardShortcutsForm : Form
    {
        internal ListView Shortcuts { get; private set; }

        internal KeyboardShortcutsForm()
        {
            Text = "快捷鍵";
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(680, 360);
            Size = new Size(820, 460);
            ShowInTaskbar = false;

            Shortcuts = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                HideSelection = false
            };
            Shortcuts.Columns.Add("按鍵", 180);
            Shortcuts.Columns.Add("功能", 250);
            Shortcuts.Columns.Add("適用範圍與條件", 350);
            Add("Ctrl+Z", "復原", "目前標註集；文字輸入時由文字欄位處理");
            Add("Ctrl+Shift+Z", "重做", "目前標註集；文字輸入時由文字欄位處理");
            Add("Ctrl+Y", "重做", "目前標註集；文字輸入時由文字欄位處理");
            Add("Ctrl+C", "複製選取的標註", "標註頁的 OBD／SEG 選取模式；文字輸入時保留文字複製");
            Add("Ctrl+V", "貼上標註到已選圖片", "標註頁；複製快照須屬於目前 OBD／SEG 標註集；套用到目前選取的一張或多張圖片；文字輸入時保留文字貼上");
            Add("Ctrl+A", "全選標註", "標註頁的 OBD／SEG 標註集；不限制目前工具；文字輸入時不觸發");
            Add("F", "適合視窗", "圖片頁與標註頁；文字輸入時不觸發，並取消未完成手勢");
            Add("[", "顯示／隱藏標註", "標註頁；文字輸入時不觸發");
            Add("Z", "減少筆刷寬度 1", "SEG 標註頁；所有 SEG 工具；範圍 1–100；文字輸入時不觸發");
            Add("X", "增加筆刷寬度 1", "SEG 標註頁；所有 SEG 工具；範圍 1–100；文字輸入時不觸發");
            Add("↑／↓", "切換上一張／下一張圖片", "圖片頁與標註頁；文字輸入時不觸發");
            Add("Delete", "刪除選取的標註", "標註畫布取得焦點且有已選取的 OBD／SEG 標註時可用；不限制目前工具");
            Add("Esc", "取消未完成的標註", "標註畫布");
            Add("滑鼠右鍵拖曳", "平移畫布", "圖片頁與標註頁；位置受圖片邊界限制");
            Add("Space+滑鼠左鍵拖曳", "平移畫布", "圖片頁與標註頁；位置受圖片邊界限制");
            Add("Ctrl+滑鼠滾輪", "縮放畫布", "圖片頁與標註頁；以滑鼠位置為縮放中心");
            Add("Shift+滑鼠滾輪", "水平平移畫布", "圖片頁與標註頁");
            Add("滑鼠滾輪", "垂直平移畫布", "圖片頁與標註頁");
            Controls.Add(Shortcuts);
        }

        private void Add(string key, string action, string scope)
        {
            var row = new ListViewItem(key);
            row.SubItems.Add(action);
            row.SubItems.Add(scope);
            Shortcuts.Items.Add(row);
        }
    }
}

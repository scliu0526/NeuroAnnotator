using System;
using System.Linq;
using System.Windows.Forms;
using Neurocle.Application.Projects;
using Neurocle.WinForms.Resources;

namespace Neurocle.WinForms
{
    // 顯示缺失圖片並提供重新連結或永久移除圖片資料的操作。
    internal sealed partial class MissingImagesForm : Form
    {
        // 提供缺失圖片查詢、內容驗證及刪除功能。
        private IProjectSession project;

        /// <summary>
        /// 初始化缺失圖片表單的設計器元件。
        /// </summary>
        internal MissingImagesForm()
        {
            InitializeComponent();
        }

        /// <summary>
        /// 設定目前專案並載入缺失圖片清單。
        /// </summary>
        public MissingImagesForm(IProjectSession project) : this()
        {
            this.project = project ?? throw new ArgumentNullException(nameof(project));
            RefreshImages();
        }

        /// <summary>
        /// 重新查詢缺失圖片並更新清單按鈕狀態。
        /// </summary>
        private void RefreshImages()
        {
            images.DataSource = project.GetMissingImages().ToList();
            UpdateButtons();
        }

        /// <summary>
        /// 依圖片選取狀態啟用或停用重新連結及刪除按鈕。
        /// </summary>
        private void UpdateButtons()
        {
            bool hasSelection = images.SelectedItem != null;
            relink.Enabled = hasSelection;
            delete.Enabled = hasSelection;
        }

        /// <summary>
        /// 選擇來源圖片並依專案驗證結果重新連結缺失檔案。
        /// </summary>
        private void RelinkSelectedImage()
        {
            ProjectImage image = images.SelectedItem as ProjectImage;
            if (image == null) return;
            using (var dialog = new OpenFileDialog { Filter = Strings.ImageFileFilter, Multiselect = false })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    project.RelinkImage(image.Id, dialog.FileName);
                    RefreshImages();
                }
                catch (ImageRelinkException exception)
                {
                    MessageBox.Show(this, GetErrorMessage(exception), Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                catch (Exception)
                {
                    MessageBox.Show(this, Strings.ProjectOperationFailed, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        /// <summary>
        /// 確認操作後永久刪除所選缺失圖片及其專案資料。
        /// </summary>
        private void DeleteSelectedImage()
        {
            ProjectImage image = images.SelectedItem as ProjectImage;
            if (image == null) return;
            string message = string.Format(Strings.DeleteMissingImagePrompt, image.OriginalFileName);
            if (MessageBox.Show(this, message, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            try
            {
                project.DeleteImages(new[] { image.Id });
                RefreshImages();
            }
            catch (Exception)
            {
                MessageBox.Show(this, Strings.ProjectOperationFailed, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// 選取清單項目變更時同步更新按鈕狀態。
        /// </summary>
        /// <param name="sender">觸發此事件的控制項或視窗。</param>
        /// <param name="args">包含此次事件詳細資料的事件資料。</param>
        private void Images_SelectedIndexChanged(object sender, EventArgs args)
        {
            UpdateButtons();
        }

        /// <summary>
        /// 處理重新連結按鈕事件。
        /// </summary>
        /// <param name="sender">觸發此事件的控制項或視窗。</param>
        /// <param name="args">包含此次事件詳細資料的事件資料。</param>
        private void Relink_Click(object sender, EventArgs args)
        {
            RelinkSelectedImage();
        }

        /// <summary>
        /// 處理刪除按鈕事件。
        /// </summary>
        /// <param name="sender">觸發此事件的控制項或視窗。</param>
        /// <param name="args">包含此次事件詳細資料的事件資料。</param>
        private void Delete_Click(object sender, EventArgs args)
        {
            DeleteSelectedImage();
        }

        /// <summary>
        /// 依重新連結失敗類型選取對應的使用者訊息。
        /// </summary>
        /// <param name="exception">發生的例外資訊。</param>
        /// <returns>轉換或取得的文字結果。</returns>
        private static string GetErrorMessage(ImageRelinkException exception)
        {
            switch (exception.Failure)
            {
                case ImageRelinkFailure.SourceNotFound: return Strings.RelinkSourceNotFound;
                case ImageRelinkFailure.ContentMismatch: return Strings.RelinkContentMismatch;
                default: return Strings.ProjectOperationFailed;
            }
        }
    }
}

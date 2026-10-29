using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Neurocle.Application.Projects;
using Neurocle.WinForms.Resources;
using AnnotationSetKind = Neurocle.Application.Projects.AnnotationSetType;

namespace Neurocle.WinForms
{
    // 收集標註集名稱與標註類型，供建立流程使用。
    internal sealed partial class AnnotationSetCreationDialog : Form
    {
        // 保存對話框載入的嵌入式類型圖示，以便關閉時釋放。
        private readonly List<Image> typeIcons = new List<Image>();
        // 保存使用者目前選取的標註類型。
        private AnnotationSetKind? selectedType;

        /// <summary>初始化標註類型卡片、文字及互動事件。</summary>
        internal AnnotationSetCreationDialog()
        {
            InitializeComponent();
            Text = Strings.NewAnnotationSet;
            titleLabel.Text = Strings.NewAnnotationSet;
            subtitleLabel.Text = Strings.AnnotationSetCreateSubtitle;
            typeLabel.Text = Strings.AnnotationSetType;
            nameLabel.Text = Strings.AnnotationSetNamePrompt;
            claCard.Text = "CLA\r\n" + Strings.AnnotationSetTypeCla + "\r\n" + Strings.AnnotationSetTypeClaDescription;
            obdCard.Text = "OBD\r\n" + Strings.AnnotationSetTypeObd + "\r\n" + Strings.AnnotationSetTypeObdDescription;
            segCard.Text = "SEG\r\n" + Strings.AnnotationSetTypeSeg + "\r\n" + Strings.AnnotationSetTypeSegDescription;
            createButton.Text = Strings.CreateAnnotationSet;
            cancelButton.Text = Strings.Cancel;
            createButton.Enabled = false;
            claCard.Image = LoadTypeIcon("CLA.png");
            obdCard.Image = LoadTypeIcon("OBD.png");
            segCard.Image = LoadTypeIcon("SEG.png");
            claCard.Click += (sender, args) => SelectType(AnnotationSetKind.Cla);
            obdCard.Click += (sender, args) => SelectType(AnnotationSetKind.Obd);
            segCard.Click += (sender, args) => SelectType(AnnotationSetKind.Seg);
            name.TextChanged += (sender, args) => UpdateCreateButton();
        }

        // 取得使用者選取的標註類型；尚未選取時為 null。
        internal AnnotationSetKind? AnnotationSetType { get { return selectedType; } }
        // 取得名稱欄位目前的文字。
        internal string AnnotationSetName { get { return name.Text; } }

        /// <summary>從嵌入式資源載入類型圖示並記錄其生命週期。</summary>
        private Image LoadTypeIcon(string resourceName)
        {
            string manifestName = "Neurocle.WinForms.model_type." + resourceName;
            using (Stream stream = typeof(AnnotationSetCreationDialog).Assembly.GetManifestResourceStream(manifestName))
            {
                if (stream == null) throw new InvalidOperationException("Missing embedded model type icon: " + resourceName);
                using (Image source = Image.FromStream(stream))
                {
                    Image icon = new Bitmap(source, new Size(48, 48));
                    typeIcons.Add(icon);
                    return icon;
                }
            }
        }

        /// <summary>更新目前選取的類型卡片並重新檢查建立按鈕狀態。</summary>
        private void SelectType(AnnotationSetKind type)
        {
            selectedType = type;
            UpdateCard(claCard, type == AnnotationSetKind.Cla);
            UpdateCard(obdCard, type == AnnotationSetKind.Obd);
            UpdateCard(segCard, type == AnnotationSetKind.Seg);
            UpdateCreateButton();
        }

        /// <summary>依選取狀態更新單一卡片的底色與邊框。</summary>
        private void UpdateCard(Button card, bool selected)
        {
            card.BackColor = selected ? SystemColors.GradientActiveCaption : SystemColors.Window;
            card.FlatAppearance.BorderColor = selected ? SystemColors.Highlight : SystemColors.ControlDark;
            card.FlatAppearance.BorderSize = selected ? 2 : 1;
        }

        /// <summary>僅在已選取類型且名稱非空白時啟用建立按鈕。</summary>
        private void UpdateCreateButton()
        {
            createButton.Enabled = selectedType.HasValue && !string.IsNullOrWhiteSpace(name.Text);
        }

        /// <summary>釋放表單元件及自行載入的圖示影像。</summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                components?.Dispose();
                foreach (Image icon in typeIcons) icon.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}

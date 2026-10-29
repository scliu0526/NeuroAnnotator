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
    internal sealed partial class AnnotationSetCreationDialog : Form
    {
        private readonly List<Image> typeIcons = new List<Image>();
        private AnnotationSetKind? selectedType;

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

        internal AnnotationSetKind? AnnotationSetType { get { return selectedType; } }
        internal string AnnotationSetName { get { return name.Text; } }

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

        private void SelectType(AnnotationSetKind type)
        {
            selectedType = type;
            UpdateCard(claCard, type == AnnotationSetKind.Cla);
            UpdateCard(obdCard, type == AnnotationSetKind.Obd);
            UpdateCard(segCard, type == AnnotationSetKind.Seg);
            UpdateCreateButton();
        }

        private void UpdateCard(Button card, bool selected)
        {
            card.BackColor = selected ? SystemColors.GradientActiveCaption : SystemColors.Window;
            card.FlatAppearance.BorderColor = selected ? SystemColors.Highlight : SystemColors.ControlDark;
            card.FlatAppearance.BorderSize = selected ? 2 : 1;
        }

        private void UpdateCreateButton()
        {
            createButton.Enabled = selectedType.HasValue && !string.IsNullOrWhiteSpace(name.Text);
        }

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

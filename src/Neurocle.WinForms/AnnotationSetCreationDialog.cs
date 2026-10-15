using System;
using System.Windows.Forms;
using Neurocle.Application.Projects;
using Neurocle.WinForms.Resources;

namespace Neurocle.WinForms
{
    internal sealed class AnnotationSetCreationDialog : Form
    {
        private readonly ComboBox type;
        private readonly TextBox name;

        public AnnotationSetCreationDialog()
        {
            Text = Strings.NewAnnotationSet;
            Width = 320;
            Height = 165;
            StartPosition = FormStartPosition.CenterParent;
            type = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList };
            type.Items.AddRange(new object[] { AnnotationSetType.Cla, AnnotationSetType.Obd, AnnotationSetType.Seg });
            type.SelectedIndex = 0;
            name = new TextBox { Dock = DockStyle.Top, AccessibleName = Strings.AnnotationSetNamePrompt };
            var cancel = new Button { Text = Strings.Cancel, Dock = DockStyle.Bottom, DialogResult = DialogResult.Cancel };
            var ok = new Button { Text = Strings.Ok, Dock = DockStyle.Bottom, DialogResult = DialogResult.OK };
            Controls.Add(name);
            Controls.Add(type);
            Controls.Add(new Label { Text = Strings.AnnotationSetType, Dock = DockStyle.Top });
            Controls.Add(cancel);
            Controls.Add(ok);
            AcceptButton = ok;
            CancelButton = cancel;
        }

        internal AnnotationSetType AnnotationSetType { get { return (AnnotationSetType)type.SelectedItem; } }
        internal string AnnotationSetName { get { return name.Text; } }
    }
}

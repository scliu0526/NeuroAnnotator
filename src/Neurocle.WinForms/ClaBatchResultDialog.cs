using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Neurocle.WinForms.Resources;

namespace Neurocle.WinForms
{
    internal sealed class ClaBatchResultDialog : Form
    {
        internal ClaBatchResultDialog(int succeededCount, IReadOnlyList<string> failures)
        {
            Text = Strings.ClaBatchAssignmentResultTitle;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MinimizeBox = false;
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(640, 420);
            MinimumSize = new Size(460, 300);

            var layout = new TableLayoutPanel
            {
                ColumnCount = 1,
                RowCount = 4,
                Dock = DockStyle.Fill,
                Padding = new Padding(12)
            };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42f));

            var summary = new Label
            {
                Name = "claBatchSummary",
                AutoSize = true,
                Text = string.Format(Strings.ClaBatchAssignmentResultSummary, succeededCount, failures.Count),
                Margin = new Padding(3, 3, 3, 10)
            };
            var failuresLabel = new Label
            {
                AutoSize = true,
                Text = Strings.ClaBatchAssignmentFailures,
                Margin = new Padding(3, 3, 3, 4)
            };
            var failureDetails = new TextBox
            {
                Name = "failureDetails",
                ReadOnly = true,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                WordWrap = true,
                Dock = DockStyle.Fill,
                Text = string.Join(System.Environment.NewLine, failures)
            };
            var actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Margin = new Padding(0, 8, 0, 0)
            };
            var close = new Button { Name = "close", Text = Strings.Close, AutoSize = true, DialogResult = DialogResult.OK };
            var copyFailures = new Button { Name = "copyFailures", Text = Strings.CopyFailures, AutoSize = true };
            copyFailures.Click += (sender, args) => Clipboard.SetText(failureDetails.Text);
            actions.Controls.Add(close);
            actions.Controls.Add(copyFailures);

            layout.Controls.Add(summary, 0, 0);
            layout.Controls.Add(failuresLabel, 0, 1);
            layout.Controls.Add(failureDetails, 0, 2);
            layout.Controls.Add(actions, 0, 3);
            Controls.Add(layout);
            AcceptButton = close;
            CancelButton = close;
        }
    }
}

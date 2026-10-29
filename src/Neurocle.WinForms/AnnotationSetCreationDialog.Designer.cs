namespace Neurocle.WinForms
{
    partial class AnnotationSetCreationDialog
    {
        private System.ComponentModel.IContainer components = null;
        private System.Windows.Forms.TableLayoutPanel rootLayout;
        private System.Windows.Forms.Panel headerPanel;
        private System.Windows.Forms.Label titleLabel;
        private System.Windows.Forms.Label subtitleLabel;
        private System.Windows.Forms.TableLayoutPanel contentLayout;
        private System.Windows.Forms.Panel typePanel;
        private System.Windows.Forms.Label typeLabel;
        private System.Windows.Forms.TableLayoutPanel cardLayout;
        private System.Windows.Forms.Button claCard;
        private System.Windows.Forms.Button obdCard;
        private System.Windows.Forms.Button segCard;
        private System.Windows.Forms.Panel namePanel;
        private System.Windows.Forms.Label nameLabel;
        private System.Windows.Forms.TextBox name;
        private System.Windows.Forms.FlowLayoutPanel footerPanel;
        private System.Windows.Forms.Button cancelButton;
        private System.Windows.Forms.Button createButton;

        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.rootLayout = new System.Windows.Forms.TableLayoutPanel();
            this.headerPanel = new System.Windows.Forms.Panel();
            this.titleLabel = new System.Windows.Forms.Label();
            this.subtitleLabel = new System.Windows.Forms.Label();
            this.contentLayout = new System.Windows.Forms.TableLayoutPanel();
            this.typePanel = new System.Windows.Forms.Panel();
            this.typeLabel = new System.Windows.Forms.Label();
            this.cardLayout = new System.Windows.Forms.TableLayoutPanel();
            this.claCard = new System.Windows.Forms.Button();
            this.obdCard = new System.Windows.Forms.Button();
            this.segCard = new System.Windows.Forms.Button();
            this.namePanel = new System.Windows.Forms.Panel();
            this.nameLabel = new System.Windows.Forms.Label();
            this.name = new System.Windows.Forms.TextBox();
            this.footerPanel = new System.Windows.Forms.FlowLayoutPanel();
            this.cancelButton = new System.Windows.Forms.Button();
            this.createButton = new System.Windows.Forms.Button();
            this.rootLayout.SuspendLayout();
            this.headerPanel.SuspendLayout();
            this.contentLayout.SuspendLayout();
            this.typePanel.SuspendLayout();
            this.cardLayout.SuspendLayout();
            this.namePanel.SuspendLayout();
            this.footerPanel.SuspendLayout();
            this.SuspendLayout();
            //
            // rootLayout
            //
            this.rootLayout.ColumnCount = 1;
            this.rootLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.rootLayout.Controls.Add(this.headerPanel, 0, 0);
            this.rootLayout.Controls.Add(this.contentLayout, 0, 1);
            this.rootLayout.Controls.Add(this.footerPanel, 0, 2);
            this.rootLayout.Dock = System.Windows.Forms.DockStyle.Fill;
            this.rootLayout.Padding = new System.Windows.Forms.Padding(18, 12, 18, 10);
            this.rootLayout.RowCount = 3;
            this.rootLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 62F));
            this.rootLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.rootLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 48F));
            //
            // headerPanel
            //
            this.headerPanel.Controls.Add(this.subtitleLabel);
            this.headerPanel.Controls.Add(this.titleLabel);
            this.headerPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            //
            // titleLabel
            //
            this.titleLabel.AutoSize = true;
            this.titleLabel.Font = new System.Drawing.Font("Microsoft JhengHei UI", 14F, System.Drawing.FontStyle.Bold);
            this.titleLabel.Location = new System.Drawing.Point(0, 0);
            this.titleLabel.Name = "titleLabel";
            this.titleLabel.Size = new System.Drawing.Size(108, 24);
            this.titleLabel.TabIndex = 0;
            this.titleLabel.Text = "新增標註集";
            //
            // subtitleLabel
            //
            this.subtitleLabel.AutoSize = true;
            this.subtitleLabel.ForeColor = System.Drawing.SystemColors.GrayText;
            this.subtitleLabel.Location = new System.Drawing.Point(2, 32);
            this.subtitleLabel.Name = "subtitleLabel";
            this.subtitleLabel.Size = new System.Drawing.Size(176, 15);
            this.subtitleLabel.TabIndex = 1;
            this.subtitleLabel.Text = "選擇標註類型並輸入名稱。";
            //
            // contentLayout
            //
            this.contentLayout.ColumnCount = 2;
            this.contentLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 66F));
            this.contentLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 34F));
            this.contentLayout.Controls.Add(this.typePanel, 0, 0);
            this.contentLayout.Controls.Add(this.namePanel, 1, 0);
            this.contentLayout.Dock = System.Windows.Forms.DockStyle.Fill;
            this.contentLayout.RowCount = 1;
            this.contentLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            //
            // typePanel
            //
            this.typePanel.Controls.Add(this.cardLayout);
            this.typePanel.Controls.Add(this.typeLabel);
            this.typePanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.typePanel.Padding = new System.Windows.Forms.Padding(0, 0, 12, 0);
            //
            // typeLabel
            //
            this.typeLabel.AutoSize = true;
            this.typeLabel.Dock = System.Windows.Forms.DockStyle.Top;
            this.typeLabel.Font = new System.Drawing.Font("Microsoft JhengHei UI", 9F, System.Drawing.FontStyle.Bold);
            this.typeLabel.Location = new System.Drawing.Point(0, 0);
            this.typeLabel.Name = "typeLabel";
            this.typeLabel.Padding = new System.Windows.Forms.Padding(0, 0, 0, 8);
            this.typeLabel.Size = new System.Drawing.Size(43, 23);
            this.typeLabel.TabIndex = 0;
            this.typeLabel.Text = "標註類型";
            //
            // cardLayout
            //
            this.cardLayout.ColumnCount = 2;
            this.cardLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 50F));
            this.cardLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 50F));
            this.cardLayout.Controls.Add(this.claCard, 0, 0);
            this.cardLayout.Controls.Add(this.obdCard, 1, 0);
            this.cardLayout.Controls.Add(this.segCard, 0, 1);
            this.cardLayout.Dock = System.Windows.Forms.DockStyle.Fill;
            this.cardLayout.Padding = new System.Windows.Forms.Padding(0, 8, 0, 0);
            this.cardLayout.RowCount = 2;
            this.cardLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 50F));
            this.cardLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 50F));
            //
            // claCard
            //
            this.claCard.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.claCard.Dock = System.Windows.Forms.DockStyle.Fill;
            this.claCard.ImageAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.claCard.Margin = new System.Windows.Forms.Padding(4);
            this.claCard.Name = "claCard";
            this.claCard.Padding = new System.Windows.Forms.Padding(12);
            this.claCard.TabIndex = 0;
            this.claCard.Text = "CLA\r\n影像分類\r\n為整張影像指定類別";
            this.claCard.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.claCard.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageBeforeText;
            this.claCard.UseVisualStyleBackColor = false;
            //
            // obdCard
            //
            this.obdCard.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.obdCard.Dock = System.Windows.Forms.DockStyle.Fill;
            this.obdCard.ImageAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.obdCard.Margin = new System.Windows.Forms.Padding(4);
            this.obdCard.Name = "obdCard";
            this.obdCard.Padding = new System.Windows.Forms.Padding(12);
            this.obdCard.TabIndex = 1;
            this.obdCard.Text = "OBD\r\n物件偵測\r\n標記影像中的物件位置";
            this.obdCard.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.obdCard.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageBeforeText;
            this.obdCard.UseVisualStyleBackColor = false;
            //
            // segCard
            //
            this.segCard.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.segCard.Dock = System.Windows.Forms.DockStyle.Fill;
            this.segCard.ImageAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.segCard.Margin = new System.Windows.Forms.Padding(4);
            this.segCard.Name = "segCard";
            this.segCard.Padding = new System.Windows.Forms.Padding(12);
            this.segCard.TabIndex = 2;
            this.segCard.Text = "SEG\r\n影像分割\r\n標記影像中的像素區域";
            this.segCard.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.segCard.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageBeforeText;
            this.segCard.UseVisualStyleBackColor = false;
            //
            // namePanel
            //
            this.namePanel.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.namePanel.Controls.Add(this.name);
            this.namePanel.Controls.Add(this.nameLabel);
            this.namePanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.namePanel.Padding = new System.Windows.Forms.Padding(18);
            //
            // nameLabel
            //
            this.nameLabel.AutoSize = true;
            this.nameLabel.Dock = System.Windows.Forms.DockStyle.Top;
            this.nameLabel.Font = new System.Drawing.Font("Microsoft JhengHei UI", 9F, System.Drawing.FontStyle.Bold);
            this.nameLabel.Name = "nameLabel";
            this.nameLabel.Padding = new System.Windows.Forms.Padding(0, 0, 0, 8);
            this.nameLabel.TabIndex = 0;
            this.nameLabel.Text = "標註集名稱";
            //
            // name
            //
            this.name.AccessibleName = "標註集名稱";
            this.name.Dock = System.Windows.Forms.DockStyle.Top;
            this.name.Name = "name";
            this.name.TabIndex = 1;
            //
            // footerPanel
            //
            this.footerPanel.Controls.Add(this.createButton);
            this.footerPanel.Controls.Add(this.cancelButton);
            this.footerPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.footerPanel.FlowDirection = System.Windows.Forms.FlowDirection.RightToLeft;
            this.footerPanel.WrapContents = false;
            this.footerPanel.Padding = new System.Windows.Forms.Padding(0, 4, 0, 0);
            //
            // cancelButton
            //
            this.cancelButton.AutoSize = true;
            this.cancelButton.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.cancelButton.Margin = new System.Windows.Forms.Padding(4);
            this.cancelButton.Name = "cancelButton";
            this.cancelButton.TabIndex = 0;
            this.cancelButton.Text = "取消";
            this.cancelButton.UseVisualStyleBackColor = true;
            //
            // createButton
            //
            this.createButton.AutoSize = true;
            this.createButton.DialogResult = System.Windows.Forms.DialogResult.OK;
            this.createButton.Margin = new System.Windows.Forms.Padding(4);
            this.createButton.Name = "createButton";
            this.createButton.TabIndex = 1;
            this.createButton.Text = "建立";
            this.createButton.UseVisualStyleBackColor = true;
            //
            // AnnotationSetCreationDialog
            //
            this.AcceptButton = this.createButton;
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.CancelButton = this.cancelButton;
            this.ClientSize = new System.Drawing.Size(940, 470);
            this.Controls.Add(this.rootLayout);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "AnnotationSetCreationDialog";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "新增標註集";
            this.rootLayout.ResumeLayout(false);
            this.headerPanel.ResumeLayout(false);
            this.headerPanel.PerformLayout();
            this.contentLayout.ResumeLayout(false);
            this.typePanel.ResumeLayout(false);
            this.typePanel.PerformLayout();
            this.cardLayout.ResumeLayout(false);
            this.namePanel.ResumeLayout(false);
            this.namePanel.PerformLayout();
            this.footerPanel.ResumeLayout(false);
            this.ResumeLayout(false);
        }

    }
}

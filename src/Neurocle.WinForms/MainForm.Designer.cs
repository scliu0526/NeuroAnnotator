using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using Neurocle.WinForms.Resources;

namespace Neurocle.WinForms
{
    partial class MainForm
    {
        private IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (this.components != null) this.components.Dispose();
                foreach (Image icon in this.annotationToolIcons) icon.Dispose();
                this.annotationToolIcons.Clear();
            }
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.components = new Container();
            this.menu = new MenuStrip();
            this.projectMenu = new ToolStripMenuItem(Strings.ProjectMenu);
            this.helpMenu = new ToolStripMenuItem("說明");
            this.newProjectMenuItem = new ToolStripMenuItem(Strings.NewProject);
            this.openProjectMenuItem = new ToolStripMenuItem(Strings.OpenProject);
            this.saveProjectMenuItem = new ToolStripMenuItem(Strings.SaveProject);
            this.saveProjectAsMenuItem = new ToolStripMenuItem(Strings.SaveProjectAs);
            this.importImagesMenuItem = new ToolStripMenuItem(Strings.ImportImages);
            this.importImagesMenuItem.Enabled = false;
            this.importFolderMenuItem = new ToolStripMenuItem(Strings.ImportFolder);
            this.importAnnotationMenuItem = new ToolStripMenuItem(Strings.ImportAnnotationJson);
            this.missingImagesMenuItem = new ToolStripMenuItem(Strings.ManageMissingImages);
            this.backupsMenuItem = new ToolStripMenuItem(Strings.ManageBackups);
            this.diagnosticsMenuItem = new ToolStripMenuItem(Strings.Diagnostics);
            this.shortcutsMenuItem = new ToolStripMenuItem("快捷鍵");
            this.status = new Label();
            this.imageBrowser = new ImageBrowserControl();
            this.imageCanvas = new ImageCanvasControl();
            this.canvas = new ImageCanvasControl();
            this.imageCanvasHost = new CanvasScrollHost(this.imageCanvas);
            this.annotationCanvasHost = new CanvasScrollHost(this.canvas);
            this.datasetAssignments = new DatasetAssignmentControl();
            this.pages = new TabControl();
            this.imagesPage = new TabPage(Strings.ImagesPage);
            this.imageHeader = new Panel();
            this.imageControls = new FlowLayoutPanel();
            this.annotationPage = new TabPage(Strings.AnnotationPage);
            this.annotationHeader = new FlowLayoutPanel();
            this.annotationSets = new ComboBox();
            this.datasetAnnotationSets = new ComboBox();
            this.annotationSidebar = new Panel();
            this.annotationClasses = new FlowLayoutPanel();
            this.annotationTools = new FlowLayoutPanel();
            this.annotationToolTips = new ToolTip(this.components);
            this.newSet = new Button();
            this.renameSet = new Button();
            this.deleteSet = new Button();
            this.addClass = new Button();
            this.obdSelect = new Button();
            this.obdRectangle = new Button();
            this.segRectangle = new Button();
            this.segEllipse = new Button();
            this.segPolygon = new Button();
            this.segBrush = new Button();
            this.segLine = new Button();
            this.segEraser = new Button();
            this.segSelect = new Button();
            this.segWidths = new TableLayoutPanel();
            this.segWidthLabel = new Label();
            this.segStrokeWidth = new NumericUpDown();
            this.segStrokeWidthSlider = new TrackBar();
            this.annotationOpacity = new TrackBar();
            this.annotationOpacityValue = new NumericUpDown();
            this.segOpacityLabel = new Label();
            this.showAnnotations = new CheckBox();
            this.fit = new Button();
            this.zoom = new ComboBox();
            this.imageFit = new Button();
            this.importImagesButton = new Button();
            this.imageZoom = new ComboBox();
            this.annotationZoomText = new Label();
            this.imageZoomText = new Label();
            this.deleteSelectedImagesButton = new Button();
            this.deleteAllImagesButton = new Button();
            this.datasetPage = new TabPage(Strings.DatasetPage);
            this.datasetHeader = new FlowLayoutPanel();
            this.datasetSetLabel = new Label();
            this.exportPage = new TabPage(Strings.ExportPage);
            this.exportJson = new Button();
            this.exportSelected = new Button();
            this.exportAll = new Button();
            this.menu.SuspendLayout();
            this.pages.SuspendLayout();
            this.imagesPage.SuspendLayout();
            this.imageHeader.SuspendLayout();
            this.annotationPage.SuspendLayout();
            this.annotationSidebar.SuspendLayout();
            this.datasetPage.SuspendLayout();
            this.exportPage.SuspendLayout();
            this.SuspendLayout();

            this.projectMenu.DropDownItems.AddRange(new ToolStripItem[]
            {
                this.newProjectMenuItem,
                this.openProjectMenuItem,
                this.saveProjectMenuItem,
                this.saveProjectAsMenuItem,
                this.importImagesMenuItem,
                this.importFolderMenuItem,
                this.importAnnotationMenuItem,
                this.missingImagesMenuItem,
                this.backupsMenuItem,
                this.diagnosticsMenuItem
            });
            this.helpMenu.DropDownItems.Add(this.shortcutsMenuItem);
            this.menu.Items.AddRange(new ToolStripItem[] { this.projectMenu, this.helpMenu });
            this.menu.Dock = DockStyle.Top;
            this.menu.Name = "menu";
            this.menu.TabIndex = 0;

            this.status.AutoSize = true;
            this.status.Dock = DockStyle.Top;
            this.status.Name = "status";
            this.status.Padding = new Padding(24, 8, 0, 8);
            this.status.TabIndex = 1;
            this.status.Text = Strings.OfflineStatus;

            this.imageBrowser.Dock = DockStyle.Left;
            this.imageBrowser.Name = "imageBrowser";
            this.imageBrowser.Width = 300;
            this.imageBrowser.TabIndex = 2;
            this.imageCanvas.Dock = DockStyle.Fill;
            this.imageCanvas.Name = "imageCanvas";
            this.canvas.Dock = DockStyle.Fill;
            this.canvas.Name = "canvas";
            this.imageCanvasHost.Dock = DockStyle.Fill;
            this.imageCanvasHost.Name = "imageCanvasHost";
            this.annotationCanvasHost.Dock = DockStyle.Fill;
            this.annotationCanvasHost.Name = "annotationCanvasHost";
            this.datasetAssignments.Dock = DockStyle.Fill;
            this.datasetAssignments.Name = "datasetAssignments";

            this.pages.Dock = DockStyle.Fill;
            this.pages.Name = "pages";
            this.pages.TabIndex = 3;
            this.imageHeader.Dock = DockStyle.Top;
            this.imageHeader.Height = 56;
            this.imageHeader.Name = "imageHeader";
            this.imageControls.Dock = DockStyle.Fill;
            this.imageControls.Name = "imageControls";
            this.imageControls.Padding = new Padding(4, 2, 4, 2);
            this.imageControls.WrapContents = true;
            this.imageFit.AutoSize = true;
            this.imageFit.Name = "imageFit";
            this.imageFit.Text = Strings.FitToWindow;
            this.importImagesButton.AutoSize = true;
            this.importImagesButton.Enabled = false;
            this.importImagesButton.Name = "importImagesButton";
            this.importImagesButton.Text = Strings.ImportImages;
            this.importImagesButton.UseVisualStyleBackColor = true;
            this.imageZoom.DropDownStyle = ComboBoxStyle.DropDownList;
            this.imageZoom.Name = "imageZoom";
            this.imageZoom.Width = 100;
            this.imageZoomText.AutoSize = true;
            this.imageZoomText.Name = "imageZoomText";
            this.imageZoomText.Margin = new Padding(8, 6, 0, 0);
            this.deleteSelectedImagesButton.AutoSize = true;
            this.deleteSelectedImagesButton.Name = "deleteSelectedImages";
            this.deleteSelectedImagesButton.Text = Strings.DeleteSelectedImages;
            this.deleteAllImagesButton.AutoSize = true;
            this.deleteAllImagesButton.Name = "deleteAllImages";
            this.deleteAllImagesButton.Text = Strings.DeleteAllImages;
            this.imageControls.Controls.AddRange(new Control[] { this.importImagesButton, this.imageFit, this.imageZoom, this.imageZoomText, this.deleteSelectedImagesButton, this.deleteAllImagesButton });
            this.imageHeader.Controls.Add(this.imageControls);
            this.imagesPage.Controls.Add(this.imageCanvasHost);
            this.imagesPage.Controls.Add(this.imageHeader);

            this.annotationHeader.AutoSize = true;
            this.annotationHeader.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            this.annotationHeader.Dock = DockStyle.Top;
            this.annotationHeader.Name = "annotationHeader";
            this.annotationHeader.Padding = new Padding(4, 2, 4, 2);
            this.annotationHeader.WrapContents = true;
            this.annotationSets.DropDownStyle = ComboBoxStyle.DropDownList;
            this.annotationSets.Name = "annotationSets";
            this.annotationSets.Width = 180;
            this.datasetAnnotationSets.DropDownStyle = ComboBoxStyle.DropDownList;
            this.datasetAnnotationSets.Name = "datasetAnnotationSets";
            this.datasetAnnotationSets.Width = 180;
            this.newSet.AutoSize = true;
            this.newSet.Name = "newSet";
            this.newSet.Text = Strings.NewAnnotationSet;
            this.renameSet.AutoSize = true;
            this.renameSet.Name = "renameSet";
            this.renameSet.Text = Strings.RenameAnnotationSet;
            this.deleteSet.AutoSize = true;
            this.deleteSet.Name = "deleteSet";
            this.deleteSet.Text = Strings.DeleteAnnotationSet;
            this.annotationOpacity.Minimum = 0;
            this.annotationOpacity.Maximum = 100;
            this.annotationOpacity.Value = 40;
            this.annotationOpacity.TickFrequency = 10;
            this.annotationOpacity.Width = 100;
            this.annotationOpacity.AutoSize = false;
            this.annotationOpacity.Height = 28;
            this.annotationOpacity.Name = "annotationOpacity";
            this.annotationOpacityValue.Minimum = 0;
            this.annotationOpacityValue.Maximum = 100;
            this.annotationOpacityValue.Value = 40;
            this.annotationOpacityValue.Width = 52;
            this.annotationOpacityValue.Name = "annotationOpacityValue";
            this.segOpacityLabel.AutoSize = true;
            this.segOpacityLabel.Margin = new Padding(8, 6, 0, 0);
            this.segOpacityLabel.Name = "segOpacityLabel";
            this.segOpacityLabel.Text = Strings.SegOpacity;
            this.showAnnotations.AutoSize = true;
            this.showAnnotations.Name = "showAnnotations";
            this.showAnnotations.Text = Strings.ShowAnnotations;
            this.showAnnotations.Checked = true;
            this.segWidths.AutoSize = true;
            this.segWidths.ColumnCount = 3;
            this.segWidths.RowCount = 1;
            this.segWidths.Name = "segWidths";
            this.segWidths.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            this.segWidths.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            this.segWidths.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            this.segStrokeWidth.Minimum = 1;
            this.segStrokeWidth.Maximum = 100;
            this.segStrokeWidth.Value = 15;
            this.segStrokeWidth.Width = 52;
            this.segStrokeWidth.Name = "segStrokeWidth";
            this.segStrokeWidthSlider.Minimum = 1;
            this.segStrokeWidthSlider.Maximum = 100;
            this.segStrokeWidthSlider.Value = 15;
            this.segStrokeWidthSlider.TickFrequency = 10;
            this.segStrokeWidthSlider.Width = 100;
            this.segStrokeWidthSlider.AutoSize = false;
            this.segStrokeWidthSlider.Height = 28;
            this.segStrokeWidthSlider.Name = "segStrokeWidthSlider";
            this.segWidthLabel.AutoSize = true;
            this.segWidthLabel.Anchor = AnchorStyles.Left;
            this.segWidthLabel.Name = "segWidthLabel";
            this.segWidthLabel.Text = Strings.SegBrushWidth;
            this.segWidths.Controls.Add(this.segWidthLabel, 0, 0);
            this.segWidths.Controls.Add(this.segStrokeWidth, 1, 0);
            this.segWidths.Controls.Add(this.segStrokeWidthSlider, 2, 0);
            this.fit.AutoSize = true;
            this.fit.Name = "fit";
            this.fit.Text = Strings.FitToWindow;
            this.zoom.DropDownStyle = ComboBoxStyle.DropDownList;
            this.zoom.Width = 100;
            this.zoom.Name = "zoom";
            this.zoom.Items.AddRange(new object[] { Strings.FitToWindow, "10%", "50%", "75%", "100%", "125%", "150%", "200%" });
            this.zoom.SelectedIndex = 0;
            this.imageZoom.Items.AddRange(new object[] { Strings.FitToWindow, "10%", "50%", "75%", "100%", "125%", "150%", "200%" });
            this.imageZoom.SelectedIndex = 0;
            this.annotationZoomText.AutoSize = true;
            this.annotationZoomText.Name = "annotationZoomText";
            this.annotationZoomText.Margin = new Padding(8, 6, 0, 0);
            this.annotationHeader.Controls.AddRange(new Control[] { this.annotationSets, this.newSet, this.renameSet, this.deleteSet, this.segOpacityLabel, this.annotationOpacity, this.annotationOpacityValue, this.showAnnotations, this.segWidths, this.fit, this.zoom, this.annotationZoomText });
            this.annotationSidebar.Dock = DockStyle.Right;
            this.annotationSidebar.Width = 180;
            this.annotationSidebar.Name = "annotationSidebar";
            this.annotationClasses.Dock = DockStyle.Fill;
            this.annotationClasses.AutoScroll = true;
            this.annotationClasses.FlowDirection = FlowDirection.TopDown;
            this.annotationClasses.WrapContents = false;
            this.annotationClasses.Name = "annotationClasses";
            this.annotationTools.Dock = DockStyle.Top;
            this.annotationTools.AutoSize = true;
            this.annotationTools.WrapContents = true;
            this.annotationTools.Name = "annotationTools";
            this.obdSelect.Name = "obdSelect";
            this.obdSelect.Text = "選取 V";
            this.obdRectangle.Name = "obdRectangle";
            this.obdRectangle.Text = "矩形 R";
            this.segSelect.Name = "segSelect";
            this.segSelect.Text = "選取 V";
            this.segRectangle.Name = "segRectangle";
            this.segRectangle.Text = "矩形 R";
            this.segEllipse.Name = "segEllipse";
            this.segEllipse.Text = "圓形 C";
            this.segPolygon.Name = "segPolygon";
            this.segPolygon.Text = "畫筆 P";
            this.segBrush.Name = "segBrush";
            this.segBrush.Text = "筆刷 B";
            this.segLine.Name = "segLine";
            this.segLine.Text = "線形 L";
            this.segEraser.Name = "segEraser";
            this.segEraser.Text = "橡皮擦 E";
            this.annotationTools.Controls.AddRange(new Control[] { this.obdSelect, this.obdRectangle, this.segSelect, this.segRectangle, this.segEllipse, this.segPolygon, this.segBrush, this.segLine, this.segEraser });
            this.addClass.Dock = DockStyle.Top;
            this.addClass.Name = "addClass";
            this.addClass.Text = Strings.AddAnnotationClass;
            this.annotationSidebar.Controls.Add(this.annotationClasses);
            this.annotationSidebar.Controls.Add(this.annotationTools);
            this.annotationSidebar.Controls.Add(this.addClass);
            this.annotationPage.Controls.Add(this.annotationCanvasHost);
            this.annotationPage.Controls.Add(this.annotationSidebar);
            this.annotationPage.Controls.Add(this.annotationHeader);

            this.datasetHeader.AutoSize = true;
            this.datasetHeader.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            this.datasetHeader.Dock = DockStyle.Top;
            this.datasetHeader.Name = "datasetHeader";
            this.datasetHeader.Padding = new Padding(4, 2, 4, 2);
            this.datasetHeader.WrapContents = true;
            this.datasetSetLabel.AutoSize = true;
            this.datasetSetLabel.Margin = new Padding(4, 6, 0, 0);
            this.datasetSetLabel.Name = "datasetSetLabel";
            this.datasetSetLabel.Text = Strings.AnnotationSet;
            this.datasetHeader.Controls.Add(this.datasetSetLabel);
            this.datasetHeader.Controls.Add(this.datasetAnnotationSets);
            this.datasetPage.Controls.Add(this.datasetAssignments);
            this.datasetPage.Controls.Add(this.datasetHeader);

            this.exportJson.Dock = DockStyle.Top;
            this.exportJson.Name = "exportJson";
            this.exportJson.Text = Strings.ExportAnnotationJson;
            this.exportSelected.Dock = DockStyle.Top;
            this.exportSelected.Name = "exportSelected";
            this.exportSelected.Text = Strings.ExportSelectedImages;
            this.exportAll.Dock = DockStyle.Top;
            this.exportAll.Name = "exportAll";
            this.exportAll.Text = Strings.ExportAllImages;
            this.exportPage.Controls.Add(this.exportSelected);
            this.exportPage.Controls.Add(this.exportAll);
            this.exportPage.Controls.Add(this.exportJson);
            this.pages.TabPages.AddRange(new TabPage[] { this.imagesPage, this.annotationPage, this.datasetPage, this.exportPage });

            this.MainMenuStrip = this.menu;
            this.Controls.Add(this.pages);
            this.Controls.Add(this.imageBrowser);
            this.Controls.Add(this.status);
            this.Controls.Add(this.menu);
            this.AutoScaleMode = AutoScaleMode.Dpi;
            this.MinimumSize = new Size(640, 360);
            this.Name = "MainForm";
            this.Size = new Size(1280, 800);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Text = Strings.ApplicationTitle;
            this.KeyPreview = true;
            this.menu.ResumeLayout(false);
            this.menu.PerformLayout();
            this.imageHeader.ResumeLayout(false);
            this.annotationSidebar.ResumeLayout(false);
            this.datasetPage.ResumeLayout(false);
            this.exportPage.ResumeLayout(false);
            this.annotationPage.ResumeLayout(false);
            this.annotationPage.PerformLayout();
            this.imagesPage.ResumeLayout(false);
            this.pages.ResumeLayout(false);
            this.ResumeLayout(false);
            this.PerformLayout();
        }
    }
}

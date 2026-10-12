using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Neurocle.Application.Projects;
using Neurocle.Infrastructure.Persistence;
using Neurocle.WinForms.Resources;

namespace Neurocle.WinForms
{
    public sealed class MainForm : Form
    {
        private readonly IProjectLifecycle projectLifecycle;
        private readonly DiagnosticLog diagnosticLog;
        private IProjectSession project;
        private readonly Label status;
        private readonly ToolStripMenuItem projectMenu;
        private readonly ImageBrowserControl imageBrowser;
        private readonly ImageCanvasControl imageCanvas;
        private readonly ImageCanvasControl canvas;
        private ImageCanvasControl activeCanvas;
        private readonly DatasetAssignmentControl datasetAssignments;
        private readonly ComboBox annotationSets;
        private readonly Panel annotationSidebar;
        private readonly FlowLayoutPanel annotationClasses;
        private readonly FlowLayoutPanel annotationTools;
        private readonly ToolTip annotationToolTips;
        private readonly TrackBar annotationOpacity;
        private readonly Label annotationOpacityValue;
        private readonly Button addClass;
        private readonly Button obdSelect;
        private readonly Button obdRectangle;
        private readonly Button segSelect;
        private readonly Button segRectangle;
        private readonly Button segEllipse;
        private readonly Button segPolygon;
        private readonly Button segBrush;
        private readonly Button segLine;
        private readonly Button segEraser;
        private string selectedImagePath;
        private string selectedImageId;
        private string selectedSegClassId;
        private bool importInProgress;
        private bool exportInProgress;
        private bool annotationOperationInProgress;
        private ImageSelectedEventArgs pendingImageSelection;
        private bool synchronizingCanvasView;
        private bool refreshingAnnotationSets;
        private string displayedAnnotationSetId;

        internal ImageBrowserControl ImageBrowser { get { return imageBrowser; } }
        internal ImageCanvasControl ImageCanvas { get { return imageCanvas; } }
        internal ImageCanvasControl AnnotationCanvas { get { return canvas; } }
        internal DatasetAssignmentControl DatasetAssignments { get { return datasetAssignments; } }

        public MainForm(IProjectLifecycle projectLifecycle)
            : this(projectLifecycle, DiagnosticLog.CreateDefault())
        {
        }

        public MainForm(IProjectLifecycle projectLifecycle, DiagnosticLog diagnosticLog)
        {
            this.projectLifecycle = projectLifecycle ?? throw new ArgumentNullException(nameof(projectLifecycle));
            this.diagnosticLog = diagnosticLog ?? throw new ArgumentNullException(nameof(diagnosticLog));
            AutoScaleMode = AutoScaleMode.Dpi;
            MinimumSize = new Size(640, 360);
            StartPosition = FormStartPosition.CenterScreen;
            Text = Strings.ApplicationTitle;
            KeyPreview = true;

            var menu = new MenuStrip();
            projectMenu = new ToolStripMenuItem(Strings.ProjectMenu);
            projectMenu.DropDownItems.Add(Strings.NewProject, null, (sender, args) => RunProjectCommand(CreateProject));
            projectMenu.DropDownItems.Add(Strings.OpenProject, null, (sender, args) => RunProjectCommand(OpenProject));
            projectMenu.DropDownItems.Add(Strings.SaveProject, null, (sender, args) => RunProjectCommand(SaveProject));
            projectMenu.DropDownItems.Add(Strings.SaveProjectAs, null, (sender, args) => RunProjectCommand(SaveProjectAs));
            projectMenu.DropDownItems.Add(Strings.ImportImages, null, async (sender, args) => await ImportFilesAsync());
            projectMenu.DropDownItems.Add(Strings.ImportFolder, null, async (sender, args) => await ImportFolderAsync());
            projectMenu.DropDownItems.Add(Strings.ImportAnnotationJson, null, (sender, args) => RunProjectCommand(ImportAnnotationJson));
            projectMenu.DropDownItems.Add(Strings.ManageMissingImages, null, (sender, args) => ManageMissingImages());
            projectMenu.DropDownItems.Add(Strings.ManageBackups, null, (sender, args) => ManageBackups());
            projectMenu.DropDownItems.Add(Strings.Diagnostics, null, (sender, args) => ManageDiagnostics());
            menu.Items.Add(projectMenu);

            status = new Label
            {
                AutoSize = true,
                Dock = DockStyle.Top,
                Padding = new Padding(24, 8, 0, 8),
                Text = Strings.OfflineStatus
            };
            imageBrowser = new ImageBrowserControl { Dock = DockStyle.Left, Width = 300 };
            imageCanvas = new ImageCanvasControl { Dock = DockStyle.Fill };
            canvas = new ImageCanvasControl { Dock = DockStyle.Fill };
            activeCanvas = imageCanvas;
            imageCanvas.ViewChanged += (sender, args) => SynchronizeCanvasView(imageCanvas, canvas);
            canvas.ViewChanged += (sender, args) => SynchronizeCanvasView(canvas, imageCanvas);
            datasetAssignments = new DatasetAssignmentControl { Dock = DockStyle.Fill };
            datasetAssignments.DatasetRequested += (ids, value) => RunAnnotationCommand(() => SetDatasetAssignments(ids, value));
            datasetAssignments.RetestRequested += (ids, value) => RunAnnotationCommand(() => SetRetest(ids, value));
            datasetAssignments.ClearClassRequested += ids => RunAnnotationCommand(() => ClearDatasetClasses(ids));
            datasetAssignments.AutoSplitRequested += value => RunAnnotationCommand(() => AutoSplitDataset(value));
            canvas.ObdRectangleCreated += rectangle => RunAnnotationCommand(() => CreateObdRectangle(rectangle));
            canvas.ObdRectanglesChanged += rectangles => RunAnnotationCommand(() => UpdateObdRectangles(rectangles));
            canvas.ObdAnnotationsDeleteRequested += ids => RunAnnotationCommand(() => DeleteObdRectangles(ids));
            canvas.SegRegionCreated += async (type, rectangle) => await CreateSegRegionAsync(type, rectangle);
            canvas.SegPathCreated += async (type, points, width) => await CreateSegPathAsync(type, points, width);
            canvas.SegEraseRequested += async (points, width) => await EraseSegPathAsync(points, width);
            canvas.SegRegionsChanged += regions => RunAnnotationCommand(() => UpdateSegRegions(regions));
            canvas.SegRegionsDeleteRequested += ids => RunAnnotationCommand(() => DeleteSegRegions(ids));
            imageBrowser.ImageSelected += (sender, args) =>
            {
                if (annotationOperationInProgress) pendingImageSelection = args;
                else ApplyImageSelection(args);
            };
            var pages = new TabControl { Dock = DockStyle.Fill };
            var imagesPage = new TabPage(Strings.ImagesPage);
            var imageHeader = new Panel { Dock = DockStyle.Top, Height = 56 };
            var imageControls = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true, Padding = new Padding(4, 2, 4, 2) };
            imageHeader.Controls.Add(imageControls);
            imagesPage.Controls.Add(imageCanvas);
            imagesPage.Controls.Add(imageHeader);
            var annotationPage = new TabPage(Strings.AnnotationPage);
            var annotationHeader = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, Padding = new Padding(4, 2, 4, 2) };
            annotationHeader.SizeChanged += (sender, args) => imageHeader.Height = annotationHeader.Height;
            annotationSets = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
            annotationSets.SelectedIndexChanged += (sender, args) =>
            {
                if (refreshingAnnotationSets) return;
                ResetAnnotationOpacity();
                canvas.DiscardUnfinishedShape();
                RefreshAnnotationClasses();
                RefreshDatasetAssignments();
            };
            annotationSidebar = new Panel { Dock = DockStyle.Right, Width = 180 };
            annotationClasses = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            annotationTools = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true };
            annotationToolTips = new ToolTip();
            var newCla = new Button { AutoSize = true, Text = Strings.NewClaSet };
            newCla.Click += (sender, args) => RunAnnotationCommand(() => CreateAnnotationSet(AnnotationSetType.Cla));
            var newObd = new Button { AutoSize = true, Text = Strings.NewObdSet };
            newObd.Click += (sender, args) => RunAnnotationCommand(() => CreateAnnotationSet(AnnotationSetType.Obd));
            var newSeg = new Button { AutoSize = true, Text = Strings.NewSegSet };
            newSeg.Click += (sender, args) => RunAnnotationCommand(() => CreateAnnotationSet(AnnotationSetType.Seg));
            var renameSet = new Button { AutoSize = true, Text = Strings.RenameAnnotationSet };
            renameSet.Click += (sender, args) => RunAnnotationCommand(RenameSelectedAnnotationSet);
            var deleteSet = new Button { AutoSize = true, Text = Strings.DeleteAnnotationSet };
            deleteSet.Click += (sender, args) => RunAnnotationCommand(DeleteSelectedAnnotationSet);
            addClass = new Button { Dock = DockStyle.Top, Name = "addClass", Text = Strings.AddAnnotationClass };
            addClass.Click += (sender, args) => RunAnnotationCommand(AddAnnotationClass);
            obdSelect = CreateAnnotationToolButton("obdSelect", "↖", "OBD 選取");
            obdSelect.Click += (sender, args) => SelectObdTool(true);
            obdRectangle = CreateAnnotationToolButton("obdRectangle", "□", "OBD 矩形");
            obdRectangle.Click += (sender, args) => SelectObdTool(false);
            segRectangle = CreateAnnotationToolButton("segRectangle", "□", Strings.SegRectangleTool);
            segRectangle.Click += (sender, args) => SelectSegTool(ImageCanvasControl.SegToolKind.Rect);
            segEllipse = CreateAnnotationToolButton("segEllipse", "○", Strings.SegEllipseTool);
            segEllipse.Click += (sender, args) => SelectSegTool(ImageCanvasControl.SegToolKind.Ellipse);
            segPolygon = CreateAnnotationToolButton("segPolygon", "✎", Strings.SegPolygonTool);
            segPolygon.Click += (sender, args) => SelectSegTool(ImageCanvasControl.SegToolKind.Polygon);
            segBrush = CreateAnnotationToolButton("segBrush", "●", Strings.SegBrushTool);
            segBrush.Click += (sender, args) => SelectSegTool(ImageCanvasControl.SegToolKind.Brush);
            segLine = CreateAnnotationToolButton("segLine", "╱", Strings.SegLineTool);
            segLine.Click += (sender, args) => SelectSegTool(ImageCanvasControl.SegToolKind.Line);
            segEraser = CreateAnnotationToolButton("segEraser", "⌫", Strings.SegEraserTool);
            segEraser.Click += (sender, args) => SelectSegTool(ImageCanvasControl.SegToolKind.Eraser);
            segSelect = CreateAnnotationToolButton("segSelect", "↖", Strings.SegSelectTool);
            segSelect.Click += (sender, args) => SelectSegTool(ImageCanvasControl.SegToolKind.Select);
            var segWidths = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, RowCount = 1 };
            segWidths.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            segWidths.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            AddSegWidthControl(segWidths, 0, Strings.SegBrushWidth, value => canvas.SegStrokeWidth = value);
            annotationOpacity = new TrackBar { Minimum = 0, Maximum = 100, Value = 40, TickFrequency = 10, Width = 100, AutoSize = false, Height = 28 };
            annotationOpacity.ValueChanged += (sender, args) => SetAnnotationOpacity(annotationOpacity.Value);
            annotationOpacityValue = new Label { AutoSize = true, Margin = new Padding(0, 6, 0, 0) };
            var segOpacityLabel = new Label { Text = Strings.SegOpacity, AutoSize = true, Margin = new Padding(8, 6, 0, 0) };
            var fit = new Button { AutoSize = true, Text = Strings.FitToWindow };
            var zoom = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100 };
            zoom.Items.AddRange(new object[] { Strings.FitToWindow, "10%", "50%", "75%", "100%", "125%", "150%", "200%" });
            zoom.SelectedIndex = 0;
            var imageFit = new Button { AutoSize = true, Text = Strings.FitToWindow };
            var imageZoom = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100 };
            imageZoom.Items.AddRange(zoom.Items.Cast<object>().ToArray());
            imageZoom.SelectedIndex = 0;
            var annotationZoomText = new Label { AutoSize = true, Margin = new Padding(8, 6, 0, 0) };
            var imageZoomText = new Label { AutoSize = true, Margin = new Padding(8, 6, 0, 0) };
            Action updateZoomText = () =>
            {
                if (IsDisposed || Disposing || annotationZoomText.IsDisposed || imageZoomText.IsDisposed) return;
                string value = Math.Round(activeCanvas.Zoom * 100d).ToString() + "%";
                annotationZoomText.Text = value;
                imageZoomText.Text = value;
            };
            Action<string> setZoom = selected =>
            {
                if (selected == Strings.FitToWindow) activeCanvas.FitToWindow();
                else activeCanvas.SetZoomPercent(double.Parse(selected.TrimEnd('%')));
            };
            fit.Click += (sender, args) => activeCanvas.FitToWindow();
            imageFit.Click += (sender, args) => activeCanvas.FitToWindow();
            zoom.SelectedIndexChanged += (sender, args) => setZoom(zoom.SelectedItem as string);
            imageZoom.SelectedIndexChanged += (sender, args) => setZoom(imageZoom.SelectedItem as string);
            imageCanvas.ZoomChanged += (sender, args) => updateZoomText();
            canvas.ZoomChanged += (sender, args) => updateZoomText();
            annotationPage.Controls.Add(canvas);
            annotationSidebar.Controls.Add(annotationClasses);
            annotationSidebar.Controls.Add(annotationTools);
            annotationSidebar.Controls.Add(addClass);
            annotationPage.Controls.Add(annotationSidebar);
            annotationHeader.Controls.Add(annotationSets);
            annotationHeader.Controls.Add(newCla);
            annotationHeader.Controls.Add(newObd);
            annotationHeader.Controls.Add(newSeg);
            annotationHeader.Controls.Add(renameSet);
            annotationHeader.Controls.Add(deleteSet);
            annotationHeader.Controls.Add(segOpacityLabel);
            annotationHeader.Controls.Add(annotationOpacity);
            annotationHeader.Controls.Add(annotationOpacityValue);
            annotationHeader.Controls.Add(segWidths);
            annotationHeader.Controls.Add(fit);
            annotationHeader.Controls.Add(zoom);
            annotationHeader.Controls.Add(annotationZoomText);
            annotationPage.Controls.Add(annotationHeader);
            imageControls.Controls.Add(imageFit);
            imageControls.Controls.Add(imageZoom);
            imageControls.Controls.Add(imageZoomText);
            pages.TabPages.Add(imagesPage);
            pages.TabPages.Add(annotationPage);
            var datasetPage = new TabPage(Strings.DatasetPage);
            datasetPage.Controls.Add(datasetAssignments);
            pages.TabPages.Add(datasetPage);
            var exportPage = new TabPage(Strings.ExportPage);
            var exportJson = new Button { Dock = DockStyle.Top, Text = Strings.ExportAnnotationJson };
            exportJson.Click += async (sender, args) => await ExportAnnotationJsonAsync();
            var exportSelected = new Button { Dock = DockStyle.Top, Text = Strings.ExportSelectedImages };
            exportSelected.Click += async (sender, args) => await ExportImagesAsync(imageBrowser.SelectedImageIds);
            var exportAll = new Button { Dock = DockStyle.Top, Text = Strings.ExportAllImages };
            exportAll.Click += async (sender, args) => await ExportImagesAsync(null);
            exportPage.Controls.Add(exportSelected);
            exportPage.Controls.Add(exportAll);
            exportPage.Controls.Add(exportJson);
            pages.TabPages.Add(exportPage);
            SetAnnotationOpacity(annotationOpacity.Value);
            pages.SelectedIndexChanged += (sender, args) =>
            {
                ImageCanvasControl selectedCanvas = pages.SelectedTab == imagesPage ? imageCanvas : pages.SelectedTab == annotationPage ? canvas : null;
                if (selectedCanvas != null && !ReferenceEquals(activeCanvas, selectedCanvas))
                {
                    SynchronizeCanvasView(activeCanvas, selectedCanvas);
                    activeCanvas = selectedCanvas;
                    updateZoomText();
                }
                canvas.DiscardUnfinishedShape();
                if (pages.SelectedTab == datasetPage) RefreshDatasetAssignments();
            };

            MainMenuStrip = menu;
            Controls.Add(pages);
            Controls.Add(imageBrowser);
            Controls.Add(status);
            Controls.Add(menu);
            FormClosing += (sender, args) =>
            {
                if (importInProgress || exportInProgress || annotationOperationInProgress) args.Cancel = true;
            };
            FormClosed += (sender, args) => project?.Dispose();
            KeyDown += (sender, args) =>
            {
                if (!args.Control) return;
                if (args.KeyCode == Keys.Z) UndoOrRedoSelectedAnnotationSet(args.Shift);
                else if (args.KeyCode == Keys.Y) UndoOrRedoSelectedAnnotationSet(true);
                else if (args.KeyCode == Keys.A && pages.SelectedTab == annotationPage && !HasTextInputFocus()) SelectAllAnnotations();
                else return;
                args.Handled = true;
                args.SuppressKeyPress = true;
            };
            KeyDown += (sender, args) =>
            {
                if (args.Control || HasTextInputFocus() || (pages.SelectedTab != imagesPage && pages.SelectedTab != annotationPage)) return;
                if (args.KeyCode == Keys.F)
                {
                    activeCanvas.FitToWindow();
                    args.Handled = true;
                    args.SuppressKeyPress = true;
                    return;
                }
                if (args.KeyCode != Keys.Up && args.KeyCode != Keys.Down) return;
                imageBrowser.MoveSelection(args.KeyCode == Keys.Up ? -1 : 1);
                args.Handled = true;
                args.SuppressKeyPress = true;
            };
        }

        private bool HasTextInputFocus()
        {
            return FindFocusedTextInput(this);
        }

        private static bool FindFocusedTextInput(Control control)
        {
            if (control is TextBoxBase && control.Focused) return true;
            return control.Controls.Cast<Control>().Any(FindFocusedTextInput);
        }

        private void SelectAllAnnotations()
        {
            AnnotationSet selected = SelectedAnnotationSet;
            if (selected == null) return;
            if (selected.Type == AnnotationSetType.Obd && canvas.ObdEnabled) canvas.SelectAllObd();
            else if (selected.Type == AnnotationSetType.Seg && canvas.SegEnabled) canvas.SelectAllSeg();
        }

        private static void AddSegWidthControl(TableLayoutPanel container, int row, string label, Action<int> apply)
        {
            var value = new NumericUpDown { Dock = DockStyle.Fill, Minimum = 1, Maximum = 100, Value = 15 };
            value.ValueChanged += (sender, args) => apply((int)value.Value);
            container.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
            container.Controls.Add(value, 1, row);
        }

        private void SynchronizeCanvasView(ImageCanvasControl source, ImageCanvasControl target)
        {
            if (synchronizingCanvasView) return;
            try
            {
                synchronizingCanvasView = true;
                target.SetViewState(source.ViewState);
            }
            finally { synchronizingCanvasView = false; }
        }

        private void SelectSegTool(ImageCanvasControl.SegToolKind tool)
        {
            canvas.DiscardUnfinishedShape();
            canvas.SegTool = tool;
        }

        private void SelectObdTool(bool select)
        {
            canvas.ObdSelectMode = select;
        }

        private Button CreateAnnotationToolButton(string name, string icon, string toolTip)
        {
            var button = new Button { Name = name, Text = icon, Size = new Size(32, 32), Margin = new Padding(2) };
            annotationToolTips.SetToolTip(button, toolTip);
            return button;
        }

        private void CreateProject()
        {
            using (var dialog = CreateProjectDialog())
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                ReplaceProject(projectLifecycle.Create(dialog.FileName));
                WriteDiagnostic(DiagnosticEvent.ProjectCreated);
            }
        }

        private void OpenProject()
        {
            using (var dialog = new OpenFileDialog { Filter = Strings.ProjectFileFilter })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    ReplaceProject(projectLifecycle.Open(dialog.FileName));
                    WriteDiagnostic(DiagnosticEvent.ProjectOpened);
                }
                catch (ProjectOpenException exception)
                {
                    RestoreCorruptProject(exception);
                }
            }
        }

        private void SaveProject()
        {
            project?.Save();
            if (project != null) WriteDiagnostic(DiagnosticEvent.ProjectSaved);
        }

        private void SaveProjectAs()
        {
            if (project == null) return;
            using (var dialog = CreateProjectDialog())
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                IProjectSession copy = project.SaveAs(dialog.FileName);
                ReplaceProject(copy);
                WriteDiagnostic(DiagnosticEvent.ProjectSaved);
            }
        }

        private void ManageMissingImages()
        {
            if (project == null) return;
            using (var dialog = new MissingImagesForm(project)) dialog.ShowDialog(this);
            UpdateProjectStatus();
            imageBrowser.LoadImages(project.GetImages(), GetImagesDirectory(project.ProjectPath));
            RefreshAnnotationSets();
        }

        private void ManageDiagnostics()
        {
            using (var dialog = new DiagnosticsForm(diagnosticLog)) dialog.ShowDialog(this);
        }

        private void CreateAnnotationSet(AnnotationSetType type)
        {
            if (project == null) return;
            string name = Prompt(Strings.AnnotationSetNamePrompt);
            if (name == null) return;
            CreateAnnotationSet(type, name);
        }

        private void CreateAnnotationSet(AnnotationSetType type, string name)
        {
            AnnotationSet created = project.CreateAnnotationSet(name, type);
            RefreshAnnotationSets(created.Id);
        }

        private void RefreshAnnotationSets(string selectedId = null)
        {
            if (annotationSets == null) return;
            if (selectedId == null) selectedId = (annotationSets.SelectedItem as AnnotationSet)?.Id;
            string previousId = (annotationSets.SelectedItem as AnnotationSet)?.Id;
            refreshingAnnotationSets = true;
            try
            {
                annotationSets.Items.Clear();
                if (project != null) annotationSets.Items.AddRange(project.GetAnnotationSets().ToArray());
                annotationSets.DisplayMember = "DisplayName";
                AnnotationSet selected = annotationSets.Items.Cast<AnnotationSet>().FirstOrDefault(item => item.Id == selectedId) ?? annotationSets.Items.Cast<AnnotationSet>().FirstOrDefault();
                annotationSets.SelectedItem = selected;
            }
            finally
            {
                refreshingAnnotationSets = false;
            }
            if (previousId != (annotationSets.SelectedItem as AnnotationSet)?.Id)
            {
                ResetAnnotationOpacity();
                canvas.DiscardUnfinishedShape();
            }
            RefreshAnnotationClasses();
            RefreshDatasetAssignments();
        }

        private void ResetAnnotationOpacity()
        {
            if (annotationOpacity != null) annotationOpacity.Value = 40;
            SetAnnotationOpacity(40);
        }

        private void SetAnnotationOpacity(int percent)
        {
            float opacity = percent / 100f;
            canvas.ObdOpacity = opacity;
            canvas.SegOpacity = opacity;
            if (annotationOpacityValue != null) annotationOpacityValue.Text = percent + "%";
            canvas.Invalidate();
        }

        private void RefreshAnnotationClasses()
        {
            if (annotationClasses == null) return;
            annotationClasses.Controls.Clear();
            AnnotationSet selected = annotationSets.SelectedItem as AnnotationSet;
            bool selectedSetChanged = displayedAnnotationSetId != (selected == null ? null : selected.Id);
            displayedAnnotationSetId = selected == null ? null : selected.Id;
            canvas.ObdEnabled = project != null && selected != null && selected.Type == AnnotationSetType.Obd && selectedImageId != null;
            canvas.SegEnabled = project != null && selected != null && selected.Type == AnnotationSetType.Seg && selectedImageId != null;
            if (selected == null || selected.Type != AnnotationSetType.Obd) canvas.ObdSelectMode = false;
            else if (selectedSetChanged) canvas.ObdSelectMode = true;
            if (selected == null || selected.Type != AnnotationSetType.Seg) canvas.SegTool = null;
            else if (selectedSetChanged || !canvas.SegTool.HasValue) canvas.SegTool = ImageCanvasControl.SegToolKind.Select;
            annotationTools.Controls.Clear();
            if (selected != null && selected.Type == AnnotationSetType.Obd)
            {
                annotationTools.Controls.Add(obdSelect);
                annotationTools.Controls.Add(obdRectangle);
            }
            else if (selected != null && selected.Type == AnnotationSetType.Seg)
            {
                annotationTools.Controls.Add(segSelect);
                annotationTools.Controls.Add(segRectangle);
                annotationTools.Controls.Add(segEllipse);
                annotationTools.Controls.Add(segPolygon);
                annotationTools.Controls.Add(segBrush);
                annotationTools.Controls.Add(segLine);
                annotationTools.Controls.Add(segEraser);
            }
            AnnotationClass[] classes = project == null || selected == null ? new AnnotationClass[0] : project.GetClasses(selected.Id).ToArray();
            if (selected == null || selected.Type != AnnotationSetType.Seg || !classes.Any(value => value.Id == selectedSegClassId)) selectedSegClassId = null;
            if (canvas.SegEnabled && selectedSegClassId == null) selectedSegClassId = classes.FirstOrDefault()?.Id;
            canvas.ObdClasses = classes.ToDictionary(value => value.Id);
            canvas.ObdAnnotations = canvas.ObdEnabled ? project.GetObdAnnotations(selected.Id, selectedImageId) : null;
            canvas.SegClasses = classes.ToDictionary(value => value.Id);
            canvas.SegRegions = canvas.SegEnabled ? project.GetSegRegions(selected.Id, selectedImageId) : null;
            if (project == null || selected == null) return;
            foreach (AnnotationClass value in classes)
            {
                var button = new Button { Text = value.Name, AutoSize = true, BackColor = ColorTranslator.FromHtml(value.Color) };
                button.Click += (sender, args) =>
                {
                    if (selected.Type == AnnotationSetType.Cla && selectedImageId != null) RunAnnotationCommand(() => project.SetClaClass(selected.Id, selectedImageId, value.Id));
                    else if (selected.Type == AnnotationSetType.Obd && selectedImageId != null && canvas.SelectedObdId != null) RunAnnotationCommand(() => ChangeSelectedObdClass(value.Id));
                    else if (selected.Type == AnnotationSetType.Seg)
                    {
                        selectedSegClassId = value.Id;
                        if (selectedImageId != null && canvas.SelectedSegIds.Count > 0) RunAnnotationCommand(() => ChangeSelectedSegClass(value.Id));
                    }
                };
                var menu = new ContextMenuStrip();
                menu.Items.Add(Strings.RenameAnnotationClass, null, (sender, args) => RunAnnotationCommand(() => RenameAnnotationClass(value)));
                menu.Items.Add(Strings.ChangeAnnotationClassColor, null, (sender, args) => RunAnnotationCommand(() => ChangeAnnotationClassColor(value)));
                menu.Items.Add(Strings.DeleteAnnotationClass, null, (sender, args) => RunAnnotationCommand(() => DeleteAnnotationClass(value)));
                button.ContextMenuStrip = menu;
                annotationClasses.Controls.Add(button);
            }
        }

        private AnnotationSet SelectedAnnotationSet { get { return annotationSets.SelectedItem as AnnotationSet; } }

        private void RefreshDatasetAssignments()
        {
            AnnotationSet selected = SelectedAnnotationSet;
            if (project == null || selected == null) { datasetAssignments.LoadRows(new DatasetRow[0]); return; }
            var images = project.GetImages().ToDictionary(value => value.Id, StringComparer.Ordinal);
            var classes = project.GetClasses(selected.Id).ToDictionary(value => value.Id, value => value.Name, StringComparer.Ordinal);
            var summaries = project.GetImageAnnotationSummaries(selected.Id).ToDictionary(value => value.ImageId, StringComparer.Ordinal);
            var rows = new List<DatasetRow>();
            foreach (DatasetAssignment assignment in project.GetDatasetAssignments(selected.Id))
            {
                ProjectImage image = images[assignment.ImageId];
                ImageAnnotationSummary summary;
                summaries.TryGetValue(image.Id, out summary);
                string[] classNames = (summary == null ? new string[0] : summary.ClassIds).Where(classes.ContainsKey).Select(classId => classes[classId]).ToArray();
                rows.Add(new DatasetRow(image, assignment, classNames, summary == null ? 0 : summary.AnnotationCount));
            }
            datasetAssignments.LoadRows(rows, selected.Type != AnnotationSetType.Cla);
        }

        private void SetDatasetAssignments(IReadOnlyList<string> imageIds, DatasetAssignmentStatus status)
        {
            AnnotationSet selected = SelectedAnnotationSet;
            if (project == null || selected == null || imageIds.Count == 0) return;
            project.SetDatasetAssignments(selected.Id, imageIds, status);
            RefreshDatasetAssignments();
        }

        private void SetRetest(IReadOnlyList<string> imageIds, bool retest)
        {
            AnnotationSet selected = SelectedAnnotationSet;
            if (project == null || selected == null || imageIds.Count == 0) return;
            project.SetRetest(selected.Id, imageIds, retest);
            RefreshDatasetAssignments();
        }

        private void ClearDatasetClasses(IReadOnlyList<string> imageIds)
        {
            AnnotationSet selected = SelectedAnnotationSet;
            if (project == null || selected == null || imageIds.Count == 0) return;
            project.ClearAnnotations(selected.Id, imageIds);
            RefreshAnnotationClasses();
            RefreshDatasetAssignments();
        }

        private void AutoSplitDataset(int trainPercent)
        {
            AnnotationSet selected = SelectedAnnotationSet;
            if (project == null || selected == null) return;
            DatasetSplitPlan plan = project.AutoSplitDataset(selected.Id, trainPercent);
            RefreshDatasetAssignments();
            if (plan.Warnings.Count > 0) MessageBox.Show(Strings.DatasetSplitCannotMeet, Strings.DatasetSplitWarning, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void ImportAnnotationJson()
        {
            if (project == null) return;
            using (var dialog = new OpenFileDialog { Filter = "JSON (*.json)|*.json", Multiselect = false })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                string json = File.ReadAllText(dialog.FileName);
                string name = Path.GetFileNameWithoutExtension(dialog.FileName);
                try { project.ImportAnnotationJson(json, name, false); }
                catch (JsonAnnotationImportException exception)
                {
                    if (exception.Message != "JSON contains unknown fields." || MessageBox.Show(Strings.ImportAnnotationUnknownFields, Strings.ImportAnnotationJson, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) throw;
                    project.ImportAnnotationJson(json, name, true);
                }
                RefreshAnnotationSets();
                WriteDiagnostic(DiagnosticEvent.AnnotationJsonImported);
            }
        }

        private async Task ExportImagesAsync(IEnumerable<string> imageIds)
        {
            if (project == null) return;
            using (var dialog = new SaveFileDialog { DefaultExt = "zip", AddExtension = true, Filter = Strings.ImageZipFileFilter, OverwritePrompt = false })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                IProjectSession session = project;
                string[] selected = imageIds == null ? null : imageIds.ToArray();
                exportInProgress = true;
                Enabled = false;
                UseWaitCursor = true;
                try
                {
                    await Task.Run(() => session.ExportImagesZip(dialog.FileName, selected));
                    WriteDiagnostic(DiagnosticEvent.ImagesExported);
                }
                catch (Exception exception)
                {
                    WriteDiagnostic(DiagnosticEvent.OperationFailed);
                    Enabled = true;
                    MessageBox.Show(this, GetErrorMessage(exception), Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally
                {
                    UseWaitCursor = false;
                    Enabled = true;
                    exportInProgress = false;
                }
            }
        }

        private async Task ExportAnnotationJsonAsync()
        {
            AnnotationSet set = annotationSets.SelectedItem as AnnotationSet;
            if (project == null || set == null) return;
            using (var dialog = new SaveFileDialog { DefaultExt = "json", AddExtension = true, Filter = Strings.AnnotationJsonFileFilter, OverwritePrompt = false, FileName = set.Name })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                IProjectSession session = project;
                exportInProgress = true;
                Enabled = false;
                UseWaitCursor = true;
                try
                {
                    await Task.Run(() => session.ExportAnnotationJson(set.Id, dialog.FileName));
                    WriteDiagnostic(DiagnosticEvent.AnnotationJsonExported);
                }
                catch (Exception exception)
                {
                    WriteDiagnostic(DiagnosticEvent.OperationFailed);
                    Enabled = true;
                    MessageBox.Show(this, GetErrorMessage(exception), Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally { UseWaitCursor = false; Enabled = true; exportInProgress = false; }
            }
        }

        private void CreateObdRectangle(Rectangle rectangle)
        {
            AnnotationSet selected = SelectedAnnotationSet;
            if (project == null || selected == null || selected.Type != AnnotationSetType.Obd || selectedImageId == null) return;
            AnnotationClass value = project.GetClasses(selected.Id).FirstOrDefault();
            if (value == null) return;
            project.AddObdAnnotation(selected.Id, selectedImageId, value.Id, rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);
            RefreshAnnotationClasses();
        }

        private async Task CreateSegRegionAsync(SegRegionType type, Rectangle rectangle)
        {
            AnnotationSet selected = SelectedAnnotationSet;
            IProjectSession session = project;
            string imageId = selectedImageId;
            if (annotationOperationInProgress || session == null || selected == null || selected.Type != AnnotationSetType.Seg || imageId == null) return;
            AnnotationClass value = session.GetClasses(selected.Id).FirstOrDefault(item => item.Id == selectedSegClassId);
            if (value == null) return;
            string annotationSetId = selected.Id;
            if (type == SegRegionType.Rect)
                await RunSegOperationAsync(session, annotationSetId, imageId, () => session.AddSegRegion(annotationSetId, imageId, value.Id, type, rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height));
            else
            {
                int radiusX = rectangle.Width / 2;
                int radiusY = rectangle.Height / 2;
                int centerX = rectangle.X + radiusX;
                int centerY = rectangle.Y + radiusY;
                await RunSegOperationAsync(session, annotationSetId, imageId, () => session.AddSegRegion(annotationSetId, imageId, value.Id, type, centerX, centerY, radiusX, radiusY));
            }
        }

        private async Task CreateSegPathAsync(SegRegionType type, IReadOnlyList<SegPoint> points, int width)
        {
            AnnotationSet selected = SelectedAnnotationSet;
            IProjectSession session = project;
            string imageId = selectedImageId;
            if (annotationOperationInProgress || session == null || selected == null || selected.Type != AnnotationSetType.Seg || imageId == null) return;
            AnnotationClass value = session.GetClasses(selected.Id).FirstOrDefault(item => item.Id == selectedSegClassId);
            if (value == null) return;
            string annotationSetId = selected.Id;
            await RunSegOperationAsync(session, annotationSetId, imageId, () => session.AddSegPath(annotationSetId, imageId, value.Id, type, points, width));
        }

        private async Task EraseSegPathAsync(IReadOnlyList<SegPoint> points, int width)
        {
            AnnotationSet selected = SelectedAnnotationSet;
            IProjectSession session = project;
            string imageId = selectedImageId;
            string classId = selectedSegClassId;
            if (annotationOperationInProgress || session == null || selected == null || selected.Type != AnnotationSetType.Seg || imageId == null || classId == null) return;
            string annotationSetId = selected.Id;
            await RunSegOperationAsync(session, annotationSetId, imageId, () => session.EraseSegPath(annotationSetId, imageId, classId, points, width));
        }

        private async Task RunSegOperationAsync(IProjectSession session, string annotationSetId, string imageId, Action command)
        {
            Exception failure = null;
            annotationOperationInProgress = true;
            projectMenu.Enabled = false;
            UseWaitCursor = true;
            try
            {
                await Task.Run(command);
            }
            catch (Exception exception) { failure = exception; }
            finally
            {
                UseWaitCursor = false;
                projectMenu.Enabled = true;
                annotationOperationInProgress = false;
            }
            if (failure is ArgumentException || failure is InvalidOperationException)
                MessageBox.Show(this, Strings.AnnotationValidationFailed, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            else if (failure != null)
                MessageBox.Show(this, Strings.ProjectOperationFailed, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            else if (ReferenceEquals(project, session) && SelectedAnnotationSet?.Id == annotationSetId && selectedImageId == imageId)
            {
                RefreshAnnotationClasses();
                canvas.Refresh();
            }
            ImageSelectedEventArgs pending = pendingImageSelection;
            pendingImageSelection = null;
            if (failure != null) imageBrowser.SelectImage(imageId);
            else if (ReferenceEquals(project, session) && pending != null)
            {
                ApplyImageSelection(pending);
            }
        }

        private void ApplyImageSelection(ImageSelectedEventArgs args)
        {
            selectedImagePath = args.Image.IsMissing ? null : args.Path;
            selectedImageId = args.Image.Id;
            imageCanvas.LoadImage(selectedImagePath);
            canvas.LoadImage(selectedImagePath);
            RefreshAnnotationClasses();
        }

        private void UpdateSegRegions(IReadOnlyList<SegRegion> regions)
        {
            if (project == null || SelectedAnnotationSet == null || SelectedAnnotationSet.Type != AnnotationSetType.Seg || regions.Count == 0) return;
            project.UpdateSegRegions(regions);
            RefreshAnnotationClasses();
        }

        private void DeleteSegRegions(IReadOnlyCollection<string> regionIds)
        {
            if (project == null || SelectedAnnotationSet == null || SelectedAnnotationSet.Type != AnnotationSetType.Seg || regionIds.Count == 0) return;
            project.DeleteSegRegions(regionIds);
            RefreshAnnotationClasses();
        }

        private void ChangeSelectedSegClass(string classId)
        {
            AnnotationSet selected = SelectedAnnotationSet;
            if (project == null || selected == null || selected.Type != AnnotationSetType.Seg || selectedImageId == null) return;
            SegRegion[] regions = project.GetSegRegions(selected.Id, selectedImageId).Where(region => canvas.SelectedSegIds.Contains(region.Id)).ToArray();
            if (regions.Length > 0) project.UpdateSegRegions(regions.Select(region => new SegRegion(region.Id, region.AnnotationSetId, region.ImageId, classId, region.Type, region.X, region.Y, region.Width, region.Height, region.ZOrder, region.Points, region.StrokeWidth)));
            RefreshAnnotationClasses();
        }

        private void UpdateObdRectangles(IReadOnlyDictionary<string, Rectangle> rectangles)
        {
            AnnotationSet selected = SelectedAnnotationSet;
            if (project == null || selected == null || selected.Type != AnnotationSetType.Obd || selectedImageId == null) return;
            ObdAnnotation[] originals = project.GetObdAnnotations(selected.Id, selectedImageId).Where(item => rectangles.ContainsKey(item.Id)).ToArray();
            if (originals.Length != rectangles.Count) return;
            project.UpdateObdAnnotations(originals.Select(annotation =>
            {
                Rectangle rectangle = rectangles[annotation.Id];
                return new ObdAnnotation(annotation.Id, annotation.AnnotationSetId, annotation.ImageId, annotation.ClassId, rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height, annotation.ZOrder);
            }));
            RefreshAnnotationClasses();
        }

        private void DeleteObdRectangles(IReadOnlyCollection<string> annotationIds)
        {
            AnnotationSet selected = SelectedAnnotationSet;
            if (project == null || selected == null || selected.Type != AnnotationSetType.Obd || selectedImageId == null) return;
            if (project.GetObdAnnotations(selected.Id, selectedImageId).Count(item => annotationIds.Contains(item.Id)) != annotationIds.Count) return;
            project.DeleteObdAnnotations(annotationIds);
            RefreshAnnotationClasses();
        }

        private void ChangeSelectedObdClass(string classId)
        {
            AnnotationSet selected = SelectedAnnotationSet;
            if (project == null || selected == null || selectedImageId == null || canvas.SelectedObdIds.Count == 0) return;
            ObdAnnotation[] annotations = project.GetObdAnnotations(selected.Id, selectedImageId).Where(item => canvas.SelectedObdIds.Contains(item.Id)).ToArray();
            if (annotations.Length != canvas.SelectedObdIds.Count) return;
            if (annotations.All(annotation => annotation.ClassId == classId)) return;
            project.UpdateObdAnnotations(annotations.Select(annotation => new ObdAnnotation(annotation.Id, annotation.AnnotationSetId, annotation.ImageId, classId, annotation.X, annotation.Y, annotation.Width, annotation.Height, annotation.ZOrder)));
            RefreshAnnotationClasses();
        }

        private void RenameSelectedAnnotationSet()
        {
            AnnotationSet selected = SelectedAnnotationSet;
            if (project == null || selected == null) return;
            string name = Prompt(Strings.AnnotationSetNamePrompt);
            if (name == null) return;
            project.RenameAnnotationSet(selected.Id, name);
            RefreshAnnotationSets();
        }

        private void DeleteSelectedAnnotationSet()
        {
            AnnotationSet selected = SelectedAnnotationSet;
            if (project == null || selected == null) return;
            string name = Prompt(Strings.DeleteAnnotationSetPrompt);
            if (name == null) return;
            project.DeleteAnnotationSet(selected.Id, name);
            RefreshAnnotationSets();
        }

        private void AddAnnotationClass()
        {
            AnnotationSet selected = SelectedAnnotationSet;
            if (project == null || selected == null) return;
            string name = Prompt(Strings.AnnotationClassNamePrompt);
            if (name == null) return;
            project.AddClass(selected.Id, name, "#FF0000");
            RefreshAnnotationClasses();
        }

        private void RenameAnnotationClass(AnnotationClass value)
        {
            string name = Prompt(Strings.AnnotationClassNamePrompt);
            if (name == null) return;
            project.RenameClass(value.Id, name);
            RefreshAnnotationClasses();
        }

        private void ChangeAnnotationClassColor(AnnotationClass value)
        {
            using (var dialog = new ColorDialog { Color = ColorTranslator.FromHtml(value.Color) })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                project.ChangeClassColor(value.Id, ColorTranslator.ToHtml(dialog.Color));
                RefreshAnnotationClasses();
            }
        }

        private void DeleteAnnotationClass(AnnotationClass value)
        {
            if (MessageBox.Show(this, Strings.DeleteAnnotationClassPrompt, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            project.DeleteClass(value.Id);
            RefreshAnnotationClasses();
        }

        private void UndoOrRedoSelectedAnnotationSet(bool redo)
        {
            AnnotationSet selected = SelectedAnnotationSet;
            if (project == null || selected == null) return;
            RunAnnotationCommand(() =>
            {
                if (redo) project.Redo(selected.Id); else project.Undo(selected.Id);
                RefreshAnnotationClasses();
                RefreshDatasetAssignments();
            });
        }

        private void RunAnnotationCommand(Action command)
        {
            try { command(); }
            catch (ArgumentException) { MessageBox.Show(this, Strings.AnnotationValidationFailed, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            catch (InvalidOperationException) { MessageBox.Show(this, Strings.AnnotationValidationFailed, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            catch (Exception) { MessageBox.Show(this, Strings.ProjectOperationFailed, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private static string Prompt(string prompt)
        {
            using (var dialog = new Form { Text = prompt, Width = 320, Height = 125, StartPosition = FormStartPosition.CenterParent })
            using (var input = new TextBox { Dock = DockStyle.Top })
            using (var ok = new Button { Text = Strings.Ok, Dock = DockStyle.Bottom, DialogResult = DialogResult.OK })
            {
                dialog.Controls.Add(input);
                dialog.Controls.Add(ok);
                dialog.AcceptButton = ok;
                return dialog.ShowDialog() == DialogResult.OK ? input.Text : null;
            }
        }
        private void ManageBackups()
        {
            if (project == null) return;
            using (var dialog = new BackupManagementForm(project))
            {
                if (dialog.ShowDialog(this) == DialogResult.OK && dialog.RestoredProject != null) ReplaceProject(dialog.RestoredProject);
            }
        }

        private void RestoreCorruptProject(ProjectOpenException exception)
        {
            if (exception.Backups.Count == 0)
            {
                MessageBox.Show(this, Strings.CorruptProjectNoBackup, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            if (MessageBox.Show(this, Strings.CorruptProjectRestorePrompt, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Error) != DialogResult.Yes) return;
            try
            {
                ReplaceProject(projectLifecycle.Restore(exception.ProjectPath, exception.Backups[0].Path));
            }
            catch (Exception)
            {
                MessageBox.Show(this, Strings.ProjectOperationFailed, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async Task ImportFilesAsync()
        {
            if (project == null) return;
            using (var dialog = new OpenFileDialog { Filter = Strings.ImageFileFilter, Multiselect = true })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                IProjectSession session = project;
                string[] selectedFiles = dialog.FileNames.ToArray();
                await ImportAsync(progress => session.ImportFilesWithReport(selectedFiles, () => progress.IsCancellationRequested));
            }
        }

        private async Task ImportFolderAsync()
        {
            if (project == null) return;
            using (var dialog = new FolderBrowserDialog())
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                DialogResult choice = MessageBox.Show(this, Strings.IncludeSubdirectoriesPrompt, Text, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (choice == DialogResult.Cancel) return;
                IProjectSession session = project;
                string selectedPath = dialog.SelectedPath;
                await ImportAsync(progress => session.ImportDirectory(selectedPath, choice == DialogResult.Yes, () => progress.IsCancellationRequested));
            }
        }

        private async Task ImportAsync(Func<ImportProgressForm, ImageImportReport> import)
        {
            using (var progress = new ImportProgressForm())
            {
                importInProgress = true;
                projectMenu.Enabled = false;
                progress.Show(this);
                try
                {
                    ImageImportReport report = await Task.Run(() => import(progress));
                    WriteDiagnostic(DiagnosticEvent.ImageImportCompleted);
                    MessageBox.Show(this, FormatImportReport(report), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception exception)
                {
                    WriteDiagnostic(DiagnosticEvent.OperationFailed);
                    MessageBox.Show(this, GetErrorMessage(exception), Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally
                {
                    progress.Close();
                    projectMenu.Enabled = true;
                    importInProgress = false;
                    if (project != null) imageBrowser.LoadImages(project.GetImages(), GetImagesDirectory(project.ProjectPath));
                }
            }
        }

        private static SaveFileDialog CreateProjectDialog()
        {
            return new SaveFileDialog { DefaultExt = "neurocle", AddExtension = true, Filter = Strings.ProjectFileFilter };
        }

        private void ReplaceProject(IProjectSession replacement)
        {
            IProjectSession previous = project;
            project = replacement;
            previous?.Dispose();
            ResetAnnotationOpacity();
            canvas.DiscardUnfinishedShape();
            UpdateProjectStatus();
            imageBrowser.LoadImages(project.GetImages(), GetImagesDirectory(project.ProjectPath));
            RefreshAnnotationSets();
        }

        private void UpdateProjectStatus()
        {
            string projectName = Path.GetFileName(project.ProjectPath);
            int missingCount = project.GetMissingImages().Count;
            status.Text = missingCount == 0 ? projectName : projectName + " - " + string.Format(Strings.MissingImagesStatus, missingCount);
        }

        private static string GetImagesDirectory(string projectPath)
        {
            return Path.Combine(Path.GetDirectoryName(projectPath), Path.GetFileNameWithoutExtension(projectPath) + ".images");
        }

        private void RunProjectCommand(Action command)
        {
            try
            {
                command();
            }
            catch (Exception exception)
            {
                WriteDiagnostic(DiagnosticEvent.OperationFailed);
                MessageBox.Show(this, GetErrorMessage(exception), Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void WriteDiagnostic(DiagnosticEvent diagnosticEvent)
        {
            try { diagnosticLog.Write(diagnosticEvent); }
            catch (DiagnosticLogException exception)
            {
                string message = DiagnosticLogMessages.GetFailureMessage(exception);
                status.Text = message;
                MessageBox.Show(this, message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static string GetErrorMessage(Exception exception)
        {
            var imageImportException = exception as ImageImportException;
            if (imageImportException == null) return Strings.ProjectOperationFailed;
            switch (imageImportException.Failure)
            {
                case ImageImportFailure.UnsupportedFormat: return Strings.ImageImportUnsupportedFormat;
                case ImageImportFailure.FileSizeLimit: return Strings.ImageImportFileSizeLimit;
                case ImageImportFailure.DimensionLimit: return Strings.ImageImportDimensionLimit;
                case ImageImportFailure.ProjectImageLimit: return Strings.ImageImportProjectLimit;
                case ImageImportFailure.FileNotFound: return Strings.ImageImportFileNotFound;
                case ImageImportFailure.InvalidImage: return Strings.ImageImportInvalidImage;
                case ImageImportFailure.DirectoryNotFound: return Strings.ImageImportDirectoryNotFound;
                case ImageImportFailure.DirectoryAccess: return Strings.ImageImportDirectoryAccess;
                default: return Strings.ProjectOperationFailed;
            }
        }

        private static string FormatImportReport(ImageImportReport report)
        {
            string unprocessedCount = report.WasCancelledDuringScan ? Strings.ImportReportUnknownCount : report.UnprocessedCount.ToString();
            string summary = string.Format(Strings.ImportReportSummary, report.Imported.Count, report.Failures.Count, unprocessedCount);
            if (report.WasCancelledDuringScan) summary += Environment.NewLine + Strings.ImportScanCancelled;
            if (report.Failures.Count == 0) return summary;
            string failures = string.Join(Environment.NewLine, report.Failures.Select(failure => Path.GetFileName(failure.FilePath) + ": " + GetImportFailureMessage(failure.Failure)));
            return summary + Environment.NewLine + failures;
        }

        private static string GetImportFailureMessage(ImageImportFailure failure)
        {
            switch (failure)
            {
                case ImageImportFailure.UnsupportedFormat: return Strings.ImageImportUnsupportedFormat;
                case ImageImportFailure.FileSizeLimit: return Strings.ImageImportFileSizeLimit;
                case ImageImportFailure.DimensionLimit: return Strings.ImageImportDimensionLimit;
                case ImageImportFailure.ProjectImageLimit: return Strings.ImageImportProjectLimit;
                case ImageImportFailure.FileNotFound: return Strings.ImageImportFileNotFound;
                case ImageImportFailure.InvalidImage: return Strings.ImageImportInvalidImage;
                case ImageImportFailure.DirectoryNotFound: return Strings.ImageImportDirectoryNotFound;
                case ImageImportFailure.DirectoryAccess: return Strings.ImageImportDirectoryAccess;
                default: return Strings.ImageImportUnknownFailure;
            }
        }
    }
}

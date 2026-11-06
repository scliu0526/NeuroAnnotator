using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using Neurocle.Application.Projects;
using Neurocle.Domain.Segmentation;
using Neurocle.Infrastructure.Persistence;
using Neurocle.WinForms.Resources;

namespace Neurocle.WinForms
{
    // 管理專案操作、標註工作流程及主視窗頁面。
    public sealed partial class MainForm : Form
    {
        // 提供建立、開啟、儲存與替換專案的生命週期操作。
        private readonly IProjectLifecycle projectLifecycle;
        // 保存主視窗共用的診斷事件記錄器。
        private readonly DiagnosticLog diagnosticLog;
        // 保存目前開啟的專案工作階段。
        private IProjectSession project;
        private Label status;
        private Label toastMessage;
        private Timer toastTimer;
        private ToolStripMenuItem projectMenu;
        private ToolStripMenuItem jobMenu;
        private ToolStripMenuItem manageMenu;
        private ImageBrowserControl imageBrowser;
        // 保存圖片瀏覽頁使用的畫布。
        private ImageCanvasControl imageCanvas;
        // 保存標註頁使用的畫布。
        private ImageCanvasControl canvas;
        private TabControl pages;
        private TabPage imagesPage;
        private TabPage annotationPage;
        private TabPage datasetPage;
        private TabPage exportPage;
        // 追蹤目前鍵盤與工具操作作用中的畫布。
        private ImageCanvasControl activeCanvas;
        // 保存資料集指派頁面的操作控制項。
        private DatasetAssignmentControl datasetAssignments;
        // 保存標註頁目前選取的標註集欄位。
        private ComboBox annotationSets;
        // 保存資料集頁面目前選取的標註集欄位。
        private ComboBox datasetAnnotationSets;
        private Panel annotationSidebar;
        // 承載目前標註集的類別控制項。
        private FlowLayoutPanel annotationClasses;
        // 承載標註工具按鈕並管理其版面。
        private FlowLayoutPanel annotationTools;
        // 提供工具名稱及快捷鍵提示。
        private ToolTip annotationToolTips;
        // 保留自載入的工具圖示以便視窗結束時釋放。
        private readonly List<Image> annotationToolIcons = new List<Image>();
        private NumericUpDown segStrokeWidth;
        private TrackBar segStrokeWidthSlider;
        private TrackBar annotationOpacity;
        private NumericUpDown annotationOpacityValue;
        private CheckBox showAnnotations;
        private Button addClass;
        private Button obdSelect;
        private Button obdRectangle;
        private Button segSelect;
        private Button segRectangle;
        private Button segEllipse;
        private Button segPolygon;
        private Button segBrush;
        private Button segLine;
        private Button segEraser;
        private MenuStrip menu;
        private ToolStripMenuItem helpMenu;
        private ToolStripMenuItem newProjectMenuItem;
        private ToolStripMenuItem openProjectMenuItem;
        private ToolStripMenuItem saveProjectMenuItem;
        private ToolStripMenuItem saveProjectAsMenuItem;
        private ToolStripMenuItem importImagesMenuItem;
        private ToolStripMenuItem importFolderMenuItem;
        private ToolStripMenuItem importAnnotationMenuItem;
        private ToolStripMenuItem importQueueMenuItem;
        private ToolStripMenuItem missingImagesMenuItem;
        private ToolStripMenuItem backupsMenuItem;
        private ToolStripMenuItem diagnosticsMenuItem;
        private ToolStripMenuItem shortcutsMenuItem;
        private CanvasScrollHost imageCanvasHost;
        private CanvasScrollHost annotationCanvasHost;
        private Panel imageHeader;
        private FlowLayoutPanel imageControls;
        private FlowLayoutPanel annotationHeader;
        private FlowLayoutPanel datasetHeader;
        private Button newSet;
        private Button renameSet;
        private Button deleteSet;
        private Button importAnnotationButton;
        private Button fit;
        private ComboBox zoom;
        private Button imageFit;
        private Button importImagesButton;
        private Button importFolderButton;
        private ComboBox imageZoom;
        private Label annotationZoomText;
        private Label imageZoomText;
        private Button deleteSelectedImagesButton;
        private Button deleteAllImagesButton;
        private TableLayoutPanel segWidths;
        private Label segWidthLabel;
        private Label segOpacityLabel;
        private Label datasetSetLabel;
        private Button exportJson;
        private Button exportSelected;
        private Button exportAll;
        // 保存目前選取圖片的檔案路徑。
        private string selectedImagePath;
        // 保存目前選取圖片的專案識別碼。
        private string selectedImageId;
        // 保存目前選取的 SEG 類別識別碼。
        private string selectedSegClassId;
        // 保存目前選取的 OBD 類別識別碼。
        private string selectedObdClassId;
        // 避免同時啟動多個圖片匯入工作。
        private bool importInProgress;
        // 依加入順序執行目前專案的圖片匯入工作。
        private ImportJobQueue importJobQueue;
        // 顯示目前專案的圖片匯入工作狀態。
        private ImportQueueForm importQueueForm;
        // 記錄已完成畫面更新的匯入工作。
        private readonly HashSet<Guid> handledImageImportJobs = new HashSet<Guid>();
        private readonly HashSet<Guid> handledImportConfirmations = new HashSet<Guid>();
        // 避免重疊執行需要背景處理的標註操作。
        private bool annotationOperationInProgress;
        // 保存標註操作期間等待套用的圖片選取事件。
        private ImageSelectedEventArgs pendingImageSelection;
        // 避免同步兩畫布視角時產生遞迴事件。
        private bool synchronizingCanvasView;
        // 避免重新載入標註集清單時觸發選取副作用。
        private bool refreshingAnnotationSets;
        // 避免標註頁與資料集頁同步選取時互相觸發。
        private bool synchronizingAnnotationSetSelection;
        // 追蹤畫布目前實際顯示的標註集識別碼。
        private string displayedAnnotationSetId;
        // 暫存剪貼簿中的 OBD 標註副本。
        private IReadOnlyList<ObdAnnotation> copiedObdAnnotations = new ObdAnnotation[0];
        // 暫存剪貼簿中的 SEG 區域副本。
        private IReadOnlyList<SegRegion> copiedSegRegions = new SegRegion[0];
        // 提供圖片刪除確認的可替換判斷函式。
        private Func<string, bool> confirmImageDeletion;
        // 提供文字輸入提示的可替換函式。
        private Func<string, string> promptText = Prompt;
        // 提供刪除拒絕提示的可替換函式。
        private Action<string> showDeleteWarning = message => MessageBox.Show(message, Strings.DeleteAnnotationSet, MessageBoxButtons.OK, MessageBoxIcon.Warning);

        // 提供測試存取圖片瀏覽控制項。
        internal ImageBrowserControl ImageBrowser { get { return imageBrowser; } }
        // 提供測試存取圖片瀏覽畫布。
        internal ImageCanvasControl ImageCanvas { get { return imageCanvas; } }
        // 提供測試存取標註畫布。
        internal ImageCanvasControl AnnotationCanvas { get { return canvas; } }
        // 提供測試存取資料集指派控制項。
        internal DatasetAssignmentControl DatasetAssignments { get { return datasetAssignments; } }

        public MainForm(IProjectLifecycle projectLifecycle)
            : this(projectLifecycle, DiagnosticLog.CreateDefault())
        {
        }

        /// <summary>
        /// 建立供 Visual Studio Designer 使用的主視窗版面。
        /// </summary>
        public MainForm()
        {
            InitializeComponent();
        }

        public MainForm(IProjectLifecycle projectLifecycle, DiagnosticLog diagnosticLog)
        {
            this.projectLifecycle = projectLifecycle ?? throw new ArgumentNullException(nameof(projectLifecycle));
            this.diagnosticLog = diagnosticLog ?? throw new ArgumentNullException(nameof(diagnosticLog));
            confirmImageDeletion = message => MessageBox.Show(this, message, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes;
            InitializeComponent();
            WindowState = FormWindowState.Maximized;
            activeCanvas = imageCanvas;
            InitializeRuntimeBehavior();
        }

        /// <summary>
        /// 在主視窗關閉前確認匯入佇列，並避免正在匯出或標註操作時離開。
        /// </summary>
        /// <param name="sender">正在關閉的主視窗。</param>
        /// <param name="args">包含關閉原因及取消狀態的事件資料。</param>
        private void MainForm_FormClosing(object sender, FormClosingEventArgs args)
        {
            if (importJobQueue != null && importJobQueue.IsBusy)
            {
                IReadOnlyList<ImportJobSnapshot> jobs = importJobQueue.Jobs;
                int running = jobs.Count(job => job.Status == ImportJobStatus.Running);
                int waiting = jobs.Count(job => job.Status == ImportJobStatus.Waiting);
                DialogResult choice = MessageBox.Show(this, string.Format(Strings.ImportQueueClosePrompt, running, waiting), Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (choice != DialogResult.Yes)
                {
                    args.Cancel = true;
                    return;
                }
                importJobQueue.CancelAll();
                if (!importJobQueue.WaitUntilIdle(TimeSpan.FromSeconds(30)))
                {
                    args.Cancel = true;
                    return;
                }
            }
            if ((importJobQueue != null && importJobQueue.IsBusy) || annotationOperationInProgress) args.Cancel = true;
        }

        /// <summary>
        /// 釋放提示訊息、匯入佇列、佇列視窗及目前專案資源。
        /// </summary>
        /// <param name="sender">已關閉的主視窗。</param>
        /// <param name="args">視窗關閉事件資料。</param>
        private void MainForm_FormClosed(object sender, FormClosedEventArgs args)
        {
            DisposeToastMessage();
            importJobQueue?.Dispose();
            importQueueForm?.Dispose();
            project?.Dispose();
        }

        /// <summary>
        /// 處理標註復原、重做、複製、貼上及全選快捷鍵。
        /// </summary>
        /// <param name="sender">接收快捷鍵的主視窗。</param>
        /// <param name="args">包含按鍵與修飾鍵狀態的事件資料。</param>
        private void MainForm_AnnotationShortcutKeyDown(object sender, KeyEventArgs args)
        {
            if (!args.Control) return;
            if (HasTextInputFocus() && (args.KeyCode == Keys.Z || args.KeyCode == Keys.Y)) return;
            if (args.KeyCode == Keys.Z) UndoOrRedoSelectedAnnotationSet(args.Shift);
            else if (args.KeyCode == Keys.Y) UndoOrRedoSelectedAnnotationSet(true);
            else if (args.KeyCode == Keys.C && !HasTextInputFocus()) CopySelectedAnnotations();
            else if (args.KeyCode == Keys.V && !HasTextInputFocus()) PasteCopiedAnnotations();
            else if (args.KeyCode == Keys.A && pages.SelectedTab == annotationPage && !HasTextInputFocus()) SelectAllAnnotations();
            else return;
            args.Handled = true;
            args.SuppressKeyPress = true;
        }

        /// <summary>
        /// 依工具快捷鍵切換目前標註工具。
        /// </summary>
        /// <param name="sender">接收快捷鍵的主視窗。</param>
        /// <param name="args">包含按鍵及事件處理狀態的資料。</param>
        private void MainForm_ToolShortcutKeyDown(object sender, KeyEventArgs args)
        {
            if (args.Handled || args.Control || args.Alt || HasTextInputFocus()) return;
            if (TrySelectAnnotationToolByShortcut(args.KeyCode))
            {
                args.Handled = true;
                args.SuppressKeyPress = true;
            }
        }

        /// <summary>
        /// 以方向鍵預覽移動目前選取的標註。
        /// </summary>
        /// <param name="sender">接收方向鍵的主視窗。</param>
        /// <param name="args">包含方向鍵與事件處理狀態的資料。</param>
        private void MainForm_AnnotationNudgeKeyDown(object sender, KeyEventArgs args)
        {
            if (args.Handled || args.Control || HasTextInputFocus() || pages.SelectedTab != annotationPage) return;
            int deltaX = args.KeyCode == Keys.Left ? -1 : args.KeyCode == Keys.Right ? 1 : 0;
            int deltaY = args.KeyCode == Keys.Up ? -1 : args.KeyCode == Keys.Down ? 1 : 0;
            if (canvas.NudgeSelectedAnnotations(deltaX, deltaY))
            {
                args.Handled = true;
                args.SuppressKeyPress = true;
            }
        }

        /// <summary>
        /// 在方向鍵放開時提交鍵盤移動的標註操作。
        /// </summary>
        /// <param name="sender">接收按鍵放開事件的主視窗。</param>
        /// <param name="args">包含放開按鍵及事件處理狀態的資料。</param>
        private void MainForm_AnnotationNudgeKeyUp(object sender, KeyEventArgs args)
        {
            if ((args.KeyCode == Keys.Up || args.KeyCode == Keys.Down || args.KeyCode == Keys.Left || args.KeyCode == Keys.Right) && canvas.CommitKeyboardNudge())
            {
                args.Handled = true;
                args.SuppressKeyPress = true;
            }
        }

        /// <summary>
        /// 處理適用於圖片與標註頁面的全域視角、顯示、筆寬及圖片瀏覽快捷鍵。
        /// </summary>
        /// <param name="sender">接收快捷鍵的主視窗。</param>
        /// <param name="args">包含按鍵、修飾鍵及事件處理狀態的資料。</param>
        private void MainForm_GlobalShortcutKeyDown(object sender, KeyEventArgs args)
        {
            if (args.Handled) return;
            if (args.Control || HasTextInputFocus() || (pages.SelectedTab != imagesPage && pages.SelectedTab != annotationPage)) return;
            if (args.KeyCode == Keys.F)
            {
                if (activeCanvas == null) return;
                activeCanvas.CancelInProgressGesture();
                activeCanvas.FitToWindow();
                args.Handled = true;
                args.SuppressKeyPress = true;
                return;
            }
            if (args.KeyCode == Keys.OemOpenBrackets && pages.SelectedTab == annotationPage)
            {
                showAnnotations.Checked = !showAnnotations.Checked;
                args.Handled = true;
                args.SuppressKeyPress = true;
                return;
            }
            if (pages.SelectedTab == annotationPage && SelectedAnnotationSet?.Type == AnnotationSetType.Seg && (args.KeyCode == Keys.Z || args.KeyCode == Keys.X))
            {
                SetSegStrokeWidth(Math.Max(1, Math.Min(100, (int)segStrokeWidth.Value + (args.KeyCode == Keys.Z ? -1 : 1))));
                args.Handled = true;
                args.SuppressKeyPress = true;
                return;
            }
            if (args.KeyCode != Keys.Up && args.KeyCode != Keys.Down) return;
            imageBrowser.MoveSelection(args.KeyCode == Keys.Up ? -1 : 1);
            args.Handled = true;
            args.SuppressKeyPress = true;
        }

        /// <summary>
        /// 將資料命令與互動事件接到 Designer 建立的固定控制項。
        /// </summary>
        private void InitializeRuntimeBehavior()
        {
            ConfigureAnnotationToolButton(obdSelect, "選取", "V", "01_cursor.png");
            ConfigureAnnotationToolButton(obdRectangle, "矩形", "R", "06_square.png");
            ConfigureAnnotationToolButton(segRectangle, "矩形", "R", "06_square.png");
            ConfigureAnnotationToolButton(segEllipse, "圓形", "C", "07_circle.png");
            ConfigureAnnotationToolButton(segPolygon, "畫筆", "P", "04_pencil.png");
            ConfigureAnnotationToolButton(segBrush, "筆刷", "B", "03_brush.png");
            ConfigureAnnotationToolButton(segLine, "線形", "L", "05_line.png");
            ConfigureAnnotationToolButton(segEraser, "橡皮擦", "E", "02_eraser.png");
            ConfigureAnnotationToolButton(segSelect, "選取", "V", "01_cursor.png");
            annotationTools.Controls.Clear();
            imageCanvas.ViewChanged += (sender, args) => SynchronizeCanvasView(imageCanvas, canvas);
            canvas.ViewChanged += (sender, args) => SynchronizeCanvasView(canvas, imageCanvas);
            datasetAssignments.DatasetRequested += (ids, value) => RunAnnotationCommand(() => SetDatasetAssignments(ids, value));
            datasetAssignments.RetestRequested += (ids, value) => RunAnnotationCommand(() => SetRetest(ids, value));
            datasetAssignments.ClearClassRequested += ids => RunAnnotationCommand(() => ClearDatasetClasses(ids));
            datasetAssignments.AutoSplitSelectedRequested += (ids, value) => RunAnnotationCommand(() => AutoSplitDataset(ids, value));
            datasetAssignments.AutoSplitAllRequested += value => RunAnnotationCommand(() => AutoSplitDataset(null, value));
            canvas.ObdRectangleCreated += rectangle => RunAnnotationCommand(() => CreateObdRectangle(rectangle));
            canvas.ObdRectanglesChanged += rectangles => RunAnnotationCommand(() => UpdateObdRectangles(rectangles));
            canvas.ObdAnnotationsDeleteRequested += ids => RunAnnotationCommand(() => DeleteObdRectangles(ids));
            canvas.SegRegionCreated += async (type, rectangle) => await CreateSegRegionAsync(type, rectangle);
            canvas.SegPathCreated += async (type, points, width) => await CreateSegPathAsync(type, points, width);
            canvas.SegGeometryEraseRequested += async (type, rectangle, points, width) => await EraseSegGeometryAsync(type, rectangle, points, width);
            canvas.SegEraseRequested += async (points, width) => await EraseSegPathAsync(points, width);
            canvas.SegRegionsChanged += regions => RunAnnotationCommand(() => UpdateSegRegions(regions));
            canvas.SegRegionsDeleteRequested += ids => RunAnnotationCommand(() => DeleteSegRegions(ids));
            imageBrowser.ImageSelected += (sender, args) =>
            {
                if (annotationOperationInProgress) pendingImageSelection = args;
                else ApplyImageSelection(args);
            };

            imageCanvas.ZoomChanged += (sender, args) => UpdateZoomText();
            canvas.ZoomChanged += (sender, args) => UpdateZoomText();
            SetAnnotationOpacity(annotationOpacity.Value);
        }

        /// <summary>
        /// 執行建立專案命令。
        /// </summary>
        /// <param name="sender">引發事件的新增專案選單項目。</param>
        /// <param name="args">選單點擊事件資料。</param>
        private void NewProjectMenuItem_Click(object sender, EventArgs args) { RunProjectCommand(CreateProject); }

        /// <summary>
        /// 執行開啟專案命令。
        /// </summary>
        /// <param name="sender">引發事件的開啟專案選單項目。</param>
        /// <param name="args">選單點擊事件資料。</param>
        private void OpenProjectMenuItem_Click(object sender, EventArgs args) { RunProjectCommand(OpenProject); }

        /// <summary>
        /// 執行儲存目前專案命令。
        /// </summary>
        /// <param name="sender">引發事件的儲存專案選單項目。</param>
        /// <param name="args">選單點擊事件資料。</param>
        private void SaveProjectMenuItem_Click(object sender, EventArgs args) { RunProjectCommand(SaveProject); }

        /// <summary>
        /// 執行另存專案命令。
        /// </summary>
        /// <param name="sender">引發事件的另存專案選單項目。</param>
        /// <param name="args">選單點擊事件資料。</param>
        private void SaveProjectAsMenuItem_Click(object sender, EventArgs args) { RunProjectCommand(SaveProjectAs); }

        /// <summary>
        /// 顯示目前專案的匯入工作佇列。
        /// </summary>
        /// <param name="sender">引發事件的匯入佇列選單項目。</param>
        /// <param name="args">選單點擊事件資料。</param>
        private void ImportQueueMenuItem_Click(object sender, EventArgs args) { ShowImportQueue(); }

        /// <summary>
        /// 開啟遺失圖片管理視窗。
        /// </summary>
        /// <param name="sender">引發事件的遺失圖片選單項目。</param>
        /// <param name="args">選單點擊事件資料。</param>
        private void MissingImagesMenuItem_Click(object sender, EventArgs args) { ManageMissingImages(); }

        /// <summary>
        /// 開啟備份管理視窗。
        /// </summary>
        /// <param name="sender">引發事件的備份選單項目。</param>
        /// <param name="args">選單點擊事件資料。</param>
        private void BackupsMenuItem_Click(object sender, EventArgs args) { ManageBackups(); }

        /// <summary>
        /// 將選取的圖片檔案加入匯入佇列。
        /// </summary>
        /// <param name="sender">引發事件的匯入圖片按鈕。</param>
        /// <param name="args">按鈕點擊事件資料。</param>
        private void ImportImagesButton_Click(object sender, EventArgs args) { QueueImageFiles(); }

        /// <summary>
        /// 將選取的資料夾加入匯入佇列。
        /// </summary>
        /// <param name="sender">引發事件的匯入資料夾按鈕。</param>
        /// <param name="args">按鈕點擊事件資料。</param>
        private void ImportFolderButton_Click(object sender, EventArgs args) { QueueFolder(); }

        /// <summary>
        /// 將選取的標註 JSON 加入匯入佇列。
        /// </summary>
        /// <param name="sender">引發事件的匯入標註按鈕。</param>
        /// <param name="args">按鈕點擊事件資料。</param>
        private void ImportAnnotationButton_Click(object sender, EventArgs args) { QueueAnnotationJson(); }

        /// <summary>
        /// 執行刪除所選圖片命令。
        /// </summary>
        /// <param name="sender">引發事件的刪除所選圖片按鈕。</param>
        /// <param name="args">按鈕點擊事件資料。</param>
        private void DeleteSelectedImagesButton_Click(object sender, EventArgs args) { RunProjectCommand(DeleteSelectedImages); }

        /// <summary>
        /// 執行刪除全部圖片命令。
        /// </summary>
        /// <param name="sender">引發事件的刪除全部圖片按鈕。</param>
        /// <param name="args">按鈕點擊事件資料。</param>
        private void DeleteAllImagesButton_Click(object sender, EventArgs args) { RunProjectCommand(DeleteAllImages); }

        /// <summary>
        /// 開啟診斷記錄視窗。
        /// </summary>
        /// <param name="sender">引發事件的診斷選單項目。</param>
        /// <param name="args">選單點擊事件資料。</param>
        private void DiagnosticsMenuItem_Click(object sender, EventArgs args) { ManageDiagnostics(); }

        /// <summary>
        /// 重新定位標註頁上的提示訊息。
        /// </summary>
        /// <param name="sender">大小已變更的標註頁。</param>
        /// <param name="args">版面尺寸變更事件資料。</param>
        private void AnnotationPage_Resize(object sender, EventArgs args) { PositionToastMessage(); }

        /// <summary>
        /// 同步圖片頁標題列高度以配合標註頁標題列。
        /// </summary>
        /// <param name="sender">大小已變更的標註頁標題列。</param>
        /// <param name="args">控制項大小變更事件資料。</param>
        private void AnnotationHeader_SizeChanged(object sender, EventArgs args) { imageHeader.Height = annotationHeader.Height; }

        /// <summary>
        /// 切換至 OBD 選取工具。
        /// </summary>
        /// <param name="sender">引發事件的 OBD 選取按鈕。</param>
        /// <param name="args">按鈕點擊事件資料。</param>
        private void ObdSelect_Click(object sender, EventArgs args) { SelectObdTool(true); }

        /// <summary>
        /// 切換至 OBD 矩形繪製工具。
        /// </summary>
        /// <param name="sender">引發事件的 OBD 矩形按鈕。</param>
        /// <param name="args">按鈕點擊事件資料。</param>
        private void ObdRectangle_Click(object sender, EventArgs args) { SelectObdTool(false); }

        /// <summary>
        /// 切換至 SEG 矩形工具。
        /// </summary>
        /// <param name="sender">引發事件的 SEG 矩形按鈕。</param>
        /// <param name="args">按鈕點擊事件資料。</param>
        private void SegRectangle_Click(object sender, EventArgs args) { SelectSegTool(ImageCanvasControl.SegToolKind.Rect); }

        /// <summary>
        /// 切換至 SEG 圓形工具。
        /// </summary>
        /// <param name="sender">引發事件的 SEG 圓形按鈕。</param>
        /// <param name="args">按鈕點擊事件資料。</param>
        private void SegEllipse_Click(object sender, EventArgs args) { SelectSegTool(ImageCanvasControl.SegToolKind.Ellipse); }

        /// <summary>
        /// 切換至 SEG 畫筆工具。
        /// </summary>
        /// <param name="sender">引發事件的 SEG 畫筆按鈕。</param>
        /// <param name="args">按鈕點擊事件資料。</param>
        private void SegPolygon_Click(object sender, EventArgs args) { SelectSegTool(ImageCanvasControl.SegToolKind.Polygon); }

        /// <summary>
        /// 切換至 SEG 筆刷工具。
        /// </summary>
        /// <param name="sender">引發事件的 SEG 筆刷按鈕。</param>
        /// <param name="args">按鈕點擊事件資料。</param>
        private void SegBrush_Click(object sender, EventArgs args) { SelectSegTool(ImageCanvasControl.SegToolKind.Brush); }

        /// <summary>
        /// 切換至 SEG 線形工具。
        /// </summary>
        /// <param name="sender">引發事件的 SEG 線形按鈕。</param>
        /// <param name="args">按鈕點擊事件資料。</param>
        private void SegLine_Click(object sender, EventArgs args) { SelectSegTool(ImageCanvasControl.SegToolKind.Line); }

        /// <summary>
        /// 切換至 SEG 橡皮擦工具。
        /// </summary>
        /// <param name="sender">引發事件的 SEG 橡皮擦按鈕。</param>
        /// <param name="args">按鈕點擊事件資料。</param>
        private void SegEraser_Click(object sender, EventArgs args) { SelectSegTool(ImageCanvasControl.SegToolKind.Eraser); }

        /// <summary>
        /// 切換至 SEG 選取工具。
        /// </summary>
        /// <param name="sender">引發事件的 SEG 選取按鈕。</param>
        /// <param name="args">按鈕點擊事件資料。</param>
        private void SegSelect_Click(object sender, EventArgs args) { SelectSegTool(ImageCanvasControl.SegToolKind.Select); }

        /// <summary>
        /// 顯示目前版本的鍵盤快捷鍵說明。
        /// </summary>
        /// <param name="sender">引發事件的快捷鍵選單項目。</param>
        /// <param name="args">選單點擊事件資料。</param>
        private void ShortcutsMenuItem_Click(object sender, EventArgs args) { using (var dialog = new KeyboardShortcutsForm()) dialog.ShowDialog(this); }

        /// <summary>
        /// 處理標註頁標註集選取變更。
        /// </summary>
        /// <param name="sender">引發事件的標註集清單。</param>
        /// <param name="args">選取項目變更事件資料。</param>
        private void AnnotationSets_SelectedIndexChanged(object sender, EventArgs args) { AnnotationSetSelectionChanged(annotationSets); }

        /// <summary>
        /// 處理資料集頁標註集選取變更。
        /// </summary>
        /// <param name="sender">引發事件的資料集標註集清單。</param>
        /// <param name="args">選取項目變更事件資料。</param>
        private void DatasetAnnotationSets_SelectedIndexChanged(object sender, EventArgs args) { AnnotationSetSelectionChanged(datasetAnnotationSets); }

        /// <summary>
        /// 執行建立標註集命令。
        /// </summary>
        /// <param name="sender">引發事件的新增標註集按鈕。</param>
        /// <param name="args">按鈕點擊事件資料。</param>
        private void NewSet_Click(object sender, EventArgs args) { RunAnnotationCommand(CreateAnnotationSet); }

        /// <summary>
        /// 執行重新命名所選標註集命令。
        /// </summary>
        /// <param name="sender">引發事件的重新命名按鈕。</param>
        /// <param name="args">按鈕點擊事件資料。</param>
        private void RenameSet_Click(object sender, EventArgs args) { RunAnnotationCommand(RenameSelectedAnnotationSet); }

        /// <summary>
        /// 執行刪除所選標註集命令。
        /// </summary>
        /// <param name="sender">引發事件的刪除標註集按鈕。</param>
        /// <param name="args">按鈕點擊事件資料。</param>
        private void DeleteSet_Click(object sender, EventArgs args) { RunAnnotationCommand(DeleteSelectedAnnotationSet); }

        /// <summary>
        /// 執行新增類別命令。
        /// </summary>
        /// <param name="sender">引發事件的新增類別按鈕。</param>
        /// <param name="args">按鈕點擊事件資料。</param>
        private void AddClass_Click(object sender, EventArgs args) { RunAnnotationCommand(AddAnnotationClass); }

        /// <summary>
        /// 套用筆寬數值欄位的新值。
        /// </summary>
        /// <param name="sender">數值已變更的筆寬欄位。</param>
        /// <param name="args">數值變更事件資料。</param>
        private void SegStrokeWidth_ValueChanged(object sender, EventArgs args) { SetSegStrokeWidth((int)segStrokeWidth.Value); }

        /// <summary>
        /// 套用筆寬滑桿的新值。
        /// </summary>
        /// <param name="sender">數值已變更的筆寬滑桿。</param>
        /// <param name="args">數值變更事件資料。</param>
        private void SegStrokeWidthSlider_ValueChanged(object sender, EventArgs args) { SetSegStrokeWidth(segStrokeWidthSlider.Value); }

        /// <summary>
        /// 套用標註透明度滑桿的新值。
        /// </summary>
        /// <param name="sender">數值已變更的透明度滑桿。</param>
        /// <param name="args">數值變更事件資料。</param>
        private void AnnotationOpacity_ValueChanged(object sender, EventArgs args) { SetAnnotationOpacity(annotationOpacity.Value); }

        /// <summary>
        /// 套用標註透明度數值欄位的新值。
        /// </summary>
        /// <param name="sender">數值已變更的透明度欄位。</param>
        /// <param name="args">數值變更事件資料。</param>
        private void AnnotationOpacityValue_ValueChanged(object sender, EventArgs args) { SetAnnotationOpacity((int)annotationOpacityValue.Value); }

        /// <summary>
        /// 切換目前標註集的標註可見狀態。
        /// </summary>
        /// <param name="sender">狀態已變更的顯示標註核取方塊。</param>
        /// <param name="args">核取狀態變更事件資料。</param>
        private void ShowAnnotations_CheckedChanged(object sender, EventArgs args) { SetAnnotationsVisible(showAnnotations.Checked); }

        /// <summary>
        /// 將目前作用中的畫布調整為符合視窗大小。
        /// </summary>
        /// <param name="sender">引發事件的標註頁符合視窗按鈕。</param>
        /// <param name="args">按鈕點擊事件資料。</param>
        private void Fit_Click(object sender, EventArgs args) { activeCanvas.FitToWindow(); }

        /// <summary>
        /// 將目前作用中的畫布調整為符合視窗大小。
        /// </summary>
        /// <param name="sender">引發事件的圖片頁符合視窗按鈕。</param>
        /// <param name="args">按鈕點擊事件資料。</param>
        private void ImageFit_Click(object sender, EventArgs args) { activeCanvas.FitToWindow(); }

        /// <summary>
        /// 套用標註頁縮放選單所選的比例。
        /// </summary>
        /// <param name="sender">選取項目已變更的標註頁縮放選單。</param>
        /// <param name="args">選取項目變更事件資料。</param>
        private void Zoom_SelectedIndexChanged(object sender, EventArgs args) { SetCanvasZoom(zoom.SelectedItem as string); }

        /// <summary>
        /// 套用圖片頁縮放選單所選的比例。
        /// </summary>
        /// <param name="sender">選取項目已變更的圖片頁縮放選單。</param>
        /// <param name="args">選取項目變更事件資料。</param>
        private void ImageZoom_SelectedIndexChanged(object sender, EventArgs args) { SetCanvasZoom(imageZoom.SelectedItem as string); }

        /// <summary>
        /// 開啟標註 JSON 匯出流程。
        /// </summary>
        /// <param name="sender">引發事件的匯出 JSON 按鈕。</param>
        /// <param name="args">按鈕點擊事件資料。</param>
        private async void ExportJson_Click(object sender, EventArgs args) { await ExportAnnotationJsonAsync(); }

        /// <summary>
        /// 匯出圖片清單目前選取的圖片。
        /// </summary>
        /// <param name="sender">引發事件的匯出所選按鈕。</param>
        /// <param name="args">按鈕點擊事件資料。</param>
        private async void ExportSelected_Click(object sender, EventArgs args) { await ExportImagesAsync(imageBrowser.SelectedImageIds); }

        /// <summary>
        /// 匯出專案中的全部圖片。
        /// </summary>
        /// <param name="sender">引發事件的匯出全部按鈕。</param>
        /// <param name="args">按鈕點擊事件資料。</param>
        private async void ExportAll_Click(object sender, EventArgs args) { await ExportImagesAsync(null); }

        /// <summary>
        /// 切換頁面時同步畫布視角、清除未完成形狀並更新資料集列。
        /// </summary>
        /// <param name="sender">目前選取頁籤已變更的頁面控制項。</param>
        /// <param name="args">頁籤選取變更事件資料。</param>
        private void Pages_SelectedIndexChanged(object sender, EventArgs args)
        {
            ImageCanvasControl selectedCanvas = pages.SelectedTab == imagesPage ? imageCanvas : pages.SelectedTab == annotationPage ? canvas : null;
            if (selectedCanvas != null && !ReferenceEquals(activeCanvas, selectedCanvas))
            {
                SynchronizeCanvasView(activeCanvas, selectedCanvas);
                activeCanvas = selectedCanvas;
                UpdateZoomText();
            }
            canvas.DiscardUnfinishedShape();
            if (pages.SelectedTab == datasetPage) RefreshDatasetAssignments();
        }

        /// <summary>
        /// 將目前作用中的畫布設為符合視窗或指定百分比縮放。
        /// </summary>
        /// <param name="selected">符合視窗文字或以百分比表示的縮放值。</param>
        private void SetCanvasZoom(string selected)
        {
            if (selected == Strings.FitToWindow) activeCanvas.FitToWindow();
            else activeCanvas.SetZoomPercent(double.Parse(selected.TrimEnd('%')));
        }

        /// <summary>
        /// 依目前作用中畫布的縮放比例更新兩個頁面的顯示文字。
        /// </summary>
        private void UpdateZoomText()
        {
            if (IsDisposed || Disposing || annotationZoomText.IsDisposed || imageZoomText.IsDisposed) return;
            string value = Math.Round(activeCanvas.Zoom * 100d).ToString() + "%";
            annotationZoomText.Text = value;
            imageZoomText.Text = value;
        }

        /// <summary>
        /// 讓圖片方向鍵先進入既有鍵盤路由，避免被 WinForms 當作焦點導覽鍵消耗。
        /// </summary>
        /// <param name="msg">目前正在處理的 Windows 訊息。</param>
        /// <param name="keyData">按鍵及其修飾鍵狀態。</param>
        /// <returns>若符合摘要所述條件則為 true，否則為 false。</returns>
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Up || keyData == Keys.Down)
            {
                var args = new KeyEventArgs(keyData);
                OnKeyDown(args);
                if (args.Handled) return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        /// <summary>
        /// 判斷焦點是否位於文字輸入控制項，避免快捷鍵干擾輸入。
        /// </summary>
        /// <returns>若符合摘要所述條件則為 true，否則為 false。</returns>
        private bool HasTextInputFocus()
        {
            return FindFocusedTextInput(this);
        }

        /// <summary>
        /// 遞迴尋找控制項樹中目前取得焦點的文字欄位。
        /// </summary>
        /// <param name="control">要檢查焦點的控制項。</param>
        /// <returns>若符合摘要所述條件則為 true，否則為 false。</returns>
        private static bool FindFocusedTextInput(Control control)
        {
            if (control is TextBoxBase && control.Focused) return true;
            return control.Controls.Cast<Control>().Any(FindFocusedTextInput);
        }

        /// <summary>
        /// 依目前標註類型選取畫布上的全部標註。
        /// </summary>
        private void SelectAllAnnotations()
        {
            AnnotationSet selected = SelectedAnnotationSet;
            if (selected == null) return;
            if (selected.Type == AnnotationSetType.Obd && canvas.ObdEnabled) canvas.SelectAllObd();
            else if (selected.Type == AnnotationSetType.Seg && canvas.SegEnabled) canvas.SelectAllSeg();
        }

        /// <summary>
        /// 複製目前選取的 OBD 或 SEG 標註。
        /// </summary>
        private void CopySelectedAnnotations()
        {
            AnnotationSet selected = SelectedAnnotationSet;
            if (pages.SelectedTab != annotationPage || selected == null) return;
            if (selected.Type == AnnotationSetType.Obd && canvas.ObdSelectMode && canvas.SelectedObdIds.Count > 0)
            {
                copiedObdAnnotations = canvas.ObdAnnotations.Where(annotation => canvas.SelectedObdIds.Contains(annotation.Id)).ToArray();
                copiedSegRegions = new SegRegion[0];
            }
            else if (selected.Type == AnnotationSetType.Seg && canvas.SegTool == ImageCanvasControl.SegToolKind.Select && canvas.SelectedSegIds.Count > 0)
            {
                copiedSegRegions = canvas.SegRegions.Where(region => canvas.SelectedSegIds.Contains(region.Id)).ToArray();
                copiedObdAnnotations = new ObdAnnotation[0];
            }
        }

        /// <summary>
        /// 將複製的標註貼到目前圖片並建立可復原命令。
        /// </summary>
        private void PasteCopiedAnnotations()
        {
            AnnotationSet selected = SelectedAnnotationSet;
            IReadOnlyList<string> targetImageIds = imageBrowser.SelectedImageIds;
            if (annotationOperationInProgress || project == null || pages.SelectedTab != annotationPage || selected == null || targetImageIds.Count == 0) return;
            if (selected.Type == AnnotationSetType.Obd && copiedObdAnnotations.Count > 0 && copiedObdAnnotations.All(annotation => annotation.AnnotationSetId == selected.Id))
            {
                HashSet<string> classIds = new HashSet<string>(project.GetClasses(selected.Id).Select(value => value.Id), StringComparer.Ordinal);
                if (copiedObdAnnotations.Any(annotation => !classIds.Contains(annotation.ClassId)))
                {
                    ShowToastMessage(Strings.CopiedAnnotationClassDeleted);
                    return;
                }
            }
            if (selected.Type == AnnotationSetType.Seg && copiedSegRegions.Count > 0 && copiedSegRegions.All(region => region.AnnotationSetId == selected.Id))
            {
                HashSet<string> classIds = new HashSet<string>(project.GetClasses(selected.Id).Select(value => value.Id), StringComparer.Ordinal);
                if (copiedSegRegions.Any(region => !classIds.Contains(region.ClassId)))
                {
                    ShowToastMessage(Strings.CopiedAnnotationClassDeleted);
                    return;
                }
            }
            if (selected.Type == AnnotationSetType.Obd && copiedObdAnnotations.Count > 0 && copiedObdAnnotations.All(annotation => annotation.AnnotationSetId == selected.Id))
            {
                RunAnnotationCommand(() =>
                {
                    IReadOnlyList<ObdAnnotation> copies = project.AddObdAnnotationCopies(selected.Id, targetImageIds, copiedObdAnnotations);
                    if (copies.Count == 0) return;
                    RefreshAnnotationClasses();
                    canvas.SelectObdAnnotations(copies.Where(annotation => annotation.ImageId == selectedImageId).Select(annotation => annotation.Id));
                });
            }
            else if (selected.Type == AnnotationSetType.Seg && copiedSegRegions.Count > 0 && copiedSegRegions.All(region => region.AnnotationSetId == selected.Id))
            {
                RunAnnotationCommand(() =>
                {
                    IReadOnlyList<SegRegion> copies = project.AddSegRegionCopies(selected.Id, targetImageIds, copiedSegRegions);
                    if (copies.Count == 0) return;
                    RefreshAnnotationClasses();
                    canvas.SelectSegRegions(copies.Where(region => region.ImageId == selectedImageId).Select(region => region.Id));
                });
            }
        }

        /// <summary>
        /// 顯示短暫操作提示並更新提示計時器。
        /// </summary>
        /// <param name="message">目前正在處理的 Windows 訊息。</param>
        private void ShowToastMessage(string message)
        {
            DisposeToastMessage();
            toastMessage = new Label { AutoSize = true, BackColor = Color.FromArgb(48, 48, 48), ForeColor = Color.White, Padding = new Padding(12, 8, 12, 8), Text = message };
            annotationPage.Controls.Add(toastMessage);
            PositionToastMessage();
            toastMessage.BringToFront();
            toastTimer = new Timer { Interval = 2600 };
            toastTimer.Tick += (sender, args) =>
            {
                DisposeToastMessage();
            };
            toastTimer.Start();
        }

        /// <summary>
        /// 停止提示計時器並釋放提示控制項。
        /// </summary>
        private void DisposeToastMessage()
        {
            if (toastTimer != null) { toastTimer.Stop(); toastTimer.Dispose(); toastTimer = null; }
            if (toastMessage == null) return;
            annotationPage.Controls.Remove(toastMessage);
            toastMessage.Dispose();
            toastMessage = null;
        }

        /// <summary>
        /// 依主視窗和狀態列位置排列操作提示。
        /// </summary>
        private void PositionToastMessage()
        {
            if (toastMessage == null) return;
            toastMessage.Location = new Point(Math.Max(0, (annotationPage.ClientSize.Width - toastMessage.Width) / 2), Math.Max(0, annotationPage.ClientSize.Height - toastMessage.Height - 24));
        }

        /// <summary>
        /// 同步更新 SEG 筆刷寬度欄位與畫布設定。
        /// </summary>
        /// <param name="width">要使用的筆寬或線寬。</param>
        private void SetSegStrokeWidth(int width)
        {
            if (segStrokeWidth.Value != width) segStrokeWidth.Value = width;
            if (segStrokeWidthSlider.Value != width) segStrokeWidthSlider.Value = width;
            canvas.SegStrokeWidth = width;
        }

        /// <summary>
        /// 同步兩個畫布的縮放倍率與視野位置。
        /// </summary>
        /// <param name="source">提供資料或數值的來源。</param>
        /// <param name="target">要更新的目標控制項或值。</param>
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

        /// <summary>
        /// 切換目前 SEG 工具並更新工具按鈕狀態。
        /// </summary>
        /// <param name="tool">要選取的標註工具。</param>
        private void SelectSegTool(ImageCanvasControl.SegToolKind tool)
        {
            canvas.DiscardUnfinishedShape();
            canvas.SegTool = tool;
            RefreshAnnotationToolButtons();
        }

        /// <summary>
        /// 切換 OBD 選取或矩形繪製工具。
        /// </summary>
        /// <param name="select">指出是否選取所有標註的旗標。</param>
        private void SelectObdTool(bool select)
        {
            canvas.DiscardUnfinishedShape();
            canvas.ObdSelectMode = select;
            RefreshAnnotationToolButtons();
        }

        /// <summary>
        /// 依快捷鍵切換標註工具並回報是否已處理按鍵。
        /// </summary>
        /// <param name="key">要處理的按鍵。</param>
        /// <returns>若符合摘要所述條件則為 true，否則為 false。</returns>
        private bool TrySelectAnnotationToolByShortcut(Keys key)
        {
            if (pages.SelectedTab != annotationPage) return false;
            AnnotationSet selected = SelectedAnnotationSet;
            if (selected == null) return false;
            if (selected.Type == AnnotationSetType.Obd)
            {
                if (key == Keys.V) SelectObdTool(true);
                else if (key == Keys.R) SelectObdTool(false);
                else return false;
                return true;
            }
            if (selected.Type != AnnotationSetType.Seg) return false;
            ImageCanvasControl.SegToolKind tool;
            switch (key)
            {
                case Keys.V: tool = ImageCanvasControl.SegToolKind.Select; break;
                case Keys.B: tool = ImageCanvasControl.SegToolKind.Brush; break;
                case Keys.P: tool = ImageCanvasControl.SegToolKind.Polygon; break;
                case Keys.C: tool = ImageCanvasControl.SegToolKind.Ellipse; break;
                case Keys.R: tool = ImageCanvasControl.SegToolKind.Rect; break;
                case Keys.E: tool = ImageCanvasControl.SegToolKind.Eraser; break;
                case Keys.L: tool = ImageCanvasControl.SegToolKind.Line; break;
                default: return false;
            }
            SelectSegTool(tool);
            return true;
        }

        /// <summary>
        /// 在執行期套用工具圖示與名稱及快捷鍵提示。
        /// </summary>
        /// <param name="button">要更新的按鈕。</param>
        /// <param name="toolName">工具顯示名稱。</param>
        /// <param name="shortcut">工具快捷鍵。</param>
        /// <param name="iconResource">工具圖示資源名稱。</param>
        private void ConfigureAnnotationToolButton(Button button, string toolName, string shortcut, string iconResource)
        {
            button.Text = string.Empty;
            button.Size = new Size(32, 32);
            button.Margin = new Padding(2);
            button.Image = LoadAnnotationToolIcon(iconResource);
            button.ImageAlign = ContentAlignment.MiddleCenter;
            annotationToolTips.SetToolTip(button, toolName + " " + shortcut);
        }

        /// <summary>
        /// 從內嵌資源載入標註工具圖示。
        /// </summary>
        /// <param name="resourceName">對應嵌入式圖片資源的名稱。</param>
        /// <returns>載入的圖片資源。</returns>
        /// <exception cref="InvalidOperationException">目前狀態不允許此操作。</exception>
        private Image LoadAnnotationToolIcon(string resourceName)
        {
            string manifestName = "Neurocle.WinForms.Label_tool." + resourceName;
            using (Stream stream = typeof(MainForm).Assembly.GetManifestResourceStream(manifestName))
            {
                if (stream == null) throw new InvalidOperationException("Missing embedded annotation tool icon: " + resourceName);
                using (Image source = Image.FromStream(stream))
                {
                    Image icon = new Bitmap(source, new Size(22, 22));
                    annotationToolIcons.Add(icon);
                    return icon;
                }
            }
        }

        /// <summary>
        /// 顯示建立專案流程並載入新專案。
        /// </summary>
        private void CreateProject()
        {
            using (var dialog = CreateProjectDialog())
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                ReplaceProject(projectLifecycle.Create(dialog.FileName));
                WriteDiagnostic(DiagnosticEvent.ProjectCreated);
            }
        }

        /// <summary>
        /// 選取並開啟既有專案。
        /// </summary>
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

        /// <summary>
        /// 儲存目前專案。
        /// </summary>
        private void SaveProject()
        {
            project?.Save();
            if (project != null) WriteDiagnostic(DiagnosticEvent.ProjectSaved);
        }

        /// <summary>
        /// 另存目前專案至使用者指定位置。
        /// </summary>
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

        /// <summary>
        /// 刪除圖片瀏覽器目前選取的圖片。
        /// </summary>
        private void DeleteSelectedImages()
        {
            if (project == null) return;
            string[] imageIds = imageBrowser.SelectedImageIds.Distinct(StringComparer.Ordinal).ToArray();
            if (imageIds.Length == 0) return;
            string message = string.Format(Strings.DeleteSelectedImagesPrompt, imageIds.Length);
            if (!confirmImageDeletion(message)) return;
            DeleteImages(imageIds);
        }

        /// <summary>
        /// 確認後刪除專案內全部圖片。
        /// </summary>
        private void DeleteAllImages()
        {
            if (project == null) return;
            string[] imageIds = project.GetImages().Select(image => image.Id).ToArray();
            if (imageIds.Length == 0) return;
            string message = string.Format(Strings.DeleteAllImagesPrompt, imageIds.Length);
            if (!confirmImageDeletion(message)) return;
            DeleteImages(imageIds);
        }

        /// <summary>
        /// 刪除指定圖片並同步更新專案和畫面。
        /// </summary>
        /// <param name="imageIds">圖片識別碼清單。</param>
        private void DeleteImages(IReadOnlyCollection<string> imageIds)
        {
            bool currentImageDeleted = selectedImageId != null && imageIds.Contains(selectedImageId);
            project.DeleteImages(imageIds);
            if (currentImageDeleted || project.GetImages().Count == 0)
            {
                selectedImageId = null;
                selectedImagePath = null;
                pendingImageSelection = null;
                imageCanvas.LoadImage(null);
                canvas.LoadImage(null);
            }
            UpdateProjectStatus();
            RefreshAnnotationClasses();
            RefreshDatasetAssignments();
            UpdateProjectMenuAvailability();
            RefreshImportedImageAvailability(importJobQueue == null ? new ImportJobSnapshot[0] : importJobQueue.Jobs);
        }

        /// <summary>
        /// 開啟遺失圖片管理介面。
        /// </summary>
        private void ManageMissingImages()
        {
            if (project == null) return;
            using (var dialog = new MissingImagesForm(project)) dialog.ShowDialog(this);
            UpdateProjectStatus();
            imageBrowser.LoadImages(project.GetImages(), GetImagesDirectory(project.ProjectPath));
            RefreshAnnotationSets();
            RefreshImportedImageAvailability(importJobQueue == null ? new ImportJobSnapshot[0] : importJobQueue.Jobs);
        }

        /// <summary>
        /// 開啟診斷日誌管理介面。
        /// </summary>
        private void ManageDiagnostics()
        {
            using (var dialog = new DiagnosticsForm(diagnosticLog)) dialog.ShowDialog(this);
        }

        /// <summary>
        /// 建立使用者選定類型的新標註集。
        /// </summary>
        private void CreateAnnotationSet()
        {
            if (project == null) return;
            using (var dialog = new AnnotationSetCreationDialog())
            {
                if (dialog.ShowDialog(this) != DialogResult.OK || !dialog.AnnotationSetType.HasValue || string.IsNullOrWhiteSpace(dialog.AnnotationSetName)) return;
                CreateAnnotationSet(dialog.AnnotationSetType.Value, dialog.AnnotationSetName.Trim());
            }
        }

        /// <summary>
        /// 建立指定類型與名稱的標註集並更新畫面。
        /// </summary>
        /// <param name="type">要處理的工具或資料類型。</param>
        /// <param name="name">要使用的名稱。</param>
        private void CreateAnnotationSet(AnnotationSetType type, string name)
        {
            AnnotationSet created = project.CreateAnnotationSet(name, type);
            RefreshAnnotationSets(created.Id);
        }

        /// <summary>
        /// 重新載入專案標註集清單並維持指定選取項目。
        /// </summary>
        /// <param name="selectedId">目前選取項目的識別碼。</param>
        private void RefreshAnnotationSets(string selectedId = null)
        {
            if (annotationSets == null) return;
            if (selectedId == null) selectedId = (annotationSets.SelectedItem as AnnotationSet)?.Id;
            string previousId = (annotationSets.SelectedItem as AnnotationSet)?.Id;
            AnnotationSet[] sets = project == null ? new AnnotationSet[0] : project.GetAnnotationSets().ToArray();
            refreshingAnnotationSets = true;
            try
            {
                annotationSets.Items.Clear();
                datasetAnnotationSets.Items.Clear();
                annotationSets.Items.AddRange(sets);
                datasetAnnotationSets.Items.AddRange(sets);
                annotationSets.DisplayMember = "DisplayName";
                datasetAnnotationSets.DisplayMember = "DisplayName";
                AnnotationSet selected = sets.FirstOrDefault(item => item.Id == selectedId) ?? sets.FirstOrDefault();
                annotationSets.SelectedItem = selected;
                datasetAnnotationSets.SelectedItem = selected;
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
            UpdateProjectMenuAvailability();
        }

        /// <summary>
        /// 處理標註集選取變更並同步相關頁面。
        /// </summary>
        /// <param name="source">提供資料或數值的來源。</param>
        private void AnnotationSetSelectionChanged(ComboBox source)
        {
            if (refreshingAnnotationSets || synchronizingAnnotationSetSelection) return;
            AnnotationSet selected = source.SelectedItem as AnnotationSet;
            ComboBox target = ReferenceEquals(source, annotationSets) ? datasetAnnotationSets : annotationSets;
            synchronizingAnnotationSetSelection = true;
            try
            {
                target.SelectedItem = selected == null ? null : target.Items.Cast<AnnotationSet>().FirstOrDefault(item => item.Id == selected.Id);
            }
            finally
            {
                synchronizingAnnotationSetSelection = false;
            }
            ResetAnnotationOpacity();
            canvas.DiscardUnfinishedShape();
            RefreshAnnotationClasses();
            RefreshDatasetAssignments();
            UpdateProjectMenuAvailability();
        }

        /// <summary>
        /// 將標註透明度恢復預設值。
        /// </summary>
        private void ResetAnnotationOpacity()
        {
            if (annotationOpacity != null) annotationOpacity.Value = 40;
            SetAnnotationOpacity(40);
        }

        /// <summary>
        /// 更新畫布標註透明度並同步數值欄位。
        /// </summary>
        /// <param name="percent">縮放或分配百分比。</param>
        private void SetAnnotationOpacity(int percent)
        {
            percent = Math.Max(0, Math.Min(100, (int)Math.Round(percent / 10d, MidpointRounding.AwayFromZero) * 10));
            if (annotationOpacity.Value != percent) annotationOpacity.Value = percent;
            if (annotationOpacityValue.Value != percent) annotationOpacityValue.Value = percent;
            float opacity = percent / 100f;
            canvas.ObdOpacity = opacity;
            canvas.SegOpacity = opacity;
            canvas.Invalidate();
        }

        /// <summary>
        /// 切換標註顯示狀態並更新畫布。
        /// </summary>
        /// <param name="visible">是否顯示標註。</param>
        private void SetAnnotationsVisible(bool visible)
        {
            imageCanvas.AnnotationsVisible = visible;
            canvas.AnnotationsVisible = visible;
        }

        /// <summary>
        /// 依目前標註集重建類別清單與畫布類別資料。
        /// </summary>
        private void RefreshAnnotationClasses()
        {
            if (annotationClasses == null) return;
            AnnotationSet selected = annotationSets.SelectedItem as AnnotationSet;
            bool selectedSetChanged = displayedAnnotationSetId != (selected == null ? null : selected.Id);
            displayedAnnotationSetId = selected == null ? null : selected.Id;
            canvas.ObdEnabled = project != null && selected != null && selected.Type == AnnotationSetType.Obd && selectedImageId != null;
            canvas.SegEnabled = project != null && selected != null && selected.Type == AnnotationSetType.Seg && selectedImageId != null;
            if (selected == null || selected.Type != AnnotationSetType.Obd) canvas.ObdSelectMode = false;
            else if (selectedSetChanged) canvas.ObdSelectMode = true;
            if (selected == null || selected.Type != AnnotationSetType.Seg) canvas.SegTool = null;
            else if (selectedSetChanged || !canvas.SegTool.HasValue) canvas.SegTool = ImageCanvasControl.SegToolKind.Select;
            Button[] requiredTools;
            if (selected != null && selected.Type == AnnotationSetType.Obd)
            {
                requiredTools = new[] { obdSelect, obdRectangle };
            }
            else if (selected != null && selected.Type == AnnotationSetType.Seg)
            {
                requiredTools = new[] { segSelect, segRectangle, segEllipse, segPolygon, segBrush, segLine, segEraser };
            }
            else requiredTools = new Button[0];
            if (!annotationTools.Controls.Cast<Control>().SequenceEqual(requiredTools.Cast<Control>()))
            {
                annotationTools.SuspendLayout();
                annotationTools.Controls.Clear();
                annotationTools.Controls.AddRange(requiredTools);
                annotationTools.ResumeLayout();
            }
            RefreshAnnotationToolButtons();
            AnnotationClass[] classes = project == null || selected == null ? new AnnotationClass[0] : project.GetClasses(selected.Id).ToArray();
            if (selected == null || selected.Type != AnnotationSetType.Seg || selectedSetChanged || !classes.Any(value => value.Id == selectedSegClassId)) selectedSegClassId = null;
            if (selected != null && selected.Type == AnnotationSetType.Seg) selectedSegClassId = selectedSegClassId ?? classes.FirstOrDefault()?.Id;
            if (selected == null || selected.Type != AnnotationSetType.Obd || selectedSetChanged || !classes.Any(value => value.Id == selectedObdClassId)) selectedObdClassId = null;
            if (selected != null && selected.Type == AnnotationSetType.Obd) selectedObdClassId = selectedObdClassId ?? classes.FirstOrDefault()?.Id;
            string previewClassId = selected == null ? null
                : selected.Type == AnnotationSetType.Seg ? selectedSegClassId
                : selected.Type == AnnotationSetType.Obd ? selectedObdClassId
                : selectedImageId == null ? null : project.GetClaClassId(selected.Id, selectedImageId);
            AnnotationClass previewClass = classes.FirstOrDefault(value => value.Id == previewClassId);
            canvas.PreviewColor = previewClass == null ? Color.White : AnnotationColorFormat.ToColor(previewClass.Color);
            canvas.ObdClasses = classes.ToDictionary(value => value.Id);
            canvas.ObdAnnotations = canvas.ObdEnabled ? project.GetObdAnnotations(selected.Id, selectedImageId) : null;
            canvas.SegClasses = classes.ToDictionary(value => value.Id);
            canvas.SegRegions = canvas.SegEnabled ? project.GetSegRegions(selected.Id, selectedImageId) : null;
            if (project == null || selected == null)
            {
                annotationClasses.Controls.Clear();
                RefreshImageBrowser();
                return;
            }
            ClassLabelButton[] existingButtons = annotationClasses.Controls.OfType<ClassLabelButton>().ToArray();
            bool reuseButtons = existingButtons.Length == classes.Length && existingButtons.Zip(classes, (button, value) =>
                string.Equals(button.Tag as string, value.Id, StringComparison.Ordinal)
                && button.Text == value.Name
                && button.ClassColor.ToArgb() == AnnotationColorFormat.ToColor(value.Color).ToArgb()).All(matches => matches);
            if (!reuseButtons)
            {
                annotationClasses.SuspendLayout();
                annotationClasses.Controls.Clear();
                foreach (AnnotationClass value in classes)
                {
                    Color classColor = AnnotationColorFormat.ToColor(value.Color);
                    var button = new ClassLabelButton { Text = value.Name, Tag = value.Id, AutoSize = false, Height = 34, ClassColor = classColor, BackColor = SystemColors.Control, FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = false };
                    button.Width = Math.Max(1, annotationClasses.ClientSize.Width - button.Margin.Horizontal);
                    button.AutoEllipsis = true;
                    annotationToolTips.SetToolTip(button, value.Name);
                    button.Click += (sender, args) =>
                    {
                        if (selected.Type == AnnotationSetType.Cla && selectedImageId != null) RunAnnotationCommand(() =>
                        {
                            project.SetClaClass(selected.Id, selectedImageId, value.Id);
                            RefreshAnnotationClasses();
                        });
                        else if (selected.Type == AnnotationSetType.Obd)
                        {
                            selectedObdClassId = value.Id;
                            if (selectedImageId != null && canvas.SelectedObdIds.Count > 0) RunAnnotationCommand(() => ChangeSelectedObdClass(value.Id));
                            else RefreshAnnotationClasses();
                        }
                        else if (selected.Type == AnnotationSetType.Seg)
                        {
                            selectedSegClassId = value.Id;
                            canvas.PreviewColor = AnnotationColorFormat.ToColor(value.Color);
                            if (selectedImageId != null && canvas.SelectedSegIds.Count > 0) RunAnnotationCommand(() => ChangeSelectedSegClass(value.Id));
                            else RefreshAnnotationClasses();
                        }
                    };
                    var menu = new ContextMenuStrip();
                    menu.Items.Add(Strings.RenameAnnotationClass, null, (sender, args) => RunAnnotationCommand(() => RenameAnnotationClass(value)));
                    menu.Items.Add(Strings.ChangeAnnotationClassColor, null, (sender, args) => RunAnnotationCommand(() => ChangeAnnotationClassColor(value)));
                    menu.Items.Add(Strings.DeleteAnnotationClass, null, (sender, args) => RunAnnotationCommand(() => DeleteAnnotationClass(value)));
                    button.ContextMenuStrip = menu;
                    annotationClasses.Controls.Add(button);
                }
                annotationClasses.ResumeLayout();
                existingButtons = annotationClasses.Controls.OfType<ClassLabelButton>().ToArray();
            }
            string activeClassId = selected.Type == AnnotationSetType.Cla
                ? selectedImageId == null ? null : project.GetClaClassId(selected.Id, selectedImageId)
                : selected.Type == AnnotationSetType.Obd ? selectedObdClassId : selectedSegClassId;
            foreach (ClassLabelButton button in existingButtons)
            {
                bool activeClass = string.Equals(button.Tag as string, activeClassId, StringComparison.Ordinal);
                if (button.IsActive == activeClass) continue;
                button.IsActive = activeClass;
                button.BackColor = activeClass ? button.ClassColor : SystemColors.Control;
                button.FlatAppearance.BorderSize = activeClass ? 3 : 1;
                button.FlatAppearance.BorderColor = activeClass ? SystemColors.Highlight : SystemColors.ControlDark;
                button.Invalidate();
            }
            RefreshImageBrowser();
        }

        // 取得目前選取的標註集模型。
        private AnnotationSet SelectedAnnotationSet { get { return annotationSets.SelectedItem as AnnotationSet; } }

        /// <summary>
        /// 依目前模式和工具更新工具按鈕及提示。
        /// </summary>
        private void RefreshAnnotationToolButtons()
        {
            AnnotationSet selected = SelectedAnnotationSet;
            bool isObd = selected != null && selected.Type == AnnotationSetType.Obd;
            bool isSeg = selected != null && selected.Type == AnnotationSetType.Seg;
            SetAnnotationToolButtonState(obdSelect, isObd && canvas.ObdSelectMode);
            SetAnnotationToolButtonState(obdRectangle, isObd && !canvas.ObdSelectMode);
            SetAnnotationToolButtonState(segSelect, isSeg && canvas.SegTool == ImageCanvasControl.SegToolKind.Select);
            SetAnnotationToolButtonState(segRectangle, isSeg && canvas.SegTool == ImageCanvasControl.SegToolKind.Rect);
            SetAnnotationToolButtonState(segEllipse, isSeg && canvas.SegTool == ImageCanvasControl.SegToolKind.Ellipse);
            SetAnnotationToolButtonState(segPolygon, isSeg && canvas.SegTool == ImageCanvasControl.SegToolKind.Polygon);
            SetAnnotationToolButtonState(segBrush, isSeg && canvas.SegTool == ImageCanvasControl.SegToolKind.Brush);
            SetAnnotationToolButtonState(segLine, isSeg && canvas.SegTool == ImageCanvasControl.SegToolKind.Line);
            SetAnnotationToolButtonState(segEraser, isSeg && canvas.SegTool == ImageCanvasControl.SegToolKind.Eraser);
        }

        /// <summary>
        /// 更新單一工具按鈕的啟用外觀。
        /// </summary>
        /// <param name="button">要更新的按鈕。</param>
        /// <param name="active">是否為目前作用中項目。</param>
        private static void SetAnnotationToolButtonState(Button button, bool active)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.UseVisualStyleBackColor = false;
            button.BackColor = active ? SystemColors.GradientActiveCaption : SystemColors.Control;
            button.ForeColor = SystemColors.ControlText;
            button.FlatAppearance.BorderSize = active ? 2 : 1;
            button.FlatAppearance.BorderColor = active ? SystemColors.Highlight : SystemColors.ControlDark;
        }

        /// <summary>
        /// 重新整理資料集指派頁面的標註集資料。
        /// </summary>
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

        /// <summary>
        /// 重新載入圖片清單和目前選取狀態。
        /// </summary>
        private void RefreshImageBrowser()
        {
            AnnotationSet selected = SelectedAnnotationSet;
            if (project == null) return;
            if (selected == null)
            {
                imageBrowser.LoadImages(project.GetImages(), GetImagesDirectory(project.ProjectPath));
                return;
            }
            var classes = project.GetClasses(selected.Id).ToDictionary(value => value.Id, value => value.Name, StringComparer.Ordinal);
            var summaries = project.GetImageAnnotationSummaries(selected.Id).ToDictionary(value => value.ImageId, StringComparer.Ordinal);
            var rows = project.GetImages().Select(image =>
            {
                ImageAnnotationSummary summary;
                summaries.TryGetValue(image.Id, out summary);
                string[] classNames = (summary == null ? new string[0] : summary.ClassIds).Where(classes.ContainsKey).Select(classId => classes[classId]).ToArray();
                return new ImageListRow(image, classNames, summary == null ? 0 : summary.AnnotationCount, selected.Type != AnnotationSetType.Cla);
            });
            imageBrowser.LoadRows(rows, GetImagesDirectory(project.ProjectPath));
        }

        /// <summary>
        /// 將指定圖片指派到指定資料集狀態。
        /// </summary>
        /// <param name="imageIds">圖片識別碼清單。</param>
        /// <param name="status">要處理或顯示的狀態。</param>
        private void SetDatasetAssignments(IReadOnlyList<string> imageIds, DatasetAssignmentStatus status)
        {
            AnnotationSet selected = SelectedAnnotationSet;
            if (project == null || selected == null || imageIds.Count == 0) return;
            project.SetDatasetAssignments(selected.Id, imageIds, status);
            RefreshDatasetAssignments();
        }

        /// <summary>
        /// 更新指定圖片的重測標記。
        /// </summary>
        /// <param name="imageIds">圖片識別碼清單。</param>
        /// <param name="retest">是否設定 Retest 旗標。</param>
        private void SetRetest(IReadOnlyList<string> imageIds, bool retest)
        {
            AnnotationSet selected = SelectedAnnotationSet;
            if (project == null || selected == null || imageIds.Count == 0) return;
            project.SetRetest(selected.Id, imageIds, retest);
            RefreshDatasetAssignments();
        }

        /// <summary>
        /// 清除指定圖片的資料集類別標記。
        /// </summary>
        /// <param name="imageIds">圖片識別碼清單。</param>
        private void ClearDatasetClasses(IReadOnlyList<string> imageIds)
        {
            AnnotationSet selected = SelectedAnnotationSet;
            if (project == null || selected == null || imageIds.Count == 0) return;
            project.ClearAnnotations(selected.Id, imageIds);
            RefreshAnnotationClasses();
            RefreshDatasetAssignments();
        }

        /// <summary>
        /// 依指定訓練比例自動分配資料集。
        /// </summary>
        /// <param name="imageIds">圖片識別碼清單。</param>
        /// <param name="trainPercent">分配給訓練集的圖片百分比。</param>
        private void AutoSplitDataset(IReadOnlyCollection<string> imageIds, int trainPercent)
        {
            AnnotationSet selected = SelectedAnnotationSet;
            if (project == null || selected == null) return;
            DatasetSplitPlan plan = project.AutoSplitDataset(selected.Id, trainPercent, imageIds);
            RefreshDatasetAssignments();
            if (plan.Warnings.Count > 0) MessageBox.Show(Strings.DatasetSplitCannotMeet, Strings.DatasetSplitWarning, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        /// <summary>
        /// 選取標註 JSON 並將其加入目前專案的匯入佇列。
        /// </summary>
        private async void QueueAnnotationJson()
        {
            AnnotationSet selected = SelectedAnnotationSet;
            if (project == null || importJobQueue == null || selected == null) return;
            IProjectSession targetProject = project;
            ImportJobQueue targetQueue = importJobQueue;
            using (var dialog = new OpenFileDialog { Filter = "JSON (*.json)|*.json", Multiselect = false })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                AnnotationJsonImportPreview preview;
                ProjectImage[] projectImages = targetProject.GetImages().ToArray();
                try
                {
                    preview = await Task.Run(() => PreflightAnnotationJson(dialog.FileName, selected.Type, projectImages));
                }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonAnnotationImportException)
                {
                    if (!ReferenceEquals(project, targetProject) || importJobQueue != targetQueue || SelectedAnnotationSet?.Id != selected.Id) return;
                    targetQueue.RecordFailure(new AnnotationJsonImportJobRequest(dialog.FileName, selected.Name, selected.Id, selected.Type, false), exception.Message);
                    ShowImportQueue();
                    return;
                }
                if (!ReferenceEquals(project, targetProject) || importJobQueue != targetQueue || SelectedAnnotationSet?.Id != selected.Id) return;
                using (var options = new AnnotationJsonImportDialog(dialog.FileName, preview))
                {
                    if (options.ShowDialog(this) != DialogResult.OK) return;
                    if (MessageBox.Show(this, Strings.ImportAnnotationIrreversiblePrompt, Strings.ImportAnnotationJson, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
                    IEnumerable<string> approvedUnknownFields = options.AllowUnknownFields ? preview.UnknownFields : new string[0];
                    TryEnqueueImportJob(new AnnotationJsonImportJobRequest(dialog.FileName, selected.Name, selected.Id, selected.Type, options.ImportAnnotationsAndClasses, options.AnnotationMode, options.ImportDataset, options.DatasetMode, approvedUnknownFields));
                }
            }
        }

        internal static AnnotationJsonImportPreview PreflightAnnotationJson(string filePath, AnnotationSetType targetType, IReadOnlyList<ProjectImage> projectImages)
        {
            AnnotationJsonImportPreview preview = AnnotationJsonImportPreview.Parse(File.ReadAllText(filePath));
            if (preview.Type != targetType) throw new JsonAnnotationImportException("JSON label type does not match the target annotation set.");
            foreach (AnnotationJsonImportPreviewImage image in preview.Images)
            {
                ProjectImage[] matches = projectImages.Where(value => String.Equals(value.OriginalFileName, image.FileName, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (matches.Length != 1) throw new JsonAnnotationImportException("JSON image does not map to exactly one project image: " + image.FileName);
                if (matches[0].Width != image.Width || matches[0].Height != image.Height) throw new JsonAnnotationImportException("JSON image dimensions do not match the project image: " + image.FileName);
            }
            return preview;
        }

        /// <summary>
        /// 非同步匯出指定圖片並顯示匯出進度。
        /// </summary>
        /// <param name="imageIds">圖片識別碼清單。</param>
        /// <returns>代表非同步操作的工作。</returns>
        private Task ExportImagesAsync(IEnumerable<string> imageIds)
        {
            if (project == null || importJobQueue == null) return Task.CompletedTask;
            using (var dialog = new SaveFileDialog { DefaultExt = "zip", AddExtension = true, Filter = Strings.ImageZipFileFilter, OverwritePrompt = false })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return Task.CompletedTask;
                string[] selected = imageIds == null ? null : imageIds.ToArray();
                TryEnqueueImportJob(new ImageZipExportJobRequest(selected, dialog.FileName));
            }
            return Task.CompletedTask;
        }

        /// <summary>
        /// 非同步匯出目前專案標註 JSON。
        /// </summary>
        /// <returns>代表非同步操作的工作。</returns>
        private Task ExportAnnotationJsonAsync()
        {
            AnnotationSet set = annotationSets.SelectedItem as AnnotationSet;
            if (project == null || set == null || importJobQueue == null) return Task.CompletedTask;
            using (var dialog = new SaveFileDialog { DefaultExt = "json", AddExtension = true, Filter = Strings.AnnotationJsonFileFilter, OverwritePrompt = false, FileName = set.Name })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return Task.CompletedTask;
                TryEnqueueImportJob(new JsonExportJobRequest(set.Id, dialog.FileName));
            }
            return Task.CompletedTask;
        }

        /// <summary>
        /// 提交新 OBD 矩形並更新標註檢視。
        /// </summary>
        /// <param name="rectangle">要處理的矩形範圍。</param>
        private void CreateObdRectangle(Rectangle rectangle)
        {
            AnnotationSet selected = SelectedAnnotationSet;
            if (project == null || selected == null || selected.Type != AnnotationSetType.Obd || selectedImageId == null) return;
            AnnotationClass value = project.GetClasses(selected.Id).FirstOrDefault(item => item.Id == selectedObdClassId);
            if (value == null) return;
            project.AddObdAnnotation(selected.Id, selectedImageId, value.Id, rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);
            RefreshAnnotationClasses();
        }

        /// <summary>
        /// 非同步建立矩形或橢圓 SEG 區域。
        /// </summary>
        /// <param name="type">要處理的工具或資料類型。</param>
        /// <param name="rectangle">要處理的矩形範圍。</param>
        /// <returns>代表非同步操作的工作。</returns>
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

        /// <summary>
        /// 非同步建立多邊形或線形 SEG 路徑。
        /// </summary>
        /// <param name="type">要處理的工具或資料類型。</param>
        /// <param name="points">依序構成 SEG 路徑的圖片座標點。</param>
        /// <param name="width">要使用的筆寬或線寬。</param>
        /// <returns>代表非同步操作的工作。</returns>
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

        /// <summary>
        /// 非同步擦除指定路徑範圍內的 SEG 標註。
        /// </summary>
        /// <param name="points">依序構成 SEG 路徑的圖片座標點。</param>
        /// <param name="width">要使用的筆寬或線寬。</param>
        /// <returns>代表非同步操作的工作。</returns>
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

        /// <summary>
        /// 非同步以完成當下的 SEG 幾何從目前類別中擦除。
        /// </summary>
        /// <param name="type">要處理的工具或資料類型。</param>
        /// <param name="rectangle">要處理的矩形範圍。</param>
        /// <param name="points">依序構成 SEG 路徑的圖片座標點。</param>
        /// <param name="width">要使用的筆寬或線寬。</param>
        /// <returns>代表非同步操作的工作。</returns>
        private async Task EraseSegGeometryAsync(SegRegionType type, Rectangle rectangle, IReadOnlyList<SegPoint> points, int width)
        {
            AnnotationSet selected = SelectedAnnotationSet;
            IProjectSession session = project;
            string imageId = selectedImageId;
            string classId = selectedSegClassId;
            if (annotationOperationInProgress || session == null || selected == null || selected.Type != AnnotationSetType.Seg || imageId == null || classId == null) return;
            string annotationSetId = selected.Id;
            await RunSegOperationAsync(session, annotationSetId, imageId, () => session.EraseSegGeometry(annotationSetId, imageId, classId, type, rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height, points, width));
        }

        /// <summary>
        /// 執行 SEG 修改命令並處理進度、狀態與失敗復原。
        /// </summary>
        /// <param name="session">目前專案的工作階段。</param>
        /// <param name="annotationSetId">要修改的標註集識別碼。</param>
        /// <param name="imageId">圖片的識別碼。</param>
        /// <param name="command">要執行的專案或標註命令。</param>
        /// <returns>代表非同步操作的工作。</returns>
        private async Task RunSegOperationAsync(IProjectSession session, string annotationSetId, string imageId, Action command)
        {
            Exception failure = null;
            annotationOperationInProgress = true;
            UpdateProjectMenuAvailability();
            UseWaitCursor = true;
            try
            {
                await Task.Run(command);
            }
            catch (Exception exception) { failure = exception; }
            finally
            {
                UseWaitCursor = false;
                annotationOperationInProgress = false;
                UpdateProjectMenuAvailability();
            }
            if (failure is SegGeometryPrototype.BridgeNetworkException)
                ShowSegBridgeNetworkFailure();
            else if (failure is ArgumentException || failure is InvalidOperationException)
                MessageBox.Show(this, Strings.AnnotationValidationFailed, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            else if (failure != null)
                MessageBox.Show(this, Strings.ProjectOperationFailed, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            else if (ReferenceEquals(project, session) && SelectedAnnotationSet?.Id == annotationSetId && selectedImageId == imageId)
            {
                RefreshAnnotationClasses();
            }
            ImageSelectedEventArgs pending = pendingImageSelection;
            pendingImageSelection = null;
            if (failure != null) imageBrowser.SelectImage(imageId);
            else if (ReferenceEquals(project, session) && pending != null)
            {
                imageBrowser.SelectImage(pending.Image.Id);
                if (selectedImageId != pending.Image.Id) ApplyImageSelection(pending);
            }
        }

        /// <summary>
        /// 套用圖片選取並載入對應的標註與畫布內容。
        /// </summary>
        /// <param name="args">包含此次事件詳細資料的事件資料。</param>
        private void ApplyImageSelection(ImageSelectedEventArgs args)
        {
            selectedImagePath = args.Image.IsMissing ? null : args.Path;
            selectedImageId = args.Image.Id;
            imageCanvas.LoadImage(selectedImagePath);
            canvas.LoadImage(selectedImagePath);
            RefreshAnnotationClasses();
        }

        /// <summary>
        /// 提交 SEG 區域幾何變更命令。
        /// </summary>
        /// <param name="regions">要處理的 SEG 區域集合。</param>
        private void UpdateSegRegions(IReadOnlyList<SegRegion> regions)
        {
            if (project == null || SelectedAnnotationSet == null || SelectedAnnotationSet.Type != AnnotationSetType.Seg || regions.Count == 0) return;
            project.UpdateSegRegions(regions);
            RefreshAnnotationClasses();
        }

        /// <summary>
        /// 刪除指定 SEG 區域並更新選取狀態。
        /// </summary>
        /// <param name="regionIds">SEG 區域識別碼清單。</param>
        private void DeleteSegRegions(IReadOnlyCollection<string> regionIds)
        {
            if (project == null || SelectedAnnotationSet == null || SelectedAnnotationSet.Type != AnnotationSetType.Seg || regionIds.Count == 0) return;
            project.DeleteSegRegions(regionIds);
            RefreshAnnotationClasses();
        }

        /// <summary>
        /// 將目前選取 SEG 區域移至指定類別。
        /// </summary>
        /// <param name="classId">類別識別碼。</param>
        private void ChangeSelectedSegClass(string classId)
        {
            AnnotationSet selected = SelectedAnnotationSet;
            if (project == null || selected == null || selected.Type != AnnotationSetType.Seg || selectedImageId == null) return;
            SegRegion[] regions = project.GetSegRegions(selected.Id, selectedImageId).Where(region => canvas.SelectedSegIds.Contains(region.Id)).ToArray();
            if (regions.Length > 0) project.UpdateSegRegions(regions.Select(region => new SegRegion(region.Id, region.AnnotationSetId, region.ImageId, classId, region.Type, region.X, region.Y, region.Width, region.Height, region.ZOrder, region.Points, region.StrokeWidth)));
            RefreshAnnotationClasses();
        }

        /// <summary>
        /// 提交一組 OBD 矩形位置或尺寸變更。
        /// </summary>
        /// <param name="rectangles">要處理的 OBD 矩形集合。</param>
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

        /// <summary>
        /// 刪除指定 OBD 標註並更新選取狀態。
        /// </summary>
        /// <param name="annotationIds">標註識別碼清單。</param>
        private void DeleteObdRectangles(IReadOnlyCollection<string> annotationIds)
        {
            AnnotationSet selected = SelectedAnnotationSet;
            if (project == null || selected == null || selected.Type != AnnotationSetType.Obd || selectedImageId == null) return;
            if (project.GetObdAnnotations(selected.Id, selectedImageId).Count(item => annotationIds.Contains(item.Id)) != annotationIds.Count) return;
            project.DeleteObdAnnotations(annotationIds);
            RefreshAnnotationClasses();
        }

        /// <summary>
        /// 將目前選取 OBD 標註移至指定類別。
        /// </summary>
        /// <param name="classId">類別識別碼。</param>
        private void ChangeSelectedObdClass(string classId)
        {
            AnnotationSet selected = SelectedAnnotationSet;
            if (project == null || selected == null || selectedImageId == null || canvas.SelectedObdIds.Count == 0) return;
            ObdAnnotation[] annotations = project.GetObdAnnotations(selected.Id, selectedImageId).Where(item => canvas.SelectedObdIds.Contains(item.Id)).ToArray();
            if (annotations.Length != canvas.SelectedObdIds.Count) return;
            if (annotations.All(annotation => annotation.ClassId == classId))
            {
                RefreshAnnotationClasses();
                return;
            }
            project.UpdateObdAnnotations(annotations.Select(annotation => new ObdAnnotation(annotation.Id, annotation.AnnotationSetId, annotation.ImageId, classId, annotation.X, annotation.Y, annotation.Width, annotation.Height, annotation.ZOrder)));
            RefreshAnnotationClasses();
        }

        /// <summary>
        /// 重新命名目前選取的標註集。
        /// </summary>
        private void RenameSelectedAnnotationSet()
        {
            AnnotationSet selected = SelectedAnnotationSet;
            if (project == null || selected == null) return;
            string name = promptText(Strings.AnnotationSetNamePrompt);
            if (name == null) return;
            project.RenameAnnotationSet(selected.Id, name);
            RefreshAnnotationSets();
        }

        /// <summary>
        /// 確認後刪除目前選取的標註集。
        /// </summary>
        private void DeleteSelectedAnnotationSet()
        {
            AnnotationSet selected = SelectedAnnotationSet;
            if (project == null || selected == null) return;
            string name = promptText(Strings.DeleteAnnotationSetPrompt);
            if (name == null) return;
            if (!project.DeleteAnnotationSet(selected.Id, name))
            {
                showDeleteWarning(string.Format(Strings.DeleteAnnotationSetMismatch, selected.Name));
                return;
            }
            RefreshAnnotationSets();
        }

        /// <summary>
        /// 依最大符合 classN 格式的編號加一，建立並新增下一個類別。
        /// </summary>
        private void AddAnnotationClass()
        {
            AnnotationSet selected = SelectedAnnotationSet;
            if (project == null || selected == null) return;
            BigInteger lastNumber = project.GetClasses(selected.Id)
                .Select(item => Regex.Match(item.Name, "^class([1-9][0-9]*)$"))
                .Where(match => match.Success)
                .Select(match => BigInteger.Parse(match.Groups[1].Value))
                .DefaultIfEmpty(0)
                .Max();
            string name = "class" + (lastNumber + BigInteger.One).ToString();
            project.AddClass(selected.Id, name, null);
            RefreshAnnotationClasses();
        }

        /// <summary>
        /// 重新命名指定標註類別。
        /// </summary>
        /// <param name="value">要套用或檢查的值。</param>
        private void RenameAnnotationClass(AnnotationClass value)
        {
            string name = promptText(Strings.AnnotationClassNamePrompt);
            if (name == null) return;
            project.RenameClass(value.Id, name);
            RefreshAnnotationClasses();
        }

        /// <summary>
        /// 變更指定標註類別顏色。
        /// </summary>
        /// <param name="value">要套用或檢查的值。</param>
        private void ChangeAnnotationClassColor(AnnotationClass value)
        {
            using (var dialog = new ColorDialog { Color = AnnotationColorFormat.ToColor(value.Color) })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                project.ChangeClassColor(value.Id, string.Format("rgba({0}, {1}, {2}, 1)-custom", dialog.Color.R, dialog.Color.G, dialog.Color.B));
                RefreshAnnotationClasses();
            }
        }

        /// <summary>
        /// 刪除指定標註類別及其相關標註。
        /// </summary>
        /// <param name="value">要套用或檢查的值。</param>
        private void DeleteAnnotationClass(AnnotationClass value)
        {
            if (MessageBox.Show(this, Strings.DeleteAnnotationClassPrompt, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            project.DeleteClass(value.Id);
            RefreshAnnotationClasses();
        }

        /// <summary>
        /// 對目前標註集執行復原或重做。
        /// </summary>
        /// <param name="redo">是否執行重做而非復原。</param>
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

        /// <summary>
        /// 執行標註命令並處理失敗與畫面更新。
        /// </summary>
        /// <param name="command">要執行的專案或標註命令。</param>
        private void RunAnnotationCommand(Action command)
        {
            try { command(); }
            catch (SegGeometryPrototype.BridgeNetworkException) { ShowSegBridgeNetworkFailure(); }
            catch (ArgumentException) { MessageBox.Show(this, Strings.AnnotationValidationFailed, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            catch (InvalidOperationException) { MessageBox.Show(this, Strings.AnnotationValidationFailed, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            catch (Exception) { MessageBox.Show(this, Strings.ProjectOperationFailed, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        /// <summary>
        /// 提示 SEG 空洞連線網路無法建立時的處理結果。
        /// </summary>
        private void ShowSegBridgeNetworkFailure()
        {
            MessageBox.Show(this, Strings.SegBridgeNetworkFailed, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        /// <summary>
        /// 顯示簡短文字輸入對話框並回傳使用者輸入。
        /// </summary>
        /// <param name="prompt">要顯示的提示文字。</param>
        /// <returns>轉換或取得的文字結果。</returns>
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
        /// <summary>
        /// 開啟專案備份管理介面。
        /// </summary>
        private void ManageBackups()
        {
            if (project == null) return;
            using (var dialog = new BackupManagementForm(project))
            {
                if (dialog.ShowDialog(this) == DialogResult.OK && dialog.RestoredProject != null) ReplaceProject(dialog.RestoredProject);
            }
        }

        /// <summary>
        /// 處理專案開啟失敗並提供復原或修復選項。
        /// </summary>
        /// <param name="exception">發生的例外資訊。</param>
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

        /// <summary>
        /// 選取多個圖片檔案並將其加入目前專案的匯入佇列。
        /// </summary>
        private void QueueImageFiles()
        {
            if (project == null || importJobQueue == null) return;
            using (var dialog = new OpenFileDialog { Filter = Strings.ImageFileFilter, Multiselect = true })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                TryEnqueueImportJob(new ImageFilesImportJobRequest(dialog.FileNames));
            }
        }

        /// <summary>
        /// 選取資料夾並將其加入目前專案的匯入佇列。
        /// </summary>
        private void QueueFolder()
        {
            if (project == null || importJobQueue == null) return;
            using (var dialog = new FolderBrowserDialog())
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                DialogResult choice = MessageBox.Show(this, Strings.IncludeSubdirectoriesPrompt, Text, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (choice == DialogResult.Cancel) return;
                TryEnqueueImportJob(new FolderImportJobRequest(dialog.SelectedPath, choice == DialogResult.Yes));
            }
        }

        /// <summary>
        /// 將匯入工作加入佇列，並將容量拒絕轉成使用者可理解的提示。
        /// </summary>
        /// <param name="request">要處理的匯入工作要求。</param>
        private void TryEnqueueImportJob(ImportJobRequest request)
        {
            try
            {
                importJobQueue.Enqueue(request);
                ShowImportQueue();
            }
            catch (ImportQueueCapacityException)
            {
                MessageBox.Show(this, Strings.ImportQueueCapacityReached, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        /// <summary>
        /// 建立目前專案專用的圖片匯入佇列並接收狀態更新。
        /// </summary>
        /// <param name="session">目前專案的工作階段。</param>
        private void InitializeImageImportQueue(IProjectSession session)
        {
            if (importJobQueue != null)
            {
                importJobQueue.JobsChanged -= ImageImportJobsChanged;
                importJobQueue.Dispose();
            }
            handledImageImportJobs.Clear();
            handledImportConfirmations.Clear();
            importJobQueue = new ProjectJobQueue((request, cancellationRequested, progress) => ExecuteImportJob(session, request, cancellationRequested, progress));
            importJobQueue.JobsChanged += ImageImportJobsChanged;
            if (importQueueForm != null && !importQueueForm.IsDisposed) importQueueForm.UpdateJobs(new ImportJobSnapshot[0]);
            importImagesButton.Enabled = true;
            importFolderButton.Enabled = true;
            importAnnotationButton.Enabled = true;
            importQueueMenuItem.Enabled = true;
        }

        /// <summary>
        /// 依匯入工作種類執行對應專案操作並轉換成共用結果。
        /// </summary>
        /// <param name="session">目前專案的工作階段。</param>
        /// <param name="request">要處理的匯入工作要求。</param>
        /// <param name="cancellationRequested">檢查是否已要求取消的函式。</param>
        /// <param name="progress">最新的匯入進度。</param>
        /// <returns>匯入工作的執行結果。</returns>
        /// <exception cref="InvalidOperationException">目前狀態不允許此操作。</exception>
        private static ImportJobResult ExecuteImportJob(IProjectSession session, ImportJobRequest request, Func<bool> cancellationRequested, Action<ImportJobProgress> progress)
        {
            var jsonExport = request as JsonExportJobRequest;
            if (jsonExport != null)
            {
                if (cancellationRequested()) return new ImportJobResult(true, new string[0]);
                progress(new ImportJobProgress(0, 1, 0, 0));
                string output = session.ExportAnnotationJson(jsonExport.AnnotationSetId, jsonExport.OutputPath);
                progress(new ImportJobProgress(1, 1, 1, 0));
                return new ImportJobResult(false, new string[0], outputPath: output);
            }
            var zipExport = request as ImageZipExportJobRequest;
            if (zipExport != null)
            {
                if (cancellationRequested()) return new ImportJobResult(true, new string[0]);
                progress(new ImportJobProgress(0, 1, 0, 0));
                string output = session.ExportImagesZip(zipExport.OutputPath, zipExport.ImageIds);
                progress(new ImportJobProgress(1, 1, 1, 0));
                return new ImportJobResult(false, new string[0], outputPath: output);
            }
            ImageImportReport report;
            var imageRequest = request as ImageFilesImportJobRequest;
            if (imageRequest != null)
            {
                report = session.ImportFilesWithReport(imageRequest.FilePaths, cancellationRequested,
                    value => progress(new ImportJobProgress(value.ProcessedCount, value.TotalCount, value.SuccessfulCount, value.FailedCount)));
            }
            else if (request is FolderImportJobRequest)
            {
                var folderRequest = request as FolderImportJobRequest;
                report = session.ImportDirectory(folderRequest.DirectoryPath, folderRequest.IncludeSubdirectories, cancellationRequested,
                    value => progress(new ImportJobProgress(value.ProcessedCount, value.TotalCount, value.SuccessfulCount, value.FailedCount)));
            }
            else
            {
                var jsonRequest = request as AnnotationJsonImportJobRequest;
                if (jsonRequest == null) throw new InvalidOperationException("Unsupported import job kind: " + request.Kind);
                if (cancellationRequested()) return new ImportJobResult(true, new string[0]);
                string json = File.ReadAllText(jsonRequest.FilePath);
                bool allowUnknownFields = jsonRequest.ApprovedUnknownFields == null;
                if (jsonRequest.ApprovedUnknownFields != null)
                {
                    AnnotationJsonImportPreview executionPreview = AnnotationJsonImportPreview.Parse(json);
                    string unexpectedUnknownField = executionPreview.UnknownFields.Except(jsonRequest.ApprovedUnknownFields, StringComparer.Ordinal).FirstOrDefault();
                    if (unexpectedUnknownField != null) throw new JsonAnnotationImportException("JSON contains an unapproved unknown field: " + unexpectedUnknownField);
                    allowUnknownFields = executionPreview.UnknownFields.Count > 0;
                }
                if (jsonRequest.TargetAnnotationSetType.HasValue && session.GetAnnotationSets().SingleOrDefault(value => value.Id == jsonRequest.TargetAnnotationSetId)?.Type != jsonRequest.TargetAnnotationSetType.Value) throw new JsonAnnotationImportException("Target annotation set changed or model type does not match.");
                if (String.IsNullOrWhiteSpace(jsonRequest.TargetAnnotationSetId)) session.ImportAnnotationJson(json, jsonRequest.AnnotationSetName, allowUnknownFields);
                else session.ImportAnnotationJsonIntoSet(json, jsonRequest.TargetAnnotationSetId, jsonRequest.ImportAnnotationsAndClasses, jsonRequest.AnnotationMode, jsonRequest.ImportDataset, jsonRequest.DatasetMode, allowUnknownFields);
                return new ImportJobResult(false, new string[0]);
            }
            return new ImportJobResult(
                report.UnprocessedCount > 0 || report.WasCancelledDuringScan,
                report.Imported.Select(image => image.Id).ToArray(), report.Failures.Count, report.UnprocessedCount, report.WasCancelledDuringScan);
        }

        /// <summary>
        /// 在 UI 執行緒顯示最新匯入狀態並於工作結束後刷新圖片清單。
        /// </summary>
        /// <param name="jobs">要顯示的匯入工作快照。</param>
        private void ImageImportJobsChanged(IReadOnlyList<ImportJobSnapshot> jobs)
        {
            if (IsDisposed || Disposing) return;
            if (InvokeRequired)
            {
                try { BeginInvoke((Action<IReadOnlyList<ImportJobSnapshot>>)ImageImportJobsChanged, jobs); }
                catch (InvalidOperationException) { }
                return;
            }

            if (importQueueForm != null && !importQueueForm.IsDisposed)
            {
                importQueueForm.UpdateJobs(jobs);
                RefreshImportedImageAvailability(jobs);
            }
            importInProgress = importJobQueue != null && importJobQueue.IsBusy;
            UpdateProjectMenuAvailability();
            foreach (ImportJobSnapshot job in jobs.Where(value => value.RequiresConfirmation && value.Status == ImportJobStatus.Waiting))
            {
                if (!handledImportConfirmations.Add(job.Id)) continue;
                var jsonRequest = job.Request as AnnotationJsonImportJobRequest;
                if (jsonRequest == null) continue;
                DialogResult choice = MessageBox.Show(this, Strings.ImportAnnotationUnknownFields, Strings.ImportAnnotationJson, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
                importJobQueue.ResolveConfirmation(job.Id, choice == DialogResult.OK ? new AnnotationJsonImportJobRequest(jsonRequest.FilePath, jsonRequest.AnnotationSetName, jsonRequest.TargetAnnotationSetId, jsonRequest.TargetAnnotationSetType, jsonRequest.ImportAnnotationsAndClasses, jsonRequest.AnnotationMode, jsonRequest.ImportDataset, jsonRequest.DatasetMode, (IEnumerable<string>)null) : null);
            }
            foreach (ImportJobSnapshot job in jobs.Where(IsEndedImageImportJob))
            {
                if (!handledImageImportJobs.Add(job.Id)) continue;
                var jsonRequest = job.Request as AnnotationJsonImportJobRequest;
                if (jsonRequest != null)
                {
                    if (job.Status == ImportJobStatus.Completed) WriteDiagnostic(DiagnosticEvent.AnnotationJsonImported);
                    else WriteDiagnostic(DiagnosticEvent.OperationFailed);
                    if (job.Status == ImportJobStatus.Completed) RefreshAnnotationSets();
                }
                else if (job.Request is ExportJobRequest)
                {
                    if (job.Status == ImportJobStatus.Completed) WriteDiagnostic(job.Request is JsonExportJobRequest ? DiagnosticEvent.AnnotationJsonExported : DiagnosticEvent.ImagesExported);
                    else WriteDiagnostic(DiagnosticEvent.OperationFailed);
                }
                else
                {
                    if (job.Status == ImportJobStatus.Completed || job.Status == ImportJobStatus.Canceled) WriteDiagnostic(DiagnosticEvent.ImageImportCompleted);
                    else WriteDiagnostic(DiagnosticEvent.OperationFailed);
                    RefreshImageBrowser();
                }
            }
        }

        /// <summary>
        /// 判斷匯入工作是否已離開等待或執行狀態。
        /// </summary>
        /// <param name="job">要處理的匯入工作。</param>
        /// <returns>若符合摘要所述條件則為 true，否則為 false。</returns>
        private static bool IsEndedImageImportJob(ImportJobSnapshot job)
        {
            return job.Status == ImportJobStatus.Completed || job.Status == ImportJobStatus.Failed || job.Status == ImportJobStatus.Canceled;
        }

        /// <summary>
        /// 切換圖片頁並選取指定匯入工作仍存在的圖片。
        /// </summary>
        /// <param name="jobId">匯入工作的識別碼。</param>
        private void OpenExportedFile(string path)
        {
            if (String.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
            Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"") { UseShellExecute = true });
        }

        private void ViewImportedImages(Guid jobId)
        {
            if (project == null || importJobQueue == null || importQueueForm == null) return;
            ImportJobSnapshot job = importJobQueue.Jobs.FirstOrDefault(value => value.Id == jobId);
            if (!CanViewImportedImages(job)) return;
            var existingImageIds = new HashSet<string>(project.GetImages().Select(image => image.Id), StringComparer.Ordinal);
            string[] imageIds = job.Result.ImportedImageIds.Where(existingImageIds.Contains).ToArray();
            if (imageIds.Length == 0)
            {
                importQueueForm.SetImportedImagesAvailability(jobId, false, Strings.ImportedImagesUnavailable);
                return;
            }
            pages.SelectedTab = imagesPage;
            imageBrowser.SelectImages(imageIds);
        }

        /// <summary>
        /// 判斷匯入工作是否包含可供批次跳轉的成功圖片。
        /// </summary>
        /// <param name="job">要檢查的匯入工作快照。</param>
        /// <returns>圖片或資料夾工作已完成或部分取消且含成功圖片時為 true。</returns>
        private static bool CanViewImportedImages(ImportJobSnapshot job)
        {
            return job != null
                && job.Result != null
                && !(job.Request is AnnotationJsonImportJobRequest)
                && (job.Status == ImportJobStatus.Completed || job.Status == ImportJobStatus.Canceled)
                && job.Result.ImportedImageIds.Count > 0;
        }

        /// <summary>
        /// 依目前專案圖片更新已完成或部分取消匯入工作的跳轉可用性。
        /// </summary>
        /// <param name="jobs">要顯示的匯入工作快照。</param>
        private void RefreshImportedImageAvailability(IReadOnlyList<ImportJobSnapshot> jobs)
        {
            if (importQueueForm == null || importQueueForm.IsDisposed) return;
            var existingImageIds = new HashSet<string>(project == null ? new string[0] : project.GetImages().Select(image => image.Id), StringComparer.Ordinal);
            foreach (ImportJobSnapshot job in (jobs ?? new ImportJobSnapshot[0]).Where(CanViewImportedImages))
            {
                bool hasExisting = job.Result.ImportedImageIds.Any(existingImageIds.Contains);
                importQueueForm.SetImportedImagesAvailability(job.Id, hasExisting, hasExisting ? null : Strings.ImportedImagesUnavailable);
            }
        }

        /// <summary>
        /// 僅在沒有匯入或背景標註操作時允許更換目前專案。
        /// </summary>
        private void UpdateProjectMenuAvailability()
        {
            bool projectChangeAllowed = !importInProgress && !annotationOperationInProgress;
            bool annotationSetLocked = IsSelectedAnnotationSetLocked();
            projectMenu.Enabled = true;
            newProjectMenuItem.Enabled = projectChangeAllowed;
            openProjectMenuItem.Enabled = projectChangeAllowed;
            saveProjectMenuItem.Enabled = projectChangeAllowed;
            saveProjectAsMenuItem.Enabled = projectChangeAllowed;
            backupsMenuItem.Enabled = projectChangeAllowed;
            missingImagesMenuItem.Enabled = projectChangeAllowed;
            importQueueMenuItem.Enabled = project != null;
            importAnnotationButton.Enabled = project != null && SelectedAnnotationSet != null && !importInProgress && !annotationSetLocked;
            deleteSelectedImagesButton.Enabled = !importInProgress;
            deleteAllImagesButton.Enabled = !importInProgress;
            exportSelected.Enabled = !importInProgress;
            exportAll.Enabled = !importInProgress;
            datasetAssignments.Enabled = !annotationSetLocked;
            annotationTools.Enabled = !annotationSetLocked;
            annotationClasses.Enabled = !annotationSetLocked;
            newSet.Enabled = projectChangeAllowed;
            renameSet.Enabled = projectChangeAllowed;
            deleteSet.Enabled = projectChangeAllowed;
            addClass.Enabled = projectChangeAllowed;
            foreach (Button classButton in annotationClasses.Controls.OfType<Button>())
            {
                if (classButton.ContextMenuStrip != null) classButton.ContextMenuStrip.Enabled = projectChangeAllowed;
            }
        }

        private bool IsSelectedAnnotationSetLocked()
        {
            AnnotationSet selected = SelectedAnnotationSet;
            if (selected == null || importJobQueue == null) return false;
            return importJobQueue.Jobs.Any(job => (job.Status == ImportJobStatus.Waiting || job.Status == ImportJobStatus.Running)
                && ((job.Request as AnnotationJsonImportJobRequest)?.TargetAnnotationSetId == selected.Id
                    || (job.Request as JsonExportJobRequest)?.AnnotationSetId == selected.Id));
        }

        /// <summary>
        /// 顯示單一非強制回應匯入佇列視窗。
        /// </summary>
        private void ShowImportQueue()
        {
            if (importQueueForm == null || importQueueForm.IsDisposed)
            {
                importQueueForm = new ImportQueueForm();
                importQueueForm.CancelRequested += id => importJobQueue?.Cancel(id);
                importQueueForm.ClearCompletedRequested += () => importJobQueue?.ClearCompleted();
                importQueueForm.ViewImportedImagesRequested += ViewImportedImages;
                importQueueForm.OpenExportRequested += OpenExportedFile;
            }
            importQueueForm.UpdateJobs(importJobQueue == null ? new ImportJobSnapshot[0] : importJobQueue.Jobs);
            RefreshImportedImageAvailability(importJobQueue == null ? new ImportJobSnapshot[0] : importJobQueue.Jobs);
            if (!importQueueForm.Visible) importQueueForm.Show(this);
            else importQueueForm.Activate();
        }

        /// <summary>
        /// 執行匯入工作並管理進度視窗與取消操作。
        /// </summary>
        /// <param name="import">執行圖片或標註匯入並回報進度的委派。</param>
        /// <returns>代表非同步操作的工作。</returns>
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
                    if (project != null) RefreshImageBrowser();
                }
            }
        }

        /// <summary>
        /// 建立設定專案儲存位置的資料夾選擇對話框。
        /// </summary>
        /// <returns>包含命中控制點種類及其所屬 SEG 區域的結果。</returns>
        private static SaveFileDialog CreateProjectDialog()
        {
            return new SaveFileDialog { DefaultExt = "neurocle", AddExtension = true, Filter = Strings.ProjectFileFilter };
        }

        /// <summary>
        /// 替換目前專案工作階段並重建相關畫面。
        /// </summary>
        /// <param name="replacement">要設為目前專案的新工作階段。</param>
        private void ReplaceProject(IProjectSession replacement)
        {
            IProjectSession previous = project;
            project = replacement;
            previous?.Dispose();
            InitializeImageImportQueue(replacement);
            ResetAnnotationOpacity();
            showAnnotations.Checked = true;
            canvas.DiscardUnfinishedShape();
            UpdateProjectStatus();
            imageBrowser.LoadImages(project.GetImages(), GetImagesDirectory(project.ProjectPath));
            RefreshAnnotationSets();
        }

        /// <summary>
        /// 依目前專案狀態更新視窗標題與狀態列。
        /// </summary>
        private void UpdateProjectStatus()
        {
            string projectName = Path.GetFileName(project.ProjectPath);
            int missingCount = project.GetMissingImages().Count;
            status.Text = missingCount == 0 ? projectName : projectName + " - " + string.Format(Strings.MissingImagesStatus, missingCount);
        }

        /// <summary>
        /// 取得專案圖片目錄的完整路徑。
        /// </summary>
        /// <param name="projectPath">專案資料庫路徑。</param>
        /// <returns>轉換或取得的文字結果。</returns>
        private static string GetImagesDirectory(string projectPath)
        {
            return Path.Combine(Path.GetDirectoryName(projectPath), Path.GetFileNameWithoutExtension(projectPath) + ".images");
        }

        /// <summary>
        /// 執行專案命令並顯示操作錯誤。
        /// </summary>
        /// <param name="command">要執行的專案或標註命令。</param>
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

        /// <summary>
        /// 寫入診斷事件並處理記錄失敗。
        /// </summary>
        /// <param name="diagnosticEvent">要記錄的診斷事件。</param>
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

        /// <summary>
        /// 將例外轉換為適合顯示的錯誤訊息。
        /// </summary>
        /// <param name="exception">發生的例外資訊。</param>
        /// <returns>轉換或取得的文字結果。</returns>
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

        /// <summary>
        /// 格式化匯入摘要與各類結果數量。
        /// </summary>
        /// <param name="report">匯入結果報告。</param>
        /// <returns>轉換或取得的文字結果。</returns>
        private static string FormatImportReport(ImageImportReport report)
        {
            string unprocessedCount = report.WasCancelledDuringScan ? Strings.ImportReportUnknownCount : report.UnprocessedCount.ToString();
            string summary = string.Format(Strings.ImportReportSummary, report.Imported.Count, report.Failures.Count, unprocessedCount);
            if (report.WasCancelledDuringScan) summary += Environment.NewLine + Strings.ImportScanCancelled;
            if (report.Failures.Count == 0) return summary;
            string failures = string.Join(Environment.NewLine, report.Failures.Select(failure => Path.GetFileName(failure.FilePath) + ": " + GetImportFailureMessage(failure.Failure)));
            return summary + Environment.NewLine + failures;
        }

        /// <summary>
        /// 依匯入失敗類型產生使用者可讀訊息。
        /// </summary>
        /// <param name="failure">匯入失敗資訊。</param>
        /// <returns>轉換或取得的文字結果。</returns>
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

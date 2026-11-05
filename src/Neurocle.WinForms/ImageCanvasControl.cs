using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Clipper2Lib;
using Neurocle.Application.Projects;
using Neurocle.Domain.Segmentation;

namespace Neurocle.WinForms
{
    // 繪製圖片、標註、互動預覽與目前畫布視角。
    internal sealed class ImageCanvasControl : Control
    {
        // 指定 OBD 及 SEG 編輯控制點的畫面尺寸。
        private const float HandleSize = 8f;
        // 設定 SEG 框選運算使用的整數縮放精度。
        private const long SegSelectionScale = 1000;
        // 保存隱藏系統游標，供自繪工具游標顯示時使用。
        private static readonly Cursor InvisibleCursor = CreateInvisibleCursor();
        // 保存圖片尺寸、縮放倍率及原圖畫面位置。
        private readonly CanvasViewport viewport = new CanvasViewport(Size.Empty, Size.Empty);
        // 確保圖片解碼不會同時進行多次。
        private readonly SemaphoreSlim imageLoadGate = new SemaphoreSlim(1, 1);
        // 保存目前顯示的圖片預覽影像。
        private Image image;
        // 保存目前載入圖片的來源路徑。
        private string loadedImagePath;
        // 保存目前圖片載入作業的取消權杖。
        private CancellationTokenSource imageLoad;
        // 指出目前是否以滑鼠拖曳平移畫布。
        private bool panning;
        // 指出目前是否正在拖曳縮圖中的視野框。
        private bool thumbnailPanning;
        // 記錄導覽縮圖是否已收合。
        private bool thumbnailCollapsed;
        // 指出目前標註手勢期間是否暫時隱藏導覽縮圖。
        private bool thumbnailHiddenForGesture;
        // 保存縮圖像素轉換為原圖位移的比例。
        private float thumbnailDragScale;
        // 保存前一個滑鼠座標供拖曳計算。
        private Point previousPoint;
        // 保存 OBD 或 SEG 框選手勢的原圖起點。
        private PointF? selectionStart;
        // 保存框選手勢起始螢幕座標以判斷是否實際拖曳。
        private Point selectionScreenStart;
        // 保存框選手勢的即時預覽矩形。
        private RectangleF? selectionPreview;
        // 指出框選是否保留既有選取並加入新項目。
        private bool selectionAdditive;
        // 指出目前框選目標是 SEG 還是 OBD。
        private bool selectionIsSeg;
        // 保存 OBD 矩形繪製的原圖起點。
        private PointF obdStart;
        // 保存尚未提交的 OBD 矩形預覽。
        private RectangleF? obdPreview;
        // 保存 OBD 拖曳或鍵盤移動中的多選預覽位置。
        private IReadOnlyDictionary<string, RectangleF> obdPreviews = new Dictionary<string, RectangleF>();
        // 保存目前畫布要繪製的 OBD 標註。
        private IReadOnlyList<ObdAnnotation> obdAnnotations = new List<ObdAnnotation>();
        // 保存 OBD 標註識別碼對應的類別顯示資料。
        private IReadOnlyDictionary<string, AnnotationClass> obdClasses = new Dictionary<string, AnnotationClass>();
        // 保存目前拖曳編輯的 OBD 標註。
        private ObdAnnotation activeObd;
        // 記錄目前 OBD 建立、移動或縮放手勢。
        private ObdDragKind obdDrag;
        // 保存 OBD 編輯前的原始矩形。
        private RectangleF obdOriginal;
        // 保存 OBD 拖曳手勢的原圖起點。
        private PointF obdDragStart;
        // 保存目前選取的全部 OBD 標註識別碼。
        private readonly HashSet<string> selectedObdIds = new HashSet<string>(StringComparer.Ordinal);
        // 保存主要 OBD 選取識別碼。
        private string selectedObdId;
        // 指出方向鍵移動預覽是否正在累積。
        private bool keyboardNudgeActive;
        // 指出目前鍵盤移動預覽屬於 OBD 或 SEG。
        private bool keyboardNudgeIsObd;
        // 保存尚未提交的鍵盤水平移動量。
        private int keyboardNudgeX;
        // 保存尚未提交的鍵盤垂直移動量。
        private int keyboardNudgeY;
        // 保存 SEG 矩形或橢圓繪製的原圖起點。
        private PointF segStart;
        // 保存尚未提交的 SEG 矩形或橢圓預覽。
        private RectangleF? segPreview;
        // 保存目前畫筆、線形或多邊形的路徑點。
        private readonly List<PointF> segPath = new List<PointF>();
        // 指出目前是否按住滑鼠持續繪製 SEG 路徑。
        private bool segPathDragging;
        // 保存線形工具目前線段的固定起點。
        private PointF? segLineStart;
        // 保存線形預覽的目前指標位置。
        private PointF segCursor;
        // 保存自繪工具游標的目前畫面座標。
        private PointF toolCursor;
        // 保存最後一次滑鼠移動座標供工具游標重算。
        private Point lastMousePosition;
        // 指出滑鼠指標目前是否位於畫布內。
        private bool pointerInside;
        // 指出目前是否需要繪製自訂工具游標。
        private bool hasToolCursor;
        // 保存目前畫布要繪製的 SEG 區域。
        private IReadOnlyList<SegRegion> segRegions = new List<SegRegion>();
        // 保存 SEG 區域識別碼對應的類別顯示資料。
        private IReadOnlyDictionary<string, AnnotationClass> segClasses = new Dictionary<string, AnnotationClass>();
        // 保存目前選取的全部 SEG 區域識別碼。
        private readonly HashSet<string> selectedSegIds = new HashSet<string>(StringComparer.Ordinal);
        // 保存主要 SEG 選取識別碼。
        private string selectedSegId;
        // 保存 SEG 拖曳、縮放、旋轉或鍵盤移動預覽。
        private IReadOnlyDictionary<string, SegRegion> segPreviews = new Dictionary<string, SegRegion>();
        // 保存 SEG 編輯手勢的原圖起點。
        private PointF segDragStart;
        // 記錄目前 SEG 移動、縮放或旋轉手勢。
        private SegDragKind segDrag;
        // 保存 SEG 編輯前的原始區域。
        private SegRegion segOriginal;
        // 保存 SEG 編輯前的外接矩形。
        private RectangleF segOriginalBounds;
        // 保存目前 SEG 旋轉預覽角度。
        private double segRotationAngle;
        // 保存目前 SEG 筆刷寬度設定。
        private int segStrokeWidth = 15;
        // 保存正在繪製路徑所使用的筆畫寬度。
        private int segPathStrokeWidth;
        // 保存目前繪製與閉環預覽使用的顏色。
        private Color previewColor = Color.White;
        // 記錄標註是否顯示並允許選取。
        private bool annotationsVisible = true;

        // 取得或設定 OBD 工具是否啟用。
        public bool ObdEnabled { get { return obdEnabled; } set { if (obdEnabled == value) return; obdEnabled = value; UpdateToolCursor(); Invalidate(); } }
        // 記錄 OBD 工具是否啟用。
        private bool obdEnabled;
        // 取得或設定 OBD 是否處於選取模式。
        public bool ObdSelectMode
        {
            get { return obdSelectMode; }
            set { if (obdSelectMode == value) return; obdSelectMode = value; UpdateToolCursor(); Invalidate(); }
        }
        // 記錄 OBD 工具是否處於選取模式。
        private bool obdSelectMode;
        // 提供目前修飾鍵狀態的可替換來源。
        internal Func<Keys> ModifierKeysProvider { get; set; }
        // 取得或設定 SEG 工具是否啟用。
        public bool SegEnabled { get { return segEnabled; } set { if (segEnabled == value) return; segEnabled = value; UpdateToolCursor(); Invalidate(); } }
        // 記錄 SEG 工具是否啟用。
        private bool segEnabled;
        // 取得或設定目前使用的 SEG 工具。
        public SegToolKind? SegTool
        {
            get { return segTool; }
            set { if (segTool == value) return; segTool = value; UpdateToolCursor(); Invalidate(); }
        }
        // 記錄目前使用的 SEG 工具種類。
        private SegToolKind? segTool;
        // 取得或設定 SEG 筆刷寬度，範圍為 1 至 100。
        public int SegStrokeWidth
        {
            get { return segStrokeWidth; }
            set
            {
                if (value < 1 || value > 100) throw new ArgumentOutOfRangeException(nameof(value));
                if (segStrokeWidth == value) return;
                segStrokeWidth = value;
                RefreshToolCursor();
                Invalidate();
            }
        }
        // 取得目前修飾鍵，必要時使用測試替代來源。
        private Keys CurrentModifierKeys { get { return ModifierKeysProvider == null ? ModifierKeys : ModifierKeysProvider(); } }
        // 設定 OBD 標註的顯示透明度。
        public float ObdOpacity { get; set; } = 0.4f;
        // 設定 SEG 標註的顯示透明度。
        public float SegOpacity { get; set; } = 0.4f;
        // 取得或設定標註的顯示與互動狀態。
        public bool AnnotationsVisible
        {
            get { return annotationsVisible; }
            set
            {
                if (annotationsVisible == value) return;
                annotationsVisible = value;
                if (!value)
                {
                    CancelInProgressGesture();
                    ClearObdSelection();
                    ClearSegSelection();
                }
                Invalidate();
            }
        }
        // 取得或設定目前繪製預覽顏色。
        public Color PreviewColor
        {
            get { return previewColor; }
            set { if (previewColor == value) return; previewColor = value; Invalidate(); }
        }
        // 取得主要選取的 SEG 區域識別碼。
        public string SelectedSegId { get { return selectedSegId; } }
        // 取得所有已選取的 SEG 區域識別碼。
        public IReadOnlyCollection<string> SelectedSegIds { get { return selectedSegIds; } }
        // 取得主要選取的 OBD 標註識別碼。
        public string SelectedObdId { get { return selectedObdId; } }
        // 取得所有已選取的 OBD 標註識別碼。
        public IReadOnlyCollection<string> SelectedObdIds { get { return selectedObdIds; } }
        /// <summary>
        /// 選取目前畫布上的全部 OBD 標註，並更新主要選取識別碼。
        /// </summary>
        public void SelectAllObd()
        {
            if (!AnnotationsVisible) return;
            selectedObdIds.Clear();
            foreach (ObdAnnotation annotation in obdAnnotations) selectedObdIds.Add(annotation.Id);
            selectedObdId = selectedObdIds.FirstOrDefault();
            Invalidate();
        }
        /// <summary>
        /// 依提供的識別碼選取現有 OBD 標註，忽略不存在的識別碼。
        /// </summary>
        /// <param name="annotationIds">標註識別碼清單。</param>
        public void SelectObdAnnotations(IEnumerable<string> annotationIds)
        {
            selectedObdIds.Clear();
            if (AnnotationsVisible && annotationIds != null)
            {
                HashSet<string> existingIds = new HashSet<string>(obdAnnotations.Select(annotation => annotation.Id), StringComparer.Ordinal);
                foreach (string id in annotationIds) if (existingIds.Contains(id)) selectedObdIds.Add(id);
            }
            selectedObdId = selectedObdIds.FirstOrDefault();
            Invalidate();
        }
        /// <summary>
        /// 選取目前畫布上的全部 SEG 區域，並更新主要選取識別碼。
        /// </summary>
        public void SelectAllSeg()
        {
            if (!AnnotationsVisible) return;
            selectedSegIds.Clear();
            foreach (SegRegion region in segRegions) selectedSegIds.Add(region.Id);
            selectedSegId = selectedSegIds.FirstOrDefault();
            Invalidate();
        }

        /// <summary>
        /// 依方向鍵累積移動所選 OBD 或 SEG 標註一像素的預覽；整組越界時拒絕移動。
        /// </summary>
        /// <param name="deltaX">水平移動量。</param>
        /// <param name="deltaY">垂直移動量。</param>
        /// <returns>若符合摘要所述條件則為 true，否則為 false。</returns>
        internal bool NudgeSelectedAnnotations(int deltaX, int deltaY)
        {
            if ((deltaX == 0 && deltaY == 0) || !AnnotationsVisible || image == null) return false;
            bool moveObd = ObdEnabled && ObdSelectMode && selectedObdIds.Count > 0;
            bool moveSeg = SegEnabled && SegTool == SegToolKind.Select && selectedSegIds.Count > 0;
            if (!moveObd && !moveSeg) return false;

            int nextX = keyboardNudgeX + deltaX;
            int nextY = keyboardNudgeY + deltaY;
            if (moveObd)
            {
                ObdAnnotation[] selected = obdAnnotations.Where(item => selectedObdIds.Contains(item.Id)).ToArray();
                if (selected.Length != selectedObdIds.Count || selected.Any(item => item.X + nextX < 0 || item.Y + nextY < 0 || item.X + item.Width + nextX > viewport.ImageSize.Width || item.Y + item.Height + nextY > viewport.ImageSize.Height)) return true;
                obdPreviews = selected.ToDictionary(item => item.Id, item => new RectangleF(item.X + nextX, item.Y + nextY, item.Width, item.Height));
            }
            else
            {
                SegRegion[] selected = segRegions.Where(item => selectedSegIds.Contains(item.Id)).ToArray();
                if (selected.Length != selectedSegIds.Count || selected.Any(item =>
                {
                    RectangleF bounds = GetStoredBounds(item);
                    return bounds.Left + nextX < 0f || bounds.Top + nextY < 0f || bounds.Right + nextX > viewport.ImageSize.Width || bounds.Bottom + nextY > viewport.ImageSize.Height;
                })) return true;
                segPreviews = selected.ToDictionary(item => item.Id, item => TranslateSegRegion(item, nextX, nextY));
            }

            keyboardNudgeActive = true;
            keyboardNudgeIsObd = moveObd;
            keyboardNudgeX = nextX;
            keyboardNudgeY = nextY;
            Invalidate();
            return true;
        }

        /// <summary>
        /// 完成鍵盤移動預覽並一次發出整組標註變更事件。
        /// </summary>
        /// <returns>若符合摘要所述條件則為 true，否則為 false。</returns>
        internal bool CommitKeyboardNudge()
        {
            if (!keyboardNudgeActive) return false;
            bool moveObd = keyboardNudgeIsObd;
            bool changed = keyboardNudgeX != 0 || keyboardNudgeY != 0;
            IReadOnlyDictionary<string, Rectangle> obdChanges = moveObd
                ? obdPreviews.ToDictionary(item => item.Key, item => ToIntegerRectangle(item.Value))
                : null;
            SegRegion[] segChanges = moveObd ? null : segPreviews.Values.ToArray();
            obdPreviews = new Dictionary<string, RectangleF>();
            segPreviews = new Dictionary<string, SegRegion>();
            keyboardNudgeActive = false;
            keyboardNudgeIsObd = false;
            keyboardNudgeX = 0;
            keyboardNudgeY = 0;
            Invalidate();
            if (!changed) return true;
            if (moveObd && obdChanges.Count > 0) ObdRectanglesChanged?.Invoke(obdChanges);
            else if (!moveObd && segChanges.Length > 0) SegRegionsChanged?.Invoke(segChanges);
            return true;
        }
        /// <summary>
        /// 依提供的識別碼選取現有 SEG 區域，忽略不存在的識別碼。
        /// </summary>
        /// <param name="regionIds">SEG 區域識別碼清單。</param>
        public void SelectSegRegions(IEnumerable<string> regionIds)
        {
            selectedSegIds.Clear();
            if (AnnotationsVisible && regionIds != null)
            {
                HashSet<string> existingIds = new HashSet<string>(segRegions.Select(region => region.Id), StringComparer.Ordinal);
                foreach (string id in regionIds) if (existingIds.Contains(id)) selectedSegIds.Add(id);
            }
            selectedSegId = selectedSegIds.FirstOrDefault();
            Invalidate();
        }
        // 取得或更新目前畫布顯示的 OBD 標註。
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public IReadOnlyList<ObdAnnotation> ObdAnnotations
        {
            get { return obdAnnotations; }
            set
            {
                obdAnnotations = value ?? new List<ObdAnnotation>();
                selectedObdIds.RemoveWhere(id => !obdAnnotations.Any(annotation => annotation.Id == id));
                if (!selectedObdIds.Contains(selectedObdId)) selectedObdId = selectedObdIds.FirstOrDefault();
                Invalidate();
            }
        }
        // 取得或更新 OBD 類別顯示資料。
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public IReadOnlyDictionary<string, AnnotationClass> ObdClasses { get { return obdClasses; } set { obdClasses = value ?? new Dictionary<string, AnnotationClass>(); Invalidate(); } }
        // 取得或更新目前畫布顯示的 SEG 區域。
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public IReadOnlyList<SegRegion> SegRegions
        {
            get { return segRegions; }
            set
            {
                segRegions = value ?? new List<SegRegion>();
                selectedSegIds.RemoveWhere(id => !segRegions.Any(region => region.Id == id));
                if (!selectedSegIds.Contains(selectedSegId)) selectedSegId = selectedSegIds.FirstOrDefault();
                Invalidate();
            }
        }
        // 取得或更新 SEG 類別顯示資料。
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public IReadOnlyDictionary<string, AnnotationClass> SegClasses { get { return segClasses; } set { segClasses = value ?? new Dictionary<string, AnnotationClass>(); Invalidate(); } }
        // 通知外層 OBD 矩形已完成繪製。
        public event Action<Rectangle> ObdRectangleCreated;
        // 通知外層一組 OBD 標註位置或尺寸已變更。
        public event Action<IReadOnlyDictionary<string, Rectangle>> ObdRectanglesChanged;
        // 通知外層刪除指定 OBD 標註。
        public event Action<IReadOnlyCollection<string>> ObdAnnotationsDeleteRequested;
        // 通知外層矩形或橢圓 SEG 區域已完成繪製。
        public event Action<SegRegionType, Rectangle> SegRegionCreated;
        // 通知外層多邊形或線形 SEG 路徑已完成繪製。
        public event Action<SegRegionType, IReadOnlyList<SegPoint>, int> SegPathCreated;
        public event Action<SegRegionType, Rectangle, IReadOnlyList<SegPoint>, int> SegGeometryEraseRequested;
        // 通知外層以指定筆畫擦除 SEG 區域。
        public event Action<IReadOnlyList<SegPoint>, int> SegEraseRequested;
        // 通知外層一組 SEG 區域幾何已變更。
        public event Action<IReadOnlyList<SegRegion>> SegRegionsChanged;
        // 通知外層刪除指定 SEG 區域。
        public event Action<IReadOnlyCollection<string>> SegRegionsDeleteRequested;

        /// <summary>
        /// 初始化雙緩衝畫布並同步視角與控制項尺寸。
        /// </summary>
        public ImageCanvasControl()
        {
            DoubleBuffered = true;
            BackColor = Color.FromArgb(45, 45, 48);
            TabStop = true;
            Resize += (sender, args) => { viewport.SetViewportSize(ClientSize); RefreshToolCursor(); Invalidate(); RaiseViewChanged(); };
        }

        // 取得目前畫布縮放倍率。
        public double Zoom { get { return viewport.Zoom; } }
        // 取得導覽縮圖是否收合。
        internal bool IsThumbnailCollapsed { get { return thumbnailCollapsed; } }
        // 取得水平捲動位置的最大值。
        internal int HorizontalScrollMaximum { get { return viewport.HorizontalScrollMaximum; } }
        // 取得垂直捲動位置的最大值。
        internal int VerticalScrollMaximum { get { return viewport.VerticalScrollMaximum; } }
        // 通知外層畫布縮放倍率已變更。
        public event EventHandler ZoomChanged;
        // 通知外層畫布位置或可視範圍已變更。
        public event EventHandler ViewChanged;
        // 取得目前載入圖片的路徑。
        internal string LoadedImagePath { get { return loadedImagePath; } }
        // 取得供畫布同步使用的縮放與位置快照。
        internal CanvasViewState ViewState { get { return new CanvasViewState(viewport.Zoom, viewport.Origin, viewport.IsFitted); } }

        /// <summary>
        /// 套用圖片頁或標註頁同步傳入的縮放與原圖位置。
        /// </summary>
        /// <param name="state">要套用的視角或狀態。</param>
        internal void SetViewState(CanvasViewState state)
        {
            viewport.SetView(state.Zoom, state.Origin, state.IsFitted);
            RefreshToolCursor();
            Invalidate();
            RaiseViewChanged();
        }

        /// <summary>
        /// 取消舊圖片載入及未完成形狀，再開始載入指定圖片。
        /// </summary>
        /// <param name="path">要處理的檔案或幾何路徑。</param>
        public void LoadImage(string path)
        {
            DiscardUnfinishedShape();
            CancelImageLoad();
            ClearImage();
            if (path != null) LoadImageAsync(path);
        }

        /// <summary>
        /// 在背景載入圖片，並將有效結果安全地套用至 UI 執行緒。
        /// </summary>
        /// <param name="path">要處理的檔案或幾何路徑。</param>
        private async void LoadImageAsync(string path)
        {
            var cancellation = new CancellationTokenSource();
            imageLoad = cancellation;
            try
            {
                CanvasImage loaded = await Task.Run(() => LoadCanvasImage(path, cancellation.Token), cancellation.Token);
                if (!TryInvokeOnUiThread(() =>
                {
                    if (cancellation.IsCancellationRequested || IsDisposed || Disposing) { loaded.Preview.Dispose(); return; }
                    image = loaded.Preview;
                    loadedImagePath = path;
                    viewport.SetImageSize(loaded.OriginalSize);
                    UpdateToolCursor();
                    Invalidate();
                    RaiseViewChanged();
                })) loaded.Preview.Dispose();
            }
            catch (OperationCanceledException) { }
            catch
            {
                if (!cancellation.IsCancellationRequested)
                    TryInvokeOnUiThread(() => { if (!IsDisposed && !Disposing) ClearImage(); });
            }
            finally
            {
                if (ReferenceEquals(imageLoad, cancellation)) imageLoad = null;
                cancellation.Dispose();
            }
        }

        /// <summary>
        /// 在可取消的載入閘門內解碼圖片並建立畫布預覽影像。
        /// </summary>
        /// <param name="path">要處理的檔案或幾何路徑。</param>
        /// <param name="cancellation">用於取消等待或圖片載入的權杖。</param>
        /// <returns>載入的畫布圖片資料。</returns>
        private CanvasImage LoadCanvasImage(string path, CancellationToken cancellation)
        {
            bool acquired = false;
            try
            {
                imageLoadGate.Wait(cancellation);
                acquired = true;
                using (var source = Image.FromFile(path))
                {
                    cancellation.ThrowIfCancellationRequested();
                    var preview = new Bitmap(source);
                    try { cancellation.ThrowIfCancellationRequested(); return new CanvasImage(preview, source.Size); }
                    catch { preview.Dispose(); throw; }
                }
            }
            finally { if (acquired) imageLoadGate.Release(); }
        }

        /// <summary>
        /// 清除目前圖片與畫布視角，並重繪空白畫布狀態。
        /// </summary>
        private void ClearImage()
        {
            Image previous = image;
            image = null;
            loadedImagePath = null;
            hasToolCursor = false;
            toolCursor = PointF.Empty;
            UpdateToolCursor();
            previous?.Dispose();
            viewport.SetImageSize(Size.Empty);
            Invalidate();
            RaiseViewChanged();
        }

        /// <summary>
        /// 取消目前尚未完成的圖片載入要求。
        /// </summary>
        private void CancelImageLoad() { imageLoad?.Cancel(); imageLoad = null; }

        /// <summary>
        /// 在控制項可用時將指定動作派送到 UI 執行緒。
        /// </summary>
        /// <param name="action">要執行的操作。</param>
        /// <returns>若符合摘要所述條件則為 true，否則為 false。</returns>
        private bool TryInvokeOnUiThread(Action action)
        {
            if (IsDisposed || Disposing) return false;
            if (!InvokeRequired) { action(); return true; }
            try
            {
                Invoke(new MethodInvoker(action.Invoke));
                return true;
            }
            catch (InvalidOperationException) { return false; }
        }
        /// <summary>
        /// 將完整圖片等比例縮放並置中於目前可視範圍。
        /// </summary>
        public void FitToWindow() { viewport.FitToWindow(); RefreshToolCursor(); Invalidate(); RaiseViewChanged(); }
        /// <summary>
        /// 以目前可視範圍中心套用指定縮放百分比。
        /// </summary>
        /// <param name="percent">縮放或分配百分比。</param>
        public void SetZoomPercent(double percent)
        {
            viewport.SetZoomAt(percent / 100d, new PointF(ClientSize.Width / 2f, ClientSize.Height / 2f));
            RefreshToolCursor();
            Invalidate();
            RaiseViewChanged();
        }

        /// <summary>
        /// 設定原圖水平與垂直捲動位置並通知視角變更。
        /// </summary>
        /// <param name="horizontal">水平捲軸。</param>
        /// <param name="vertical">垂直捲軸。</param>
        internal void SetScrollPosition(int horizontal, int vertical)
        {
            viewport.SetScrollPosition(horizontal, vertical);
            RefreshToolCursor();
            Invalidate();
            RaiseViewChanged();
        }

        /// <summary>
        /// 取消尚未完成的繪製、選取、路徑或工具預覽。
        /// </summary>
        public void DiscardUnfinishedShape()
        {
            CancelInProgressGesture();
            selectedSegIds.Clear();
            selectedSegId = null;
        }

        /// <summary>
        /// 取消目前滑鼠手勢並清除尚未提交的預覽與滑鼠捕捉。
        /// </summary>
        internal void CancelInProgressGesture()
        {
            obdPreview = null;
            obdPreviews = new Dictionary<string, RectangleF>();
            selectionStart = null;
            selectionPreview = null;
            segPreview = null;
            segPath.Clear();
            segPathStrokeWidth = 0;
            segPathDragging = false;
            segLineStart = null;
            segPreviews = new Dictionary<string, SegRegion>();
            segDrag = SegDragKind.None;
            segRotationAngle = 0d;
            activeObd = null;
            obdDrag = ObdDragKind.None;
            panning = false;
            keyboardNudgeActive = false;
            keyboardNudgeIsObd = false;
            keyboardNudgeX = 0;
            keyboardNudgeY = 0;
            thumbnailPanning = false;
            thumbnailHiddenForGesture = false;
            Capture = false;
            RefreshToolCursor();
            ApplyToolCursor();
            Invalidate();
        }

        /// <summary>
        /// 設定標註手勢期間的縮圖暫時隱藏狀態。
        /// </summary>
        /// <param name="hidden">是否隱藏縮圖。</param>
        private void SetThumbnailHiddenForGesture(bool hidden)
        {
            if (thumbnailHiddenForGesture == hidden) return;
            thumbnailHiddenForGesture = hidden;
            RefreshToolCursor();
            ApplyToolCursor();
            Invalidate();
        }

        /// <summary>
        /// 依目前視角繪製圖片、標註、編輯預覽、工具游標及導覽縮圖。
        /// </summary>
        /// <param name="args">包含此次事件詳細資料的事件資料。</param>
        protected override void OnPaint(PaintEventArgs args)
        {
            base.OnPaint(args);
            if (image == null) return;
            PointF origin = viewport.Origin;
            args.Graphics.DrawImage(image, new RectangleF(origin.X, origin.Y, (float)(viewport.ImageSize.Width * viewport.Zoom), (float)(viewport.ImageSize.Height * viewport.Zoom)));
            foreach (ObdAnnotation annotation in obdAnnotations.Where(item => !selectedObdIds.Contains(item.Id)).Concat(obdAnnotations.Where(item => selectedObdIds.Contains(item.Id))))
            {
                if (!AnnotationsVisible) continue;
                RectangleF imageRectangle;
                if (!obdPreviews.TryGetValue(annotation.Id, out imageRectangle)) imageRectangle = new RectangleF(annotation.X, annotation.Y, annotation.Width, annotation.Height);
                RectangleF rectangle = ToScreenRectangle(imageRectangle.X, imageRectangle.Y, imageRectangle.Width, imageRectangle.Height);
                RectangleF paintBounds = rectangle;
                paintBounds.Inflate(selectedObdIds.Contains(annotation.Id) ? 8f : 3f, selectedObdIds.Contains(annotation.Id) ? 8f : 3f);
                if (!paintBounds.IntersectsWith(args.ClipRectangle)) continue;
                AnnotationClass annotationClass;
                obdClasses.TryGetValue(annotation.ClassId, out annotationClass);
                Color color = annotationClass == null ? Color.Red : AnnotationColorFormat.ToColor(annotationClass.Color);
                using (var fill = new SolidBrush(Color.FromArgb(ToAlpha(ObdOpacity), color))) args.Graphics.FillRectangle(fill, rectangle);
                using (var pen = new Pen(color, selectedObdIds.Contains(annotation.Id) ? 3f : 2f)) args.Graphics.DrawRectangle(pen, rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);
                if (annotationClass != null) using (var text = new SolidBrush(color)) args.Graphics.DrawString(annotationClass.Name, Font, text, rectangle.Left + 2f, rectangle.Top + 2f);
                if (selectedObdIds.Contains(annotation.Id)) DrawHandles(args.Graphics, rectangle);
            }
            if (obdPreview.HasValue)
            {
                RectangleF rectangle = ToScreenRectangle(obdPreview.Value.X, obdPreview.Value.Y, obdPreview.Value.Width, obdPreview.Value.Height);
                RectangleF interior = rectangle;
                interior.Inflate(-1f, -1f);
                if (interior.Width > 0f && interior.Height > 0f)
                    using (var fill = new SolidBrush(PreviewTrackColor)) args.Graphics.FillRectangle(fill, interior);
                using (var pen = new Pen(PreviewTrackColor, 2f)) args.Graphics.DrawRectangle(pen, rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);
            }
            foreach (SegRegion stored in segRegions.Where(item => !selectedSegIds.Contains(item.Id)).Concat(segRegions.Where(item => selectedSegIds.Contains(item.Id))))
            {
                if (!AnnotationsVisible) continue;
                SegRegion region;
                if (!segPreviews.TryGetValue(stored.Id, out region)) region = stored;
                RectangleF paintBounds = ToScreenRectangle(GetStoredBounds(region));
                paintBounds.Inflate(selectedSegIds.Contains(stored.Id) ? 30f : 3f, selectedSegIds.Contains(stored.Id) ? 30f : 3f);
                if (!paintBounds.IntersectsWith(args.ClipRectangle)) continue;
                AnnotationClass annotationClass;
                segClasses.TryGetValue(region.ClassId, out annotationClass);
                Color color = annotationClass == null ? Color.Red : AnnotationColorFormat.ToColor(annotationClass.Color);
                using (var fill = new SolidBrush(Color.FromArgb(ToAlpha(SegOpacity), color)))
                using (var pen = new Pen(color, 2f))
                {
                    if (region.Type == SegRegionType.Rect)
                    {
                        RectangleF rectangle = ToScreenRectangle(region.X, region.Y, region.Width, region.Height);
                        args.Graphics.FillRectangle(fill, rectangle);
                        args.Graphics.DrawRectangle(pen, rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);
                    }
                    else if (region.Type == SegRegionType.Ellipse)
                    {
                        RectangleF ellipse = ToScreenRectangle(region.X - region.Width, region.Y - region.Height, region.Width * 2, region.Height * 2);
                        args.Graphics.FillEllipse(fill, ellipse);
                        args.Graphics.DrawEllipse(pen, ellipse);
                    }
                    else if (region.Points.Count > 0)
                    {
                        PointF[] points = region.Points.Select(point => viewport.ImageToScreen(new PointF(point.X, point.Y))).ToArray();
                        if (region.Type == SegRegionType.PolyGon && points.Length > 2)
                        {
                            args.Graphics.FillPolygon(fill, points);
                            args.Graphics.DrawPolygon(pen, points);
                        }
                        else if (region.Type == SegRegionType.PolyLine && points.Length == 1)
                        {
                            float diameter = Math.Max(1f, (float)(region.StrokeWidth * viewport.Zoom));
                            args.Graphics.FillEllipse(fill, points[0].X - diameter / 2f, points[0].Y - diameter / 2f, diameter, diameter);
                        }
                        else
                        {
                            using (var stroke = new Pen(Color.FromArgb(ToAlpha(SegOpacity), color), Math.Max(1f, (float)(region.StrokeWidth * viewport.Zoom))))
                            {
                                stroke.LineJoin = LineJoin.Round;
                                stroke.StartCap = LineCap.Round;
                                stroke.EndCap = LineCap.Round;
                                args.Graphics.DrawLines(stroke, points);
                            }
                        }
                    }
                }
                if (selectedSegIds.Contains(stored.Id))
                {
                    if (segDrag == SegDragKind.Rotate && segPreviews.ContainsKey(stored.Id)) DrawRotatedSegHandles(args.Graphics, stored, segRotationAngle);
                    else DrawSegHandles(args.Graphics, ToScreenRectangle(GetSegBounds(region)));
                }
            }
            if (selectionPreview.HasValue)
            {
                RectangleF rectangle = ToScreenRectangle(selectionPreview.Value);
                using (var pen = new Pen(Color.Black, 2f) { DashStyle = DashStyle.Dash }) args.Graphics.DrawRectangle(pen, rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);
            }
            if (segPreview.HasValue)
            {
                RectangleF rectangle = ToScreenRectangle(segPreview.Value.X, segPreview.Value.Y, segPreview.Value.Width, segPreview.Value.Height);
                RectangleF interior = rectangle;
                interior.Inflate(-1f, -1f);
                using (var fill = new SolidBrush(PreviewTrackColor))
                using (var pen = new Pen(PreviewTrackColor, 2f))
                {
                    if (SegTool == SegToolKind.Ellipse)
                    {
                        if (interior.Width > 0f && interior.Height > 0f) args.Graphics.FillEllipse(fill, interior);
                        args.Graphics.DrawEllipse(pen, rectangle);
                    }
                    else
                    {
                        if (interior.Width > 0f && interior.Height > 0f) args.Graphics.FillRectangle(fill, interior);
                        args.Graphics.DrawRectangle(pen, rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);
                    }
                }
            }
            if (segPath.Count > 0)
            {
                PointF[] points = segPath.Select(viewport.ImageToScreen).ToArray();
                float width = SegTool == SegToolKind.Polygon ? 3f : SegTool == SegToolKind.Brush || SegTool == SegToolKind.Eraser ? Math.Max(1f, (float)(segPathStrokeWidth * viewport.Zoom)) : 1f;
                bool canClosePolygon = SegTool == SegToolKind.Polygon && segPath.Count >= 3 && DistanceInScreenPixels(segPath[0], segCursor) <= 8f;
                using (var pen = new Pen(PreviewTrackColor, width))
                {
                    pen.LineJoin = LineJoin.Round;
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    if (points.Length > 1) args.Graphics.DrawLines(pen, points);
                    if (SegTool == SegToolKind.Polygon)
                    {
                        PointF previewEnd = canClosePolygon ? points[0] : viewport.ImageToScreen(segCursor);
                        args.Graphics.DrawLine(pen, points[points.Length - 1], previewEnd);
                    }
                    else if (SegTool == SegToolKind.Brush || SegTool == SegToolKind.Eraser)
                    {
                        float diameter = Math.Max(1f, (float)(segPathStrokeWidth * viewport.Zoom));
                        using (var fill = new SolidBrush(PreviewTrackColor)) args.Graphics.FillEllipse(fill, points[0].X - diameter / 2f, points[0].Y - diameter / 2f, diameter, diameter);
                    }
                }
                if (SegTool == SegToolKind.Polygon)
                {
                    PointF start = points[0];
                    float diameter = canClosePolygon ? 10f : 8f;
                    using (var fill = new SolidBrush(canClosePolygon ? previewColor : Color.White))
                    using (var outline = new Pen(Color.Black, 1f))
                    {
                        RectangleF marker = new RectangleF(start.X - diameter / 2f, start.Y - diameter / 2f, diameter, diameter);
                        args.Graphics.FillEllipse(fill, marker);
                        args.Graphics.DrawEllipse(outline, marker);
                    }
                }
            }
            if (segLineStart.HasValue && SegTool == SegToolKind.Line)
            {
                using (var pen = new Pen(PreviewTrackColor, Math.Max(1f, (float)(segPathStrokeWidth * viewport.Zoom))))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    args.Graphics.DrawLine(pen, viewport.ImageToScreen(segLineStart.Value), viewport.ImageToScreen(segCursor));
                }
            }
            DrawToolCursor(args.Graphics);
            DrawCanvasThumbnail(args.Graphics);
        }

        /// <summary>
        /// 依縮放狀態繪製原圖縮圖、目前視野框及收合切換鈕。
        /// </summary>
        /// <param name="graphics">供繪圖使用的圖形表面。</param>
        private void DrawCanvasThumbnail(Graphics graphics)
        {
            CanvasThumbnailLayout layout;
            float dpiScale = DeviceDpi / 96f;
            if (!CanvasThumbnailLayout.TryCreate(viewport.ImageSize, ClientSize, viewport.Zoom, viewport.Origin, dpiScale, out layout)) return;
            if (thumbnailHiddenForGesture) return;
            if (!thumbnailCollapsed)
            {
                using (var background = new SolidBrush(Color.FromArgb(210, 24, 24, 27))) graphics.FillRectangle(background, layout.ImageBounds);
                graphics.DrawImage(image, layout.ImageBounds);
                using (var shade = new SolidBrush(Color.FromArgb(42, Color.DeepSkyBlue))) graphics.FillRectangle(shade, layout.ViewportBounds);
                using (var outline = new Pen(Color.Black, 3f * dpiScale)) graphics.DrawRectangle(outline, layout.ViewportBounds.X, layout.ViewportBounds.Y, layout.ViewportBounds.Width, layout.ViewportBounds.Height);
                using (var frame = new Pen(Color.White, 1.5f * dpiScale)) graphics.DrawRectangle(frame, layout.ViewportBounds.X, layout.ViewportBounds.Y, layout.ViewportBounds.Width, layout.ViewportBounds.Height);
                using (var border = new Pen(Color.FromArgb(230, Color.White), dpiScale)) graphics.DrawRectangle(border, layout.ImageBounds.X, layout.ImageBounds.Y, layout.ImageBounds.Width, layout.ImageBounds.Height);
            }
            DrawThumbnailToggle(graphics, layout.ToggleBounds, thumbnailCollapsed, dpiScale);
        }

        /// <summary>
        /// 依縮圖收合狀態繪製方向提示按鈕。
        /// </summary>
        /// <param name="graphics">供繪圖使用的圖形表面。</param>
        /// <param name="bounds">繪製或命中測試所使用的範圍。</param>
        /// <param name="collapsed">是否以收合狀態顯示。</param>
        /// <param name="dpiScale">目前顯示器的 DPI 縮放比例。</param>
        private static void DrawThumbnailToggle(Graphics graphics, RectangleF bounds, bool collapsed, float dpiScale)
        {
            using (var background = new SolidBrush(Color.FromArgb(220, 24, 24, 27))) graphics.FillRectangle(background, bounds);
            using (var border = new Pen(Color.White, dpiScale)) graphics.DrawRectangle(border, bounds.X, bounds.Y, bounds.Width, bounds.Height);
            float centerX = bounds.X + bounds.Width / 2f;
            float centerY = bounds.Y + bounds.Height / 2f;
            float direction = collapsed ? 1f : -1f;
            using (var arrow = new Pen(Color.White, 2f * dpiScale))
            {
                graphics.DrawLine(arrow, centerX - direction * 3f * dpiScale, centerY - 4f * dpiScale, centerX + direction * 2f * dpiScale, centerY);
                graphics.DrawLine(arrow, centerX + direction * 2f * dpiScale, centerY, centerX - direction * 3f * dpiScale, centerY + 4f * dpiScale);
            }
        }

        /// <summary>
        /// 只有圖片超出可視範圍時建立縮圖及視野框配置。
        /// </summary>
        /// <param name="layout">要用來換算畫布座標的縮圖版面。</param>
        /// <returns>若符合摘要所述條件則為 true，否則為 false。</returns>
        private bool TryGetThumbnailLayout(out CanvasThumbnailLayout layout)
        {
            layout = default(CanvasThumbnailLayout);
            return image != null && CanvasThumbnailLayout.TryCreate(viewport.ImageSize, ClientSize, viewport.Zoom, viewport.Origin, DeviceDpi / 96f, out layout);
        }

        /// <summary>
        /// 將縮圖點位轉回原圖座標並以該點置中目前畫布視角。
        /// </summary>
        /// <param name="thumbnailPoint">縮圖中的座標點。</param>
        /// <param name="layout">要用來換算畫布座標的縮圖版面。</param>
        private void CenterViewportOnThumbnailPoint(PointF thumbnailPoint, CanvasThumbnailLayout layout)
        {
            float scale = layout.ImageBounds.Width / viewport.ImageSize.Width;
            PointF imagePoint = new PointF(
                (thumbnailPoint.X - layout.ImageBounds.Left) / scale,
                (thumbnailPoint.Y - layout.ImageBounds.Top) / scale);
            PointF origin = new PointF(
                ClientSize.Width / 2f - imagePoint.X * (float)viewport.Zoom,
                ClientSize.Height / 2f - imagePoint.Y * (float)viewport.Zoom);
            viewport.SetView(viewport.Zoom, origin, false);
            RefreshToolCursor();
            Invalidate();
            RaiseViewChanged();
        }

        /// <summary>
        /// 依目前標註工具繪製滑鼠位置的筆刷、線型或橡皮擦預覽。
        /// </summary>
        /// <param name="graphics">供繪圖使用的圖形表面。</param>
        private void DrawToolCursor(Graphics graphics)
        {
            if (!hasToolCursor || (!ObdEnabled && !SegEnabled)) return;
            bool ring = SegEnabled && (SegTool == SegToolKind.Brush || SegTool == SegToolKind.Line || SegTool == SegToolKind.Eraser);
            bool crosshair = !ring && ((!SegEnabled && ObdEnabled && !ObdSelectMode) || (SegEnabled && SegTool.HasValue && SegTool != SegToolKind.Select));
            if (!ring && !crosshair) return;
            PointF center = toolCursor;
            using (var pen = new Pen(Color.White, 1f))
            using (var outline = new Pen(Color.Black, 1f))
            {
                if (ring)
                {
                    float diameter = Math.Max(1f, (float)(SegStrokeWidth * viewport.Zoom));
                    RectangleF bounds = new RectangleF(center.X - diameter / 2f, center.Y - diameter / 2f, diameter, diameter);
                    graphics.DrawEllipse(outline, bounds.X - 1f, bounds.Y - 1f, bounds.Width + 2f, bounds.Height + 2f);
                    graphics.DrawEllipse(pen, bounds);
                    string symbol = (CurrentModifierKeys & Keys.Control) == Keys.Control || SegTool == SegToolKind.Eraser ? "−" : "+";
                    using (var font = new Font(Font.FontFamily, 9f, FontStyle.Bold))
                    using (var brush = new SolidBrush(Color.White))
                    using (var shadow = new SolidBrush(Color.Black))
                    {
                        float x = center.X + diameter / 2f + 2f, y = center.Y + diameter / 2f + 2f;
                        graphics.DrawString(symbol, font, shadow, x + 1f, y + 1f);
                        graphics.DrawString(symbol, font, brush, x, y);
                    }
                }
                else
                {
                    graphics.DrawLine(outline, center.X - 6f, center.Y, center.X - 2f, center.Y);
                    graphics.DrawLine(outline, center.X + 2f, center.Y, center.X + 6f, center.Y);
                    graphics.DrawLine(outline, center.X, center.Y - 6f, center.X, center.Y - 2f);
                    graphics.DrawLine(outline, center.X, center.Y + 2f, center.X, center.Y + 6f);
                    graphics.DrawLine(pen, center.X - 6f, center.Y, center.X - 2f, center.Y);
                    graphics.DrawLine(pen, center.X + 2f, center.Y, center.X + 6f, center.Y);
                    graphics.DrawLine(pen, center.X, center.Y - 6f, center.X, center.Y - 2f);
                    graphics.DrawLine(pen, center.X, center.Y + 2f, center.X, center.Y + 6f);
                    if ((CurrentModifierKeys & Keys.Control) == Keys.Control)
                    {
                        using (var font = new Font(Font.FontFamily, 9f, FontStyle.Bold))
                        using (var brush = new SolidBrush(Color.White))
                        using (var shadow = new SolidBrush(Color.Black))
                        {
                            float x = center.X + 8f, y = center.Y + 5f;
                            graphics.DrawString("−", font, shadow, x + 1f, y + 1f);
                            graphics.DrawString("−", font, brush, x, y);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 依啟用工具與指標位置更新系統游標樣式。
        /// </summary>
        private void UpdateToolCursor()
        {
            RefreshToolCursor();
            ApplyToolCursor();
        }

        /// <summary>
        /// 在系統游標與隱藏游標之間切換目前畫布工具游標。
        /// </summary>
        private void ApplyToolCursor()
        {
            bool crosshair = ObdEnabled && !ObdSelectMode || SegEnabled && (SegTool == SegToolKind.Rect || SegTool == SegToolKind.Ellipse || SegTool == SegToolKind.Polygon);
            bool ring = SegEnabled && (SegTool == SegToolKind.Brush || SegTool == SegToolKind.Line || SegTool == SegToolKind.Eraser);
            Cursor = hasToolCursor && (crosshair || ring) ? InvisibleCursor : Cursors.Arrow;
        }

        /// <summary>
        /// 建立供畫布自繪工具游標時使用的透明系統游標。
        /// </summary>
        /// <returns>建立或取得的游標。</returns>
        private static Cursor CreateInvisibleCursor()
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write((ushort)0);
                writer.Write((ushort)2);
                writer.Write((ushort)1);
                writer.Write((byte)1);
                writer.Write((byte)1);
                writer.Write((byte)0);
                writer.Write((byte)0);
                writer.Write((ushort)0);
                writer.Write((ushort)0);
                writer.Write((uint)48);
                writer.Write((uint)22);
                writer.Write((uint)40);
                writer.Write((int)1);
                writer.Write((int)2);
                writer.Write((ushort)1);
                writer.Write((ushort)32);
                writer.Write((uint)0);
                writer.Write((uint)4);
                writer.Write((int)0);
                writer.Write((int)0);
                writer.Write((uint)0);
                writer.Write((uint)0);
                writer.Write(new byte[4]);
                writer.Write(new byte[] { 255, 255, 255, 255 });
                stream.Position = 0;
                return new Cursor(stream);
            }
        }

        private Color PreviewTrackColor { get { return Color.FromArgb(ToAlpha(0.4f), previewColor); } }

        /// <summary>
        /// 將不透明度比例限制後轉換為 GDI+ 使用的 Alpha 值。
        /// </summary>
        /// <param name="opacity">標註顯示透明度。</param>
        /// <returns>計算或選取後的整數結果。</returns>
        private static int ToAlpha(float opacity)
        {
            return Math.Max(0, Math.Min(255, (int)Math.Round(255f * opacity)));
        }

        /// <summary>
        /// 在 OBD 選取矩形周圍繪製調整控制點。
        /// </summary>
        /// <param name="graphics">供繪圖使用的圖形表面。</param>
        /// <param name="rectangle">要處理的矩形範圍。</param>
        private static void DrawHandles(Graphics graphics, RectangleF rectangle)
        {
            using (var outline = new Pen(Color.Black, 3f)) graphics.DrawRectangle(outline, rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);
            using (var outline = new Pen(Color.White, 1f)) graphics.DrawRectangle(outline, rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);
            foreach (PointF point in GetHandlePoints(rectangle))
            {
                var handle = new RectangleF(point.X - HandleSize / 2f, point.Y - HandleSize / 2f, HandleSize, HandleSize);
                using (var fill = new SolidBrush(Color.White)) graphics.FillRectangle(fill, handle);
                using (var pen = new Pen(Color.Black)) graphics.DrawRectangle(pen, handle.X, handle.Y, handle.Width, handle.Height);
            }
        }

        /// <summary>
        /// 依 SEG 區域類型繪製移動、縮放或旋轉控制點。
        /// </summary>
        /// <param name="graphics">供繪圖使用的圖形表面。</param>
        /// <param name="rectangle">要處理的矩形範圍。</param>
        private static void DrawSegHandles(Graphics graphics, RectangleF rectangle)
        {
            DrawHandles(graphics, rectangle);
            PointF rotation = new PointF(rectangle.Left + rectangle.Width / 2f, rectangle.Top - 24f);
            using (var pen = new Pen(Color.Black)) graphics.DrawLine(pen, rectangle.Left + rectangle.Width / 2f, rectangle.Top, rotation.X, rotation.Y);
            using (var fill = new SolidBrush(Color.White)) graphics.FillEllipse(fill, rotation.X - 5f, rotation.Y - 5f, 10f, 10f);
            using (var pen = new Pen(Color.Black)) graphics.DrawEllipse(pen, rotation.X - 5f, rotation.Y - 5f, 10f, 10f);
        }

        /// <summary>
        /// 依旋轉後的矩形角點繪製 SEG 旋轉控制點。
        /// </summary>
        /// <param name="graphics">供繪圖使用的圖形表面。</param>
        /// <param name="region">要處理的 SEG 區域。</param>
        /// <param name="angle">要套用的旋轉角度。</param>
        private void DrawRotatedSegHandles(Graphics graphics, SegRegion region, double angle)
        {
            RectangleF bounds = GetSegBounds(region);
            PointF center = new PointF(bounds.Left + bounds.Width / 2f, bounds.Top + bounds.Height / 2f);
            PointF[] points = GetHandlePoints(bounds).Select(point => viewport.ImageToScreen(RotatePoint(point, center, angle))).ToArray();
            using (var outline = new Pen(Color.Black, 3f)) graphics.DrawPolygon(outline, points);
            using (var outline = new Pen(Color.White, 1f)) graphics.DrawPolygon(outline, points);
            foreach (PointF point in points)
            {
                var handle = new RectangleF(point.X - HandleSize / 2f, point.Y - HandleSize / 2f, HandleSize, HandleSize);
                using (var fill = new SolidBrush(Color.White)) graphics.FillRectangle(fill, handle);
                using (var pen = new Pen(Color.Black)) graphics.DrawRectangle(pen, handle.X, handle.Y, handle.Width, handle.Height);
            }
            PointF top = points[1];
            PointF rotation = new PointF(top.X + (float)Math.Sin(angle) * 24f, top.Y - (float)Math.Cos(angle) * 24f);
            using (var pen = new Pen(Color.Black)) graphics.DrawLine(pen, top, rotation);
            using (var fill = new SolidBrush(Color.White)) graphics.FillEllipse(fill, rotation.X - 5f, rotation.Y - 5f, 10f, 10f);
            using (var pen = new Pen(Color.Black)) graphics.DrawEllipse(pen, rotation.X - 5f, rotation.Y - 5f, 10f, 10f);
        }

        /// <summary>
        /// 以指定中心及角度計算點位旋轉後的座標。
        /// </summary>
        /// <param name="point">要處理的座標點。</param>
        /// <param name="center">旋轉或對齊的中心點。</param>
        /// <param name="angle">要套用的旋轉角度。</param>
        /// <returns>計算後的浮點座標點。</returns>
        private static PointF RotatePoint(PointF point, PointF center, double angle)
        {
            double cosine = Math.Cos(angle), sine = Math.Sin(angle);
            return new PointF((float)(center.X + (point.X - center.X) * cosine - (point.Y - center.Y) * sine), (float)(center.Y + (point.X - center.X) * sine + (point.Y - center.Y) * cosine));
        }

        /// <summary>
        /// 依 Ctrl、Shift 或未修飾滾輪分別縮放、水平或垂直平移。
        /// </summary>
        /// <param name="args">包含此次事件詳細資料的事件資料。</param>
        protected override void OnMouseWheel(MouseEventArgs args)
        {
            if ((ModifierKeys & Keys.Control) == Keys.Control) viewport.SetZoomAt(viewport.Zoom * (args.Delta > 0 ? 1.1d : 1d / 1.1d), args.Location);
            else if ((ModifierKeys & Keys.Shift) == Keys.Shift) viewport.Pan(args.Delta / 120f * 48f, 0f);
            else viewport.Pan(0f, args.Delta / 120f * 48f);
            RefreshToolCursor();
            Invalidate();
            RaiseViewChanged();
            base.OnMouseWheel(args);
        }

        /// <summary>
        /// 依縮圖、平移狀態與目前標註工具開始對應的滑鼠手勢。
        /// </summary>
        /// <param name="args">包含此次事件詳細資料的事件資料。</param>
        protected override void OnMouseDown(MouseEventArgs args)
        {
            Focus();
            panning = image != null && (args.Button == MouseButtons.Right || (args.Button == MouseButtons.Left && (CurrentModifierKeys & Keys.Space) == Keys.Space));
            if (panning)
            {
                previousPoint = args.Location;
                Capture = true;
            }
            CanvasThumbnailLayout thumbnailLayout;
            if (!panning && args.Button == MouseButtons.Left && !thumbnailHiddenForGesture && TryGetThumbnailLayout(out thumbnailLayout))
            {
                if (thumbnailLayout.ToggleBounds.Contains(args.Location))
                {
                    thumbnailCollapsed = !thumbnailCollapsed;
                    RefreshToolCursor();
                    ApplyToolCursor();
                    Invalidate();
                    base.OnMouseDown(args);
                    return;
                }
                if (!thumbnailCollapsed && thumbnailLayout.ImageBounds.Contains(args.Location))
                {
                    thumbnailPanning = thumbnailLayout.ViewportBounds.Contains(args.Location);
                    thumbnailDragScale = thumbnailLayout.ImageBounds.Width / viewport.ImageSize.Width;
                    if (thumbnailPanning) previousPoint = args.Location;
                    CenterViewportOnThumbnailPoint(args.Location, thumbnailLayout);
                    Capture = thumbnailPanning;
                    base.OnMouseDown(args);
                    return;
                }
            }
            if (args.Button == MouseButtons.Left && ObdEnabled && !panning && image != null)
            {
                PointF point = viewport.ScreenToImage(args.Location);
                if (!ObdSelectMode)
                {
                    ClearObdSelection();
                    obdStart = ClampPoint(point);
                    obdPreview = new RectangleF(obdStart, SizeF.Empty);
                    obdDrag = ObdDragKind.Create;
                    SetThumbnailHiddenForGesture(true);
                    Capture = true;
                    Invalidate();
                }
                else
                {
                    ObdAnnotation handleAnnotation;
                    ObdDragKind handle = HitHandle(args.Location, out handleAnnotation);
                    if (handle != ObdDragKind.None)
                    {
                        selectedObdId = handleAnnotation.Id;
                        BeginObdDrag(handle, point, handleAnnotation);
                    }
                    else
                    {
                        ObdAnnotation hit = TopmostObdAt(point);
                        if (hit == null)
                        {
                            BeginSelectionGesture(ClampPoint(point), args.Location, false);
                            Invalidate();
                            base.OnMouseDown(args);
                            return;
                        }
                        else if ((CurrentModifierKeys & Keys.Control) == Keys.Control)
                        {
                            if (!selectedObdIds.Add(hit.Id))
                            {
                                selectedObdIds.Remove(hit.Id);
                                if (selectedObdId == hit.Id) selectedObdId = selectedObdIds.FirstOrDefault();
                            }
                            else selectedObdId = hit.Id;
                            Invalidate();
                            base.OnMouseDown(args);
                            return;
                        }
                        else
                        {
                            if (!selectedObdIds.Contains(hit.Id))
                            {
                                ClearObdSelection();
                                selectedObdIds.Add(hit.Id);
                                selectedObdId = hit.Id;
                            }
                            BeginObdDrag(ObdDragKind.Move, point, hit);
                        }
                    }
                    Capture = true;
                    Invalidate();
                }
            }
            else if (args.Button == MouseButtons.Left && SegEnabled && SegTool.HasValue && !panning && image != null)
            {
                if (SegTool == SegToolKind.Select)
                {
                    PointF selectionPoint = ClampPoint(viewport.ScreenToImage(args.Location));
                    SegRegion handleRegion;
                    SegDragKind handle = HitSegHandle(args.Location, out handleRegion);
                    if (handle != SegDragKind.None)
                    {
                        selectedSegId = handleRegion.Id;
                        segOriginal = handleRegion;
                        segOriginalBounds = GetSegBounds(segOriginal);
                        segDragStart = selectionPoint;
                        segDrag = handle;
                        segRotationAngle = 0d;
                        Capture = true;
                        base.OnMouseDown(args);
                        return;
                    }
                    SegRegion hit = TopmostSegAt(selectionPoint);
                    if (hit == null)
                    {
                        BeginSelectionGesture(selectionPoint, args.Location, true);
                        Invalidate();
                        base.OnMouseDown(args);
                        return;
                    }
                    else if ((CurrentModifierKeys & Keys.Control) == Keys.Control)
                    {
                        if (!selectedSegIds.Add(hit.Id)) selectedSegIds.Remove(hit.Id);
                        selectedSegId = selectedSegIds.Contains(hit.Id) ? hit.Id : selectedSegIds.FirstOrDefault();
                    }
                    else
                    {
                        if (!selectedSegIds.Contains(hit.Id)) { ClearSegSelection(); selectedSegIds.Add(hit.Id); selectedSegId = hit.Id; }
                        segDragStart = selectionPoint;
                        segDrag = SegDragKind.Move;
                        Capture = true;
                    }
                    Invalidate();
                    base.OnMouseDown(args);
                    return;
                }
                if ((SegTool == SegToolKind.Brush || SegTool == SegToolKind.Line || SegTool == SegToolKind.Eraser) && !CanFitSegPath())
                {
                    DiscardUnfinishedShape();
                    base.OnMouseDown(args);
                    return;
                }
                PointF point = ClampSegPathPoint(viewport.ScreenToImage(args.Location));
                if (SegTool == SegToolKind.Rect || SegTool == SegToolKind.Ellipse)
                {
                    segStart = point;
                    segPreview = new RectangleF(segStart, SizeF.Empty);
                    SetThumbnailHiddenForGesture(true);
                    Capture = true;
                }
                else if (SegTool == SegToolKind.Line)
                {
                    if (!segLineStart.HasValue) { segLineStart = point; segPathStrokeWidth = SegStrokeWidth; SetThumbnailHiddenForGesture(true); }
                    else
                    {
                        SegPoint first = ToSegPoint(segLineStart.Value);
                        SegPoint second = ToSegPoint(point);
                        if (first.X != second.X || first.Y != second.Y)
                        {
                            if ((CurrentModifierKeys & Keys.Control) == Keys.Control) SegGeometryEraseRequested?.Invoke(SegRegionType.PolyLine, Rectangle.Empty, new[] { first, second }, segPathStrokeWidth);
                            else SegPathCreated?.Invoke(SegRegionType.PolyLine, new[] { first, second }, segPathStrokeWidth);
                        }
                        segLineStart = null;
                        segPathStrokeWidth = 0;
                        SetThumbnailHiddenForGesture(false);
                    }
                }
                else
                {
                    if (SegTool == SegToolKind.Polygon && segPath.Count >= 3 && DistanceInScreenPixels(segPath[0], point) <= 8f)
                    {
                        SegPoint[] polygon = segPath.Select(ToSegPoint).ToArray();
                        if ((CurrentModifierKeys & Keys.Control) == Keys.Control) SegGeometryEraseRequested?.Invoke(SegRegionType.PolyGon, Rectangle.Empty, polygon, 0);
                        else SegPathCreated?.Invoke(SegRegionType.PolyGon, polygon, 0);
                        segPath.Clear();
                        SetThumbnailHiddenForGesture(false);
                        Invalidate();
                        base.OnMouseDown(args);
                        return;
                    }
                    if (segPath.Count == 0) { segPathStrokeWidth = SegTool == SegToolKind.Brush || SegTool == SegToolKind.Eraser ? SegStrokeWidth : 0; segPath.Add(point); SetThumbnailHiddenForGesture(true); }
                    else segPath.Add(point);
                    segPathDragging = false;
                    Capture = true;
                }
                Invalidate();
            }
            previousPoint = args.Location;
            base.OnMouseDown(args);
        }

        /// <summary>
        /// 記錄所選 OBD 拖曳的起點及原始矩形，準備移動或縮放預覽。
        /// </summary>
        /// <param name="drag">目前的拖曳操作。</param>
        /// <param name="point">要處理的座標點。</param>
        /// <param name="annotation">要處理的標註資料。</param>
        private void BeginObdDrag(ObdDragKind drag, PointF point, ObdAnnotation annotation)
        {
            activeObd = annotation;
            obdDrag = drag;
            obdOriginal = new RectangleF(annotation.X, annotation.Y, annotation.Width, annotation.Height);
            obdDragStart = point;
            obdPreview = null;
            obdPreviews = new Dictionary<string, RectangleF>();
        }

        /// <summary>
        /// 依目前手勢更新平移、框選、標註編輯及繪製工具預覽。
        /// </summary>
        /// <param name="args">包含此次事件詳細資料的事件資料。</param>
        protected override void OnMouseMove(MouseEventArgs args)
        {
            pointerInside = true;
            lastMousePosition = args.Location;
            PointF previousSegCursor = segCursor;
            bool hadToolCursor = hasToolCursor;
            Rectangle oldCursorBounds = hadToolCursor ? GetToolCursorBounds(toolCursor) : Rectangle.Empty;
            RefreshToolCursor();
            ApplyToolCursor();
            if (SegEnabled && image != null) segCursor = ClampPoint(viewport.ScreenToImage(args.Location));
            if (thumbnailPanning && (args.Button & MouseButtons.Left) == MouseButtons.Left)
            {
                float scale = thumbnailDragScale;
                viewport.Pan(-(args.X - previousPoint.X) / scale * (float)viewport.Zoom, -(args.Y - previousPoint.Y) / scale * (float)viewport.Zoom);
                previousPoint = args.Location;
                RefreshToolCursor();
                Invalidate();
                RaiseViewChanged();
            }
            else if (panning)
            {
                viewport.Pan(args.X - previousPoint.X, args.Y - previousPoint.Y);
                previousPoint = args.Location;
                RefreshToolCursor();
                Invalidate();
                RaiseViewChanged();
            }
            else if (selectionStart.HasValue && (args.Button & MouseButtons.Left) == MouseButtons.Left)
            {
                selectionPreview = Normalize(selectionStart.Value, ClampPoint(viewport.ScreenToImage(args.Location)));
                Invalidate();
            }
            else if (obdDrag != ObdDragKind.None && (args.Button & MouseButtons.Left) == MouseButtons.Left)
            {
                PointF point = ClampPoint(viewport.ScreenToImage(args.Location));
                if (obdDrag == ObdDragKind.Create)
                {
                    RectangleF? previousPreview = obdPreview;
                    obdPreview = Normalize(obdStart, point);
                    InvalidateShapePreview(previousPreview, obdPreview, oldCursorBounds);
                }
                else if (obdDrag == ObdDragKind.Move) obdPreviews = MoveRectangles(point);
                else
                {
                    obdPreviews = new Dictionary<string, RectangleF> { { activeObd.Id, ResizeRectangle(point) } };
                }
                if (obdDrag != ObdDragKind.Create) Invalidate();
            }
            else if (segDrag != SegDragKind.None && (args.Button & MouseButtons.Left) == MouseButtons.Left)
            {
                PointF point = ClampPoint(viewport.ScreenToImage(args.Location));
                if (segDrag == SegDragKind.Move) segPreviews = MoveSegRegions(point);
                else if (segDrag == SegDragKind.Rotate)
                {
                    IReadOnlyDictionary<string, SegRegion> previews;
                    double angle;
                    if (TryRotateSegRegions(point, out previews, out angle))
                    {
                        segPreviews = previews;
                        segRotationAngle = angle;
                    }
                }
                else segPreviews = new Dictionary<string, SegRegion> { { segOriginal.Id, ResizeSegRegion(segOriginal, point) } };
                Invalidate();
            }
            else if (segPreview.HasValue && (args.Button & MouseButtons.Left) == MouseButtons.Left)
            {
                RectangleF? previousPreview = segPreview;
                segPreview = Normalize(segStart, ClampPoint(viewport.ScreenToImage(args.Location)));
                InvalidateShapePreview(previousPreview, segPreview, oldCursorBounds);
            }
            else if (SegEnabled && SegTool == SegToolKind.Line && segLineStart.HasValue)
            {
                segCursor = ClampSegPathPoint(viewport.ScreenToImage(args.Location));
                InvalidateStrokePreview(segLineStart.Value, previousSegCursor, segCursor, segPathStrokeWidth, oldCursorBounds);
            }
            else if (segPath.Count > 0 && (args.Button & MouseButtons.Left) == MouseButtons.Left && (SegTool == SegToolKind.Polygon || SegTool == SegToolKind.Brush || SegTool == SegToolKind.Eraser))
            {
                PointF previousPoint = segPath[segPath.Count - 1];
                PointF nextPoint = ClampSegPathPoint(viewport.ScreenToImage(args.Location));
                if (DistanceInScreenPixels(segPath[segPath.Count - 1], viewport.ScreenToImage(args.Location)) >= 2f)
                {
                    segPath.Add(nextPoint);
                    segPathDragging = true;
                }
                float width = SegTool == SegToolKind.Polygon ? 3f : segPathStrokeWidth;
                InvalidateStrokePreview(previousPoint, previousPoint, nextPoint, width, oldCursorBounds);
            }
            else if (segPath.Count > 0 && SegTool == SegToolKind.Polygon)
            {
                InvalidateStrokePreview(segPath[segPath.Count - 1], previousSegCursor, segCursor, 3f, oldCursorBounds);
            }
            else if (hasToolCursor)
            {
                Rectangle newCursorBounds = GetToolCursorBounds(toolCursor);
                Invalidate(hadToolCursor ? Rectangle.Union(oldCursorBounds, newCursorBounds) : newCursorBounds);
            }
            base.OnMouseMove(args);
        }

        /// <summary>
        /// 只重繪矩形或橢圓預覽前後涵蓋的畫布區域。
        /// </summary>
        /// <param name="previous">更新前的原圖預覽範圍。</param>
        /// <param name="current">更新後的原圖預覽範圍。</param>
        /// <param name="oldCursorBounds">更新前的工具游標範圍。</param>
        private void InvalidateShapePreview(RectangleF? previous, RectangleF? current, Rectangle oldCursorBounds)
        {
            RectangleF bounds = current.HasValue ? ToScreenRectangle(current.Value) : RectangleF.Empty;
            if (previous.HasValue) bounds = bounds.IsEmpty ? ToScreenRectangle(previous.Value) : RectangleF.Union(bounds, ToScreenRectangle(previous.Value));
            bounds.Inflate(4f, 4f);
            InvalidatePreviewBounds(Rectangle.Ceiling(bounds), oldCursorBounds);
        }

        /// <summary>
        /// 只重繪線段或自由筆畫新增與移動所涵蓋的畫布區域。
        /// </summary>
        /// <param name="start">預覽線段的原圖起點。</param>
        /// <param name="previousEnd">更新前的原圖終點。</param>
        /// <param name="currentEnd">更新後的原圖終點。</param>
        /// <param name="strokeWidth">原圖座標中的筆畫寬度。</param>
        /// <param name="oldCursorBounds">更新前的工具游標範圍。</param>
        private void InvalidateStrokePreview(PointF start, PointF previousEnd, PointF currentEnd, float strokeWidth, Rectangle oldCursorBounds)
        {
            PointF screenStart = viewport.ImageToScreen(start);
            PointF screenPrevious = viewport.ImageToScreen(previousEnd);
            PointF screenCurrent = viewport.ImageToScreen(currentEnd);
            RectangleF bounds = RectangleF.FromLTRB(
                Math.Min(screenStart.X, Math.Min(screenPrevious.X, screenCurrent.X)),
                Math.Min(screenStart.Y, Math.Min(screenPrevious.Y, screenCurrent.Y)),
                Math.Max(screenStart.X, Math.Max(screenPrevious.X, screenCurrent.X)),
                Math.Max(screenStart.Y, Math.Max(screenPrevious.Y, screenCurrent.Y)));
            float padding = Math.Max(4f, (float)(strokeWidth * viewport.Zoom / 2d + 3f));
            bounds.Inflate(padding, padding);
            InvalidatePreviewBounds(Rectangle.Ceiling(bounds), oldCursorBounds);
        }

        /// <summary>
        /// 合併預覽與新舊游標範圍後，限制在目前畫布內排程局部重繪。
        /// </summary>
        /// <param name="previewBounds">需要更新的預覽範圍。</param>
        /// <param name="oldCursorBounds">更新前的工具游標範圍。</param>
        private void InvalidatePreviewBounds(Rectangle previewBounds, Rectangle oldCursorBounds)
        {
            Rectangle bounds = previewBounds;
            if (!oldCursorBounds.IsEmpty) bounds = Rectangle.Union(bounds, oldCursorBounds);
            if (hasToolCursor) bounds = Rectangle.Union(bounds, GetToolCursorBounds(toolCursor));
            bounds.Intersect(ClientRectangle);
            if (!bounds.IsEmpty) Invalidate(bounds);
        }

        /// <summary>
        /// 計算工具游標重繪所需的畫面範圍。
        /// </summary>
        /// <param name="center">旋轉或對齊的中心點。</param>
        /// <returns>計算後的整數矩形範圍。</returns>
        private Rectangle GetToolCursorBounds(PointF center)
        {
            float radius = SegEnabled && (SegTool == SegToolKind.Brush || SegTool == SegToolKind.Line || SegTool == SegToolKind.Eraser)
                ? (float)(SegStrokeWidth * viewport.Zoom / 2d + 14f)
                : 8f;
            return Rectangle.Round(new RectangleF(center.X - radius, center.Y - radius, radius * 2f, radius * 2f));
        }

        /// <summary>
        /// 依最後指標位置及目前工具寬度重新計算游標位置。
        /// </summary>
        private void RefreshToolCursor()
        {
            hasToolCursor = pointerInside && image != null && IsPointerInsideImage() && !IsPointerOverThumbnail();
            if (!hasToolCursor) return;
            PointF imagePoint = viewport.ScreenToImage(lastMousePosition);
            bool widthTool = SegEnabled && (SegTool == SegToolKind.Brush || SegTool == SegToolKind.Line || SegTool == SegToolKind.Eraser);
            toolCursor = viewport.ImageToScreen(widthTool ? ClampSegPathPoint(imagePoint) : ClampPoint(imagePoint));
        }

        /// <summary>
        /// 判斷實體滑鼠位置是否位於目前圖片範圍內。
        /// </summary>
        /// <returns>若位於圖片範圍內則為 true。</returns>
        private bool IsPointerInsideImage()
        {
            PointF point = viewport.ScreenToImage(lastMousePosition);
            return point.X >= 0f && point.Y >= 0f && point.X <= viewport.ImageSize.Width && point.Y <= viewport.ImageSize.Height;
        }

        /// <summary>
        /// 判斷實體滑鼠是否位於可操作的導覽縮圖上。
        /// </summary>
        /// <returns>若位於展開縮圖上則為 true。</returns>
        private bool IsPointerOverThumbnail()
        {
            if (thumbnailCollapsed || thumbnailHiddenForGesture) return false;
            CanvasThumbnailLayout layout;
            return TryGetThumbnailLayout(out layout) && layout.ImageBounds.Contains(lastMousePosition);
        }

        /// <summary>
        /// 離開畫布時隱藏自繪工具游標並恢復系統游標。
        /// </summary>
        /// <param name="args">包含此次事件詳細資料的事件資料。</param>
        protected override void OnMouseLeave(EventArgs args)
        {
            pointerInside = false;
            hasToolCursor = false;
            ApplyToolCursor();
            Invalidate();
            base.OnMouseLeave(args);
        }

        /// <summary>
        /// 完成目前滑鼠手勢，提交標註變更事件並釋放滑鼠捕捉。
        /// </summary>
        /// <param name="args">包含此次事件詳細資料的事件資料。</param>
        protected override void OnMouseUp(MouseEventArgs args)
        {
            if (args.Button == MouseButtons.Left && thumbnailPanning)
            {
                thumbnailPanning = false;
                Capture = false;
                Invalidate();
                base.OnMouseUp(args);
                return;
            }
            if (args.Button == MouseButtons.Left && selectionStart.HasValue)
            {
                bool dragged = Math.Abs(args.X - selectionScreenStart.X) > 2 || Math.Abs(args.Y - selectionScreenStart.Y) > 2;
                RectangleF selection = selectionPreview ?? new RectangleF(selectionStart.Value, SizeF.Empty);
                bool isSeg = selectionIsSeg;
                selectionStart = null;
                selectionPreview = null;
                Capture = false;
                if (isSeg)
                {
                    if (!selectionAdditive) ClearSegSelection();
                }
                else if (!selectionAdditive) ClearObdSelection();
                if (dragged && AnnotationsVisible)
                {
                    if (isSeg)
                    {
                        foreach (SegRegion region in segRegions)
                        {
                            if (IntersectsSegArea(region, selection))
                            {
                                selectedSegIds.Add(region.Id);
                                if (selectedSegId == null) selectedSegId = region.Id;
                            }
                        }
                        if (!selectedSegIds.Contains(selectedSegId)) selectedSegId = selectedSegIds.FirstOrDefault();
                    }
                    else
                    {
                        foreach (ObdAnnotation annotation in obdAnnotations)
                        {
                            RectangleF annotationBounds = new RectangleF(annotation.X, annotation.Y, annotation.Width, annotation.Height);
                            if (RectanglesTouch(selection, annotationBounds))
                            {
                                selectedObdIds.Add(annotation.Id);
                                if (selectedObdId == null) selectedObdId = annotation.Id;
                            }
                        }
                        if (!selectedObdIds.Contains(selectedObdId)) selectedObdId = selectedObdIds.FirstOrDefault();
                    }
                }
                Invalidate();
            }
            else if (args.Button == MouseButtons.Left && obdDrag != ObdDragKind.None)
            {
                ObdDragKind completed = obdDrag;
                var previews = obdPreviews.ToDictionary(item => item.Key, item => item.Value);
                if (completed == ObdDragKind.Create && obdPreview.HasValue) previews.Add(string.Empty, obdPreview.Value);
                obdPreview = null;
                obdPreviews = new Dictionary<string, RectangleF>();
                activeObd = null;
                obdDrag = ObdDragKind.None;
                Capture = false;
                if (completed == ObdDragKind.Create)
                {
                    Rectangle rectangle = ToIntegerRectangle(previews[string.Empty]);
                    if (rectangle.Width >= 1 && rectangle.Height >= 1) ObdRectangleCreated?.Invoke(rectangle);
                }
                else if (previews.Count > 0)
                {
                    var changed = new Dictionary<string, Rectangle>();
                    foreach (var preview in previews)
                    {
                        ObdAnnotation original = obdAnnotations.FirstOrDefault(item => item.Id == preview.Key);
                        if (original != null && ToIntegerRectangle(preview.Value) != new Rectangle(original.X, original.Y, original.Width, original.Height)) changed.Add(preview.Key, ToIntegerRectangle(preview.Value));
                    }
                    if (changed.Count > 0) ObdRectanglesChanged?.Invoke(changed);
                }
                SetThumbnailHiddenForGesture(false);
                Invalidate();
            }
            else if (args.Button == MouseButtons.Left && segDrag != SegDragKind.None)
            {
                SegRegion[] changed = segPreviews.Values.Where(value => !SegEquals(value, segRegions.First(item => item.Id == value.Id))).ToArray();
                segPreviews = new Dictionary<string, SegRegion>();
                segDrag = SegDragKind.None;
                Capture = false;
                if (changed.Length > 0) SegRegionsChanged?.Invoke(changed);
                segRotationAngle = 0d;
                Invalidate();
            }
            else if (args.Button == MouseButtons.Left && segPreview.HasValue)
            {
                Rectangle preview = ToIntegerRectangle(segPreview.Value);
                SegToolKind? tool = SegTool;
                segPreview = null;
                Capture = false;
                bool erase = (CurrentModifierKeys & Keys.Control) == Keys.Control;
                if (tool == SegToolKind.Rect && preview.Width >= 1 && preview.Height >= 1)
                {
                    if (erase) SegGeometryEraseRequested?.Invoke(SegRegionType.Rect, preview, new SegPoint[0], 0);
                    else SegRegionCreated?.Invoke(SegRegionType.Rect, preview);
                }
                else if (tool == SegToolKind.Ellipse)
                {
                    int radiusX = preview.Width / 2;
                    int radiusY = preview.Height / 2;
                    if (radiusX >= 1 && radiusY >= 1)
                    {
                        Rectangle ellipse = new Rectangle(preview.X + radiusX, preview.Y + radiusY, radiusX, radiusY);
                        if (erase) SegGeometryEraseRequested?.Invoke(SegRegionType.Ellipse, ellipse, new SegPoint[0], 0);
                        else SegRegionCreated?.Invoke(SegRegionType.Ellipse, preview);
                    }
                }
                SetThumbnailHiddenForGesture(false);
                Invalidate();
            }
            else if (args.Button == MouseButtons.Left && Capture && (SegTool == SegToolKind.Polygon || SegTool == SegToolKind.Brush || SegTool == SegToolKind.Eraser))
            {
                Capture = false;
                PointF point = ClampSegPathPoint(viewport.ScreenToImage(args.Location));
                if ((SegTool == SegToolKind.Brush || SegTool == SegToolKind.Eraser) && segPath.Count > 0)
                {
                    SegPoint last = ToSegPoint(segPath[segPath.Count - 1]);
                    SegPoint end = ToSegPoint(point);
                    if (last.X != end.X || last.Y != end.Y) segPath.Add(point);
                }
                if (segPathDragging && SegTool == SegToolKind.Polygon && segPath.Count >= 3 && DistanceInScreenPixels(segPath[0], point) <= 8f)
                {
                    SegPoint[] polygon = segPath.Select(ToSegPoint).ToArray();
                    if ((CurrentModifierKeys & Keys.Control) == Keys.Control) SegGeometryEraseRequested?.Invoke(SegRegionType.PolyGon, Rectangle.Empty, polygon, 0);
                    else SegPathCreated?.Invoke(SegRegionType.PolyGon, polygon, 0);
                    segPath.Clear();
                }
                else if (SegTool == SegToolKind.Brush)
                {
                    if (segPath.Count >= 1)
                    {
                        SegPoint[] path = segPath.Select(ToSegPoint).ToArray();
                        if ((CurrentModifierKeys & Keys.Control) == Keys.Control) SegGeometryEraseRequested?.Invoke(SegRegionType.PolyLine, Rectangle.Empty, path, segPathStrokeWidth);
                        else SegPathCreated?.Invoke(SegRegionType.PolyLine, path, segPathStrokeWidth);
                    }
                    segPath.Clear();
                    segPathStrokeWidth = 0;
                }
                else if (SegTool == SegToolKind.Eraser)
                {
                    if (segPath.Count >= 1) SegEraseRequested?.Invoke(segPath.Select(ToSegPoint).ToArray(), segPathStrokeWidth);
                    segPath.Clear();
                    segPathStrokeWidth = 0;
                }
                segPathDragging = false;
                SetThumbnailHiddenForGesture(false);
                Invalidate();
            }
            if (panning) Capture = false;
            panning = false;
            base.OnMouseUp(args);
        }

        /// <summary>
        /// 處理畫布焦點中的 Escape 取消及 Delete 標註刪除命令。
        /// </summary>
        /// <param name="args">包含此次事件詳細資料的事件資料。</param>
        protected override void OnKeyDown(KeyEventArgs args)
        {
            if (args.KeyCode == Keys.Escape) { DiscardUnfinishedShape(); args.Handled = true; }
            else if (args.KeyCode == Keys.Delete && ObdEnabled && selectedObdIds.Count > 0)
            {
                ObdAnnotationsDeleteRequested?.Invoke(selectedObdIds.ToArray());
                args.Handled = true;
                args.SuppressKeyPress = true;
            }
            else if (args.KeyCode == Keys.Delete && SegEnabled && selectedSegIds.Count > 0)
            {
                SegRegionsDeleteRequested?.Invoke(selectedSegIds.ToArray());
                args.Handled = true;
                args.SuppressKeyPress = true;
            }
            RefreshToolCursor();
            Invalidate();
            base.OnKeyDown(args);
        }

        /// <summary>
        /// Ctrl 狀態變化時即時更新擦除游標符號。
        /// </summary>
        /// <param name="args">包含此次事件詳細資料的事件資料。</param>
        protected override void OnKeyUp(KeyEventArgs args)
        {
            RefreshToolCursor();
            Invalidate();
            base.OnKeyUp(args);
        }

        /// <summary>
        /// 依指標偏移計算選取 OBD 標註的預覽矩形。
        /// </summary>
        private IReadOnlyDictionary<string, RectangleF> MoveRectangles(PointF point)
        {
            ObdAnnotation[] selected = obdAnnotations.Where(item => selectedObdIds.Contains(item.Id)).ToArray();
            float xOffset = point.X - obdDragStart.X;
            float yOffset = point.Y - obdDragStart.Y;
            xOffset = Math.Max(selected.Max(item => -item.X), Math.Min(xOffset, selected.Min(item => viewport.ImageSize.Width - item.X - item.Width)));
            yOffset = Math.Max(selected.Max(item => -item.Y), Math.Min(yOffset, selected.Min(item => viewport.ImageSize.Height - item.Y - item.Height)));
            return selected.ToDictionary(item => item.Id, item => new RectangleF(item.X + xOffset, item.Y + yOffset, item.Width, item.Height));
        }

        /// <summary>
        /// 依指標偏移計算選取 SEG 區域的預覽位置。
        /// </summary>
        private IReadOnlyDictionary<string, SegRegion> MoveSegRegions(PointF point)
        {
            SegRegion[] selected = segRegions.Where(item => selectedSegIds.Contains(item.Id)).ToArray();
            float xOffset = point.X - segDragStart.X;
            float yOffset = point.Y - segDragStart.Y;
            xOffset = Math.Max(selected.Max(item => -GetStoredBounds(item).Left), Math.Min(xOffset, selected.Min(item => viewport.ImageSize.Width - GetStoredBounds(item).Right)));
            yOffset = Math.Max(selected.Max(item => -GetStoredBounds(item).Top), Math.Min(yOffset, selected.Min(item => viewport.ImageSize.Height - GetStoredBounds(item).Bottom)));
            return selected.ToDictionary(item => item.Id, item => TranslateSegRegion(item, xOffset, yOffset));
        }

        /// <summary>
        /// 建立將指定位移套用至 SEG 座標與點位的新區域。
        /// </summary>
        /// <param name="region">要處理的 SEG 區域。</param>
        /// <param name="xOffset">水平位移量。</param>
        /// <param name="yOffset">垂直位移量。</param>
        /// <returns>轉換後的 SEG 區域。</returns>
        private static SegRegion TranslateSegRegion(SegRegion region, float xOffset, float yOffset)
        {
            if (region.Points.Count > 0)
            {
                return new SegRegion(region.Id, region.AnnotationSetId, region.ImageId, region.ClassId, region.Type, 0, 0, 0, 0, region.ZOrder, region.Points.Select(point => new SegPoint((int)Math.Round(point.X + xOffset, MidpointRounding.AwayFromZero), (int)Math.Round(point.Y + yOffset, MidpointRounding.AwayFromZero))).ToArray(), region.StrokeWidth);
            }
            return new SegRegion(region.Id, region.AnnotationSetId, region.ImageId, region.ClassId, region.Type, (int)Math.Round(region.X + xOffset, MidpointRounding.AwayFromZero), (int)Math.Round(region.Y + yOffset, MidpointRounding.AwayFromZero), region.Width, region.Height, region.ZOrder);
        }

        /// <summary>
        /// 依畫布顯示順序找出指定原圖座標最上層的 SEG 區域。
        /// </summary>
        /// <param name="point">要處理的座標點。</param>
        /// <returns>轉換後的 SEG 區域。</returns>
        private SegRegion TopmostSegAt(PointF point)
        {
            if (!AnnotationsVisible) return null;
            return segRegions.Where(item => selectedSegIds.Contains(item.Id)).Reverse().Concat(segRegions.Where(item => !selectedSegIds.Contains(item.Id)).Reverse()).FirstOrDefault(item => ContainsSegPoint(item, point));
        }

        /// <summary>
        /// 依矩形、橢圓、線段筆畫或多邊形填色判斷點位是否命中。
        /// </summary>
        /// <param name="region">要處理的 SEG 區域。</param>
        /// <param name="point">要處理的座標點。</param>
        /// <returns>若符合摘要所述條件則為 true，否則為 false。</returns>
        private static bool ContainsSegPoint(SegRegion region, PointF point)
        {
            if (region.Type == SegRegionType.Rect) return new RectangleF(region.X, region.Y, region.Width, region.Height).Contains(point);
            if (region.Type == SegRegionType.Ellipse)
            {
                float x = (point.X - region.X) / region.Width, y = (point.Y - region.Y) / region.Height;
                return x * x + y * y <= 1f;
            }
            if (region.Type == SegRegionType.PolyLine)
            {
                if (region.Points.Count == 1) return DistanceToSegment(point, region.Points[0], region.Points[0]) <= region.StrokeWidth / 2f;
                return region.Points.Zip(region.Points.Skip(1), (first, second) => DistanceToSegment(point, first, second)).Any(distance => distance <= region.StrokeWidth / 2f);
            }
            int winding = 0;
            for (int index = 0, previous = region.Points.Count - 1; index < region.Points.Count; previous = index++)
            {
                SegPoint a = region.Points[index], b = region.Points[previous];
                double cross = (b.X - a.X) * (point.Y - a.Y) - (point.X - a.X) * (b.Y - a.Y);
                if (a.Y <= point.Y && b.Y > point.Y && cross > 0d) winding++;
                else if (a.Y > point.Y && b.Y <= point.Y && cross < 0d) winding--;
            }
            return winding != 0;
        }

        /// <summary>
        /// 計算點到線段最近投影位置的歐氏距離。
        /// </summary>
        /// <param name="point">要處理的座標點。</param>
        /// <param name="first">第一個要處理的值。</param>
        /// <param name="second">第二個要處理的值。</param>
        /// <returns>計算後的單精度數值。</returns>
        private static float DistanceToSegment(PointF point, SegPoint first, SegPoint second)
        {
            float x = second.X - first.X, y = second.Y - first.Y;
            float length = x * x + y * y;
            float t = length == 0f ? 0f : Math.Max(0f, Math.Min(1f, ((point.X - first.X) * x + (point.Y - first.Y) * y) / length));
            float dx = point.X - (first.X + t * x), dy = point.Y - (first.Y + t * y);
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>
        /// 取得 SEG 區域在原圖座標中的外接範圍。
        /// </summary>
        /// <param name="region">要處理的 SEG 區域。</param>
        /// <returns>計算後的浮點矩形範圍。</returns>
        private static RectangleF GetStoredBounds(SegRegion region)
        {
            if (region.Points.Count == 0) return GetSegBounds(region);
            int inset = region.Type == SegRegionType.PolyLine ? (region.StrokeWidth + 1) / 2 : 0;
            return RectangleF.FromLTRB(region.Points.Min(point => point.X) - inset, region.Points.Min(point => point.Y) - inset, region.Points.Max(point => point.X) + inset, region.Points.Max(point => point.Y) + inset);
        }

        /// <summary>
        /// 判斷指標命中的 SEG 編輯控制點及所屬區域。
        /// </summary>
        /// <param name="point">要處理的座標點。</param>
        /// <param name="hitRegion">命中測試結果區域。</param>
        /// <returns>包含命中控制點種類及其所屬 SEG 區域的結果。</returns>
        private SegDragKind HitSegHandle(Point point, out SegRegion hitRegion)
        {
            if (!AnnotationsVisible) { hitRegion = null; return SegDragKind.None; }
            SegDragKind[] kinds = { SegDragKind.ResizeNw, SegDragKind.ResizeN, SegDragKind.ResizeNe, SegDragKind.ResizeE, SegDragKind.ResizeSe, SegDragKind.ResizeS, SegDragKind.ResizeSw, SegDragKind.ResizeW };
            foreach (SegRegion region in segRegions.Where(item => selectedSegIds.Contains(item.Id)).Reverse())
            {
                RectangleF rectangle = ToScreenRectangle(GetSegBounds(region));
                PointF rotation = new PointF(rectangle.Left + rectangle.Width / 2f, rectangle.Top - 24f);
                if (Math.Abs(point.X - rotation.X) <= HandleSize && Math.Abs(point.Y - rotation.Y) <= HandleSize)
                {
                    hitRegion = region;
                    return SegDragKind.Rotate;
                }
                PointF[] points = GetHandlePoints(rectangle);
                for (int index = 0; index < points.Length; index++)
                {
                    if (Math.Abs(point.X - points[index].X) > HandleSize || Math.Abs(point.Y - points[index].Y) > HandleSize) continue;
                    hitRegion = region;
                    return kinds[index];
                }
            }
            hitRegion = null;
            return SegDragKind.None;
        }

        /// <summary>
        /// 依目前拖曳控制點調整 SEG 區域幾何尺寸或邊界。
        /// </summary>
        /// <param name="region">要處理的 SEG 區域。</param>
        /// <param name="point">要處理的座標點。</param>
        /// <returns>轉換後的 SEG 區域。</returns>
        private SegRegion ResizeSegRegion(SegRegion region, PointF point)
        {
            float left = segOriginalBounds.Left, top = segOriginalBounds.Top, right = segOriginalBounds.Right, bottom = segOriginalBounds.Bottom;
            if (segDrag == SegDragKind.ResizeN || segDrag == SegDragKind.ResizeNw || segDrag == SegDragKind.ResizeNe) top = Math.Min(point.Y, bottom - 1f);
            if (segDrag == SegDragKind.ResizeS || segDrag == SegDragKind.ResizeSw || segDrag == SegDragKind.ResizeSe) bottom = Math.Max(point.Y, top + 1f);
            if (segDrag == SegDragKind.ResizeW || segDrag == SegDragKind.ResizeNw || segDrag == SegDragKind.ResizeSw) left = Math.Min(point.X, right - 1f);
            if (segDrag == SegDragKind.ResizeE || segDrag == SegDragKind.ResizeNe || segDrag == SegDragKind.ResizeSe) right = Math.Max(point.X, left + 1f);
            RectangleF target = Clamp(RectangleF.FromLTRB(left, top, right, bottom));
            if (region.Points.Count > 0)
            {
                int inset = region.Type == SegRegionType.PolyLine ? (region.StrokeWidth + 1) / 2 : 0;
                int maximumRight = region.Type == SegRegionType.PolyLine ? viewport.ImageSize.Width - inset : viewport.ImageSize.Width;
                int maximumBottom = region.Type == SegRegionType.PolyLine ? viewport.ImageSize.Height - inset : viewport.ImageSize.Height;
                target = RectangleF.FromLTRB(Math.Max(inset, target.Left), Math.Max(inset, target.Top), Math.Min(maximumRight, target.Right), Math.Min(maximumBottom, target.Bottom));
            }
            return ScaleSegRegion(region, segOriginalBounds, target);
        }

        /// <summary>
        /// 以指定中心及比例調整 SEG 區域及其路徑點座標。
        /// </summary>
        /// <param name="region">要處理的 SEG 區域。</param>
        /// <param name="source">提供資料或數值的來源。</param>
        /// <param name="target">要更新的目標控制項或值。</param>
        /// <returns>轉換後的 SEG 區域。</returns>
        private static SegRegion ScaleSegRegion(SegRegion region, RectangleF source, RectangleF target)
        {
            float scaleX = target.Width / source.Width, scaleY = target.Height / source.Height;
            Func<float, float, SegPoint> scale = (x, y) => new SegPoint((int)Math.Round(target.Left + (x - source.Left) * scaleX, MidpointRounding.AwayFromZero), (int)Math.Round(target.Top + (y - source.Top) * scaleY, MidpointRounding.AwayFromZero));
            if (region.Type == SegRegionType.Rect) return new SegRegion(region.Id, region.AnnotationSetId, region.ImageId, region.ClassId, region.Type, (int)Math.Round(target.Left), (int)Math.Round(target.Top), Math.Max(1, (int)Math.Round(target.Width)), Math.Max(1, (int)Math.Round(target.Height)), region.ZOrder);
            if (region.Type == SegRegionType.Ellipse)
            {
                SegPoint center = scale(region.X, region.Y);
                return new SegRegion(region.Id, region.AnnotationSetId, region.ImageId, region.ClassId, region.Type, center.X, center.Y, Math.Max(1, (int)Math.Round(region.Width * scaleX)), Math.Max(1, (int)Math.Round(region.Height * scaleY)), region.ZOrder);
            }
            if (region.Type == SegRegionType.PolyLine)
            {
                var line = new Path64(region.Points.Select(value => new Point64(value.X, value.Y)));
                SegPoint[] outline = Clipper.InflatePaths(new Paths64 { line }, region.StrokeWidth / 2d, JoinType.Round, EndType.Round).SelectMany(path => path).Select(value => scale(value.X, value.Y)).ToArray();
                return new SegRegion(region.Id, region.AnnotationSetId, region.ImageId, region.ClassId, SegRegionType.PolyGon, 0, 0, 0, 0, region.ZOrder, outline);
            }
            return new SegRegion(region.Id, region.AnnotationSetId, region.ImageId, region.ClassId, region.Type, 0, 0, 0, 0, region.ZOrder, region.Points.Select(value => scale(value.X, value.Y)).ToArray(), region.StrokeWidth);
        }


        /// <summary>
        /// 計算所選 SEG 區域的共同旋轉預覽並回報角度。
        /// </summary>
        /// <param name="point">要處理的座標點。</param>
        /// <param name="previews">目前的預覽資料。</param>
        /// <param name="angle">要套用的旋轉角度。</param>
        /// <returns>若符合摘要所述條件則為 true，否則為 false。</returns>
        private bool TryRotateSegRegions(PointF point, out IReadOnlyDictionary<string, SegRegion> previews, out double angle)
        {
            PointF pivot = new PointF(segOriginalBounds.Left + segOriginalBounds.Width / 2f, segOriginalBounds.Top + segOriginalBounds.Height / 2f);
            double start = Math.Atan2(segDragStart.Y - pivot.Y, segDragStart.X - pivot.X);
            double current = Math.Atan2(point.Y - pivot.Y, point.X - pivot.X);
            angle = current - start;
            if (angle > Math.PI) angle -= 2d * Math.PI;
            else if (angle < -Math.PI) angle += 2d * Math.PI;
            SegRegion[] selected = segRegions.Where(region => selectedSegIds.Contains(region.Id)).ToArray();
            var result = new Dictionary<string, SegRegion>();
            foreach (SegRegion region in selected)
            {
                SegRegion rotated = RotateSegRegion(region, angle);
                if (rotated == null)
                {
                    previews = null;
                    return false;
                }
                result.Add(region.Id, rotated);
            }
            previews = result;
            return true;
        }

        /// <summary>
        /// 建立套用指定中心與角度的 SEG 區域副本。
        /// </summary>
        /// <param name="region">要處理的 SEG 區域。</param>
        /// <param name="angle">要套用的旋轉角度。</param>
        /// <returns>轉換後的 SEG 區域。</returns>
        private SegRegion RotateSegRegion(SegRegion region, double angle)
        {
            RectangleF bounds = GetSegBounds(region);
            PointF center = new PointF(bounds.Left + bounds.Width / 2f, bounds.Top + bounds.Height / 2f);
            if (Math.Abs(angle) < 0.000001d) return region;
            SegPoint[] transformed = GetRotationPoints(region).Select(value =>
            {
                PointF rotated = RotatePoint(new PointF(value.X, value.Y), center, angle);
                return new SegPoint((int)Math.Round(rotated.X, MidpointRounding.AwayFromZero), (int)Math.Round(rotated.Y, MidpointRounding.AwayFromZero));
            }).ToArray();
            SegRegion result = new SegRegion(region.Id, region.AnnotationSetId, region.ImageId, region.ClassId, region.Type == SegRegionType.PolyLine ? SegRegionType.PolyLine : SegRegionType.PolyGon, 0, 0, 0, 0, region.ZOrder, transformed, region.StrokeWidth);
            RectangleF rotatedBounds = GetSegBounds(result);
            if (transformed.Any(value => value.X < 0 || value.Y < 0 || value.X > viewport.ImageSize.Width || value.Y > viewport.ImageSize.Height) || rotatedBounds.Left < 0 || rotatedBounds.Top < 0 || rotatedBounds.Right > viewport.ImageSize.Width || rotatedBounds.Bottom > viewport.ImageSize.Height) return null;
            return result;
        }

        /// <summary>
        /// 產生旋轉矩形四角與旋轉控制點的原圖座標。
        /// </summary>
        /// <param name="region">要處理的 SEG 區域。</param>
        /// <returns>包含矩形四角及旋轉控制點的座標集合。</returns>
        private static IEnumerable<SegPoint> GetRotationPoints(SegRegion region)
        {
            if (region.Points.Count > 0) return region.Points;
            if (region.Type == SegRegionType.Rect) return new[] { new SegPoint(region.X, region.Y), new SegPoint(region.X + region.Width, region.Y), new SegPoint(region.X + region.Width, region.Y + region.Height), new SegPoint(region.X, region.Y + region.Height) };
            return Enumerable.Range(0, 32).Select(index => new SegPoint((int)Math.Round(region.X + region.Width * Math.Cos(2d * Math.PI * index / 32d)), (int)Math.Round(region.Y + region.Height * Math.Sin(2d * Math.PI * index / 32d))));
        }

        /// <summary>
        /// 清除目前的 SEG 區域選取與主要選取識別碼。
        /// </summary>
        private void ClearSegSelection()
        {
            selectedSegIds.Clear();
            selectedSegId = null;
            segPreviews = new Dictionary<string, SegRegion>();
        }

        /// <summary>
        /// 比較兩個 SEG 區域的識別碼、幾何、點位及繪製屬性。
        /// </summary>
        /// <param name="first">第一個要處理的值。</param>
        /// <param name="second">第二個要處理的值。</param>
        /// <returns>若符合摘要所述條件則為 true，否則為 false。</returns>
        private static bool SegEquals(SegRegion first, SegRegion second)
        {
            return first.ClassId == second.ClassId && first.Type == second.Type && first.X == second.X && first.Y == second.Y && first.Width == second.Width && first.Height == second.Height && first.StrokeWidth == second.StrokeWidth && first.Points.Count == second.Points.Count && first.Points.Zip(second.Points, (left, right) => left.X == right.X && left.Y == right.Y).All(equal => equal);
        }

        /// <summary>
        /// 依指定拖曳控制點及指標位置調整矩形邊界。
        /// </summary>
        /// <param name="point">要處理的座標點。</param>
        /// <returns>計算後的浮點矩形範圍。</returns>
        private RectangleF ResizeRectangle(PointF point)
        {
            float left = obdOriginal.Left, top = obdOriginal.Top, right = obdOriginal.Right, bottom = obdOriginal.Bottom;
            if (obdDrag == ObdDragKind.ResizeN || obdDrag == ObdDragKind.ResizeNw || obdDrag == ObdDragKind.ResizeNe) top = Math.Min(point.Y, bottom - 1f);
            if (obdDrag == ObdDragKind.ResizeS || obdDrag == ObdDragKind.ResizeSw || obdDrag == ObdDragKind.ResizeSe) bottom = Math.Max(point.Y, top + 1f);
            if (obdDrag == ObdDragKind.ResizeW || obdDrag == ObdDragKind.ResizeNw || obdDrag == ObdDragKind.ResizeSw) left = Math.Min(point.X, right - 1f);
            if (obdDrag == ObdDragKind.ResizeE || obdDrag == ObdDragKind.ResizeNe || obdDrag == ObdDragKind.ResizeSe) right = Math.Max(point.X, left + 1f);
            return Clamp(RectangleF.FromLTRB(left, top, right, bottom));
        }

        /// <summary>
        /// 判斷指標命中的 OBD 矩形縮放控制點及標註。
        /// </summary>
        /// <param name="point">要處理的座標點。</param>
        /// <param name="hitAnnotation">命中測試找到的標註。</param>
        /// <returns>包含命中控制點種類及其所屬 SEG 區域的結果。</returns>
        private ObdDragKind HitHandle(Point point, out ObdAnnotation hitAnnotation)
        {
            if (!AnnotationsVisible) { hitAnnotation = null; return ObdDragKind.None; }
            ObdDragKind[] kinds = { ObdDragKind.ResizeNw, ObdDragKind.ResizeN, ObdDragKind.ResizeNe, ObdDragKind.ResizeE, ObdDragKind.ResizeSe, ObdDragKind.ResizeS, ObdDragKind.ResizeSw, ObdDragKind.ResizeW };
            foreach (ObdAnnotation annotation in obdAnnotations.Where(item => selectedObdIds.Contains(item.Id)).Reverse())
            {
                PointF[] points = GetHandlePoints(ToScreenRectangle(annotation.X, annotation.Y, annotation.Width, annotation.Height));
                for (int index = 0; index < points.Length; index++)
                {
                    if (Math.Abs(point.X - points[index].X) > HandleSize || Math.Abs(point.Y - points[index].Y) > HandleSize) continue;
                    hitAnnotation = annotation;
                    return kinds[index];
                }
            }
            hitAnnotation = null;
            return ObdDragKind.None;
        }

        /// <summary>
        /// 依畫布顯示順序找出指定原圖座標最上層的 OBD 標註。
        /// </summary>
        /// <param name="point">要處理的座標點。</param>
        /// <returns>包含命中控制點種類及其所屬 SEG 區域的結果。</returns>
        private ObdAnnotation TopmostObdAt(PointF point)
        {
            if (!AnnotationsVisible) return null;
            ObdAnnotation selected = obdAnnotations.Where(item => selectedObdIds.Contains(item.Id)).Reverse().FirstOrDefault(item => new RectangleF(item.X, item.Y, item.Width, item.Height).Contains(point));
            if (selected != null) return selected;
            return obdAnnotations.Reverse().FirstOrDefault(item => !selectedObdIds.Contains(item.Id) && new RectangleF(item.X, item.Y, item.Width, item.Height).Contains(point));
        }

        /// <summary>
        /// 清除目前的 OBD 標註選取與主要選取識別碼。
        /// </summary>
        private void ClearObdSelection()
        {
            selectedObdIds.Clear();
            selectedObdId = null;
        }

        /// <summary>
        /// 判斷兩個矩形範圍是否相交或接觸。
        /// </summary>
        /// <param name="first">第一個要處理的值。</param>
        /// <param name="second">第二個要處理的值。</param>
        /// <returns>若符合摘要所述條件則為 true，否則為 false。</returns>
        private static bool RectanglesTouch(RectangleF first, RectangleF second)
        {
            return first.Left <= second.Right && first.Right >= second.Left && first.Top <= second.Bottom && first.Bottom >= second.Top;
        }

        /// <summary>
        /// 記錄框選起點、螢幕座標及是否加入既有選取。
        /// </summary>
        /// <param name="point">要處理的座標點。</param>
        /// <param name="screenPoint">螢幕座標點。</param>
        /// <param name="isSeg">目前標註是否為 SEG 類型。</param>
        private void BeginSelectionGesture(PointF point, Point screenPoint, bool isSeg)
        {
            selectionStart = point;
            selectionScreenStart = screenPoint;
            selectionPreview = new RectangleF(point, SizeF.Empty);
            selectionAdditive = (CurrentModifierKeys & Keys.Control) == Keys.Control;
            selectionIsSeg = isSeg;
            Capture = true;
        }

        /// <summary>
        /// 判斷 SEG 區域是否與框選矩形相交。
        /// </summary>
        /// <param name="region">要處理的 SEG 區域。</param>
        /// <param name="selection">目前選取的標註集合。</param>
        /// <returns>若符合摘要所述條件則為 true，否則為 false。</returns>
        private static bool IntersectsSegArea(SegRegion region, RectangleF selection)
        {
            long left = ScaleSegCoordinate(selection.Left), top = ScaleSegCoordinate(selection.Top);
            long right = ScaleSegCoordinate(selection.Right), bottom = ScaleSegCoordinate(selection.Bottom);
            if (right <= left || bottom <= top) return false;

            Paths64 area;
            if (region.Type == SegRegionType.Rect)
            {
                area = new Paths64 { SegGeometryPrototype.Rectangle((long)region.X * SegSelectionScale, (long)region.Y * SegSelectionScale, (long)(region.X + region.Width) * SegSelectionScale, (long)(region.Y + region.Height) * SegSelectionScale) };
            }
            else if (region.Type == SegRegionType.Ellipse)
            {
                area = new Paths64 { SegGeometryPrototype.Ellipse((long)region.X * SegSelectionScale, (long)region.Y * SegSelectionScale, (long)region.Width * SegSelectionScale, (long)region.Height * SegSelectionScale, 32) };
            }
            else
            {
                Path64 path = new Path64(region.Points.Select(point => new Point64((long)point.X * SegSelectionScale, (long)point.Y * SegSelectionScale)).ToList());
                area = region.Type == SegRegionType.PolyLine
                    ? SegGeometryPrototype.Stroke(path, (long)region.StrokeWidth * SegSelectionScale)
                    : new Paths64 { path };
            }

            Paths64 overlap = Clipper.Intersect(area, new Paths64 { SegGeometryPrototype.Rectangle(left, top, right, bottom) }, FillRule.NonZero);
            return SegGeometryPrototype.Area(overlap) > 0d;
        }

        /// <summary>
        /// 依比例調整座標並限制在圖片允許的範圍內。
        /// </summary>
        /// <param name="value">要套用或檢查的值。</param>
        /// <returns>計算後的長整數結果。</returns>
        private static long ScaleSegCoordinate(float value)
        {
            return (long)Math.Round(value * SegSelectionScale, MidpointRounding.AwayFromZero);
        }

        /// <summary>
        /// 取得矩形各邊中點與角點的控制點座標。
        /// </summary>
        /// <param name="rectangle">要處理的矩形範圍。</param>
        /// <returns>各區域經目前畫布轉換後的像素矩形陣列。</returns>
        private static PointF[] GetHandlePoints(RectangleF rectangle)
        {
            return new[] { new PointF(rectangle.Left, rectangle.Top), new PointF(rectangle.Left + rectangle.Width / 2f, rectangle.Top), new PointF(rectangle.Right, rectangle.Top), new PointF(rectangle.Right, rectangle.Top + rectangle.Height / 2f), new PointF(rectangle.Right, rectangle.Bottom), new PointF(rectangle.Left + rectangle.Width / 2f, rectangle.Bottom), new PointF(rectangle.Left, rectangle.Bottom), new PointF(rectangle.Left, rectangle.Top + rectangle.Height / 2f) };
        }

        /// <summary>
        /// 將原圖座標矩形轉成畫布螢幕座標矩形。
        /// </summary>
        /// <param name="x">水平座標。</param>
        /// <param name="y">垂直座標。</param>
        /// <param name="width">要使用的筆寬或線寬。</param>
        /// <param name="height">控制項或圖形的高度。</param>
        /// <returns>計算後的浮點矩形範圍。</returns>
        private RectangleF ToScreenRectangle(float x, float y, float width, float height)
        {
            PointF topLeft = viewport.ImageToScreen(new PointF(x, y));
            return new RectangleF(topLeft.X, topLeft.Y, (float)(width * viewport.Zoom), (float)(height * viewport.Zoom));
        }

        /// <summary>
        /// 將原圖座標矩形轉成畫布螢幕座標矩形。
        /// </summary>
        /// <param name="rectangle">要處理的矩形範圍。</param>
        /// <returns>計算後的浮點矩形範圍。</returns>
        private RectangleF ToScreenRectangle(RectangleF rectangle) { return ToScreenRectangle(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height); }

        /// <summary>
        /// 依區域種類取得 SEG 幾何的原圖座標範圍。
        /// </summary>
        /// <param name="region">要處理的 SEG 區域。</param>
        /// <returns>計算後的浮點矩形範圍。</returns>
        private static RectangleF GetSegBounds(SegRegion region)
        {
            if (region.Type == SegRegionType.Rect) return new RectangleF(region.X, region.Y, region.Width, region.Height);
            if (region.Type == SegRegionType.Ellipse) return new RectangleF(region.X - region.Width, region.Y - region.Height, region.Width * 2, region.Height * 2);
            int left = region.Points.Min(point => point.X), right = region.Points.Max(point => point.X), top = region.Points.Min(point => point.Y), bottom = region.Points.Max(point => point.Y);
            float inset = region.Type == SegRegionType.PolyLine ? region.StrokeWidth / 2f : 0f;
            return RectangleF.FromLTRB(left - inset, top - inset, right + inset, bottom + inset);
        }

        /// <summary>
        /// 將座標限制在圖片邊界內。
        /// </summary>
        /// <param name="value">要套用或檢查的值。</param>
        /// <returns>計算後的浮點座標點。</returns>
        private PointF ClampPoint(PointF value) { return new PointF(Math.Max(0f, Math.Min(value.X, viewport.ImageSize.Width)), Math.Max(0f, Math.Min(value.Y, viewport.ImageSize.Height))); }
        /// <summary>
        /// 通知外層控制項目前畫布視角已變更。
        /// </summary>
        private void RaiseViewChanged()
        {
            if (IsDisposed || Disposing) return;
            if (InvokeRequired)
            {
                try { BeginInvoke((MethodInvoker)RaiseViewChanged); }
                catch (InvalidOperationException) { }
                return;
            }
            ZoomChanged?.Invoke(this, EventArgs.Empty);
            ViewChanged?.Invoke(this, EventArgs.Empty);
        }
        /// <summary>
        /// 將數值限制在指定的最小值與最大值之間。
        /// </summary>
        /// <param name="value">要套用或檢查的值。</param>
        /// <returns>計算後的浮點矩形範圍。</returns>
        private RectangleF Clamp(RectangleF value)
        {
            float left = Math.Max(0f, Math.Min(value.Left, viewport.ImageSize.Width));
            float top = Math.Max(0f, Math.Min(value.Top, viewport.ImageSize.Height));
            float right = Math.Max(left, Math.Min(value.Right, viewport.ImageSize.Width));
            float bottom = Math.Max(top, Math.Min(value.Bottom, viewport.ImageSize.Height));
            return RectangleF.FromLTRB(left, top, right, bottom);
        }
        /// <summary>
        /// 依兩個對角點建立非負寬高的矩形。
        /// </summary>
        /// <param name="first">第一個要處理的值。</param>
        /// <param name="second">第二個要處理的值。</param>
        /// <returns>計算後的浮點矩形範圍。</returns>
        private static RectangleF Normalize(PointF first, PointF second) { return RectangleF.FromLTRB(Math.Min(first.X, second.X), Math.Min(first.Y, second.Y), Math.Max(first.X, second.X), Math.Max(first.Y, second.Y)); }
        /// <summary>
        /// 將浮點矩形四捨五入為整數座標矩形。
        /// </summary>
        /// <param name="rectangle">要處理的矩形範圍。</param>
        /// <returns>計算後的整數矩形範圍。</returns>
        private static Rectangle ToIntegerRectangle(RectangleF rectangle) { return Rectangle.FromLTRB((int)Math.Round(rectangle.Left), (int)Math.Round(rectangle.Top), (int)Math.Round(rectangle.Right), (int)Math.Round(rectangle.Bottom)); }
        /// <summary>
        /// 依筆畫半徑將 SEG 路徑點限制在圖片可繪製範圍。
        /// </summary>
        /// <param name="point">要處理的座標點。</param>
        /// <returns>計算後的浮點座標點。</returns>
        private PointF ClampSegPathPoint(PointF point)
        {
            int width = SegTool == SegToolKind.Brush || SegTool == SegToolKind.Eraser || SegTool == SegToolKind.Line ? SegStrokeWidth : 0;
            int inset = width == 0 ? 0 : (width + 1) / 2;
            Size size = viewport.ImageSize;
            return new PointF(Math.Max(inset, Math.Min(size.Width - inset, point.X)), Math.Max(inset, Math.Min(size.Height - inset, point.Y)));
        }
        /// <summary>
        /// 判斷目前圖片是否有足夠空間容納完整筆畫寬度。
        /// </summary>
        /// <returns>若符合摘要所述條件則為 true，否則為 false。</returns>
        private bool CanFitSegPath()
        {
            int width = SegStrokeWidth;
            int inset = (width + 1) / 2;
            return viewport.ImageSize.Width >= inset * 2 && viewport.ImageSize.Height >= inset * 2;
        }
        /// <summary>
        /// 將畫布點位四捨五入為整數 SEG 座標。
        /// </summary>
        /// <param name="point">要處理的座標點。</param>
        /// <returns>包含命中控制點種類及其所屬 SEG 區域的結果。</returns>
        private static SegPoint ToSegPoint(PointF point) { return new SegPoint((int)Math.Round(point.X, MidpointRounding.AwayFromZero), (int)Math.Round(point.Y, MidpointRounding.AwayFromZero)); }
        /// <summary>
        /// 將兩個原圖點位轉成螢幕座標後計算距離。
        /// </summary>
        /// <param name="first">第一個要處理的值。</param>
        /// <param name="second">第二個要處理的值。</param>
        /// <returns>計算後的單精度數值。</returns>
        private float DistanceInScreenPixels(PointF first, PointF second) { PointF a = viewport.ImageToScreen(first); PointF b = viewport.ImageToScreen(second); float x = a.X - b.X, y = a.Y - b.Y; return (float)Math.Sqrt(x * x + y * y); }

        /// <summary>
        /// 釋放載入中的取消權杖及目前圖片資源。
        /// </summary>
        /// <param name="disposing">指出是否正在處置元件的旗標。</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing) { CancelImageLoad(); image?.Dispose(); }
            base.Dispose(disposing);
        }

        // 表示 OBD 建立、移動及各方向縮放手勢狀態。
        private enum ObdDragKind { None, Create, Move, ResizeN, ResizeS, ResizeW, ResizeE, ResizeNw, ResizeNe, ResizeSw, ResizeSe }
        // 表示 SEG 移動、各方向縮放及旋轉手勢狀態。
        private enum SegDragKind { None, Move, ResizeN, ResizeS, ResizeW, ResizeE, ResizeNw, ResizeNe, ResizeSw, ResizeSe, Rotate }
        // 列出標註畫布支援的 SEG 工具種類。
        public enum SegToolKind { Select, Rect, Ellipse, Polygon, Brush, Line, Eraser }
        // 保存解碼後的圖片預覽及來源尺寸。
        private sealed class CanvasImage
        {
            /// <summary>
            /// 保存畫布預覽影像及其原始圖片尺寸。
            /// </summary>
            public CanvasImage(Image preview, Size originalSize) { Preview = preview; OriginalSize = originalSize; }
            // 取得用於畫布顯示的預覽影像。
            public Image Preview { get; private set; }
            // 取得來源圖片的原始尺寸。
            public Size OriginalSize { get; private set; }
        }

        // 表示可在圖片頁與標註頁間同步的畫布視角。
        internal struct CanvasViewState
        {
            /// <summary>
            /// 保存畫布縮放倍率、原圖位置與適配視窗狀態。
            /// </summary>
            public CanvasViewState(double zoom, PointF origin, bool isFitted)
            {
                Zoom = zoom;
                Origin = origin;
                IsFitted = isFitted;
            }

            // 取得此視角的縮放倍率。
            public double Zoom { get; private set; }
            // 取得此視角的原圖畫面原點。
            public PointF Origin { get; private set; }
            // 指出此視角是否由適配視窗狀態產生。
            public bool IsFitted { get; private set; }
        }

        // 保存畫布導覽縮圖與視野框的幾何配置。
        internal struct CanvasThumbnailLayout
        {
            // 限制縮圖的最大畫面寬度。
            private const float MaximumWidth = 180f;
            // 限制縮圖的最大畫面高度。
            private const float MaximumHeight = 120f;
            // 設定縮圖與畫布邊緣間距。
            private const float Margin = 8f;

            /// <summary>
            /// 保存縮圖、視野框及縮圖切換鈕的畫面範圍。
            /// </summary>
            private CanvasThumbnailLayout(RectangleF imageBounds, RectangleF viewportBounds, RectangleF toggleBounds)
            {
                ImageBounds = imageBounds;
                ViewportBounds = viewportBounds;
                ToggleBounds = toggleBounds;
            }

            // 取得縮圖在畫布上的顯示範圍。
            internal RectangleF ImageBounds { get; private set; }
            // 取得縮圖中目前視野框的範圍。
            internal RectangleF ViewportBounds { get; private set; }
            // 取得縮圖收合切換鈕的範圍。
            internal RectangleF ToggleBounds { get; private set; }

            /// <summary>
            /// 在圖片超出可視範圍時建立符合 DPI 與畫布尺寸的縮圖配置。
            /// </summary>
            /// <param name="imageSize">圖片尺寸。</param>
            /// <param name="viewportSize">可視區域尺寸。</param>
            /// <param name="zoom">目前或要求的縮放比例。</param>
            /// <param name="origin">圖片視角原點。</param>
            /// <param name="dpiScale">目前顯示器的 DPI 縮放比例。</param>
            /// <param name="layout">要用來換算畫布座標的縮圖版面。</param>
            /// <returns>若符合摘要所述條件則為 true，否則為 false。</returns>
            internal static bool TryCreate(Size imageSize, Size viewportSize, double zoom, PointF origin, float dpiScale, out CanvasThumbnailLayout layout)
            {
                layout = default(CanvasThumbnailLayout);
                if (imageSize.Width <= 0 || imageSize.Height <= 0 || viewportSize.Width <= 0 || viewportSize.Height <= 0 || zoom <= 0d || dpiScale <= 0f) return false;
                if (imageSize.Width * zoom <= viewportSize.Width && imageSize.Height * zoom <= viewportSize.Height) return false;

                float margin = Margin * dpiScale;
                float availableWidth = Math.Min(MaximumWidth * dpiScale, viewportSize.Width - margin * 2f);
                float availableHeight = Math.Min(MaximumHeight * dpiScale, viewportSize.Height - margin * 2f);
                if (availableWidth <= 0f || availableHeight <= 0f) return false;
                float scale = Math.Min(availableWidth / imageSize.Width, availableHeight / imageSize.Height);
                float width = imageSize.Width * scale;
                float height = imageSize.Height * scale;
                var imageBounds = new RectangleF(viewportSize.Width - margin - width, margin, width, height);

                float left = Math.Max(0f, Math.Min(imageSize.Width, (float)(-origin.X / zoom)));
                float top = Math.Max(0f, Math.Min(imageSize.Height, (float)(-origin.Y / zoom)));
                float right = Math.Max(left, Math.Min(imageSize.Width, (float)((viewportSize.Width - origin.X) / zoom)));
                float bottom = Math.Max(top, Math.Min(imageSize.Height, (float)((viewportSize.Height - origin.Y) / zoom)));
                var viewportBounds = RectangleF.FromLTRB(
                    imageBounds.Left + left * scale,
                    imageBounds.Top + top * scale,
                    imageBounds.Left + right * scale,
                    imageBounds.Top + bottom * scale);
                float toggleSize = 20f * dpiScale;
                var toggleBounds = new RectangleF(imageBounds.Right - toggleSize, imageBounds.Top, toggleSize, toggleSize);
                layout = new CanvasThumbnailLayout(imageBounds, viewportBounds, toggleBounds);
                return true;
            }
        }
    }
}

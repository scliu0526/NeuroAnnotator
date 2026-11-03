# 標註介面與工作流程更新：Designer 事件稽核

## 固定控制項事件

| Form／UserControl | Designer 可見控制項事件 |
|---|---|
| `MainForm` | 固定選單、匯入／刪除／標註工具與匯出按鈕、比例／透明度欄位，以及 FormClosing、KeyDown、KeyUp；接至具名處理函式。 |
| `DatasetAssignmentControl` | 資料集比例欄位、篩選欄位、虛擬清單、手動分配／Retest／切分按鈕與固定右鍵選單項目；接至具名處理函式。 |
| `ImageBrowserControl` | 搜尋欄及虛擬清單的搜尋、列取、選取及欄位排序事件；接至具名處理函式。 |
| `ImportQueueForm` | 工作清單選取、取消及清除已完成按鈕；接至具名處理函式。 |
| `ImportJobCardControl` | 展開詳細資料、取消及查看匯入圖片按鈕；接至具名處理函式。卡片為執行時動態建立的 UserControl。 |
| `ImportProgressForm` | 取消按鈕；接至具名處理函式。 |

## 保留執行期接線的自訂事件

- `ImageCanvasControl` 的 OBD／SEG 幾何建立、修改與刪除事件由 `MainForm` 連接；這些是畫布自訂事件，不列在一般控制項的 Designer 事件頁。
- `DatasetAssignmentControl` 對外的分配、Retest、類別清除及切分請求，以及 `ImageBrowserControl.ImageSelected`，由 `MainForm` 連接；這些是 UserControl 自訂事件。
- 動態建立的匯入工作卡片與類別按鈕，其資料請求及選取接線留在建立該動態控制項的程式碼中。

## 驗證紀錄

- Debug x64 WinForms 建置成功。
- Release x64 WinForms 建置成功。
- MSTest：Debug 與 Release 均為 243 項通過。
- Visual Studio Designer 開啟 `MainForm` 與 `DatasetAssignmentControl`，主要版面可見；`MainForm` 的「匯入圖片」按鈕事件頁顯示 `Click → ImportImagesButton_Click`。
- 本次 Visual Studio 2022 重載後，`DatasetAssignmentControl` 版面可見；「切分所選」按鈕的事件頁顯示 `Click → SplitSelectedButton_Click`，雙擊後跳至對應程式碼。
- `ImageBrowserControl` Designer 版面可見；選取 `list` 後，事件頁顯示 `SelectedIndexChanged → List_SelectedIndexChanged`，雙擊事件值可跳至對應程式碼。
- 執行期畫面曾在 1920×1080、96 DPI 確認；1366×768 與 150%／200% DPI 的桌面驗收仍待完成。缺少上述驗收前，Issue #115 維持開啟、PR 維持 Draft。

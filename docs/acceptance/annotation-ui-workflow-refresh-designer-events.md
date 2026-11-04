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
- Visual Studio Designer 稽核發現 `ImportQueueForm` 的 `Columns.AddRange(new[] { ... })` 無法解析；改為明確的 `ColumnHeader[]` 後，`ImportQueueForm`、`ImportJobCardControl` 與 `ImportProgressForm` 均可正常開啟 Designer。另以回歸測試禁止 Designer 檔案再次使用該隱含陣列語法。
- 2026-11-03 的流暢度修正 `31e7f2a` 將繪製預覽改為局部重繪、略過裁切區外的既有標註，並避免類別與工具控制項在狀態刷新時整批重建；最新 Release x64 建置、237 項非手動測試及 9 項手動效能門檻測試均通過，GitHub Actions `build-test-package` 通過。
- 加入 `ImportQueueForm` Designer 回歸測試後，最新 Release x64 建置、238 項非手動測試及 9 項手動效能門檻測試均通過。
- `ImportJobCardControl` 在 Designer 設計時提供 sample source/status/progress 與操作按鈕內容，避免動態工作卡片在設計介面呈現為空白；執行時仍由 `UpdateJob` 更新實際工作資料。
- 在隔離暫存專案以 1920×1080、96 DPI 實際切換長名稱類別並完成 SEG 筆刷標註；類別列無殘影、錯位或自身捲軸，筆刷完成後標註數量由 0 更新為 1，未出現 `CreateHandle`／`Dispose` 例外。驗收後已移除暫存專案。
- 在 100% DPI 下將視窗縮至 1266×768（比 1366×768 更窄且同高度）後，標註工具、類別列、長名稱省略、上方控制項及中央畫布均完整可用，工具與類別區沒有自身捲軸；完成 SEG 筆刷標註後，類別與標註數量由 0 更新為 1，未見版面殘影或例外。驗收後已移除暫存專案。
- 150%／200% DPI 的桌面驗收仍待完成。缺少上述驗收前，Issue #115 維持開啟、PR 維持 Draft。

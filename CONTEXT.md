# Neurocle 領域脈絡

## 產品目的

Neurocle 是完全離線的 Windows 桌面圖片標記工具。它管理同一批圖片，以及可獨立切換、命名、編輯與匯出的 CLA、OBD、SEG 標註集；第一版不包含模型訓練、推論或自動標記。

## 核心詞彙

| 詞彙 | 定義 |
|---|---|
| 專案（Project） | 一個 SQLite 專案資料庫及其受管理圖片目錄。 |
| 圖片（Image） | 匯入專案且具有不可變內部識別碼、來源內容雜湊、來源檔名、顯示檔名與目前顯示序號的圖片。 |
| 來源檔名 | 圖片匯入時所選檔案的名稱；轉檔後仍用於辨識原始來源及重新連結。 |
| 顯示檔名 | 圖片在清單及標註 JSON 中使用的名稱；新匯入 BMP 轉成 PNG 後使用唯一的 `.png` 名稱。 |
| 標註集（Annotation Set） | 對共享圖片池的一份獨立標記成果；具有不可變識別碼、使用者名稱及 CLA、OBD、SEG 其中一種型別。名稱可重複。 |
| 類別（Class） | 隸屬單一標註集的標籤名稱與顏色。同一標註集內名稱區分大小寫且不可完全重複。 |
| 資料集狀態（Dataset Assignment） | 圖片在單一標註集中的 `train`、`test`、未分配或 `not_used` 狀態。`test` 同時作為驗證集。 |
| Retest | 與資料集狀態獨立的 0/1 旗標。 |
| CLA 標註 | 一張圖片在單一 CLA 標註集中的零或一個類別。 |
| OBD 物件 | 一個帶類別的軸對齊矩形框。 |
| SEG 區域 | 一個帶類別、可編輯且可有洞的填色區域；同類別由新繪製造成面積重疊時合併，既有物件經變形造成重疊時保持獨立。 |
| 標註複本 | 在同一標註集內，依 OBD 物件或 SEG 區域的既有幾何與類別建立、具有新身分的獨立物件。 |
| 標註數量 | 一張圖片在單一 OBD 或 SEG 標註集中，目前實際存在的 OBD 物件或 SEG 區域總數；不計操作次數、類別數或面積。 |
| 空洞橋接 | SEG 區域以單一路徑表示外圈與洞時，連接內外輪廓且往返重合的路徑段；它不增加填色面積。 |
| 畫布視角（Canvas View） | 目前圖片在畫布中的實際縮放比例與原圖左上角位置；圖片頁與標註頁共享同一視角。 |
| 可用圖片（Available Image） | 受管理圖片檔仍存在且可讀取的圖片。缺失圖片不匯出。 |
| 完成操作（Completed Operation） | 已通過有效性檢查且可形成單一 Undo/Redo 記錄的使用者動作。 |
| 專案工作（Project Job） | 對目前開啟專案提出的一次匯入或匯出要求；具有等待中、執行中、已完成、完成但有警告、失敗或已取消狀態。 |
| 匯入工作（Import Job） | 匯入圖片檔案、資料夾或標註 JSON 的專案工作。 |
| 匯出工作（Export Job） | 匯出標註 JSON、所選圖片 ZIP 或全部圖片 ZIP 的專案工作。 |
| 專案工作佇列（Project Job Queue） | 同一專案及程式工作階段內，依全域加入順序保存並逐一執行匯入與匯出工作的集合。工作與歷史不跨程式重啟保存。 |

## 不變條件

- 圖片池由專案共享；標註、類別、資料集狀態與 Retest 均由標註集隔離。
- 每個標註集至少保留一個類別；新標註集預設包含 `class1`。
- 對外顯示序號永遠是連續的 1..N；內部關聯不得使用會因刪除而變動的序號。
- 新匯入圖片會出現在所有標註集，初始為未分配、無標註、Retest 0。
- 所有標註幾何必須位於圖片邊界內；邊界判斷以外接矩形為準。
- SEG 不同類別可以覆蓋同一像素；同類別只有「新繪製」造成面積重疊時自動聯集。
- SEG 區域純平移時，全部點位與既有空洞橋接使用相同位移，不重新建立空洞橋接。
- 標註複本即使與同類別既有 SEG 區域重疊，也維持獨立，不視為新繪製所造成的聯集。
- 永久刪除圖片會刪除該圖片在所有標註集中的資料；永久刪除標註集會刪除其全部資料。
- JSON 是交換格式，不是專案的完整持久化格式。
- 新匯入 BMP 的顯示檔名反映轉換後 PNG 格式；既有專案圖片不因新匯入規則改名。
- 同一專案的匯入與匯出工作共用單一 FIFO，一次只執行一項；佇列有活動工作時不得更換目標專案。
- 標註 JSON 匯入的目標是加入工作時選取的標註集；匯入不建立 Undo/Redo，也不修改 Retest。
- 切換至相同像素尺寸圖片時保留畫布視角；新圖片寬或高不同時使用適合視窗。

## 主要工作流程

1. 建立或開啟專案，將圖片、資料夾、標註 JSON 或匯出要求排入專案工作佇列。
2. 建立或選擇標註集與類別。
3. 依 CLA、OBD 或 SEG 工具完成標註。
4. 手動或自動分配 train、test、未分配、not_used，並視需要設定 Retest。
5. 單獨匯出所選標註集的 JSON，或單獨將圖片匯出為 ZIP。

## 系統邊界

- 執行環境：Windows 10/11 x64、.NET Framework 4.7.2 WinForms。
- 同一 Windows 登入工作階段只允許一個 Neurocle 應用程式執行個體。
- 儲存位置：本機固定磁碟；不支援網路磁碟、NAS、雲端同步目錄或可移除磁碟。
- 網路：應用程式執行期間不需要且不得主動使用網路。
- 外部交換：PNG、JPG、JPEG、BMP 圖片與指定 JSON 格式。
- 第一版範圍外：訓練、推論、自動標記、雲端同步、協作、遙測與自動更新。

## 參考文件

- 正式產品規格：`docs/specs/ai-labeling-tool.md`
- 標註介面與 SEG 增量規格：`docs/specs/annotation-ui-seg-performance.md`
- 圖片匯入與標註操作增量規格：`docs/specs/annotation-workflow-import-update.md`
- 畫布互動、標註複製與集體旋轉增量規格：`docs/specs/annotation-canvas-interactions.md`
- 標註介面與匯入工作流程增量規格：`docs/specs/annotation-ui-workflow-refresh.md`
- 標註工作流程、專案工作佇列與畫布互動增量規格：`docs/specs/annotation-workflow-queues-and-canvas-fixes.md`
- 標註工作流程安全與效能增量規格：`docs/specs/annotation-workflow-performance-safety.md`
- 實作順序：`docs/plans/implementation-plan.md`
- 標註介面與 SEG 增量計畫：`docs/plans/annotation-ui-seg-performance.md`
- 圖片匯入與標註操作增量計畫：`docs/plans/annotation-workflow-import-update.md`
- 畫布互動、標註複製與集體旋轉增量計畫：`docs/plans/annotation-canvas-interactions.md`
- 標註介面與匯入工作流程增量計畫：`docs/plans/annotation-ui-workflow-refresh.md`
- 標註工作流程、專案工作佇列與畫布互動增量計畫：`docs/plans/annotation-workflow-queues-and-canvas-fixes.md`
- 標註工作流程安全與效能增量計畫：`docs/plans/annotation-workflow-performance-safety.md`
- 測試與驗收：`docs/plans/test-and-acceptance-plan.md`
- 已知風險：`docs/plans/known-risks.md`
- 架構決策：`docs/adr/`

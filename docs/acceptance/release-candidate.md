# Release Candidate 驗收紀錄

此文件是 Neurocle 可攜式 Release Candidate 的可重跑驗收程序與證據紀錄。驗收不得使用 `images/` 或 `labels/` 的完整資料集；請以 `tools/New-AcceptanceAssets.ps1` 產生匿名 64×64 PNG。

## 建立素材與封裝

```powershell
& .\tools\New-AcceptanceAssets.ps1 -OutputDirectory .\artifacts\acceptance\images
& 'C:\Program Files (x86)\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\amd64\MSBuild.exe' Neurocle.sln -m:1 -p:Configuration=Release -p:Platform=x64
Compress-Archive -Path .\src\Neurocle.WinForms\bin\x64\Release\* -DestinationPath .\artifacts\Neurocle-win-x64.zip -Force
```

保留 `artifacts/` 中的 ZIP、測試輸出、此文件副本及量測原始資料；該目錄受 `.gitignore` 排除，以免誤提交本機產物。

## 每台目標電腦的檢查表

| 項目 | Windows 10 x64 | Windows 11 x64 |
|---|---:|---:|
| 從全新解壓縮目錄啟動 ZIP 內 `Neurocle.WinForms.exe` | ☐ | ☐ |
| 斷網後建立專案、匯入三張合成圖片、重新開啟 | ☐ | ☐ |
| 建立 CLA、OBD、SEG 各一組，完成最少一個標記 | ☐ | ☐ |
| 分配 train/test、設定 Retest，匯出 JSON 與圖片 ZIP | ☐ | ☐ |
| 備份管理、還原與診斷日誌功能 | ☐ | ☐ |
| 1366×768 的 100%、125%、150%、175%、200% DPI | ☐ | ☐ |
| 10,000 筆清單、5K 圖片、100 圖瀏覽與 SEG 互動效能量測 | ☐ | ☐ |

## 可攜式內容檢查

解壓後應至少存在：`Neurocle.WinForms.exe`、`Microsoft.Data.Sqlite.dll`、`SQLitePCLRaw.batteries_v2.dll`、`SQLitePCLRaw.core.dll`、`SQLitePCLRaw.provider.dynamic_cdecl.dll`、`e_sqlite3.dll` 和 `THIRD-PARTY-NOTICES.md`。程式不應包含訓練、推論、雲端、遙測或自動更新功能。

## 目前證據

- CI 在 `windows-2022` 會執行 Release x64 建置、全部非手動 MSTest、封裝及上述相依項／授權檢查。
- 2026-10-06，在指定 Windows 10 IoT Enterprise LTSC x64 電腦：Release x64 建置成功、86 個 MSTest 通過；以匿名素材建立並檢查 ZIP，必要執行相依與授權檔均存在，且從全新解壓縮目錄啟動後程序回應正常。
- 2026-10-14，在 `annotation-workflow-import-update` 分支：Release x64 建置成功、133 個非手動 MSTest 與 6 個手動效能門檻測試全數通過（包含 JSON／ZIP 往返）；匿名素材封裝的 ZIP 通過 7 個必要檔案檢查，SHA-256 為 `651F9B48B68E0248E6A670236DB49169CEABB64A8FF37DA9FBA2E4A6B3C806E1`，並從全新解壓縮目錄完成啟動與回應 smoke test。
- Windows 10 與 Windows 11 實機、DPI、完整斷網操作及硬體效能項目必須在各自目標電腦逐項勾選；CI 不能取代這些證據。

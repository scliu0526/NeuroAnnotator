<p align="right"><a href="README.md">English</a></p>

# Neurocle（繁體中文）

Neurocle 是一套完全離線的 Windows 桌面圖片資料集人工標記工具。它讓同一圖片池可建立並獨立管理多份分類（CLA）、物件偵測（OBD）與分割（SEG）標註集。

> 第一版刻意不包含模型訓練、推論、自動標記、雲端儲存、帳號、遙測或自動更新。

## 功能

- 將 PNG、JPG、JPEG、BMP 檔案或資料夾匯入受管理的本機專案。
- 瀏覽、搜尋、排序、預覽及處理遺失的專案圖片。
- 建立多份彼此隔離的 CLA、OBD、SEG 標註集；每份都有獨立類別、資料集分配、Retest 旗標與 Undo/Redo 歷程。
- 支援 CLA 類別指定、OBD 矩形，以及 SEG 矩形、橢圓、多邊形、筆刷、線型與局部橡皮擦。
- 將圖片設為 `train`、`test`、未分配或 `not_used`；支援分層自動切分及 Retest 管理。
- 嚴格驗證並匯入／匯出一份選取的標註集 JSON；選取圖片或所有可用圖片可獨立輸出為扁平 ZIP。
- 保存本機資料庫備份與每日診斷日誌；在檔案替換失敗時可安全還原專案。
- 可從可攜式 x64 ZIP 完全離線執行。

## 系統需求

- Windows 10 或 Windows 11 x64
- .NET Framework 4.7.2 Runtime
- 專案必須位於本機固定磁碟；程式會刻意拒絕網路、可移除、NAS 與雲端同步位置

## 建置與測試

可使用安裝 .NET Framework 4.7.2 支援的 Visual Studio 2019 以上版本開啟 `Neurocle.sln`，或在 PowerShell 執行：

```powershell
& 'C:\Program Files (x86)\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\amd64\MSBuild.exe' Neurocle.sln -m:1 -p:Configuration=Release -p:Platform=x64
& 'C:\Program Files (x86)\Microsoft Visual Studio\2019\Community\Common7\IDE\CommonExtensions\Microsoft\TestWindow\vstest.console.exe' 'tests\Neurocle.Tests\bin\x64\Release\Neurocle.Tests.dll'
```

目前自動化測試共有 86 個 MSTest。GitHub Actions 會在 `windows-2022` 執行建置、測試、封裝與可攜式執行內容檢查。

## 可攜式發佈

應用程式輸出位於 `src\Neurocle.WinForms\bin\x64\Release`。以下命令可建立可攜式壓縮檔：

```powershell
New-Item -ItemType Directory -Force -Path artifacts | Out-Null
Compress-Archive -Path .\src\Neurocle.WinForms\bin\x64\Release\* -DestinationPath .\artifacts\Neurocle-win-x64.zip -Force
```

解壓縮後直接執行 `Neurocle.WinForms.exe`。ZIP 會包含 x64 SQLite 原生程式庫與 `THIRD-PARTY-NOTICES.md`。

## 匿名驗收素材

Release 驗收時請不要使用私人資料集。可改用下列命令建立三張小型合成 PNG：

```powershell
& .\tools\New-AcceptanceAssets.ps1 -OutputDirectory .\artifacts\acceptance\images
```

完整且可重跑的檢查表請參考[Release Candidate 驗收文件](docs/acceptance/release-candidate.md)。

## 文件

- [領域脈絡](CONTEXT.md)
- [正式產品規格](docs/specs/ai-labeling-tool.md)
- [實作計畫](docs/plans/implementation-plan.md)
- [測試與驗收計畫](docs/plans/test-and-acceptance-plan.md)
- [架構決策紀錄](docs/adr/)
- [第三方授權聲明](licenses/THIRD-PARTY-NOTICES.md)

## 授權聲明

第三方執行期授權資訊請見 [licenses/THIRD-PARTY-NOTICES.md](licenses/THIRD-PARTY-NOTICES.md)，且會隨可攜式發佈包一併提供。

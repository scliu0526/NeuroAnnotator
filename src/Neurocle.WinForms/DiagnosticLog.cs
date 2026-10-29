using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace Neurocle.WinForms
{
    // 表示寫入診斷記錄時發生的失敗類型。
    public enum DiagnosticEvent
    {
        // 應用程式啟動。
        ApplicationStarted,
        // 建立專案。
        ProjectCreated,
        // 開啟專案。
        ProjectOpened,
        // 儲存專案。
        ProjectSaved,
        // 完成圖片匯入。
        ImageImportCompleted,
        // 匯入標註 JSON。
        AnnotationJsonImported,
        // 匯出標註 JSON。
        AnnotationJsonExported,
        // 匯出圖片封存檔。
        ImagesExported,
        // 專案操作失敗。
        OperationFailed
    }

    // 將診斷記錄檔操作失敗轉成可由畫面辨識的例外。
    public sealed class DiagnosticLogException : Exception
    {
        /// <summary>包裝原始記錄檔例外並分類失敗原因。</summary>
        public DiagnosticLogException(Exception innerException)
            : base(innerException.Message, innerException)
        {
            Failure = GetFailure(innerException);
        }

        // 取得檔案操作失敗的分類。
        public DiagnosticLogFailure Failure { get; }

        /// <summary>依原始例外類型分類存取、路徑或一般 IO 失敗。</summary>
        private static DiagnosticLogFailure GetFailure(Exception exception)
        {
            if (exception is UnauthorizedAccessException) return DiagnosticLogFailure.AccessDenied;
            if (exception is NotSupportedException) return DiagnosticLogFailure.UnsupportedPath;
            return DiagnosticLogFailure.Io;
        }
    }

    // 分類診斷記錄檔操作的失敗原因。
    public enum DiagnosticLogFailure
    {
        // 一般檔案輸入輸出失敗。
        Io,
        // 作業系統拒絕存取目錄或檔案。
        AccessDenied,
        // 目錄路徑或作業系統不支援此操作。
        UnsupportedPath
    }

    // 將非敏感的應用程式事件寫入本機每日診斷記錄檔。
    public sealed class DiagnosticLog
    {
        // 保存每日記錄檔所在目錄。
        private readonly string directory;
        // 提供可替換的本機時間來源，讓記錄檔日期可受控。
        private readonly Func<DateTime> localNow;

        /// <summary>建立指定目錄的診斷記錄器及本機時間來源。</summary>
        public DiagnosticLog(string directory, Func<DateTime> localNow = null)
        {
            if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("A log directory is required.", nameof(directory));
            this.directory = directory;
            this.localNow = localNow ?? (() => DateTime.Now);
        }

        // 取得診斷記錄檔所在目錄。
        public string DirectoryPath { get { return directory; } }

        /// <summary>建立使用本機應用程式資料目錄的預設診斷記錄器。</summary>
        public static DiagnosticLog CreateDefault()
        {
            return new DiagnosticLog(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Neurocle", "Logs"));
        }

        /// <summary>以 UTC 時間及事件名稱附加一行記錄，並保留檔案 IO 原因。</summary>
        public void Write(DiagnosticEvent diagnosticEvent)
        {
            DateTime now = localNow();
            string line = now.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture) + "\t" + diagnosticEvent + Environment.NewLine;
            try
            {
                System.IO.Directory.CreateDirectory(directory);
                File.AppendAllText(Path.Combine(directory, now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".log"), line, new UTF8Encoding(false));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is NotSupportedException)
            {
                throw new DiagnosticLogException(exception);
            }
        }

        /// <summary>加總記錄目錄中每日 log 檔的位元組數。</summary>
        public long GetUsageBytes()
        {
            try
            {
                if (!System.IO.Directory.Exists(directory)) return 0;
                long total = 0;
                foreach (string path in System.IO.Directory.EnumerateFiles(directory, "*.log", SearchOption.TopDirectoryOnly)) total += new FileInfo(path).Length;
                return total;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is NotSupportedException)
            {
                throw new DiagnosticLogException(exception);
            }
        }

        /// <summary>建立記錄目錄並以系統檔案總管開啟。</summary>
        public void OpenFolder()
        {
            try
            {
                System.IO.Directory.CreateDirectory(directory);
                Process.Start(new ProcessStartInfo { FileName = directory, UseShellExecute = true });
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is System.ComponentModel.Win32Exception || exception is NotSupportedException)
            {
                throw new DiagnosticLogException(exception);
            }
        }
    }
}

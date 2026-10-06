using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace Neurocle.WinForms
{
    public enum DiagnosticEvent
    {
        ApplicationStarted,
        ProjectCreated,
        ProjectOpened,
        ProjectSaved,
        ImageImportCompleted,
        AnnotationJsonImported,
        AnnotationJsonExported,
        ImagesExported,
        OperationFailed
    }

    public sealed class DiagnosticLogException : Exception
    {
        public DiagnosticLogException(Exception innerException)
            : base(innerException.Message, innerException)
        {
            Failure = GetFailure(innerException);
        }

        public DiagnosticLogFailure Failure { get; }

        private static DiagnosticLogFailure GetFailure(Exception exception)
        {
            if (exception is UnauthorizedAccessException) return DiagnosticLogFailure.AccessDenied;
            if (exception is NotSupportedException) return DiagnosticLogFailure.UnsupportedPath;
            return DiagnosticLogFailure.Io;
        }
    }

    public enum DiagnosticLogFailure
    {
        Io,
        AccessDenied,
        UnsupportedPath
    }

    public sealed class DiagnosticLog
    {
        private readonly string directory;
        private readonly Func<DateTime> localNow;

        public DiagnosticLog(string directory, Func<DateTime> localNow = null)
        {
            if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("A log directory is required.", nameof(directory));
            this.directory = directory;
            this.localNow = localNow ?? (() => DateTime.Now);
        }

        public string DirectoryPath { get { return directory; } }

        public static DiagnosticLog CreateDefault()
        {
            return new DiagnosticLog(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Neurocle", "Logs"));
        }

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

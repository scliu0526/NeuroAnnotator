using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Neurocle.WinForms;

namespace Neurocle.Tests
{
    [TestClass]
    public class DiagnosticLogTests
    {
        [TestMethod]
        public void Writes_one_permanent_file_per_local_day_without_sensitive_payloads()
        {
            string directory = CreateTemporaryDirectory();
            DateTime now = new DateTime(2026, 10, 5, 23, 59, 0, DateTimeKind.Local);
            try
            {
                var log = new DiagnosticLog(directory, () => now);
                log.Write(DiagnosticEvent.ApplicationStarted);
                now = now.AddMinutes(2);
                log.Write(DiagnosticEvent.OperationFailed);

                string first = File.ReadAllText(Path.Combine(directory, "2026-10-05.log"));
                string second = File.ReadAllText(Path.Combine(directory, "2026-10-06.log"));
                Assert.IsTrue(first.Contains("ApplicationStarted"));
                Assert.IsTrue(second.Contains("OperationFailed"));
                Assert.IsFalse(first.Contains("x="));
                Assert.IsFalse(first.Contains("image"));
                Assert.AreEqual(new FileInfo(Path.Combine(directory, "2026-10-05.log")).Length + new FileInfo(Path.Combine(directory, "2026-10-06.log")).Length, log.GetUsageBytes());
            }
            finally { Directory.Delete(directory, true); }
        }

        [TestMethod]
        public void Write_failure_exposes_the_operating_system_reason()
        {
            string filePath = Path.GetTempFileName();
            try
            {
                var log = new DiagnosticLog(filePath);
                DiagnosticLogException exception = Assert.ThrowsException<DiagnosticLogException>(() => log.Write(DiagnosticEvent.ApplicationStarted));
                Assert.IsFalse(string.IsNullOrWhiteSpace(exception.Message));
                Assert.AreEqual(DiagnosticLogFailure.Io, exception.Failure);
            }
            finally { File.Delete(filePath); }
        }

        private static string CreateTemporaryDirectory()
        {
            string path = Path.Combine(Path.GetTempPath(), "Neurocle.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }
    }
}

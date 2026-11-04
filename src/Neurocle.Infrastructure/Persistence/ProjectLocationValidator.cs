using System;
using System.IO;

namespace Neurocle.Infrastructure.Persistence
{
    // 集中驗證專案資料庫可安全使用的儲存位置。
    public static class ProjectLocationValidator
    {
        /// <summary>
        /// 確認專案路徑位於本機固定磁碟，且不在已知雲端同步目錄內。
        /// </summary>
        /// <param name="projectPath">專案資料庫檔案路徑。</param>
        /// <exception cref="ArgumentException">輸入資料不符合操作契約。</exception>
        /// <exception cref="NotSupportedException">操作遇到 NotSupportedException 所代表的錯誤狀態。</exception>
        public static void EnsureSupported(string projectPath)
        {
            if (string.IsNullOrWhiteSpace(projectPath))
            {
                throw new ArgumentException("A project path is required.", nameof(projectPath));
            }

            string fullPath = Path.GetFullPath(projectPath);
            DriveInfo drive = new DriveInfo(Path.GetPathRoot(fullPath));
            if (drive.DriveType != DriveType.Fixed)
            {
                throw new NotSupportedException("Projects must be stored on a local fixed disk.");
            }

            string oneDrive = Environment.GetEnvironmentVariable("OneDrive");
            if (!string.IsNullOrWhiteSpace(oneDrive) && IsUnder(fullPath, oneDrive))
            {
                throw new NotSupportedException("Projects cannot be stored in a cloud-synchronised directory.");
            }

            foreach (string synchronizedName in new[] { "OneDrive", "Dropbox", "Google Drive", "iCloud Drive" })
            {
                if (fullPath.IndexOf(Path.DirectorySeparatorChar + synchronizedName + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    throw new NotSupportedException("Projects cannot be stored in a cloud-synchronised directory.");
                }
            }
        }

        /// <summary>
        /// 判斷正規化後的路徑是否位於指定目錄之下。
        /// </summary>
        /// <param name="path">要讀取、檢查或轉換的路徑。</param>
        /// <param name="parent">作為範圍邊界的父目錄路徑。</param>
        /// <returns>路徑位於父目錄之下時為 <see langword="true"/>，否則為 <see langword="false"/>。</returns>
        private static bool IsUnder(string path, string parent)
        {
            string normalizedParent = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return path.StartsWith(normalizedParent, StringComparison.OrdinalIgnoreCase);
        }
    }
}

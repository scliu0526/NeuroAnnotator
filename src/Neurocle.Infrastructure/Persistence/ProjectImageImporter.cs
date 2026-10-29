using System;
using System.Drawing;
using System.IO;
using System.Security.Cryptography;
using Neurocle.Application.Projects;

namespace Neurocle.Infrastructure.Persistence
{
    // 驗證來源圖片並轉存至專案管理的圖片目錄。
    internal static class ProjectImageImporter
    {
        // 限制單一圖片的來源檔案大小。
        internal const long MaximumFileSizeBytes = 100L * 1024L * 1024L;
        // 限制圖片任一邊的像素尺寸。
        internal const int MaximumDimension = 10000;
        // 限制解碼後的總像素數，避免過大的圖片佔用過多記憶體。
        internal const long MaximumPixelCount = 100000000L;

        /// <summary>驗證來源格式與尺寸，並建立圖片資料及來源內容雜湊。</summary>
        internal static ProjectImage Read(string sourcePath, string managedFileName, int sequence)
        {
            string extension = Path.GetExtension(sourcePath);
            if (!IsSupported(extension)) throw new ImageImportException(ImageImportFailure.UnsupportedFormat);
            var info = new FileInfo(sourcePath);
            if (!info.Exists) throw new ImageImportException(ImageImportFailure.FileNotFound);
            ValidateFileSize(info.Length);
            try
            {
                using (var image = Image.FromFile(sourcePath, false))
                {
                    ValidateDimensions(image.Width, image.Height);
                    return new ProjectImage(Guid.NewGuid().ToString("D"), managedFileName, managedFileName, Hash(sourcePath), image.Width, image.Height, sequence);
                }
            }
            catch (OutOfMemoryException) { throw new ImageImportException(ImageImportFailure.InvalidImage); }
        }

        /// <summary>判斷來源副檔名是否為需要轉成 PNG 的 BMP。</summary>
        internal static bool RequiresPngConversion(string sourcePath)
        {
            return string.Equals(Path.GetExtension(sourcePath), ".bmp", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>依來源格式複製或轉換圖片至受管理目錄。</summary>
        internal static void CopyToManagedStorage(string sourcePath, string destinationPath)
        {
            if (!RequiresPngConversion(sourcePath) && !IsBitmapImage(sourcePath))
            {
                File.Copy(sourcePath, destinationPath);
                return;
            }
            // 轉檔完成前使用暫存副檔名，完成後再移至受管理檔名。
            string temporaryPath = destinationPath + ".importing";
            try
            {
                using (var image = Image.FromFile(sourcePath, false)) image.Save(temporaryPath, System.Drawing.Imaging.ImageFormat.Png);
                File.Move(temporaryPath, destinationPath);
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
        }

        /// <summary>依解碼後的圖片格式判斷來源內容是否為 BMP。</summary>
        private static bool IsBitmapImage(string sourcePath)
        {
            using (var image = Image.FromFile(sourcePath, false)) return image.RawFormat.Guid == System.Drawing.Imaging.ImageFormat.Bmp.Guid;
        }

        /// <summary>拒絕超過單檔大小上限的來源圖片。</summary>
        internal static void ValidateFileSize(long length)
        {
            if (length > MaximumFileSizeBytes) throw new ImageImportException(ImageImportFailure.FileSizeLimit);
        }

        /// <summary>拒絕超過邊長或總像素數上限的圖片尺寸。</summary>
        internal static void ValidateDimensions(int width, int height)
        {
            if (width > MaximumDimension || height > MaximumDimension || (long)width * height > MaximumPixelCount) throw new ImageImportException(ImageImportFailure.DimensionLimit);
        }

        /// <summary>判斷來源副檔名是否屬於支援的圖片格式。</summary>
        private static bool IsSupported(string extension)
        {
            return string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase) || string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase) || string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase) || string.Equals(extension, ".bmp", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>計算來源檔案內容的 SHA-256 雜湊值。</summary>
        internal static string Hash(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty);
        }
    }
}

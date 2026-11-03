using Neurocle.WinForms.Resources;

namespace Neurocle.WinForms
{
    // 將診斷記錄例外分類映射成使用者可閱讀的訊息。
    internal static class DiagnosticLogMessages
    {
        /// <summary>
        /// 依失敗類型選取對應的本地化錯誤訊息。
        /// </summary>
        /// <param name="exception">發生的例外資訊。</param>
        /// <returns>轉換或取得的文字結果。</returns>
        internal static string GetFailureMessage(DiagnosticLogException exception)
        {
            switch (exception.Failure)
            {
                case DiagnosticLogFailure.AccessDenied: return Strings.DiagnosticLogAccessDenied;
                case DiagnosticLogFailure.UnsupportedPath: return Strings.DiagnosticLogUnsupportedPath;
                default: return Strings.DiagnosticLogIoFailure;
            }
        }
    }
}

using Neurocle.WinForms.Resources;

namespace Neurocle.WinForms
{
    internal static class DiagnosticLogMessages
    {
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

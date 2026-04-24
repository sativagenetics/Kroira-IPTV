#nullable enable
using Kroira.App.Services;

namespace Kroira.App.Services.Parsing
{
    internal static class EpgDiagnosticFormatter
    {
        private static readonly ISensitiveDataRedactionService Redactor = new SensitiveDataRedactionService();

        public static string Redact(string? value)
        {
            return Redactor.RedactLooseText(value);
        }

        public static string RedactUrl(string? value)
        {
            return Redactor.RedactUrl(value);
        }

        public static string Format(string? value)
        {
            var redacted = Redact(value);
            if (string.IsNullOrWhiteSpace(redacted))
            {
                return "\"\"";
            }

            return $"\"{redacted.Replace("\"", "'")}\"";
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;

namespace CopyRecord
{
    internal static class ClipboardCapturePolicy
    {
        internal static bool ShouldIgnoreSource(AppSettings settings, string sourceApp)
        {
            if (settings == null || string.IsNullOrWhiteSpace(sourceApp)) return false;
            HashSet<string> excluded = new HashSet<string>(SplitApplications(settings.ExcludedApplications),
                StringComparer.OrdinalIgnoreCase);
            return excluded.Contains(NormalizeApplication(sourceApp));
        }

        internal static bool ShouldIgnoreText(AppSettings settings, string text)
        {
            if (settings == null || !settings.IgnoreSensitiveText || string.IsNullOrWhiteSpace(text)) return false;
            string value = text.Trim();
            if (value.IndexOf('\n') >= 0 || value.IndexOf('\r') >= 0) return false;

            string digits = new string(value.Where(char.IsDigit).ToArray());
            bool onlyDigitsAndSeparators = value.All(character =>
                char.IsDigit(character) || char.IsWhiteSpace(character) || character == '-' || character == '+');
            if (onlyDigitsAndSeparators && digits.Length >= 4 && digits.Length <= 24) return true;

            if (value.Length < 8 || value.Length > 64 || value.Any(char.IsWhiteSpace)) return false;
            int categories = 0;
            if (value.Any(char.IsLower)) categories++;
            if (value.Any(char.IsUpper)) categories++;
            if (value.Any(char.IsDigit)) categories++;
            if (value.Any(character => !char.IsLetterOrDigit(character))) categories++;
            return categories >= 3;
        }

        internal static IEnumerable<string> SplitApplications(string value)
        {
            return (value ?? string.Empty)
                .Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(NormalizeApplication)
                .Where(item => !string.IsNullOrEmpty(item))
                .Distinct(StringComparer.OrdinalIgnoreCase);
        }

        private static string NormalizeApplication(string value)
        {
            string normalized = (value ?? string.Empty).Trim();
            return normalized.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? normalized.Substring(0, normalized.Length - 4)
                : normalized;
        }
    }
}

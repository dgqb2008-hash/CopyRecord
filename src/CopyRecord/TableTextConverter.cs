using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace CopyRecord
{
    internal enum TableDelimiterMode
    {
        Auto,
        SingleColumn,
        Tab,
        Comma,
        ChineseComma,
        Semicolon,
        Pipe,
        MultipleSpaces
    }

    internal sealed class TableTextResult
    {
        internal List<string[]> Rows { get; set; }
        internal int RowCount { get { return Rows == null ? 0 : Rows.Count; } }
        internal int ColumnCount { get { return Rows == null || Rows.Count == 0 ? 0 : Rows.Max(row => row.Length); } }
        internal TableDelimiterMode DetectedMode { get; set; }
        internal string Tsv { get; set; }
        internal string Html { get; set; }
    }

    internal static class TableTextConverter
    {
        internal static TableTextResult Convert(string text, TableDelimiterMode requestedMode, bool preserveAsText)
        {
            string[] lines = SplitLines(text);
            TableDelimiterMode mode = requestedMode == TableDelimiterMode.Auto ? DetectMode(lines) : requestedMode;
            List<string[]> rows = new List<string[]>();
            foreach (string line in lines) rows.Add(ParseLine(line, mode));
            if (rows.Count == 0) rows.Add(new[] { string.Empty });
            return new TableTextResult
            {
                Rows = rows,
                DetectedMode = mode,
                Tsv = BuildTsv(rows),
                Html = BuildCfHtml(rows, preserveAsText)
            };
        }

        internal static string ModeName(TableDelimiterMode mode)
        {
            switch (mode)
            {
                case TableDelimiterMode.Tab: return "制表符";
                case TableDelimiterMode.Comma: return "逗号";
                case TableDelimiterMode.ChineseComma: return "中文逗号";
                case TableDelimiterMode.Semicolon: return "分号";
                case TableDelimiterMode.Pipe: return "竖线";
                case TableDelimiterMode.MultipleSpaces: return "连续空格";
                case TableDelimiterMode.SingleColumn: return "每行一格";
                default: return "自动识别";
            }
        }

        private static string[] SplitLines(string text)
        {
            string normalized = (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n')
                .Replace('\u2028', '\n').Replace('\u2029', '\n');
            string[] lines = normalized.Split(new[] { '\n' }, StringSplitOptions.None);
            int length = lines.Length;
            while (length > 1 && lines[length - 1].Length == 0) length--;
            if (length == lines.Length) return lines;
            string[] trimmed = new string[length];
            Array.Copy(lines, trimmed, length);
            return trimmed;
        }

        private static TableDelimiterMode DetectMode(string[] lines)
        {
            string[] nonEmpty = lines.Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
            if (nonEmpty.Length < 2) return TableDelimiterMode.SingleColumn;
            TableDelimiterMode[] candidates =
            {
                TableDelimiterMode.Tab, TableDelimiterMode.Comma, TableDelimiterMode.ChineseComma,
                TableDelimiterMode.Semicolon, TableDelimiterMode.Pipe
            };
            foreach (TableDelimiterMode candidate in candidates)
            {
                int[] counts = nonEmpty.Select(line => ParseLine(line, candidate).Length).ToArray();
                int expected = counts.GroupBy(count => count).OrderByDescending(group => group.Count())
                    .ThenByDescending(group => group.Key).Select(group => group.Key).FirstOrDefault();
                int matching = counts.Count(count => count == expected);
                if (expected > 1 && matching >= Math.Ceiling(nonEmpty.Length * 0.75)) return candidate;
            }
            return TableDelimiterMode.SingleColumn;
        }

        private static string[] ParseLine(string line, TableDelimiterMode mode)
        {
            if (mode == TableDelimiterMode.SingleColumn || mode == TableDelimiterMode.Auto) return new[] { line ?? string.Empty };
            if (mode == TableDelimiterMode.MultipleSpaces)
                return Regex.Split((line ?? string.Empty).Trim(), " {2,}");

            char delimiter = mode == TableDelimiterMode.Tab ? '\t'
                : mode == TableDelimiterMode.Comma ? ','
                : mode == TableDelimiterMode.ChineseComma ? '，'
                : mode == TableDelimiterMode.Semicolon ? ';' : '|';
            List<string> cells = new List<string>();
            StringBuilder cell = new StringBuilder();
            bool quoted = false;
            string value = line ?? string.Empty;
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                if (character == '"')
                {
                    if (quoted && index + 1 < value.Length && value[index + 1] == '"')
                    {
                        cell.Append('"');
                        index++;
                    }
                    else if (quoted) quoted = false;
                    else if (cell.Length == 0 && HasClosingQuote(value, index + 1)) quoted = true;
                    else cell.Append('"');
                }
                else if (character == delimiter && !quoted)
                {
                    cells.Add(cell.ToString());
                    cell.Clear();
                }
                else cell.Append(character);
            }
            cells.Add(cell.ToString());
            return cells.ToArray();
        }

        private static bool HasClosingQuote(string value, int startIndex)
        {
            for (int index = startIndex; index < value.Length; index++)
            {
                if (value[index] != '"') continue;
                if (index + 1 < value.Length && value[index + 1] == '"')
                {
                    index++;
                    continue;
                }
                return true;
            }
            return false;
        }

        private static string BuildTsv(IEnumerable<string[]> rows)
        {
            return string.Join("\r\n", rows.Select(row => string.Join("\t", row.Select(EscapeTsv))));
        }

        private static string EscapeTsv(string value)
        {
            string cell = value ?? string.Empty;
            if (cell.IndexOfAny(new[] { '\t', '\r', '\n', '"' }) < 0) return cell;
            return "\"" + cell.Replace("\"", "\"\"") + "\"";
        }

        private static string BuildCfHtml(IEnumerable<string[]> rows, bool preserveAsText)
        {
            StringBuilder fragment = new StringBuilder("<table border=\"1\" cellspacing=\"0\" cellpadding=\"2\">");
            foreach (string[] row in rows)
            {
                fragment.Append("<tr>");
                foreach (string value in row)
                {
                    fragment.Append(preserveAsText ? "<td style=\"mso-number-format:'\\@';\">" : "<td>");
                    fragment.Append(WebUtility.HtmlEncode(value ?? string.Empty).Replace("\r\n", "<br>").Replace("\n", "<br>"));
                    fragment.Append("</td>");
                }
                fragment.Append("</tr>");
            }
            fragment.Append("</table>");

            const string headerFormat = "Version:0.9\r\nStartHTML:{0:D10}\r\nEndHTML:{1:D10}\r\nStartFragment:{2:D10}\r\nEndFragment:{3:D10}\r\n";
            const string prefix = "<html><head><meta charset=\"utf-8\"></head><body><!--StartFragment-->";
            const string suffix = "<!--EndFragment--></body></html>";
            Encoding utf8 = Encoding.UTF8;
            string emptyHeader = string.Format(headerFormat, 0, 0, 0, 0);
            int startHtml = utf8.GetByteCount(emptyHeader);
            int startFragment = startHtml + utf8.GetByteCount(prefix);
            int endFragment = startFragment + utf8.GetByteCount(fragment.ToString());
            int endHtml = endFragment + utf8.GetByteCount(suffix);
            return string.Format(headerFormat, startHtml, endHtml, startFragment, endFragment) + prefix + fragment + suffix;
        }
    }
}

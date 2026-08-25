using System;
using System.Collections.Generic;

namespace CopyRecord
{
    internal enum PasteEntryMode
    {
        Original,
        PlainText,
        Table
    }

    internal sealed class PasteQueueEntry
    {
        internal ClipboardItem Item { get; private set; }
        internal PasteEntryMode Mode { get; private set; }
        internal string Text { get; private set; }
        internal string Html { get; private set; }

        internal string Preview
        {
            get
            {
                if (Mode == PasteEntryMode.Table) return Compact(Text, "表格");
                return Item == null ? "剪贴板项目" : Item.Preview;
            }
        }

        internal static PasteQueueEntry FromItem(ClipboardItem item, bool plainText)
        {
            if (item == null) return null;
            return new PasteQueueEntry
            {
                Item = CloneItem(item),
                Mode = plainText ? PasteEntryMode.PlainText : PasteEntryMode.Original
            };
        }

        internal static PasteQueueEntry FromTable(ClipboardItem source, TableTextResult table)
        {
            if (source == null || table == null) return null;
            return new PasteQueueEntry
            {
                Item = CloneItem(source),
                Mode = PasteEntryMode.Table,
                Text = table.Tsv,
                Html = table.Html
            };
        }

        private static ClipboardItem CloneItem(ClipboardItem item)
        {
            return new ClipboardItem
            {
                Id = item.Id,
                Text = item.Text,
                Rtf = item.Rtf,
                Html = item.Html,
                FilePathsText = item.FilePathsText,
                ContentType = item.ContentType,
                ImagePath = item.ImagePath,
                ImageHash = item.ImageHash,
                ImageWidth = item.ImageWidth,
                ImageHeight = item.ImageHeight,
                SourceApp = item.SourceApp,
                SourceTitle = item.SourceTitle,
                CreatedAt = item.CreatedAt,
                LastUsedAt = item.LastUsedAt,
                UseCount = item.UseCount,
                CopyCount = item.CopyCount,
                IsFavorite = item.IsFavorite
            };
        }

        private static string Compact(string value, string fallback)
        {
            string compact = (value ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Replace("\t", "  ").Trim();
            while (compact.Contains("  ")) compact = compact.Replace("  ", " ");
            if (string.IsNullOrEmpty(compact)) return fallback;
            return compact.Length > 60 ? compact.Substring(0, 60) + "…" : compact;
        }
    }

    internal sealed class PasteQueueSession
    {
        private readonly List<PasteQueueEntry> _entries = new List<PasteQueueEntry>();

        internal int Count { get { return _entries.Count; } }
        internal bool IsActive { get; private set; }
        internal bool IsDispatching { get; private set; }
        internal PasteQueueEntry Next { get { return _entries.Count == 0 ? null : _entries[0]; } }

        internal void Add(PasteQueueEntry entry)
        {
            if (entry != null) _entries.Add(entry);
        }

        internal string BuildCombinedText()
        {
            List<string> parts = new List<string>();
            foreach (PasteQueueEntry entry in _entries)
            {
                string text = GetEntryText(entry);
                if (!string.IsNullOrEmpty(text)) parts.Add(text);
            }
            return string.Join("\n", parts);
        }

        private static string GetEntryText(PasteQueueEntry entry)
        {
            if (entry == null) return null;
            if (entry.Mode == PasteEntryMode.Table) return entry.Text;
            ClipboardItem item = entry.Item;
            if (item == null) return null;
            if (item.IsFileList) return item.FilePathsText;
            if (item.IsImage) return null;
            return item.Text;
        }

        internal bool Start()
        {
            if (_entries.Count == 0) return false;
            IsActive = true;
            return true;
        }

        internal bool TryBeginNext(out PasteQueueEntry entry)
        {
            entry = null;
            if (!IsActive || IsDispatching || _entries.Count == 0) return false;
            IsDispatching = true;
            entry = _entries[0];
            return true;
        }

        internal void CompleteCurrent(bool success)
        {
            if (!IsDispatching) return;
            if (success && _entries.Count > 0) _entries.RemoveAt(0);
            IsDispatching = false;
            if (_entries.Count == 0) IsActive = false;
        }

        internal void Clear()
        {
            if (IsDispatching) return;
            _entries.Clear();
            IsActive = false;
        }
    }
}

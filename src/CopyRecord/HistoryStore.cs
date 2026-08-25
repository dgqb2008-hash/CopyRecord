using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media.Imaging;

namespace CopyRecord
{
    internal enum ContentTypeFilterMode
    {
        All,
        Text,
        Image,
        Files,
        Link,
        Code
    }

    [DataContract]
    internal sealed class HistoryDocument
    {
        [DataMember] public List<ClipboardItem> Items { get; set; }
        public HistoryDocument() { Items = new List<ClipboardItem>(); }
    }

    internal sealed class HistoryStore : IDisposable
    {
        private const string Columns =
            "id,text,rtf,html,file_paths,content_type,image_path,image_hash,image_width,image_height," +
            "source_app,source_title,created_at,last_used_at,use_count,copy_count,is_favorite";

        private readonly string _directory;
        private readonly string _legacyPath;
        private readonly string _imageDirectory;
        private readonly SqliteDatabase _database;
        private readonly object _gate = new object();
        private AppSettings _settings;
        private bool _hasFts;

        internal HistoryStore(AppSettings settings)
        {
            _settings = settings ?? AppSettings.Defaults();
            string overrideDirectory = Environment.GetEnvironmentVariable("COPYRECORD_DATA_DIR");
            _directory = string.IsNullOrWhiteSpace(overrideDirectory)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CopyRecord")
                : overrideDirectory;
            _legacyPath = Path.Combine(_directory, "history.xml");
            _imageDirectory = Path.Combine(_directory, "images");
            Directory.CreateDirectory(_directory);
            _database = new SqliteDatabase(Path.Combine(_directory, "copyrecord.db"));
            CreateSchema();
            ImportLegacyHistory();
            CleanupImages();
        }

        internal void ApplySettings(AppSettings settings)
        {
            lock (_gate)
            {
                _settings = settings ?? AppSettings.Defaults();
                Trim();
                CleanupImages();
            }
        }

        internal ClipboardItem AddOrRefresh(string text, string rtf, string html, string sourceApp, string sourceTitle)
        {
            lock (_gate)
            {
                ClipboardItem existing = QueryOne("SELECT " + Columns + " FROM items WHERE text=? AND content_type='Text' LIMIT 1;", text);
                DateTime now = DateTime.Now;
                if (existing != null)
                {
                    existing.CreatedAt = now;
                    existing.CopyCount++;
                    existing.SourceApp = sourceApp;
                    existing.SourceTitle = sourceTitle;
                    if (!string.IsNullOrEmpty(rtf)) existing.Rtf = rtf;
                    if (!string.IsNullOrEmpty(html)) existing.Html = html;
                    UpdateItem(existing);
                    return existing;
                }

                ClipboardItem created = new ClipboardItem
                {
                    Id = Guid.NewGuid().ToString("N"), Text = text, Rtf = rtf, Html = html,
                    ContentType = "Text", SourceApp = sourceApp, SourceTitle = sourceTitle,
                    CreatedAt = now, LastUsedAt = DateTime.MinValue, CopyCount = 1,
                    UseCount = 0, IsFavorite = false
                };
                InsertItem(created);
                Trim();
                return created;
            }
        }

        internal ClipboardItem AddOrRefreshImage(BitmapSource bitmap, string sourceApp, string sourceTitle)
        {
            if (bitmap == null) return null;
            byte[] pngBytes = EncodeOpaquePng(bitmap);
            string hash;
            using (SHA256 sha = SHA256.Create())
            {
                hash = BitConverter.ToString(sha.ComputeHash(pngBytes)).Replace("-", string.Empty).ToLowerInvariant();
            }

            ClipboardItem result;
            bool createdNew = false;
            lock (_gate)
            {
                ClipboardItem existing = QueryOne("SELECT " + Columns + " FROM items WHERE image_hash=? LIMIT 1;", hash);
                DateTime now = DateTime.Now;
                if (existing != null)
                {
                    existing.CreatedAt = now;
                    existing.CopyCount++;
                    existing.SourceApp = sourceApp;
                    existing.SourceTitle = sourceTitle;
                    UpdateItem(existing);
                    return existing;
                }

                Directory.CreateDirectory(_imageDirectory);
                string imagePath = Path.Combine(_imageDirectory, hash + ".png");
                if (!File.Exists(imagePath)) File.WriteAllBytes(imagePath, pngBytes);
                ClipboardItem created = new ClipboardItem
                {
                    Id = Guid.NewGuid().ToString("N"), ContentType = "Image",
                    ImagePath = imagePath, ImageHash = hash,
                    ImageWidth = bitmap.PixelWidth, ImageHeight = bitmap.PixelHeight,
                    SourceApp = sourceApp, SourceTitle = sourceTitle,
                    CreatedAt = now, LastUsedAt = DateTime.MinValue,
                    CopyCount = 1, UseCount = 0, IsFavorite = false
                };
                InsertItem(created);
                Trim();
                result = created;
                createdNew = true;
            }
            if (createdNew) CleanupImages();
            return result;
        }

        internal ClipboardItem AddOrRefreshFiles(IEnumerable<string> paths, string sourceApp, string sourceTitle)
        {
            string[] normalized = (paths ?? Enumerable.Empty<string>())
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => path.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (normalized.Length == 0) return null;
            string serialized = string.Join("\n", normalized);
            lock (_gate)
            {
                ClipboardItem existing = QueryOne(
                    "SELECT " + Columns + " FROM items WHERE file_paths=? AND content_type='Files' LIMIT 1;", serialized);
                DateTime now = DateTime.Now;
                if (existing != null)
                {
                    existing.CreatedAt = now;
                    existing.CopyCount++;
                    existing.SourceApp = sourceApp;
                    existing.SourceTitle = sourceTitle;
                    UpdateItem(existing);
                    return existing;
                }

                ClipboardItem created = new ClipboardItem
                {
                    Id = Guid.NewGuid().ToString("N"), ContentType = "Files", FilePathsText = serialized,
                    SourceApp = sourceApp, SourceTitle = sourceTitle, CreatedAt = now,
                    LastUsedAt = DateTime.MinValue, CopyCount = 1, UseCount = 0, IsFavorite = false
                };
                InsertItem(created);
                Trim();
                return created;
            }
        }

        internal List<ClipboardItem> Search(string query, int limit)
        {
            return Search(query, null, limit);
        }

        internal List<ClipboardItem> Search(string query, string sourceApp, int limit,
            bool favoritesOnly = false, ContentTypeFilterMode contentFilter = ContentTypeFilterMode.All)
        {
            lock (_gate)
            {
                int safeLimit = Math.Max(1, Math.Min(limit, 500));
                List<string> conditions = new List<string>();
                List<object> parameters = new List<object>();
                if (favoritesOnly) conditions.Add("is_favorite=1");
                if (!string.IsNullOrWhiteSpace(sourceApp))
                {
                    conditions.Add("source_app=? COLLATE NOCASE");
                    parameters.Add(sourceApp);
                }
                string typeClause = BuildContentTypeClause(contentFilter);
                if (typeClause != null) conditions.Add(typeClause);

                if (string.IsNullOrWhiteSpace(query))
                {
                    string where = conditions.Count > 0 ? " WHERE " + string.Join(" AND ", conditions) : string.Empty;
                    parameters.Add(safeLimit);
                    List<ClipboardItem> results = _database.Query("SELECT " + Columns + " FROM items" + where +
                        " ORDER BY created_at DESC LIMIT ?;", ReadItem, parameters.ToArray());
                    return ApplyContentFilter(results, contentFilter);
                }

                string[] words = query.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                StringBuilder fuzzy = new StringBuilder();
                for (int index = 0; index < words.Length; index++)
                {
                    if (index > 0) fuzzy.Append(" AND ");
                    fuzzy.Append("(text LIKE ? ESCAPE '\\' COLLATE NOCASE OR source_app LIKE ? ESCAPE '\\' COLLATE NOCASE OR file_paths LIKE ? ESCAPE '\\' COLLATE NOCASE");
                    string pattern = "%" + EscapeLike(words[index]) + "%";
                    parameters.Add(pattern);
                    parameters.Add(pattern);
                    parameters.Add(pattern);
                    if (IsImageWord(words[index])) fuzzy.Append(" OR content_type='Image'");
                    if (IsFileWord(words[index])) fuzzy.Append(" OR content_type='Files'");
                    fuzzy.Append(")");
                }

                string whereClause = fuzzy.ToString();
                if (_hasFts)
                {
                    whereClause = "(rowid IN (SELECT rowid FROM items_fts WHERE items_fts MATCH ?) OR (" + whereClause + "))";
                    parameters.Insert(0, BuildFtsQuery(words));
                }
                if (conditions.Count > 0)
                {
                    whereClause = string.Join(" AND ", conditions) + " AND (" + whereClause + ")";
                }
                parameters.Add(safeLimit);
                List<ClipboardItem> queryResults = _database.Query("SELECT " + Columns + " FROM items WHERE " + whereClause +
                    " ORDER BY created_at DESC LIMIT ?;", ReadItem, parameters.ToArray());
                return ApplyContentFilter(queryResults, contentFilter);
            }
        }

        private static string BuildContentTypeClause(ContentTypeFilterMode mode)
        {
            switch (mode)
            {
                case ContentTypeFilterMode.Text: return "content_type='Text'";
                case ContentTypeFilterMode.Image: return "content_type IN ('Image','Files')";
                case ContentTypeFilterMode.Files: return "content_type='Files'";
                case ContentTypeFilterMode.Link: return "content_type='Text' AND (text LIKE 'http://%' OR text LIKE 'https://%' OR text LIKE 'ftp://%' OR text LIKE 'www.%')";
                case ContentTypeFilterMode.Code: return "content_type='Text'";
                default: return null;
            }
        }

        private static List<ClipboardItem> ApplyContentFilter(List<ClipboardItem> items, ContentTypeFilterMode filter)
        {
            if (items == null) return items;
            if (filter == ContentTypeFilterMode.Code) return items.Where(IsCodeText).ToList();
            if (filter == ContentTypeFilterMode.Image) return items.Where(item => item != null && (item.IsImage || IsImageFileList(item))).ToList();
            if (filter == ContentTypeFilterMode.Files) return items.Where(item => item != null && !IsImageFileList(item)).ToList();
            return items;
        }

        private static bool IsImageFileList(ClipboardItem item)
        {
            return item != null && item.IsImageFileList;
        }

        private static bool IsCodeText(ClipboardItem item)
        {
            if (item == null) return false;
            string text = item.Text;
            if (string.IsNullOrEmpty(text)) return false;
            if (text.IndexOf('{') >= 0 && text.IndexOf('}') >= 0) return true;
            if (text.IndexOf(";\n") >= 0 || text.IndexOf(";\r") >= 0) return true;
            return text.Contains("function ") || text.Contains("class ") || text.Contains("def ") ||
                text.Contains("import ") || text.Contains("return ") || text.Contains("var ") ||
                text.Contains("let ") || text.Contains("const ") || text.Contains("public ") ||
                text.Contains("private ") || text.Contains("void ") || text.Contains("if (") ||
                text.Contains("for (") || text.Contains("while (") || text.Contains("=>") ||
                text.Contains("::") || text.Contains("#include") || text.Contains("#define") ||
                text.Contains("#pragma") || text.Contains("namespace ");
        }

        internal List<ClipboardItem> SearchInvalidFiles(int limit)
        {
            return SearchInvalidFiles(null, limit);
        }

        internal List<ClipboardItem> SearchInvalidFiles(string sourceApp, int limit)
        {
            lock (_gate)
            {
                List<ClipboardItem> files = _database.Query("SELECT " + Columns +
                    " FROM items WHERE content_type='Files' ORDER BY created_at DESC;", ReadItem);
                return files.Where(item => item.HasInvalidFilePaths &&
                    (string.IsNullOrWhiteSpace(sourceApp) || string.Equals(item.SourceApp, sourceApp, StringComparison.OrdinalIgnoreCase)))
                    .Take(Math.Max(1, limit)).ToList();
            }
        }

        internal List<string> GetSourceApplications(int limit)
        {
            lock (_gate)
            {
                return _database.Query("SELECT source_app FROM items WHERE source_app IS NOT NULL AND trim(source_app)<>'' " +
                    "GROUP BY source_app COLLATE NOCASE ORDER BY MAX(created_at) DESC LIMIT ?;",
                    statement => SqliteDatabase.ColumnText(statement, 0), Math.Max(1, Math.Min(limit, 100)));
            }
        }

        internal int RemoveInvalidFiles()
        {
            lock (_gate)
            {
                List<ClipboardItem> invalid = _database.Query("SELECT " + Columns +
                    " FROM items WHERE content_type='Files' AND is_favorite=0;", ReadItem)
                    .Where(item => item.HasInvalidFilePaths).ToList();
                foreach (ClipboardItem item in invalid) _database.Execute("DELETE FROM items WHERE id=?;", item.Id);
                return invalid.Count;
            }
        }

        internal void ReplaceFilePath(ClipboardItem item, string oldPath, string newPath)
        {
            if (item == null || !item.IsFileList || string.IsNullOrWhiteSpace(oldPath) || string.IsNullOrWhiteSpace(newPath)) return;
            lock (_gate)
            {
                string[] paths = item.FilePaths;
                for (int index = 0; index < paths.Length; index++)
                {
                    if (string.Equals(paths[index], oldPath, StringComparison.OrdinalIgnoreCase)) paths[index] = newPath;
                }
                item.FilePathsText = string.Join("\n", paths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
                UpdateItem(item);
            }
        }

        internal void MarkUsed(ClipboardItem item)
        {
            if (item == null) return;
            lock (_gate)
            {
                item.LastUsedAt = DateTime.Now;
                item.UseCount++;
                _database.Execute("UPDATE items SET last_used_at=?,use_count=use_count+1 WHERE id=?;",
                    ToStorageTime(item.LastUsedAt), item.Id);
            }
        }

        internal void ToggleFavorite(ClipboardItem item)
        {
            if (item == null) return;
            lock (_gate)
            {
                item.IsFavorite = !item.IsFavorite;
                _database.Execute("UPDATE items SET is_favorite=? WHERE id=?;", item.IsFavorite, item.Id);
            }
        }

        internal void Remove(ClipboardItem item)
        {
            if (item == null) return;
            lock (_gate)
            {
                _database.Execute("DELETE FROM items WHERE id=?;", item.Id);
                DeleteImageFileIfUnused(item.ImagePath);
            }
        }

        internal void ClearUnfavorited()
        {
            lock (_gate)
            {
                List<string> imagePaths = _database.Query(
                    "SELECT image_path FROM items WHERE is_favorite=0 AND image_path IS NOT NULL;",
                    statement => SqliteDatabase.ColumnText(statement, 0));
                _database.Execute("DELETE FROM items WHERE is_favorite=0;");
                foreach (string path in imagePaths.Distinct(StringComparer.OrdinalIgnoreCase)) DeleteImageFileIfUnused(path);
            }
        }

        private void CreateSchema()
        {
            _database.Execute("CREATE TABLE IF NOT EXISTS metadata(key TEXT PRIMARY KEY,value TEXT NOT NULL);");
            _database.Execute("CREATE TABLE IF NOT EXISTS items(" +
                "id TEXT PRIMARY KEY NOT NULL,text TEXT,rtf TEXT,html TEXT,file_paths TEXT,content_type TEXT NOT NULL," +
                "image_path TEXT,image_hash TEXT,image_width INTEGER NOT NULL DEFAULT 0,image_height INTEGER NOT NULL DEFAULT 0," +
                "source_app TEXT,source_title TEXT,created_at INTEGER NOT NULL,last_used_at INTEGER NOT NULL," +
                "use_count INTEGER NOT NULL DEFAULT 0,copy_count INTEGER NOT NULL DEFAULT 0,is_favorite INTEGER NOT NULL DEFAULT 0);");
            try { _database.Execute("ALTER TABLE items ADD COLUMN file_paths TEXT;"); }
            catch { }
            _database.Execute("CREATE UNIQUE INDEX IF NOT EXISTS ix_items_image_hash ON items(image_hash) WHERE image_hash IS NOT NULL;");
            _database.Execute("CREATE INDEX IF NOT EXISTS ix_items_recent ON items(created_at DESC);");
            _database.Execute("CREATE INDEX IF NOT EXISTS ix_items_source_app ON items(source_app);");

            try
            {
                string ftsSchema = Convert.ToString(_database.Scalar(
                    "SELECT sql FROM sqlite_master WHERE type='table' AND name='items_fts';"));
                bool existed = !string.IsNullOrEmpty(ftsSchema) && ftsSchema.IndexOf("file_paths", StringComparison.OrdinalIgnoreCase) >= 0;
                if (!existed && !string.IsNullOrEmpty(ftsSchema))
                {
                    _database.Execute("DROP TRIGGER IF EXISTS items_ai;");
                    _database.Execute("DROP TRIGGER IF EXISTS items_ad;");
                    _database.Execute("DROP TRIGGER IF EXISTS items_au;");
                    _database.Execute("DROP TABLE IF EXISTS items_fts;");
                }
                _database.Execute("CREATE VIRTUAL TABLE IF NOT EXISTS items_fts USING fts5(text,source_app,file_paths,content='items',content_rowid='rowid');");
                _database.Execute("CREATE TRIGGER IF NOT EXISTS items_ai AFTER INSERT ON items BEGIN " +
                    "INSERT INTO items_fts(rowid,text,source_app,file_paths) VALUES(new.rowid,coalesce(new.text,''),coalesce(new.source_app,''),coalesce(new.file_paths,'')); END;");
                _database.Execute("CREATE TRIGGER IF NOT EXISTS items_ad AFTER DELETE ON items BEGIN " +
                    "INSERT INTO items_fts(items_fts,rowid,text,source_app,file_paths) VALUES('delete',old.rowid,coalesce(old.text,''),coalesce(old.source_app,''),coalesce(old.file_paths,'')); END;");
                _database.Execute("CREATE TRIGGER IF NOT EXISTS items_au AFTER UPDATE OF text,source_app,file_paths ON items BEGIN " +
                    "INSERT INTO items_fts(items_fts,rowid,text,source_app,file_paths) VALUES('delete',old.rowid,coalesce(old.text,''),coalesce(old.source_app,''),coalesce(old.file_paths,'')); " +
                    "INSERT INTO items_fts(rowid,text,source_app,file_paths) VALUES(new.rowid,coalesce(new.text,''),coalesce(new.source_app,''),coalesce(new.file_paths,'')); END;");
                if (!existed) _database.Execute("INSERT INTO items_fts(items_fts) VALUES('rebuild');");
                _hasFts = true;
            }
            catch
            {
                _hasFts = false;
            }
        }

        private void ImportLegacyHistory()
        {
            object imported = _database.Scalar("SELECT value FROM metadata WHERE key='legacy_xml_imported';");
            if (imported != null || !File.Exists(_legacyPath)) return;

            List<ClipboardItem> legacyItems;
            try
            {
                DataContractSerializer serializer = new DataContractSerializer(typeof(HistoryDocument));
                using (FileStream stream = File.OpenRead(_legacyPath))
                {
                    HistoryDocument document = (HistoryDocument)serializer.ReadObject(stream);
                    legacyItems = document.Items ?? new List<ClipboardItem>();
                }
            }
            catch
            {
                return;
            }

            _database.Transaction(delegate
            {
                foreach (ClipboardItem item in legacyItems)
                {
                    if (string.IsNullOrEmpty(item.Id)) item.Id = Guid.NewGuid().ToString("N");
                    _database.Execute("INSERT OR IGNORE INTO items(" + Columns + ") VALUES(?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?);",
                        ItemValues(item));
                }
                _database.Execute("INSERT OR REPLACE INTO metadata(key,value) VALUES('legacy_xml_imported',?);", DateTime.UtcNow.ToString("o"));
            });
            Trim();

            try
            {
                string backup = Path.Combine(_directory, "history.xml.migrated-backup");
                if (!File.Exists(backup)) File.Move(_legacyPath, backup);
            }
            catch { }
        }

        private void InsertItem(ClipboardItem item)
        {
            _database.Execute("INSERT INTO items(" + Columns + ") VALUES(?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?);", ItemValues(item));
        }

        private void UpdateItem(ClipboardItem item)
        {
            object[] values = ItemValues(item).Skip(1).Concat(new object[] { item.Id }).ToArray();
            _database.Execute("UPDATE items SET text=?,rtf=?,html=?,file_paths=?,content_type=?,image_path=?,image_hash=?," +
                "image_width=?,image_height=?,source_app=?,source_title=?,created_at=?,last_used_at=?," +
                "use_count=?,copy_count=?,is_favorite=? WHERE id=?;", values);
        }

        private ClipboardItem QueryOne(string sql, params object[] values)
        {
            List<ClipboardItem> items = _database.Query(sql, ReadItem, values);
            return items.Count == 0 ? null : items[0];
        }

        private void Trim()
        {
            long count = Convert.ToInt64(_database.Scalar("SELECT COUNT(*) FROM items;"));
            long excess = count - _settings.MaximumItems;
            if (excess <= 0) return;
            List<ClipboardItem> removable = _database.Query(
                "SELECT " + Columns + " FROM items WHERE is_favorite=0 ORDER BY created_at LIMIT ?;",
                ReadItem, excess);
            foreach (ClipboardItem item in removable)
            {
                _database.Execute("DELETE FROM items WHERE id=?;", item.Id);
                DeleteImageFileIfUnused(item.ImagePath);
            }
        }

        internal void CleanupImages()
        {
            lock (_gate)
            {
                List<ClipboardItem> images = _database.Query(
                    "SELECT " + Columns + " FROM items WHERE content_type='Image' AND is_favorite=0 ORDER BY created_at ASC;", ReadItem);
                DateTime cutoff = _settings.ImageRetentionDays <= 0
                    ? DateTime.MinValue : DateTime.Now.AddDays(-_settings.ImageRetentionDays);
                foreach (ClipboardItem item in images.Where(item => item.CreatedAt < cutoff).ToList())
                {
                    _database.Execute("DELETE FROM items WHERE id=?;", item.Id);
                    DeleteImageFileIfUnused(item.ImagePath);
                    images.Remove(item);
                }

                long maximumBytes = (long)_settings.ImageMaximumMegabytes * 1024L * 1024L;
                long totalBytes = images.Sum(item => SafeFileLength(item.ImagePath));
                foreach (ClipboardItem item in images)
                {
                    if (totalBytes <= maximumBytes) break;
                    long length = SafeFileLength(item.ImagePath);
                    _database.Execute("DELETE FROM items WHERE id=?;", item.Id);
                    DeleteImageFileIfUnused(item.ImagePath);
                    totalBytes -= length;
                }

                if (Directory.Exists(_imageDirectory))
                {
                    HashSet<string> referenced = new HashSet<string>(_database.Query(
                        "SELECT image_path FROM items WHERE image_path IS NOT NULL;",
                        statement => SqliteDatabase.ColumnText(statement, 0)), StringComparer.OrdinalIgnoreCase);
                    foreach (string path in Directory.GetFiles(_imageDirectory, "*.png"))
                    {
                        if (referenced.Contains(path)) continue;
                        try { File.Delete(path); }
                        catch { }
                    }
                }
            }
        }

        private void DeleteImageFileIfUnused(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            long count = Convert.ToInt64(_database.Scalar("SELECT COUNT(*) FROM items WHERE image_path=?;", path));
            if (count == 0)
            {
                try { File.Delete(path); }
                catch { }
            }
        }

        private static byte[] EncodeOpaquePng(BitmapSource bitmap)
        {
            FormatConvertedBitmap opaque = new FormatConvertedBitmap();
            opaque.BeginInit();
            opaque.Source = bitmap;
            opaque.DestinationFormat = System.Windows.Media.PixelFormats.Bgr32;
            opaque.EndInit();
            opaque.Freeze();
            PngBitmapEncoder encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(opaque));
            using (MemoryStream stream = new MemoryStream())
            {
                encoder.Save(stream);
                return stream.ToArray();
            }
        }

        private static long SafeFileLength(string path)
        {
            try { return string.IsNullOrEmpty(path) || !File.Exists(path) ? 0 : new FileInfo(path).Length; }
            catch { return 0; }
        }

        private static object[] ItemValues(ClipboardItem item)
        {
            return new object[]
            {
                item.Id, item.Text, item.Rtf, item.Html, item.FilePathsText, item.ContentType ?? (item.IsImage ? "Image" : "Text"),
                item.ImagePath, item.ImageHash, item.ImageWidth, item.ImageHeight, item.SourceApp, item.SourceTitle,
                ToStorageTime(item.CreatedAt), ToStorageTime(item.LastUsedAt), item.UseCount, item.CopyCount, item.IsFavorite
            };
        }

        private static ClipboardItem ReadItem(IntPtr statement)
        {
            return new ClipboardItem
            {
                Id = SqliteDatabase.ColumnText(statement, 0), Text = SqliteDatabase.ColumnText(statement, 1),
                Rtf = SqliteDatabase.ColumnText(statement, 2), Html = SqliteDatabase.ColumnText(statement, 3),
                FilePathsText = SqliteDatabase.ColumnText(statement, 4), ContentType = SqliteDatabase.ColumnText(statement, 5),
                ImagePath = SqliteDatabase.ColumnText(statement, 6), ImageHash = SqliteDatabase.ColumnText(statement, 7),
                ImageWidth = SqliteDatabase.ColumnInt(statement, 8), ImageHeight = SqliteDatabase.ColumnInt(statement, 9),
                SourceApp = SqliteDatabase.ColumnText(statement, 10), SourceTitle = SqliteDatabase.ColumnText(statement, 11),
                CreatedAt = FromStorageTime(SqliteDatabase.ColumnInt64(statement, 12)), LastUsedAt = FromStorageTime(SqliteDatabase.ColumnInt64(statement, 13)),
                UseCount = SqliteDatabase.ColumnInt(statement, 14), CopyCount = SqliteDatabase.ColumnInt(statement, 15),
                IsFavorite = SqliteDatabase.ColumnInt(statement, 16) != 0
            };
        }

        private static long ToStorageTime(DateTime value) { return value.ToBinary(); }
        private static DateTime FromStorageTime(long value)
        {
            try { return DateTime.FromBinary(value); }
            catch { return DateTime.MinValue; }
        }

        private static string EscapeLike(string value)
        {
            return value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
        }

        private static bool IsImageWord(string word)
        {
            string value = word.ToLowerInvariant();
            return value == "图片" || value == "截图" || value == "image" || value == "screenshot";
        }

        private static bool IsFileWord(string word)
        {
            string value = word.ToLowerInvariant();
            return value == "文件" || value == "文件夹" || value == "file" || value == "folder";
        }

        private static string BuildFtsQuery(IEnumerable<string> words)
        {
            return string.Join(" AND ", words.Select(word => "\"" + word.Replace("\"", "\"\"") + "\"*"));
        }

        public void Dispose() { _database.Dispose(); }
    }
}

using System;
using System.IO;
using System.Runtime.Serialization;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Media;

namespace CopyRecord
{
    [DataContract]
    public sealed class ClipboardItem
    {
        [DataMember] public string Id { get; set; }
        [DataMember] public string Text { get; set; }
        [DataMember] public string Rtf { get; set; }
        [DataMember] public string Html { get; set; }
        [DataMember] public string FilePathsText { get; set; }
        [DataMember] public string ContentType { get; set; }
        [DataMember] public string ImagePath { get; set; }
        [DataMember] public string ImageHash { get; set; }
        [DataMember] public int ImageWidth { get; set; }
        [DataMember] public int ImageHeight { get; set; }
        [DataMember] public string SourceApp { get; set; }
        [DataMember] public string SourceTitle { get; set; }
        [DataMember] public DateTime CreatedAt { get; set; }
        [DataMember] public DateTime LastUsedAt { get; set; }
        [DataMember] public int UseCount { get; set; }
        [DataMember] public int CopyCount { get; set; }
        [DataMember] public bool IsFavorite { get; set; }
        private BitmapSource _thumbnail;
        private Brush _thumbnailBrush;

        public bool IsImage
        {
            get { return string.Equals(ContentType, "Image", StringComparison.OrdinalIgnoreCase) || !string.IsNullOrEmpty(ImagePath); }
        }

        public bool IsFileList
        {
            get { return string.Equals(ContentType, "Files", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(FilePathsText); }
        }

        public string[] FilePaths
        {
            get { return string.IsNullOrEmpty(FilePathsText) ? new string[0] : FilePathsText.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries); }
        }

        public bool IsImageFileList
        {
            get { return IsFileList && FilePaths.Length > 0 && Array.TrueForAll(FilePaths, IsImageFilePath); }
        }

        public static bool IsImageFilePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            string extension = Path.GetExtension(path.Trim());
            if (string.IsNullOrEmpty(extension)) return false;
            switch (extension.ToLowerInvariant())
            {
                case ".png":
                case ".jpg":
                case ".jpeg":
                case ".gif":
                case ".bmp":
                case ".tif":
                case ".tiff":
                case ".webp":
                case ".ico":
                case ".svg":
                case ".heic":
                case ".heif":
                case ".jfif":
                case ".avif":
                    return true;
                default:
                    return false;
            }
        }

        public bool HasInvalidFilePaths
        {
            get { return IsFileList && Array.Exists(FilePaths, path => !File.Exists(path) && !Directory.Exists(path)); }
        }

        public bool HasAnyExistingFilePath
        {
            get { return IsFileList && Array.Exists(FilePaths, path => File.Exists(path) || Directory.Exists(path)); }
        }

        public BitmapSource Thumbnail
        {
            get
            {
                if (!IsImage || string.IsNullOrEmpty(ImagePath) || !File.Exists(ImagePath)) return null;
                if (_thumbnail != null) return _thumbnail;
                try
                {
                    byte[] bytes = File.ReadAllBytes(ImagePath);
                    using (MemoryStream stream = new MemoryStream(bytes))
                    {
                        BitmapDecoder decoder = BitmapDecoder.Create(stream,
                            BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                        BitmapSource decoded = decoder.Frames[0];
                        FormatConvertedBitmap compatible = new FormatConvertedBitmap();
                        compatible.BeginInit();
                        compatible.Source = decoded;
                        // QQ/WeChat bitmap data can contain valid RGB with an all-zero alpha channel.
                        // Bgr32 deliberately ignores that broken alpha so screenshots remain visible.
                        compatible.DestinationFormat = System.Windows.Media.PixelFormats.Bgr32;
                        compatible.EndInit();
                        compatible.Freeze();
                        BitmapSource source = compatible;
                        double scale = source.PixelWidth > 520 ? 520.0 / source.PixelWidth : 1.0;
                        if (scale < 1.0)
                        {
                            TransformedBitmap resized = new TransformedBitmap(source,
                                new System.Windows.Media.ScaleTransform(scale, scale));
                            resized.Freeze();
                            _thumbnail = resized;
                        }
                        else
                        {
                            source.Freeze();
                            _thumbnail = source;
                        }
                    }
                    return _thumbnail;
                }
                catch { return null; }
            }
        }

        public Thickness ContentMargin
        {
            get { return IsImage ? new Thickness(84, 0, 0, 0) : new Thickness(0); }
        }

        public Brush ThumbnailBrush
        {
            get
            {
                if (_thumbnailBrush != null) return _thumbnailBrush;
                BitmapSource image = Thumbnail;
                if (image == null) return Brushes.Transparent;
                ImageBrush brush = new ImageBrush(image)
                {
                    Stretch = Stretch.Uniform,
                    AlignmentX = AlignmentX.Center,
                    AlignmentY = AlignmentY.Center
                };
                brush.Freeze();
                _thumbnailBrush = brush;
                return _thumbnailBrush;
            }
        }

        public string Preview
        {
            get
            {
                if (IsImage) return "图片  " + ImageWidth + " × " + ImageHeight;
                if (IsFileList)
                {
                    string[] paths = FilePaths;
                    string name = paths.Length == 0 ? "文件" : Path.GetFileName(paths[0].TrimEnd(Path.DirectorySeparatorChar));
                    return paths.Length > 1 ? name + "  等 " + paths.Length + " 个项目" : name;
                }
                string value = (Text ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Replace("\t", " ").Trim();
                while (value.Contains("  ")) value = value.Replace("  ", " ");
                return value.Length > 120 ? value.Substring(0, 120) + "…" : value;
            }
        }

        public string DisplayPreview
        {
            get { return Preview; }
        }

        public string Detail
        {
            get
            {
                string app = string.IsNullOrWhiteSpace(SourceApp) ? "未知应用" : SourceApp;
                TimeSpan age = DateTime.Now - CreatedAt;
                string time;
                if (age.TotalMinutes < 1) time = "刚刚";
                else if (age.TotalHours < 1) time = ((int)age.TotalMinutes).ToString() + " 分钟前";
                else if (age.TotalDays < 1) time = ((int)age.TotalHours).ToString() + " 小时前";
                else time = CreatedAt.ToString("MM-dd HH:mm");
                return app + "  ·  " + time;
            }
        }
    }
}

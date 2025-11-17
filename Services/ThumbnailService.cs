using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WpfIndexer.Services
{
    public static class ThumbnailService
    {
        // Thumbnail cache
        private static readonly ConcurrentDictionary<string, ImageSource?> _imageCache = new();
        private static readonly ConcurrentDictionary<string, string> _videoCache = new();
        private static readonly ConcurrentDictionary<string, string> _htmlCache = new();

        // -----------------------------
        // 1) IMAGE THUMBNAIL
        // -----------------------------
        public static async Task<ImageSource?> GetThumbnailAsync(string imagePath)
        {
            if (_imageCache.TryGetValue(imagePath, out var cached))
                return cached;

            if (!File.Exists(imagePath))
                return null;

            return _imageCache[imagePath] = await Task.Run(() =>
            {
                try
                {
                    BitmapImage bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.DecodePixelWidth = 200;
                    bmp.UriSource = new Uri(imagePath);
                    bmp.EndInit();
                    bmp.Freeze();
                    return (ImageSource)bmp;
                }
                catch
                {
                    return null;
                }
            });
        }

        // -----------------------------
        // 2) VIDEO THUMBNAIL
        // -----------------------------
        public static void SetVideoThumbnail(string videoPath, string thumbnailPath)
        {
            if (!string.IsNullOrEmpty(videoPath) &&
                !string.IsNullOrEmpty(thumbnailPath))
            {
                _videoCache[videoPath] = thumbnailPath;
            }
        }

        public static string? GetVideoThumbnail(string videoPath)
        {
            if (_videoCache.TryGetValue(videoPath, out var thumb))
                return thumb;
            return null;
        }

        // -----------------------------
        // 3) HTML PREVIEW CACHE
        // -----------------------------
        public static void SetHtmlPreviewPath(string sourcePath, string htmlPath)
        {
            if (!string.IsNullOrEmpty(sourcePath) &&
                !string.IsNullOrEmpty(htmlPath))
            {
                _htmlCache[sourcePath] = htmlPath;
            }
        }

        public static string? GetCachedHtmlPreview(string sourcePath)
        {
            if (_htmlCache.TryGetValue(sourcePath, out var html))
                return html;
            return null;
        }
    }
}

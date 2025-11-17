using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace WpfIndexer.Helpers
{
    public static class ThumbnailCache
    {
        private static readonly ConcurrentDictionary<string, BitmapImage?> _imageCache = new();
        private static readonly ConcurrentDictionary<string, string?> _htmlCache = new();
        private static readonly ConcurrentDictionary<string, string?> _videoThumbCache = new();

        private static DateTime _lastCleanup = DateTime.Now;

        // Her dosya için max 128px thumbnail üretilir
        public static async Task<BitmapImage?> GetImageThumbnailAsync(string path)
        {
            if (_imageCache.TryGetValue(path, out var cached))
                return cached;

            if (!File.Exists(path))
                return null;

            return _imageCache[path] = await Task.Run(() =>
            {
                try
                {
                    BitmapImage bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(path);
                    bmp.DecodePixelWidth = 128; // Küçük resim
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    bmp.Freeze(); // UI thread dışı kullanım için
                    return bmp;
                }
                catch
                {
                    return null;
                }
            });
        }

        public static string? GetHtmlPreviewPath(string path)
        {
            _htmlCache.TryGetValue(path, out var val);
            return val;
        }

        public static void SetHtmlPreviewPath(string path, string htmlPath)
        {
            _htmlCache[path] = htmlPath;
        }

        public static string? GetVideoThumbnail(string path)
        {
            _videoThumbCache.TryGetValue(path, out var val);
            return val;
        }

        public static void SetVideoThumbnail(string path, string thumb)
        {
            _videoThumbCache[path] = thumb;
        }

        // Gereksiz cache büyümesini engellemek için 10dk’da bir temizleme
        public static void Cleanup()
        {
            if ((DateTime.Now - _lastCleanup).TotalMinutes < 10)
                return;

            _imageCache.Clear();
            _htmlCache.Clear();
            _videoThumbCache.Clear();

            _lastCleanup = DateTime.Now;
        }
    }
}

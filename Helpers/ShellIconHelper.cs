using System;
using System.Collections.Concurrent; // Hızlı ve güvenli önbellek (cache) için
using System.Runtime.InteropServices; // [DllImport] ve P/Invoke için
using System.Windows;                 // Int32Rect için
using System.Windows.Interop;         // HwndSource, Imaging için
using System.Windows.Media;           // ImageSource için
using System.Windows.Media.Imaging;   // BitmapSource için

namespace WpfIndexer.Helpers
{
    /// <summary>
    /// Windows Shell API (SHGetFileInfo) kullanarak dosya ikonlarını ve
    /// dosya türü adlarını ("Metin Belgesi" gibi) getiren yardımcı sınıf.
    /// Sonuçları yüksek performans için önbelleğe alır.
    /// </summary>
    public static class ShellIconHelper
    {
        // 1. ÖNBELLEK (CACHE) ALANLARI
        // Uzantıdan (örn: ".pdf") -> İkona (ImageSource) giden önbellek.
        private static readonly ConcurrentDictionary<string, ImageSource?> _iconCache = new();

        // Uzantıdan (örn: ".pdf") -> Tür Adına (örn: "Adobe PDF Belgesi") giden önbellek.
        private static readonly ConcurrentDictionary<string, string> _typeNameCache = new();

        // Boş veya bilinmeyen dosya türleri için varsayılan bir değer
        private const string UnknownFileType = "Dosya";


        // 2. PUBLIC METOTLAR (ViewModel buradan çağıracak)

        /// <summary>
        /// Verilen dosya uzantısı için sistem varsayılan ikonunu alır.
        /// Sonuçları önbellekten getirir veya Windows API'den alıp önbelleğe alır.
        /// </summary>
        /// <param name="extension">".txt", ".pdf" gibi dosya uzantısı</param>
        /// <returns>WPF uyumlu bir ImageSource (ikon)</returns>
        public static ImageSource? GetFileIcon(string extension)
        {
            // Eğer uzantı null veya boşsa, uğraşma
            if (string.IsNullOrWhiteSpace(extension))
            {
                return null;
            }

            // Önbellekten getirmeyi dene. Bulursa, hemen döndür.
            return _iconCache.GetOrAdd(extension, (ext) =>
            {
                // Önbellekte yoksa: Windows API'den çek ve önbelleğe ekle
                return GetIconFromFile(ext);
            });
        }

        /// <summary>
        /// Verilen dosya uzantısı için sistem görünen tür adını alır (örn: "Metin Belgesi").
        /// Sonuçları önbellekten getirir veya Windows API'den alıp önbelleğe alır.
        /// </summary>
        /// <param name="extension">".txt", ".pdf" gibi dosya uzantısı</param>
        /// <returns>Görünen dosya türü adı</returns>
        public static string GetFileTypeName(string extension)
        {
            if (string.IsNullOrWhiteSpace(extension))
            {
                return UnknownFileType;
            }

            // Önbellekten getirmeyi dene.
            return _typeNameCache.GetOrAdd(extension, (ext) =>
            {
                // Önbellekte yoksa: Windows API'den çek ve önbelleğe ekle
                SHFILEINFO sfi = new SHFILEINFO();
                // SHGFI_TYPENAME: Sadece tür adını istiyoruz.
                // SHGFI_USEFILEATTRIBUTES: Diske gerçekten gitme, sadece uzantıya göre bilgi ver. (HIZLI)
                uint flags = SHGFI_TYPENAME | SHGFI_USEFILEATTRIBUTES;

                if (SHGetFileInfo(ext, FILE_ATTRIBUTE_NORMAL, ref sfi, (uint)Marshal.SizeOf(sfi), flags) != IntPtr.Zero)
                {
                    // Başarılıysa ve boş değilse döndür
                    if (!string.IsNullOrWhiteSpace(sfi.szTypeName))
                    {
                        return sfi.szTypeName;
                    }
                }

                // Başarısızsa veya boşsa, uzantının kendisini büyük harfle döndür (örn: "PDF")
                return extension.TrimStart('.').ToUpperInvariant();
            });
        }


        // 3. ÖZEL YARDIMCI METOT (İkonu çeken asıl iş)

        /// <summary>
        /// Windows API'yi çağırarak bir uzantının küçük (small) ikonunu çeker.
        /// </summary>
        private static ImageSource? GetIconFromFile(string extension)
        {
            SHFILEINFO sfi = new SHFILEINFO();

            // SHGFI_ICON: İkon istiyoruz.
            // SHGFI_SMALLICON: Küçük ikon (16x16) istiyoruz.
            // SHGFI_USEFILEATTRIBUTES: Diske gitme, uzantıya bak.
            uint flags = SHGFI_ICON | SHGFI_SMALLICON | SHGFI_USEFILEATTRIBUTES;

            var result = SHGetFileInfo(extension, FILE_ATTRIBUTE_NORMAL, ref sfi, (uint)Marshal.SizeOf(sfi), flags);

            if (result != IntPtr.Zero && sfi.hIcon != IntPtr.Zero)
            {
                // Windows'tan bir "ikon handle" (IntPtr) aldık.
                // Bunu WPF'in anlayacağı 'ImageSource'a çevirmeliyiz.
                ImageSource? imgSource = ConvertIconToImageSource(sfi.hIcon);
                return imgSource;
            }

            return null; // İkon bulunamadı
        }

        /// <summary>
        /// Windows GDI/User32 (IntPtr) ikonunu, WPF (ImageSource) ikonuna çevirir.
        /// Çevirdikten sonra GDI kaynağını serbest bırakarak hafıza sızıntısını önler.
        /// </summary>
        private static ImageSource? ConvertIconToImageSource(IntPtr hIcon)
        {
            try
            {
                // IntPtr'ı WPF'in kullanabileceği bir BitmapSource'a dönüştür.
                ImageSource? imgSource = Imaging.CreateBitmapSourceFromHIcon(
                    hIcon,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());

                imgSource?.Freeze(); // Performans için: Bu nesne bir daha değişmeyecek.
                return imgSource;
            }
            finally
            {
                // ÇOK ÖNEMLİ:
                // Windows'tan aldığımız ikon kaynağını serbest bırakmalıyız,
                // yoksa program hafıza sızdırır (memory leak).
                DestroyIcon(hIcon);
            }
        }


        // 4. WINDOWS API TANIMLAMALARI (P/Invoke)

        // SHGetFileInfo fonksiyonunu shell32.dll'den import ediyoruz
        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SHGetFileInfo(
            string pszPath,
            uint dwFileAttributes,
            ref SHFILEINFO psfi,
            uint cbFileInfo,
            uint uFlags);

        // DestroyIcon fonksiyonunu user32.dll'den import ediyoruz (Hafıza sızıntısı önleme)
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        // SHGetFileInfo'nun ihtiyaç duyduğu Windows veri yapısı (struct)
        // !!!!! DÜZELTME BURADA !!!!!
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct SHFILEINFO
        {
            public IntPtr hIcon; // İkonun handle'ı (IntPtr)
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szDisplayName; // Dosya adı (kullanmıyoruz)
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
            public string szTypeName; // Dosya türü adı (örn: "Metin Belgesi")
        }
        // !!!!! DÜZELTME BİTTİ !!!!!


        // SHGetFileInfo'ya gönderdiğimiz bayraklar (flags)
        private const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;
        private const uint SHGFI_ICON = 0x000000100;     // İkonu al
        private const uint SHGFI_TYPENAME = 0x000000400;  // Tür adını al
        private const uint SHGFI_USEFILEATTRIBUTES = 0x000000010; // Dosyayı okuma, uzantıya bak
        private const uint SHGFI_SMALLICON = 0x000000001; // Küçük ikon (16x16 veya 32x32)
    }
}
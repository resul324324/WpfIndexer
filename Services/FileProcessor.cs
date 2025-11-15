using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Presentation;
using UglyToad.PdfPig;
using MsgReader.Outlook;
using Microsoft.Data.Sqlite;
using Tesseract; // OCR
using System.Drawing; // image stream handling for tiff/jpeg/png support
using Ghostscript.NET.Rasterizer; // PDF -> Resim için
using System.Drawing.Imaging;     // ImageFormat için
using WpfIndexer.Models; // ProgressReportModel için
using System.Collections.Concurrent; // Paralel işlem sonuçları için
using Ghostscript.NET; // Ghostscript Kütüphanesi
using System.Windows.Media; // Media Player (Video Thumbnail)
using System.Windows.Media.Imaging; // BitmapEncoder (Video Thumbnail)
using System.Threading; // Interlocked (sayaç) için
// NPOI Kütüphaneleri (Eski Office formatları .doc, .xls için)
using NPOI.HWPF; // .doc (NPOI.HWPF paketinden)
using NPOI.HSSF.UserModel; // .xls (ana NPOI paketinden)
// KALDIRILDI: using NPOI.HSLF.UserModel; (.ppt desteği kaldırıldı)
using NPOI.SS.UserModel; // ICell ve IRow için ana arayüz
using NPOI.POIFS.FileSystem;

namespace WpfIndexer.Services
{
    public static class FileProcessor
    {
        // GÜNCELLEME: .ppt kaldırıldı
        public static readonly Dictionary<string, string[]> RecommendedGroup = new()
        {
            { "⭐ Önerilen (Hızlı İndex)", new[] {
                ".docx", ".xlsx", ".pptx", ".doc", ".xls", // ".ppt" kaldırıldı
                ".pdf", ".txt", ".jpeg", ".png",
                ".jpg", ".tif",
                ".mp4", ".avi", ".mp3"
            } }
        };
        public static readonly Dictionary<string, string[]> FileTypeGroups = new()
        {
             { "🖼️ Görsel Formatları", new[] { ".gif", ".bmp", ".tiff", ".webp", ".svg", ".psd", ".ai", ".eps" } },
            { "🎬 Video Formatları", new[] { ".mkv", ".mov", ".webm", ".flv", ".wmv", ".mpeg" } },
            { "🔊 Ses Formatları", new[] { ".wav", ".flac", ".aac", ".ogg", ".m4a", ".wma", ".aiff" } },
            { "📄 Belge ve Sunu Formatları", new[] { ".odt", ".rtf", ".epub", ".mobi", ".azw" } }, // .ppt burada kalabilir, zararı yok
            { "📊 Tablo ve Veri Formatları", new[] { ".ods", ".csv", ".tsv", ".sav", ".dta", ".json", ".xml", ".yaml" } },
            { "📝 Kod ve Betik Formatları", new[] { ".py", ".js", ".java", ".c", ".cpp", ".cs", ".php", ".html", ".css", ".bat", ".sh", ".cmd" } },
            { "📦 Sıkıştırılmış ve Arşiv Formatları", new[] { ".zip", ".rar", ".7z", ".tar" } },
            { "📬 E-posta ve Mesajlaşma Formatları", new[] { ".eml", ".msg", ".pst", ".ost", ".chat" } },
            { "🗃️ Veritabanı Formatları", new[] { ".db", ".sqlite", ".accdb", ".mdb" } },
            { "📁 Tasarım ve Proje Formatları", new[] { ".xd", ".fig", ".sketch", ".blend", ".3ds", ".obj", ".fbx" } }
        };

        // "Yüksek Kalite Modu" Ayarları (DPI 300)
        private static readonly string _tessDataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tessdata");

        public static IEnumerable<string> GetSupportedExtensions()
        {
            return RecommendedGroup.SelectMany(group => group.Value)
                .Concat(FileTypeGroups.SelectMany(group => group.Value));
        }

        public static async Task<string> ExtractTextAsync(string path, OcrQuality ocrQuality,
            IProgress<ProgressReportModel>? progress = null, int currentCount = 0, int totalCount = 0)
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            try
            {
                var fileInfo = new FileInfo(path);
                if (fileInfo.Length == 0) return string.Empty;

                using (var fileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    return await ExtractTextFromStreamAsync(fileStream, ext, path, ocrQuality, progress, currentCount, totalCount);
                }
            }
            catch (IOException ioEx)
            {
                Console.WriteLine($"[FileProcessor] I/O Hatası: {path} - {ioEx.Message}");
                return string.Empty;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FileProcessor] Genel Hata: {path} - {ex.Message}");
                return string.Empty;
            }
        }

        public static async Task<string> ExtractTextFromStreamAsync(Stream stream, string ext, string debugPath = "", OcrQuality ocrQuality = OcrQuality.Off,
            IProgress<ProgressReportModel>? progress = null, int currentCount = 0, int totalCount = 0)
        {
            if (stream == null || stream.Length == 0) return string.Empty;

            try
            {
                switch (ext)
                {
                    // Metin tabanlı formatlar
                    case ".txt":
                    case ".md":
                    case ".log":
                    case ".csv":
                    case ".json":
                    case ".xml":
                    case ".ini":
                    case ".config":
                    case ".py":
                    case ".js":
                    case ".java":
                    case ".c":
                    case ".cpp":
                    case ".cs":
                    case ".php":
                    case ".html":
                    case ".css":
                    case ".bat":
                    case ".sh":
                    case ".cmd":
                    case ".yaml":
                    case ".rtf":
                    case ".tsv":
                    case ".svg":
                    case ".reg":
                    case ".conf":
                    case ".env":
                    case ".plist":
                        using (var reader = new StreamReader(stream, Encoding.UTF8, true, 4096, leaveOpen: true))
                        {
                            stream.Position = 0;
                            return await reader.ReadToEndAsync();
                        }

                    case ".pdf":
                        stream.Position = 0;
                        var pdfText = ExtractTextFromPdf(stream);

                        if (!string.IsNullOrWhiteSpace(pdfText) && pdfText.Length > 200)
                        {
                            return pdfText;
                        }

                        if (ocrQuality != OcrQuality.Off)
                        {
                            try
                            {
                                stream.Position = 0;
                                return await Task.Run(() => RunGhostscriptAndTesseractOnPdfStream(stream,
                                    debugPath, progress, currentCount, totalCount, ocrQuality));
                            }
                            catch (Exception ocrEx)
                            {
                                Console.WriteLine($"[FileProcessor] PDF OCR hatası: {debugPath} - {ocrEx.Message}");
                                return string.Empty;
                            }
                        }
                        return string.Empty;

                    // --- Eski .doc, .xls desteği (NPOI) ---
                    case ".doc":
                        return await Task.Run(() => ExtractTextFromDoc(stream));
                    case ".xls":
                        return await Task.Run(() => ExtractTextFromXls(stream));

                    // --- GÜNCELLEME: .ppt desteği kaldırıldı ---
                    case ".ppt":
                        return string.Empty; // Sadece dosya adı indekslenecek (içerik okunmayacak)

                    // --- Mevcut OpenXML formatları ---
                    case ".pdfa": return string.Empty;
                    case ".docx": return await Task.Run(() => ExtractTextFromWord(stream));
                    case ".xlsx": return await Task.Run(() => ExtractTextFromExcel(stream));
                    case ".pptx": return await Task.Run(() => ExtractTextFromPowerPoint(stream));
                    case ".msg": case ".eml": return await Task.Run(() => ExtractTextFromEmail(stream));
                    case ".sqlite": case ".db": return await Task.Run(() => ExtractTextFromSqlite(stream));

                    // Görüntü formatları
                    case ".jpg":
                    case ".jpeg":
                    case ".png":
                    case ".bmp":
                    case ".gif":
                    case ".tiff":
                    case ".tif":
                    case ".webp":
                    case ".psd":
                    case ".ai":
                    case ".eps":
                        if (ocrQuality != OcrQuality.Off)
                        {
                            try
                            {
                                using var ms = new MemoryStream();
                                stream.Position = 0;
                                await stream.CopyToAsync(ms);
                                var bytes = ms.ToArray();
                                return await Task.Run(() => RunTesseractOnImageBytes(bytes,
                                    debugPath, progress, currentCount, totalCount, ocrQuality));
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"[FileProcessor] OCR hatası: {debugPath} - {ex.Message}");
                                return string.Empty;
                            }
                        }
                        return string.Empty;

                    default:
                        return string.Empty;
                }
            }
            catch (Exception ex)
            {
                // NPOI eski/bozuk dosyalarda hata verebilir
                Console.WriteLine($"[FileProcessor] Stream okuma hatası: {debugPath} (ext: {ext}) - {ex.Message}");
                return string.Empty;
            }
        }

        #region Extraction helpers

        // --- NPOI (Eski Office) Metin Çıkarıcılar ---

        private static string ExtractTextFromDoc(Stream stream)
        {
            try
            {
                // DÜZELTME: NPOI 2.7.5 + NPOI.HWPF 2.3.0
                // bu yapıcıyı (constructor) kullanır
                var doc = new HWPFDocument(stream);
                return doc.Text.ToString();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FileProcessor] .doc okuma hatası: {ex.Message}");
                return string.Empty;
            }
        }

        private static string ExtractTextFromXls(Stream stream)
        {
            var sb = new StringBuilder();
            try
            {
                var workbook = new HSSFWorkbook(stream);
                for (int i = 0; i < workbook.NumberOfSheets; i++)
                {
                    var sheet = workbook.GetSheetAt(i);
                    // DÜZELTME: CS0104 - Belirsizliği önlemek için tam yolu (NPOI.SS.UserModel) belirtildi
                    foreach (NPOI.SS.UserModel.IRow row in sheet)
                    {
                        foreach (NPOI.SS.UserModel.ICell cell in row)
                        {
                            sb.Append(cell.ToString() + " ");
                        }
                        sb.AppendLine();
                    }
                }
                return sb.ToString();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FileProcessor] .xls okuma hatası: {ex.Message}");
                return string.Empty;
            }
        }

        // GÜNCELLEME: .ppt metodu kaldırıldı
        // private static string ExtractTextFromPpt(Stream stream)
        // { ... }

        // --- Mevcut OpenXML (docx, xlsx, pptx) Metin Çıkarıcılar ---

        private static string ExtractTextFromPdf(Stream stream)
        {
            var sb = new StringBuilder();
            try
            {
                using (var doc = PdfDocument.Open(stream))
                {
                    foreach (var page in doc.GetPages())
                    {
                        sb.AppendLine(page.Text);
                    }
                }
            }
            catch (Exception ex) { Console.WriteLine($"[FileProcessor] PDF stream okuma hatası - {ex.Message}"); }
            return sb.ToString();
        }

        private static string ExtractTextFromWord(Stream stream)
        {
            try
            {
                var sb = new StringBuilder();
                using (var doc = WordprocessingDocument.Open(stream, false))
                {
                    if (doc.MainDocumentPart?.Document.Body == null) return string.Empty;
                    foreach (var para in doc.MainDocumentPart.Document.Body.Descendants<Paragraph>())
                    {
                        sb.AppendLine(para.InnerText);
                    }
                }
                return sb.ToString();
            }
            catch (Exception ex) { Console.WriteLine($"[FileProcessor] Word (docx) stream okuma hatası - {ex.Message}"); return string.Empty; }
        }

        private static string ExtractTextFromExcel(Stream stream)
        {
            try
            {
                var sb = new StringBuilder();
                using (var doc = SpreadsheetDocument.Open(stream, false))
                {
                    var workbookPart = doc.WorkbookPart;
                    if (workbookPart == null) return string.Empty;
                    var sstPart = workbookPart.GetPartsOfType<SharedStringTablePart>().FirstOrDefault();
                    var sst = sstPart?.SharedStringTable;
                    foreach (var worksheetPart in workbookPart.WorksheetParts)
                    {
                        var sheetData = worksheetPart.Worksheet.Elements<SheetData>().FirstOrDefault();
                        if (sheetData == null) continue;
                        foreach (var row in sheetData.Elements<Row>())
                        {
                            foreach (var cell in row.Elements<Cell>())
                            {
                                if (cell.CellValue == null) continue;
                                var cellValue = cell.CellValue.InnerText;
                                if (cell.DataType != null && cell.DataType.Value == CellValues.SharedString)
                                {
                                    if (sst != null && int.TryParse(cellValue, out int sstId))
                                    {
                                        sb.Append(sst.ChildElements[sstId].InnerText + " ");
                                    }
                                }
                                else { sb.Append(cellValue + " "); }
                            }
                            sb.AppendLine();
                        }
                    }
                }
                return sb.ToString();
            }
            catch (Exception ex) { Console.WriteLine($"[FileProcessor] Excel (xlsx) stream okuma hatası - {ex.Message}"); return string.Empty; }
        }

        private static string ExtractTextFromPowerPoint(Stream stream)
        {
            try
            {
                var sb = new StringBuilder();
                using (var doc = PresentationDocument.Open(stream, false))
                {
                    var presentationPart = doc.PresentationPart;
                    if (presentationPart == null) return string.Empty;
                    foreach (var slidePart in presentationPart.SlideParts)
                    {
                        if (slidePart.Slide == null) continue;
                        var paragraphs = slidePart.Slide.Descendants<DocumentFormat.OpenXml.Drawing.Paragraph>();
                        foreach (var para in paragraphs)
                        {
                            foreach (var text in para.Descendants<DocumentFormat.OpenXml.Drawing.Text>())
                            {
                                sb.Append(text.Text + " ");
                            }
                            sb.AppendLine();
                        }
                    }
                }
                return sb.ToString();
            }
            catch (Exception ex) { Console.WriteLine($"[FileProcessor] PowerPoint (pptx) stream okuma hatası - {ex.Message}"); return string.Empty; }
        }

        private static string ExtractTextFromEmail(Stream stream)
        {
            try
            {
                using (var msg = new Storage.Message(stream))
                {
                    var sb = new StringBuilder();
                    sb.AppendLine($"Gönderen: {msg.Sender?.DisplayName} <{msg.Sender?.Email}>");
                    sb.AppendLine($"Alan: {string.Join("; ", msg.Recipients.Select(r => $"{r.DisplayName} <{r.Email}>"))}");
                    sb.AppendLine($"Konu: {msg.Subject}");
                    sb.AppendLine("---");
                    sb.AppendLine(msg.BodyText);

                    if (msg.Attachments.Count > 0)
                    {
                        sb.AppendLine("--- EKLER ---");
                        foreach (var attachment in msg.Attachments)
                        {
                            if (attachment is Storage.Attachment att)
                            {
                                sb.AppendLine(att.FileName);
                            }
                        }
                    }
                    return sb.ToString();
                }
            }
            catch (Exception ex) { Console.WriteLine($"[FileProcessor] Email stream okuma hatası - {ex.Message}"); return string.Empty; }
        }

        private static string ExtractTextFromSqlite(Stream stream)
        {
            var tempPath = Path.GetTempFileName();
            try
            {
                using (var fileStream = File.Create(tempPath))
                {
                    stream.CopyTo(fileStream);
                }

                var sb = new StringBuilder();
                using (var connection = new SqliteConnection($"Data Source={tempPath}"))
                {
                    connection.Open();
                    sb.AppendLine("Veritabanı Şeması (Tablolar ve Sütunlar):");

                    var tables = new List<string>();
                    var command = connection.CreateCommand();
                    command.CommandText = "SELECT name FROM sqlite_master WHERE type='table';";
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            tables.Add(reader.GetString(0));
                        }
                    }

                    foreach (var table in tables.Where(t => !t.StartsWith("sqlite_")))
                    {
                        sb.AppendLine($"Tablo: [{table}]");

                        var textColumns = new List<string>();
                        var colCommand = connection.CreateCommand();
                        colCommand.CommandText = $"PRAGMA table_info({table});";

                        using (var colReader = colCommand.ExecuteReader())
                        {
                            while (colReader.Read())
                            {
                                var colName = colReader.GetString(1);
                                var colType = colReader.GetString(2).ToUpperInvariant();
                                sb.Append(colName + " ");
                                if (colType.Contains("TEXT") || colType.Contains("CHAR") || colType == "STRING" || colType.Contains("VARCHAR"))
                                {
                                    textColumns.Add($"\"{colName}\"");
                                }
                            }
                        }
                        sb.AppendLine();

                        if (textColumns.Any())
                        {
                            sb.AppendLine($"--- [{table}] Metin İçeriği ---");
                            var contentQuery = $"SELECT {string.Join(", ", textColumns)} FROM \"{table}\";";
                            var contentCommand = connection.CreateCommand();
                            contentCommand.CommandText = contentQuery;
                            try
                            {
                                using (var contentReader = contentCommand.ExecuteReader())
                                {
                                    while (contentReader.Read())
                                    {
                                        for (int i = 0; i < contentReader.FieldCount; i++)
                                        {
                                            var val = contentReader.GetValue(i);
                                            if (val != null && val != DBNull.Value)
                                            {
                                                sb.Append(val.ToString() + " ");
                                            }
                                        }
                                        sb.AppendLine();
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                sb.AppendLine($"[{table}] içeriği okunurken hata: {ex.Message}");
                            }
                            sb.AppendLine("--- İçerik Sonu ---");
                        }
                    }
                }

                return sb.ToString();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FileProcessor] SQLite stream okuma hatası - {ex.Message}");
                return string.Empty;
            }
            finally
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();

                if (File.Exists(tempPath))
                {
                    try
                    {
                        File.Delete(tempPath);
                    }
                    catch (IOException ex)
                    {
                        Console.WriteLine($"[FileProcessor] Geçici dosya silinemedi: {tempPath} - {ex.Message}");
                    }
                }
            }
        }
        #endregion

        #region OCR helper (Tesseract)

        private static string RunTesseractOnImageBytes(byte[] bytes,
            string debugPath, IProgress<ProgressReportModel>? progress, int currentCount, int totalCount, OcrQuality ocrQuality)
        {
            (int dpi, string lang, EngineMode mode) = GetOcrSettings(ocrQuality);

            try
            {
                progress?.Report(new ProgressReportModel
                {
                    Message = "Görüntü OCR işleniyor...",
                    CurrentFile = debugPath,
                    IsIndeterminate = true,
                    Current = currentCount,
                    Total = totalCount
                });

                using var engine = new TesseractEngine(_tessDataPath, lang, mode);
                using var img = Pix.LoadFromMemory(bytes);
                using var page = engine.Process(img);
                var text = page.GetText();
                return text ?? string.Empty;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FileProcessor] Tesseract OCR hatası: {ex.Message}");
                return string.Empty;
            }
        }

        private static string RunGhostscriptAndTesseractOnPdfStream(Stream stream,
            string debugPath, IProgress<ProgressReportModel>? progress, int currentCount, int totalCount, OcrQuality ocrQuality)
        {
            var pageImages = new List<byte[]>();
            var extractedTextBag = new ConcurrentBag<string>();

            (int dpi, string lang, EngineMode mode) = GetOcrSettings(ocrQuality);

            try
            {
                // ----- AŞAMA 1: PDF'i SAYFALARA AYIR (Producer) -----
                using (var rasterizer = new GhostscriptRasterizer())
                {
                    rasterizer.Open(stream);
                    var pageCount = rasterizer.PageCount;

                    for (int pageNumber = 1; pageNumber <= pageCount; pageNumber++)
                    {
                        progress?.Report(new ProgressReportModel
                        {
                            Message = $"PDF Çıkarılıyor (Sayfa {pageNumber}/{pageCount})",
                            CurrentFile = debugPath,
                            IsIndeterminate = true,
                            Current = currentCount,
                            Total = totalCount
                        });

                        using (var img = rasterizer.GetPage(dpi, pageNumber))
                        {
                            using (var ms = new MemoryStream())
                            {
                                img.Save(ms, System.Drawing.Imaging.ImageFormat.Bmp);
                                pageImages.Add(ms.ToArray());
                            }
                        }
                    }
                }

                // ----- AŞAMA 2: SAYFALARI PARALEL İŞLE (Consumer / OCR) -----
                int processedPageCount = 0;
                int totalPages = pageImages.Count;

                var parallelOptions = new ParallelOptions
                {
                    MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2)
                };

                Parallel.ForEach(pageImages, parallelOptions, (imgBytes) =>
                {
                    using (var localEngine = new TesseractEngine(_tessDataPath, lang, mode))
                    {
                        using (var pix = Pix.LoadFromMemory(imgBytes))
                        {
                            using (var page = localEngine.Process(pix))
                            {
                                extractedTextBag.Add(page.GetText());
                            }
                        }
                    }

                    int currentPage = Interlocked.Increment(ref processedPageCount);

                    progress?.Report(new ProgressReportModel
                    {
                        Message = $"PDF OCR (Sayfa {currentPage}/{totalPages})",
                        CurrentFile = debugPath,
                        IsIndeterminate = false,
                        Current = currentCount,
                        Total = totalCount
                    });
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FileProcessor] Ghostscript/Tesseract PDF hatası: {ex.Message}");
                return string.Join(Environment.NewLine, extractedTextBag);
            }

            return string.Join(Environment.NewLine, extractedTextBag);
        }

        private static (int Dpi, string Lang, EngineMode Mode) GetOcrSettings(OcrQuality quality)
        {
            switch (quality)
            {
                case OcrQuality.Low:
                    return (Dpi: 150, Lang: "tur", Mode: EngineMode.Default);

                case OcrQuality.Balanced:
                    return (Dpi: 250, Lang: "tur", Mode: EngineMode.Default);

                case OcrQuality.High:
                    return (Dpi: 300, Lang: "tur+eng", Mode: EngineMode.Default);

                default:
                    return (Dpi: 0, Lang: "", Mode: EngineMode.Default);
            }
        }

        private static bool IsPageBlank(Image img, int threshold = 245)
        {
            try
            {
                using (var bmp = new Bitmap(img))
                {
                    int w = bmp.Width;
                    int h = bmp.Height;

                    Point[] samplePoints = new Point[]
                    {
                        new Point(w / 10, h / 10), new Point(w * 9 / 10, h / 10),
                        new Point(w / 10, h * 9 / 10), new Point(w * 9 / 10, h * 9 / 10),
                        new Point(w / 2, h / 10), new Point(w / 2, h * 9 / 10),
                        new Point(w / 10, h / 2), new Point(w * 9 / 10, h / 2),
                        new Point(w / 4, h / 4), new Point(w * 3 / 4, h * 3 / 4),
                        new Point(w / 2, h / 2)
                    };

                    foreach (var p in samplePoints)
                    {
                        System.Drawing.Color c = bmp.GetPixel(p.X, p.Y);
                        if (c.R < threshold || c.G < threshold || c.B < threshold)
                        {
                            return false;
                        }
                    }
                    return true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FileProcessor] Boş sayfa kontrolü hatası: {ex.Message}");
                return false;
            }
        }

        public static string ExtractVideoThumbnail(string videoPath)
        {
            try
            {
                string tempDir = Path.Combine(Path.GetTempPath(), "VideoThumbs");
                Directory.CreateDirectory(tempDir);

                string thumbPath = Path.Combine(tempDir, Path.GetFileNameWithoutExtension(videoPath) + ".jpg");

                try
                {
                    // YENİ: ffmpeg'i, programın çalıştığı yerdeki ffmpeg_bin klasöründe ara
                    var ffmpeg = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg_bin", "ffmpeg.exe");

                    if (File.Exists(ffmpeg))
                    {
                        var proc = new System.Diagnostics.Process
                        {
                            StartInfo = new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = ffmpeg,
                                Arguments = $"-i \"{videoPath}\" -ss 00:00:01 -vframes 1 \"{thumbPath}\" -y -loglevel quiet",
                                CreateNoWindow = true,
                                UseShellExecute = false
                            }
                        };
                        proc.Start();
                        proc.WaitForExit();
                    }
                }
                catch { }

                if (File.Exists(thumbPath))
                    return thumbPath;

                var player = new System.Windows.Media.MediaPlayer();
                player.Open(new Uri(videoPath));

                // MediaPlayer'ın açılması için biraz zaman tanıyın
                System.Threading.Thread.Sleep(500); // 500ms -> 1000ms

                // Pozisyonu 0.5 saniye yerine 2. saniyeye ayarlayın
                player.Position = TimeSpan.FromSeconds(1);

                // Seek (atlama) işleminin tamamlanması için ek zaman tanıyın
                //System.Threading.Thread.Sleep(1000);

                int w = 320, h = 180;
                var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap(w, h, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                var dv = new System.Windows.Media.DrawingVisual();

                using (var dc = dv.RenderOpen())
                {
                    dc.DrawVideo(player, new System.Windows.Rect(0, 0, w, h));
                }

                bmp.Render(dv);
                BitmapEncoder encoder = new JpegBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bmp));

                using (var fs = new FileStream(thumbPath, FileMode.Create))
                    encoder.Save(fs);

                return thumbPath;
            }
            catch
            {
                return "";
            }
        }
        #endregion
    }
}
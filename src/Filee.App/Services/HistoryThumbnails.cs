// Small square pictures of converted files for the Home page's recent conversions. Skia decodes images off the UI
// thread, at a fraction of the picture's size where the format allows it (JPEG, WebP), from a copy of the file read in
// one go, so the file is held open only while it is read and others may still write, rename or delete it meanwhile.
// Video formats (MP4, MKV, WebM, etc.) have a representative frame extracted via FFmpeg off the UI thread without
// locking the file. Documents (PDF, DOCX) and text/data files render document previews or extracted thumbnails.
// A small cache keeps them while the files don't change: a search lists the same conversions again at once.

using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Filee.Core.Formats;
using Filee.Core.Platform;
using Filee.Engines.Media;
using SkiaSharp;

// PDFtoImage marks its API for desktop platforms that are all covered.
#pragma warning disable CA1416

namespace Filee.App.Services;

internal static class HistoryThumbnails
{
    /// <summary>Width and height in pixels: the card shows it at 42, so this is sharp up to 200 % scaling.</summary>
    public const int Size = 96;

    /// <summary>Larger image files are not read: the card shows their format instead.</summary>
    internal const long MaxFileBytes = 64L * 1024 * 1024;

    /// <summary>
    /// Most pixels decoded for one thumbnail. JPEG and WebP decode at down to 1/8 of their size, so only a huge one
    /// comes near; PNG, GIF and BMP decode whole, so a larger one shows its format instead.
    /// </summary>
    internal const long MaxDecodedPixels = 4096L * 4096L;

    private const int CacheSize = 120;

    /// <summary>Pictures Skia decodes natively on every platform.</summary>
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "png", "jpg", "jpeg", "jfif", "webp", "gif", "bmp", "ico",
    };

    /// <summary>PDF documents rendered via PDFium / PDFtoImage.</summary>
    private static readonly HashSet<string> PdfExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "pdf",
    };

    /// <summary>Word documents inspected for embedded thumbnails or text previews.</summary>
    private static readonly HashSet<string> DocxExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "docx", "docm", "dotx", "dotm",
    };

    /// <summary>Text, code, markdown, and structured data formats rendered as clean document preview cards.</summary>
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "txt", "md", "markdown", "json", "xml", "csv", "tsv", "yaml", "yml", "ini", "toml", "log",
        "cs", "js", "ts", "html", "htm", "css", "sql", "sh", "bat", "ps1", "py", "rs", "go", "cpp", "c", "h", "rtf",
    };

    private sealed record Entry(DateTime Written, long Length, Bitmap? Bitmap);

    private static readonly Dictionary<string, Entry> Cache = new(FileSystemPaths.Comparer);
    private static readonly Lock CacheLock = new();

    /// <summary>Whether <paramref name="path"/> is a file a thumbnail can be made of (by its extension only).</summary>
    public static bool CanShow(string path)
    {
        var ext = Path.GetExtension(path).TrimStart('.');
        return ImageExtensions.Contains(ext)
            || PdfExtensions.Contains(ext)
            || DocxExtensions.Contains(ext)
            || TextExtensions.Contains(ext)
            || IsVideo(ext);
    }

    internal static bool IsVideo(string ext) =>
        FormatRegistry.FindByExtension(ext)?.Category == FormatCategory.Video;

    /// <summary>The thumbnail made last time for <paramref name="path"/>, without looking at the file.</summary>
    public static Bitmap? Cached(string path)
    {
        lock (CacheLock)
            return Cache.TryGetValue(path, out var entry) ? entry.Bitmap : null;
    }

    /// <summary>
    /// The thumbnail of <paramref name="path"/>, from the cache while the file is unchanged. Null when the file is gone,
    /// too large or not a picture/video/document Filee reads. Reads the disk: never call it on the UI thread.
    /// </summary>
    public static Bitmap? Load(string path)
    {
        FileInfo file;
        try
        {
            file = new FileInfo(path);
            if (!file.Exists)
                return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }

        var written = file.LastWriteTimeUtc;
        var length = file.Length;
        lock (CacheLock)
        {
            if (Cache.TryGetValue(path, out var hit) && hit.Written == written && hit.Length == length)
                return hit.Bitmap;
        }

        var ext = Path.GetExtension(path).TrimStart('.');
        Bitmap? bitmap = null;

        if (IsVideo(ext))
        {
            if (length > 0)
                bitmap = DecodeVideo(path, MaxDecodedPixels);
        }
        else if (PdfExtensions.Contains(ext))
        {
            if (length > 0 && length <= MaxFileBytes)
                bitmap = DecodePdf(path, MaxDecodedPixels);
        }
        else if (DocxExtensions.Contains(ext))
        {
            if (length > 0 && length <= MaxFileBytes)
                bitmap = DecodeDocx(path);
        }
        else if (TextExtensions.Contains(ext))
        {
            if (length > 0 && length <= MaxFileBytes)
                bitmap = RenderTextThumbnail(path, ext);
        }
        else if (ImageExtensions.Contains(ext))
        {
            if (length <= MaxFileBytes)
                bitmap = Decode(path, MaxDecodedPixels);
        }

        lock (CacheLock)
        {
            if (Cache.Count >= CacheSize)
                Cache.Clear();
            Cache[path] = new Entry(written, length, bitmap);
        }
        return bitmap;
    }

    /// <summary>
    /// Renders page 0 of PDF document <paramref name="path"/> using PDFium and crops it into a square from the top.
    /// </summary>
    internal static Bitmap? DecodePdf(string path, long maxPixels)
    {
        try
        {
            var fileInfo = new FileInfo(path);
            if (!fileInfo.Exists || fileInfo.Length < 8)
                return null;

            byte[] bytes;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
                       64 * 1024, FileOptions.SequentialScan))
            {
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                bytes = ms.ToArray();
            }

            if (bytes.Length < 8)
                return null;

            // Verify PDF header magic bytes "%PDF"
            if (bytes[0] != 0x25 || bytes[1] != 0x50 || bytes[2] != 0x44 || bytes[3] != 0x46)
                return null;

            using var pageBitmap = PDFtoImage.Conversion.ToImage(bytes, page: 0, password: null, options: new PDFtoImage.RenderOptions(Dpi: 72));
            if (pageBitmap is null || pageBitmap.Width <= 0 || pageBitmap.Height <= 0)
                return null;

            if ((long)pageBitmap.Width * pageBitmap.Height > maxPixels)
                return null;

            return Square(pageBitmap, SKEncodedOrigin.TopLeft, fromTop: true);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Inspects DOCX archive <paramref name="path"/> for embedded thumbnail or extracts text snippets to render a preview card.
    /// </summary>
    internal static Bitmap? DecodeDocx(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
                64 * 1024, FileOptions.SequentialScan);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

            // 1. Check for embedded thumbnail (docProps/thumbnail.jpeg or .png)
            var thumbEntry = archive.GetEntry("docProps/thumbnail.jpeg")
                ?? archive.GetEntry("docProps/thumbnail.jpg")
                ?? archive.GetEntry("docProps/thumbnail.png");

            if (thumbEntry is not null)
            {
                using var entryStream = thumbEntry.Open();
                using var ms = new MemoryStream();
                entryStream.CopyTo(ms);
                var thumbData = ms.ToArray();
                using var data = SKData.CreateCopy(thumbData);
                using var codec = SKCodec.Create(data);
                if (codec is not null && codec.Info.Width > 0 && codec.Info.Height > 0)
                {
                    var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
                    using var decoded = new SKBitmap(info);
                    if (codec.GetPixels(info, decoded.GetPixels()) is SKCodecResult.Success or SKCodecResult.IncompleteInput)
                    {
                        return Square(decoded, codec.EncodedOrigin, fromTop: true);
                    }
                }
            }

            // 2. Fall back to extracting text snippets from word/document.xml
            var docEntry = archive.GetEntry("word/document.xml");
            if (docEntry is not null)
            {
                using var entryStream = docEntry.Open();
                var text = ExtractTextFromXml(entryStream, maxChars: 300);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return RenderDocumentCard(text, "DOCX", new SKColor(0x2B, 0x57, 0x9A));
                }
            }
        }
        catch (Exception)
        {
            // Corrupt or unreadable archive
        }
        return null;
    }

    private static string ExtractTextFromXml(Stream xmlStream, int maxChars)
    {
        var sb = new StringBuilder();
        using var reader = XmlReader.Create(xmlStream, new XmlReaderSettings
        {
            IgnoreComments = true,
            IgnoreWhitespace = true,
            DtdProcessing = DtdProcessing.Ignore,
        });

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "t")
            {
                var text = reader.ReadElementContentAsString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    if (sb.Length > 0)
                        sb.Append(' ');
                    sb.Append(text.Trim());
                    if (sb.Length >= maxChars)
                        break;
                }
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Reads the first few hundred characters of a text file and draws a clean document preview card.
    /// </summary>
    internal static Bitmap? RenderTextThumbnail(string path, string ext)
    {
        try
        {
            string content;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
                       8192, FileOptions.SequentialScan))
            using (var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 2048, leaveOpen: false))
            {
                var chars = new char[500];
                var read = reader.Read(chars, 0, chars.Length);
                if (read <= 0)
                    return null;
                content = new string(chars, 0, read);
            }

            var badgeText = ext.ToUpperInvariant();
            if (badgeText.Length > 4)
                badgeText = badgeText[..4];

            var badgeColor = ext.ToLowerInvariant() switch
            {
                "json" or "js" or "ts" or "cs" or "py" or "rs" or "go" or "cpp" or "c" or "h" => new SKColor(0xD9, 0x77, 0x06),
                "md" or "markdown" => new SKColor(0x02, 0x84, 0xC7),
                "csv" or "tsv" => new SKColor(0x05, 0x96, 0x69),
                "xml" or "html" or "htm" => new SKColor(0xEA, 0x58, 0x0C),
                _ => new SKColor(0x4B, 0x55, 0x63),
            };

            return RenderDocumentCard(content, badgeText, badgeColor);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Draws a paper sheet preview card with a category badge and lines of formatted text.
    /// </summary>
    internal static Bitmap RenderDocumentCard(string text, string badgeText, SKColor badgeColor)
    {
        using var square = new SKBitmap(new SKImageInfo(Size, Size, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(square))
        {
            canvas.Clear(SKColors.Transparent);

            // Document sheet: soft neutral paper with subtle rounded corners and thin border
            var paperRect = new SKRoundRect(new SKRect(5, 5, Size - 5, Size - 5), 6, 6);
            using (var paperPaint = new SKPaint { Color = new SKColor(0xFA, 0xFA, 0xFC), IsAntialias = true, Style = SKPaintStyle.Fill })
            {
                canvas.DrawRoundRect(paperRect, paperPaint);
            }
            using (var borderPaint = new SKPaint { Color = new SKColor(0x00, 0x00, 0x00, 0x22), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1f })
            {
                canvas.DrawRoundRect(paperRect, borderPaint);
            }

            // Format badge pill
            var badgeWidth = Math.Clamp(badgeText.Length * 7f + 10f, 28f, 54f);
            var badgeRect = new SKRoundRect(new SKRect(11, 10, 11 + badgeWidth, 23), 3, 3);
            using (var badgeBg = new SKPaint { Color = badgeColor, IsAntialias = true, Style = SKPaintStyle.Fill })
            {
                canvas.DrawRoundRect(badgeRect, badgeBg);
            }

            using (var badgeFont = new SKFont(SKTypeface.Default, 8f))
            using (var badgeTextPaint = new SKPaint { Color = SKColors.White, IsAntialias = true })
            {
                canvas.DrawText(badgeText, 15, 19.5f, badgeFont, badgeTextPaint);
            }

            // Preview text lines
            var rawLines = text.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries);
            using (var textFont = new SKFont(SKTypeface.Default, 6.5f))
            using (var textPaint = new SKPaint { Color = new SKColor(0x37, 0x41, 0x51), IsAntialias = true })
            {
                var y = 33f;
                var lineCount = 0;
                foreach (var rawLine in rawLines)
                {
                    var line = rawLine.Trim();
                    if (string.IsNullOrEmpty(line))
                        continue;

                    while (line.Length > 0 && lineCount < 6)
                    {
                        var chunkLen = Math.Min(line.Length, 20);
                        var chunk = line[..chunkLen];
                        canvas.DrawText(chunk, 12, y, textFont, textPaint);
                        y += 9f;
                        lineCount++;
                        line = line[chunkLen..].TrimStart();
                    }
                    if (lineCount >= 6)
                        break;
                }
            }
        }

        return new Bitmap(PixelFormat.Bgra8888, AlphaFormat.Premul, square.GetPixels(), new PixelSize(Size, Size),
            new Vector(96, 96), square.RowBytes);
    }

    /// <summary>
    /// Extracts a representative frame of video <paramref name="path"/> using FFmpeg and decodes it into a <see cref="Size"/> square.
    /// Null when FFmpeg is unavailable, video is broken, or frame decoding fails.
    /// </summary>
    internal static Bitmap? DecodeVideo(string path, long maxPixels)
    {
        var ffmpeg = FfmpegConverter.Locate()?.Ffmpeg;
        if (ffmpeg is null || !File.Exists(ffmpeg))
            return null;

        // Try seeking to 1s first (avoids blank/black title frame), then fall back to the beginning (e.g. videos shorter than 1s).
        var frameBytes = ExtractVideoFrame(ffmpeg, path, seekSeconds: 1)
                         ?? ExtractVideoFrame(ffmpeg, path, seekSeconds: null);
        if (frameBytes is null || frameBytes.Length == 0)
            return null;

        try
        {
            using var data = SKData.CreateCopy(frameBytes);
            using var codec = SKCodec.Create(data);
            if (codec is null || codec.Info.Width <= 0 || codec.Info.Height <= 0)
                return null;

            var scale = Math.Min(1f, (float)Size / Math.Min(codec.Info.Width, codec.Info.Height));
            var scaled = codec.GetScaledDimensions(scale);
            if (scaled.Width <= 0 || scaled.Height <= 0 || (long)scaled.Width * scaled.Height > maxPixels)
                return null;

            var info = new SKImageInfo(scaled.Width, scaled.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var decoded = new SKBitmap(info);
            if (codec.GetPixels(info, decoded.GetPixels()) is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
                return null;

            return Square(decoded, codec.EncodedOrigin);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static byte[]? ExtractVideoFrame(string ffmpeg, string path, int? seekSeconds)
    {
        var tempPng = Path.Combine(Path.GetTempPath(), $"filee-thumb-{Guid.NewGuid():N}.png");
        try
        {
            var psi = new ProcessStartInfo(ffmpeg)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("-hide_banner");
            psi.ArgumentList.Add("-nostdin");
            psi.ArgumentList.Add("-v");
            psi.ArgumentList.Add("error");
            if (seekSeconds.HasValue)
            {
                psi.ArgumentList.Add("-ss");
                psi.ArgumentList.Add(seekSeconds.Value.ToString(CultureInfo.InvariantCulture));
            }
            psi.ArgumentList.Add("-i");
            psi.ArgumentList.Add(Path.GetFullPath(path));
            psi.ArgumentList.Add("-frames:v");
            psi.ArgumentList.Add("1");
            psi.ArgumentList.Add("-y");
            psi.ArgumentList.Add(tempPng);

            using var process = new Process { StartInfo = psi };
            if (!process.Start())
                return null;

            if (!process.WaitForExit(15_000))
            {
                try
                {
                    if (!process.HasExited)
                        process.Kill(entireProcessTree: true);
                }
                catch { }
                return null;
            }

            if (process.ExitCode == 0 && File.Exists(tempPng))
            {
                return File.ReadAllBytes(tempPng);
            }
            return null;
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            try
            {
                if (File.Exists(tempPng))
                    File.Delete(tempPng);
            }
            catch { }
        }
    }

    /// <summary>
    /// Decodes <paramref name="path"/> into a <see cref="Size"/> square: upright (the EXIF orientation applied) and
    /// cropped from the middle. Null when it can't, or would have to decode more than <paramref name="maxPixels"/>.
    /// </summary>
    internal static Bitmap? Decode(string path, long maxPixels)
    {
        SKData? data;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
                64 * 1024, FileOptions.SequentialScan);
            data = SKData.Create(stream);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
        if (data is null)
            return null;

        using (data)
        using (var codec = SKCodec.Create(data))
        {
            if (codec is null || codec.Info.Width <= 0 || codec.Info.Height <= 0)
                return null;

            // The shorter side still covers the square; the codec picks the nearest scale it decodes at.
            var scale = Math.Min(1f, (float)Size / Math.Min(codec.Info.Width, codec.Info.Height));
            var scaled = codec.GetScaledDimensions(scale);
            if (scaled.Width <= 0 || scaled.Height <= 0 || (long)scaled.Width * scaled.Height > maxPixels)
                return null;

            var info = new SKImageInfo(scaled.Width, scaled.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var decoded = new SKBitmap(info);
            if (codec.GetPixels(info, decoded.GetPixels()) is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
                return null;
            return Square(decoded, codec.EncodedOrigin);
        }
    }

    private static Bitmap Square(SKBitmap source, SKEncodedOrigin origin, bool fromTop = false)
    {
        // Upright first: the orientations from LeftTop on turn the picture a quarter, swapping width and height.
        var turned = origin >= SKEncodedOrigin.LeftTop;
        using var upright = new SKBitmap(new SKImageInfo(turned ? source.Height : source.Width, turned ? source.Width : source.Height,
            SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(upright))
        {
            canvas.Clear(SKColors.Transparent);
            canvas.SetMatrix(Upright(origin, source.Width, source.Height));
            canvas.DrawBitmap(source, 0, 0);
        }

        var side = Math.Min(upright.Width, upright.Height);
        var cropY = fromTop ? 0f : (upright.Height - side) / 2f;
        var crop = SKRect.Create((upright.Width - side) / 2f, cropY, side, side);
        using var square = new SKBitmap(new SKImageInfo(Size, Size, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(square))
        using (var image = SKImage.FromBitmap(upright))
        {
            canvas.Clear(SKColors.Transparent);
            canvas.DrawImage(image, crop, SKRect.Create(Size, Size), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        }
        // Avalonia copies the pixels.
        return new Bitmap(PixelFormat.Bgra8888, AlphaFormat.Premul, square.GetPixels(), new PixelSize(Size, Size),
            new Vector(96, 96), square.RowBytes);
    }

    /// <summary>
    /// Maps the stored pixels of a <paramref name="width"/> × <paramref name="height"/> picture to how it is shown, for
    /// each EXIF orientation (x' = ScaleX·x + SkewX·y + TransX, y' = SkewY·x + ScaleY·y + TransY).
    /// </summary>
    internal static SKMatrix Upright(SKEncodedOrigin origin, int width, int height) => origin switch
    {
        SKEncodedOrigin.TopRight => new SKMatrix(-1, 0, width, 0, 1, 0, 0, 0, 1),
        SKEncodedOrigin.BottomRight => new SKMatrix(-1, 0, width, 0, -1, height, 0, 0, 1),
        SKEncodedOrigin.BottomLeft => new SKMatrix(1, 0, 0, 0, -1, height, 0, 0, 1),
        SKEncodedOrigin.LeftTop => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),
        SKEncodedOrigin.RightTop => new SKMatrix(0, -1, height, 1, 0, 0, 0, 0, 1),
        SKEncodedOrigin.RightBottom => new SKMatrix(0, -1, height, -1, 0, width, 0, 0, 1),
        SKEncodedOrigin.LeftBottom => new SKMatrix(0, 1, 0, -1, 0, width, 0, 0, 1),
        _ => SKMatrix.Identity,
    };
}

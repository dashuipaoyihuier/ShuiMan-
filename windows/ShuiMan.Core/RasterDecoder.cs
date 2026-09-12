using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ImageMagick;

namespace ShuiMan.Core;

internal record RasterInfo(uint Width, uint Height, int Frame = 0, string? Error = null);

internal static class RasterDecoder
{
    private const ulong PixelLimit = 150_000_000;
    static RasterDecoder()
    {
        // Bound native pixel caches, including inputs with misleading dimensions.
        ResourceLimits.Memory = 384UL * 1024 * 1024;
        ResourceLimits.Disk = 1024UL * 1024 * 1024;
        ResourceLimits.MaxMemoryRequest = 512UL * 1024 * 1024;
        ResourceLimits.ListLength = 10_000;
        ResourceLimits.Thread = 2;
    }

    internal static List<RasterInfo> Describe(byte[] bytes, bool allFrames = false)
    {
        var format = RasterFormat(bytes);
        if (!allFrames)
        {
            using var image = new MagickImage();
            image.Ping(bytes, new MagickReadSettings { FrameIndex = 0, FrameCount = 1, Format = format });
            return [Info(image, 0)];
        }
        using var frames = new MagickImageCollection();
        frames.Ping(bytes, new MagickReadSettings { Format = format });
        if (frames.Count > 10_000) throw new InvalidDataException("TIFF 包含过多页面。");
        return frames.Select((image, index) =>
        {
            try { return Info(image, index); }
            catch (InvalidDataException ex) { return new RasterInfo(0, 0, index, ex.Message); }
        }).ToList();
    }

    private static RasterInfo Info(IMagickImage<byte> image, int index)
    {
        if (image.Width == 0 || image.Height == 0 || (ulong)image.Width * image.Height > PixelLimit)
            throw new InvalidDataException("图片尺寸无效或超过 1.5 亿像素限制。");
        bool swap = image.Orientation is OrientationType.LeftTop or OrientationType.RightTop or OrientationType.RightBottom or OrientationType.LeftBottom;
        return new RasterInfo(swap ? image.Height : image.Width, swap ? image.Width : image.Height, index);
    }

    internal static BitmapSource Decode(byte[] bytes, int frame, int maxEdge)
    {
        maxEdge = Math.Clamp(maxEdge, 64, 8192);
        var format = RasterFormat(bytes);
        var settings = new MagickReadSettings { FrameIndex = (uint)Math.Max(0, frame), FrameCount = 1, Format = format };
        using var probe = new MagickImageCollection();
        probe.Ping(bytes, new MagickReadSettings { Format = format });
        if (frame < 0 || frame >= probe.Count) throw new InvalidDataException("图片页码越界。");
        _ = Info(probe[frame], frame);
        using var decoded = new MagickImageCollection();
        decoded.Read(bytes, settings);
        if (decoded.Count == 0) throw new InvalidDataException("图片页面无法解码。");
        var image = decoded[0];
        image.AutoOrient();
        if (image.Width > maxEdge || image.Height > maxEdge)
            image.Resize(new MagickGeometry((uint)maxEdge, (uint)maxEdge) { Greater = true });
        if (!image.TransformColorSpace(ColorProfiles.SRGB)) image.ColorSpace = ColorSpace.sRGB;
        image.Depth = 8;
        var data = image.ToByteArray(MagickFormat.Bgra);
        var bitmap = BitmapSource.Create((int)image.Width, (int)image.Height, 96, 96, PixelFormats.Bgra32, null, data, checked((int)image.Width * 4));
        bitmap.Freeze();
        return bitmap;
    }

    private static MagickFormat RasterFormat(byte[] bytes)
    {
        // Restrict the native decoder to raster codecs. A mislabeled PNG must never
        // activate SVG/PDF/script delegates or external resource loading.
        var data = bytes.AsSpan();
        if (data.StartsWith(new byte[] { 0xff, 0xd8, 0xff })) return MagickFormat.Jpeg;
        if (data.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return MagickFormat.Png;
        if (data.StartsWith("GIF87a"u8) || data.StartsWith("GIF89a"u8)) return MagickFormat.Gif;
        if (data.StartsWith("BM"u8)) return MagickFormat.Bmp;
        if (data.StartsWith(new byte[] { 73, 73, 42, 0 }) || data.StartsWith(new byte[] { 77, 77, 0, 42 }) ||
            data.StartsWith(new byte[] { 73, 73, 43, 0 }) || data.StartsWith(new byte[] { 77, 77, 0, 43 })) return MagickFormat.Tiff;
        if (data.Length >= 12 && data[..4].SequenceEqual("RIFF"u8) && data.Slice(8, 4).SequenceEqual("WEBP"u8)) return MagickFormat.WebP;
        if (data.StartsWith(new byte[] { 0xff, 0x0a }) || data.StartsWith(new byte[] { 0, 0, 0, 12, 74, 88, 76, 32, 13, 10, 135, 10 })) return MagickFormat.Jxl;
        if (data.Length >= 16 && data.Slice(4, 4).SequenceEqual("ftyp"u8))
        {
            uint boxLength = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(data);
            int end = (int)Math.Min(Math.Min(boxLength, (uint)data.Length), 1024);
            bool heic = false;
            for (int i = 8; i + 4 <= end; i += 4)
            {
                if (i == 12) continue; // minor version
                var brand = System.Text.Encoding.ASCII.GetString(data.Slice(i, 4));
                if (brand is "avif" or "avis") return MagickFormat.Avif;
                if (brand is "heic" or "heix" or "hevc" or "hevx" or "mif1" or "msf1") heic = true;
            }
            if (heic) return MagickFormat.Heic;
        }
        throw new InvalidDataException("内容不是受支持的位图格式，或图片文件头已损坏。");
    }
}

internal sealed class ImageBook : RasterBook
{
    private readonly BookArchive? archive;
    private readonly Dictionary<string, string> files = new(StringComparer.Ordinal);

    public ImageBook(string path, CancellationToken token)
    {
        Publication = DocumentEngine.Describe(path, "images");
        IEnumerable<string> paths;
        if (!Directory.Exists(path) && (Path.GetExtension(path).ToLowerInvariant() is ".zip" or ".cbz"))
        {
            archive = new BookArchive(path);
            Publication.Kind = "zip";
            paths = archive.Paths.Where(BookArchive.IsVisible).Where(DocumentEngine.IsImage);
        }
        else if (Directory.Exists(path))
        {
            var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint };
            foreach (var file in Directory.EnumerateFiles(path, "*", options))
            {
                token.ThrowIfCancellationRequested();
                if (!DocumentEngine.IsImage(file)) continue;
                var relative = Path.GetRelativePath(path, file).Replace('\\', '/');
                if (BookArchive.IsVisible(relative)) files[relative] = file;
                if (files.Count > 100_000) throw new InvalidDataException("文件夹包含过多图片。");
            }
            paths = files.Keys;
        }
        else
        {
            files[Path.GetFileName(path)] = path;
            paths = files.Keys;
        }
        try
        {
            int occurrence = 0;
            foreach (string imagePath in paths.Order(NaturalPathComparer.Instance))
            {
                token.ThrowIfCancellationRequested();
                List<RasterInfo>? infos = null;
                string? error = null;
                try { infos = RasterDecoder.Describe(Resource(imagePath)!, Path.GetExtension(imagePath).ToLowerInvariant() is ".tif" or ".tiff"); }
                catch (Exception ex) when (ex is not OperationCanceledException) { error = $"图片无法读取：{ex.Message}"; }
                foreach (var info in infos is { Count: > 0 } ? infos : [new RasterInfo(0, 0)])
                    Publication.Units.Add(new ReadingUnit
                    {
                        // Archive/folder paths are unique. Keep IDs independent of sorted
                        // positions so adding an earlier page does not erase corrections.
                        Locator = new SourceLocator(imagePath, 0, info.Frame), ImagePath = imagePath,
                        Title = Path.GetFileName(imagePath) + (infos?.Count > 1 ? $" · {info.Frame + 1}" : ""),
                        Width = info.Width, Height = info.Height, IsCover = occurrence == 0 && info.Frame == 0, Error = error ?? info.Error
                    });
                occurrence++;
            }
            if (Publication.Units.Count == 0) throw new InvalidDataException("没有找到可阅读的图片。支持 ZIP/CBZ 内的嵌套文件夹与常见图片格式。");
            if (Publication.Units.Any(unit => unit.Error != null)) Publication.Warnings.Add("部分图片无法解码，已保留原阅读位置。");
            if (Directory.Exists(path))
            {
                var signature = string.Join('\n', files.Select(pair => $"{pair.Key}:{new FileInfo(pair.Value).Length}:{File.GetLastWriteTimeUtc(pair.Value).Ticks}"));
                Publication.Revision = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(signature)));
            }
        }
        catch { archive?.Dispose(); throw; }
    }

    public override byte[]? Resource(string path)
    {
        ObjectDisposedException.ThrowIf(Disposed, this);
        if (archive != null) return archive.Contains(path) ? archive.Bytes(path) : null;
        return files.TryGetValue(path, out var file) ? DocumentEngine.ReadFile(file) : null;
    }
    public override void Dispose() { base.Dispose(); archive?.Dispose(); }
}

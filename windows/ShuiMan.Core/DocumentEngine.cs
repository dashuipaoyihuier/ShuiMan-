using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media.Imaging;

namespace ShuiMan.Core;

/// <summary>Opens original publications read-only. Parsing and image decoding never run on the UI thread.</summary>
public static class DocumentEngine
{
    public static readonly IReadOnlySet<string> ImageExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    { ".jpg", ".jpeg", ".jpe", ".png", ".gif", ".bmp", ".tif", ".tiff", ".webp", ".avif", ".heic", ".heif", ".jxl" };
    public static readonly IReadOnlySet<string> SupportedExtensions = new HashSet<string>(
        ImageExtensions.Concat(new[] { ".zip", ".cbz", ".epub", ".pdf", ".mobi" }), StringComparer.OrdinalIgnoreCase);
    public static bool IsSupported(string path) => Directory.Exists(path) || SupportedExtensions.Contains(Path.GetExtension(path));
    public static bool IsImage(string path) => ImageExtensions.Contains(Path.GetExtension(path));

    /// <summary>Adapts TIFF/HEIF/JPEG XL resources for the browser without changing source files.</summary>
    public static byte[] RasterToPng(byte[] data, int maxEdge = 8192)
    {
        var bitmap = RasterDecoder.Decode(data, 0, maxEdge);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = new MemoryStream();
        encoder.Save(output);
        return output.ToArray();
    }

    public static Task<IOpenBook> OpenAsync(string path, string? password = null, CancellationToken cancellationToken = default) =>
        Task.Run(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            path = Path.GetFullPath(path);
            if (Directory.Exists(path)) return (IOpenBook)new ImageBook(path, cancellationToken);
            if (!File.Exists(path)) throw new FileNotFoundException("找不到漫画文件。", path);
            var extension = Path.GetExtension(path).ToLowerInvariant();
            return extension switch
            {
                ".zip" or ".cbz" => new ImageBook(path, cancellationToken),
                ".epub" => new EpubBook(path, cancellationToken),
                ".mobi" => new MobiBook(path, cancellationToken),
                ".pdf" => await PdfBook.OpenAsync(path, password, cancellationToken),
                _ when IsImage(path) => new ImageBook(path, cancellationToken),
                _ => throw new NotSupportedException("不支持此文件格式。请选择 EPUB、PDF、MOBI、ZIP/CBZ、图片或图片文件夹。")
            };
        }, cancellationToken);

    internal static Publication Describe(string path, string kind)
    {
        var full = Path.GetFullPath(path);
        var info = new FileInfo(full);
        var stamp = Directory.Exists(full) ? Directory.GetLastWriteTimeUtc(full).Ticks.ToString() : $"{info.Length}:{info.LastWriteTimeUtc.Ticks}";
        return new Publication
        {
            SourcePath = full,
            Identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToUpperInvariant()))).ToLowerInvariant(),
            Revision = stamp,
            Title = Directory.Exists(full) ? new DirectoryInfo(full).Name : Path.GetFileNameWithoutExtension(full),
            Kind = kind
        };
    }

    internal static byte[] ReadFile(string path, long limit = BookArchive.ResourceLimit)
    {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return ReadBounded(input, input.Length, limit);
    }

    internal static byte[] ReadBounded(Stream input, long expected, long limit)
    {
        if (expected < 0 || expected > limit || expected > int.MaxValue) throw new InvalidDataException("单个资源超过读取上限。请先缩小图片。");
        var bytes = new byte[(int)expected];
        input.ReadExactly(bytes);
        if (input.ReadByte() != -1) throw new InvalidDataException("资源实际展开大小与目录记录不一致。");
        return bytes;
    }
}

internal abstract class RasterBook : IOpenBook
{
    private const long CacheBudget = 96L * 1024 * 1024;
    private readonly object cacheGate = new();
    private readonly LinkedList<RenderCacheEntry> recentRenders = new();
    private readonly Dictionary<(int Index, int Edge), LinkedListNode<RenderCacheEntry>> renderCache = [];
    private long cacheBytes;
    private sealed record RenderCacheEntry((int Index, int Edge) Key, BitmapSource Bitmap, long Bytes);
    public Publication Publication { get; protected set; } = new();
    protected volatile bool Disposed;
    public abstract byte[]? Resource(string path);
    protected virtual int FrameIndex(ReadingUnit unit) => unit.Locator.ImageIndex;
    public virtual Task<BitmapSource> RenderAsync(int index, int maxEdge = 2400, CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        ObjectDisposedException.ThrowIf(Disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (index < 0 || index >= Publication.Units.Count) throw new ArgumentOutOfRangeException(nameof(index));
        maxEdge = Math.Clamp(maxEdge, 64, 8192);
        var key = (index, maxEdge);
        lock (cacheGate)
        {
            if (renderCache.TryGetValue(key, out var cached))
            {
                recentRenders.Remove(cached);
                recentRenders.AddFirst(cached);
                return cached.Value.Bitmap;
            }
        }
        var unit = Publication.Units[index];
        if (unit.Error != null) throw new InvalidDataException(unit.Error);
        if (unit.Complex) throw new NotSupportedException("此阅读位置不包含可显示的原生漫画图片。");
        var data = Resource(unit.ImagePath ?? unit.Locator.Resource) ?? throw new InvalidDataException("页面图片资源缺失。");
        var bitmap = RasterDecoder.Decode(data, FrameIndex(unit), maxEdge);
        cancellationToken.ThrowIfCancellationRequested();
        long bytes = (long)bitmap.PixelWidth * bitmap.PixelHeight * 4;
        lock (cacheGate)
        {
            ObjectDisposedException.ThrowIf(Disposed, this);
            if (bytes <= CacheBudget && !renderCache.ContainsKey(key))
            {
                while (cacheBytes + bytes > CacheBudget && recentRenders.Last is { } oldest)
                {
                    cacheBytes -= oldest.Value.Bytes;
                    renderCache.Remove(oldest.Value.Key);
                    recentRenders.RemoveLast();
                }
                var cached = recentRenders.AddFirst(new RenderCacheEntry(key, bitmap, bytes));
                renderCache.Add(key, cached);
                cacheBytes += bytes;
            }
        }
        return bitmap;
    }, cancellationToken);
    public virtual void Dispose()
    {
        lock (cacheGate)
        {
            Disposed = true;
            renderCache.Clear();
            recentRenders.Clear();
            cacheBytes = 0;
        }
    }
}

/// <summary>Ordinal natural order, comparing arbitrarily long digit runs without numeric overflow.</summary>
public sealed class NaturalPathComparer : IComparer<string>
{
    public static NaturalPathComparer Instance { get; } = new();
    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;
        int a = 0, b = 0;
        while (a < x.Length && b < y.Length)
        {
            if (char.IsAsciiDigit(x[a]) && char.IsAsciiDigit(y[b]))
            {
                int ae = a, be = b;
                while (ae < x.Length && char.IsAsciiDigit(x[ae])) ae++;
                while (be < y.Length && char.IsAsciiDigit(y[be])) be++;
                int az = a, bz = b;
                while (az < ae - 1 && x[az] == '0') az++;
                while (bz < be - 1 && y[bz] == '0') bz++;
                int result = (ae - az).CompareTo(be - bz);
                if (result == 0) result = x.AsSpan(az, ae - az).SequenceCompareTo(y.AsSpan(bz, be - bz));
                if (result != 0) return result;
                a = ae; b = be;
            }
            else
            {
                int result = char.ToUpperInvariant(x[a]).CompareTo(char.ToUpperInvariant(y[b]));
                if (result != 0) return result;
                a++; b++;
            }
        }
        int length = (x.Length - a).CompareTo(y.Length - b);
        return length != 0 ? length : StringComparer.Ordinal.Compare(x, y);
    }
}

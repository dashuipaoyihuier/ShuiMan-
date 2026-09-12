using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace ShuiMan.Core;

/// <summary>A bounded exact-name archive view; never extracts book content to the filesystem.</summary>
internal sealed class BookArchive : IDisposable
{
    internal const long ResourceLimit = 256L * 1024 * 1024;
    private const long TotalLimit = 8L * 1024 * 1024 * 1024;
    private readonly ZipArchive zip;
    private readonly FileStream stream;
    private readonly Dictionary<string, ZipArchiveEntry> entries = new(StringComparer.Ordinal);
    private readonly object gate = new();
    private static readonly uint[] CrcTable = BuildCrcTable();
    private bool disposed;
    public IEnumerable<string> Paths => entries.Keys;

    public BookArchive(string path)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            var names = ReadCentralNames(stream);
            stream.Position = 0;
            zip = new ZipArchive(stream, ZipArchiveMode.Read, true, Encoding.GetEncoding(54936));
            if (zip.Entries.Count > 100_000) throw new InvalidDataException("压缩包包含过多资源（超过 100,000 项）。");
            long total = 0;
            for (int i = 0; i < zip.Entries.Count; i++)
            {
                var entry = zip.Entries[i];
                var name = names != null && i < names.Count ? names[i] : entry.FullName;
                if (name.EndsWith('/') || name.EndsWith('\\')) continue;
                var normalized = Normalize(name.Replace('\\', '/'));
                if (!entries.TryAdd(normalized, entry)) throw new InvalidDataException($"压缩包包含重名资源：{normalized}");
                total = checked(total + entry.Length);
                if (total > TotalLimit) throw new InvalidDataException("压缩包展开体积超过 8 GiB 上限。");
                if (entry.Length > 16L * 1024 * 1024 && entry.CompressedLength < entry.Length / 1000)
                    throw new InvalidDataException($"压缩包资源压缩比异常：{normalized}");
            }
        }
        catch { stream.Dispose(); throw; }
    }

    public bool Contains(string path) => entries.ContainsKey(path);
    public byte[] Bytes(string path, long limit = ResourceLimit)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var normalized = Normalize(path);
            if (!entries.TryGetValue(normalized, out var entry)) throw new FileNotFoundException($"缺少出版物资源：{normalized}");
            if (entry.Length > limit) throw new InvalidDataException($"资源超过读取上限：{normalized}");
            using var input = entry.Open();
            var bytes = DocumentEngine.ReadBounded(input, entry.Length, limit);
            if (Crc32(bytes) != entry.Crc32) throw new InvalidDataException($"资源 CRC 校验失败：{normalized}");
            return bytes;
        }
    }

    public static bool IsVisible(string path) => path.Split('/').All(part =>
        !part.StartsWith('.') && !part.Equals("__MACOSX", StringComparison.OrdinalIgnoreCase) &&
        !part.Equals("Thumbs.db", StringComparison.OrdinalIgnoreCase));

    public static string Normalize(string path)
    {
        if (path.StartsWith('/') || path.Contains('\\') || path.Contains('\0') || path.Contains(':'))
            throw new InvalidDataException("出版物资源路径无效。");
        var parts = new List<string>();
        foreach (var part in path.Split('/'))
        {
            if (part is "" or ".") continue;
            if (part == "..")
            {
                if (parts.Count == 0) throw new InvalidDataException("资源路径超出出版物范围。");
                parts.RemoveAt(parts.Count - 1);
            }
            else parts.Add(part);
        }
        if (parts.Count == 0) throw new InvalidDataException("出版物资源路径为空。");
        return string.Join('/', parts);
    }

    public static string Resolve(string reference, string relativeTo)
    {
        var cut = reference.IndexOfAny(['#', '?']);
        var path = Uri.UnescapeDataString(cut >= 0 ? reference[..cut] : reference);
        if (path.StartsWith('/') || path.Contains(':')) throw new InvalidDataException("不允许引用出版物以外的资源。");
        if (path.Length == 0) return Normalize(relativeTo);
        int slash = relativeTo.LastIndexOf('/');
        return Normalize((slash >= 0 ? relativeTo[..(slash + 1)] : "") + path);
    }

    // ZIP tools on Windows may omit the UTF-8 flag. Prefer Info-ZIP Unicode Path,
    // then valid UTF-8, then GB18030, preserving legacy Chinese chapter names.
    private static List<string>? ReadCentralNames(FileStream input)
    {
        int tailLength = (int)Math.Min(input.Length, 65557);
        if (tailLength < 22) throw new InvalidDataException("ZIP 文件被截断。");
        var tail = new byte[tailLength];
        input.Position = input.Length - tailLength;
        input.ReadExactly(tail);
        int eocd = -1;
        for (int i = tail.Length - 22; i >= 0; i--)
            if (U32(tail, i) == 0x06054b50 && i + 22 + U16(tail, i + 20) == tail.Length) { eocd = i; break; }
        if (eocd < 0) throw new InvalidDataException("找不到 ZIP 中央目录。");
        if (U16(tail, eocd + 4) != 0 || U16(tail, eocd + 6) != 0) throw new NotSupportedException("暂不支持分卷 ZIP，请合并后打开。");
        long directory = U32(tail, eocd + 16);
        long count = U16(tail, eocd + 10);
        if (count == ushort.MaxValue || directory == uint.MaxValue)
        {
            long endPosition = input.Length - tailLength + eocd;
            if (endPosition < 20) throw new InvalidDataException("ZIP64 目录无效。");
            input.Position = endPosition - 20;
            var locator = new byte[20]; input.ReadExactly(locator);
            if (U32(locator, 0) != 0x07064b50) throw new InvalidDataException("ZIP64 定位记录缺失。");
            var zip64Offset = BinaryPrimitives.ReadUInt64LittleEndian(locator.AsSpan(8));
            if (zip64Offset > (ulong)Math.Max(0, input.Length - 56)) throw new InvalidDataException("ZIP64 偏移越界。");
            input.Position = (long)zip64Offset;
            var header = new byte[56]; input.ReadExactly(header);
            if (U32(header, 0) != 0x06064b50) throw new InvalidDataException("ZIP64 目录无效。");
            count = checked((long)BinaryPrimitives.ReadUInt64LittleEndian(header.AsSpan(32)));
            directory = checked((long)BinaryPrimitives.ReadUInt64LittleEndian(header.AsSpan(48)));
        }
        if (count > 100_000 || count < 0 || directory < 0 || directory > input.Length) throw new InvalidDataException("ZIP 目录超出限制。");
        input.Position = directory;
        var names = new List<string>((int)count);
        var strictUtf8 = new UTF8Encoding(false, true);
        for (int i = 0; i < count; i++)
        {
            var header = new byte[46]; input.ReadExactly(header);
            if (U32(header, 0) != 0x02014b50) throw new InvalidDataException("ZIP 中央目录损坏。");
            if ((U16(header, 8) & 1) != 0) throw new NotSupportedException("此 ZIP 已加密，请使用无密码图片压缩包。");
            var name = new byte[U16(header, 28)]; input.ReadExactly(name);
            var extra = new byte[U16(header, 30)]; input.ReadExactly(extra);
            input.Seek(U16(header, 32), SeekOrigin.Current);
            string decoded;
            try { decoded = strictUtf8.GetString(name); }
            catch (DecoderFallbackException) { decoded = Encoding.GetEncoding(54936).GetString(name); }
            for (int j = 0; j + 4 <= extra.Length;)
            {
                int id = U16(extra, j), size = U16(extra, j + 2);
                if (size > extra.Length - j - 4) break;
                if (id == 0x7075 && size >= 5 && extra[j + 4] == 1 && U32(extra, j + 5) == Crc32(name))
                {
                    try { decoded = strictUtf8.GetString(extra, j + 9, size - 5); } catch (DecoderFallbackException) { }
                }
                j += 4 + size;
            }
            names.Add(decoded);
        }
        return names;
    }
    private static ushort U16(byte[] data, int start) => BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(start, 2));
    private static uint U32(byte[] data, int start) => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(start, 4));
    private static uint Crc32(byte[] data)
    {
        uint crc = uint.MaxValue;
        foreach (byte value in data) crc = (crc >> 8) ^ CrcTable[(crc ^ value) & 255];
        return ~crc;
    }
    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < table.Length; i++)
        {
            uint value = i;
            for (int j = 0; j < 8; j++) value = (value >> 1) ^ ((value & 1) != 0 ? 0xedb88320U : 0);
            table[i] = value;
        }
        return table;
    }
    public void Dispose()
    {
        lock (gate) { if (disposed) return; disposed = true; zip.Dispose(); stream.Dispose(); }
    }
}

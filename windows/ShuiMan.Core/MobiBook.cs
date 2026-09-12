using System.Buffers.Binary;
using System.IO;
using System.Text;
using AngleSharp.Html.Parser;

namespace ShuiMan.Core;

/// <summary>Unencrypted MOBI 6 image comics, including the MOBI 6 half of hybrid books.</summary>
internal sealed class MobiBook : RasterBook
{
    private const int TextLimit = 32 * 1024 * 1024;
    private readonly byte[] data;
    private readonly int[] offsets;
    private readonly Dictionary<int, (RasterInfo? Info, string? Error)> infos = [];

    public MobiBook(string path, CancellationToken token)
    {
        Publication = DocumentEngine.Describe(path, "mobi");
        data = DocumentEngine.ReadFile(path, 512L * 1024 * 1024);
        if (data.Length < 78 || !data.AsSpan(60, 8).SequenceEqual("BOOKMOBI"u8)) throw Invalid("不是 BOOKMOBI 容器。");
        int count = Number(data, 76, 2);
        if (count < 2 || 78 + count * 8 > data.Length) throw Invalid("记录表不完整。");
        offsets = new int[count + 1];
        for (int i = 0; i < count; i++)
        {
            int offset = Number(data, 78 + i * 8, 4);
            if (offset < 78 + count * 8 || offset >= data.Length || (i > 0 && offset <= offsets[i - 1])) throw Invalid("记录偏移越界或无序。");
            offsets[i] = offset;
        }
        offsets[count] = data.Length;
        var header = Record(0);
        int compression = Number(header, 0, 2), textLength = Number(header, 4, 4), textRecords = Number(header, 8, 2);
        if (Number(header, 12, 2) != 0) throw new NotSupportedException("此 MOBI 受 DRM 保护，无法读取。请使用无 DRM 的漫画文件。");
        if (header.Length < 116 || !header.AsSpan(16, 4).SequenceEqual("MOBI"u8)) throw Invalid("缺少 MOBI 头。");
        int length = Number(header, 20, 4), version = Number(header, 36, 4);
        if (length < 100 || length > header.Length - 16) throw Invalid("MOBI 头长度越界。");
        if (version > 6) throw new NotSupportedException("当前支持 MOBI 6 及含 MOBI 6 的双格式漫画；纯 KF8/AZW3 请先转换为 EPUB。");
        if (compression is not (1 or 2)) throw new NotSupportedException("此 MOBI 使用尚未支持的 HUFF/CDIC 或其他压缩方式，请先转换为 EPUB。");
        if (textLength <= 0 || textLength > TextLimit || textRecords <= 0 || textRecords >= count) throw Invalid("正文长度或记录数量异常。");
        int imageStart = Number(header, 108, 4);
        if (imageStart <= textRecords || imageStart >= count) throw Invalid("图片记录起点无效。");
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        int encodingId = Number(header, 28, 4);
        Encoding encoding = encodingId switch
        {
            65001 => new UTF8Encoding(false, true), 1252 => Encoding.GetEncoding(1252),
            _ => throw new NotSupportedException($"暂不支持此 MOBI 的文字编码（{encodingId}）。")
        };
        var metadata = new Dictionary<int, byte[]>();
        if (length >= 116 && (Number(header, 128, 4) & 0x40) != 0)
        {
            int start = 16 + length;
            if (start + 12 > header.Length || !header.AsSpan(start, 4).SequenceEqual("EXTH"u8)) throw Invalid("EXTH 元数据缺失。");
            int size = Number(header, start + 4, 4), entries = Number(header, start + 8, 4);
            if (size < 12 || size > header.Length - start || entries > (size - 12) / 8) throw Invalid("EXTH 长度无效。");
            int cursor = start + 12;
            for (int i = 0; i < entries; i++)
            {
                if (cursor + 8 > start + size) throw Invalid("EXTH 记录越界。");
                int type = Number(header, cursor, 4), bytes = Number(header, cursor + 4, 4);
                if (bytes < 8 || bytes > start + size - cursor) throw Invalid("EXTH 记录长度无效。");
                metadata[type] = header.AsSpan(cursor + 8, bytes - 8).ToArray();
                cursor += bytes;
            }
        }
        int nameOffset = Number(header, 84, 4), nameLength = Number(header, 88, 4);
        if (nameOffset <= header.Length && nameLength <= header.Length - nameOffset)
        {
            string title = encoding.GetString(header, nameOffset, nameLength).Trim('\0', ' ', '\n', '\r');
            if (title.Length > 0) Publication.Title = title;
        }
        if (metadata.TryGetValue(503, out var titleBytes))
        {
            var title = encoding.GetString(titleBytes).Trim();
            if (title.Length > 0) Publication.Title = title;
        }
        // EXTH 525 is writing mode, not authoritative physical page progression.
        if (metadata.TryGetValue(121, out var boundary) && boundary.Length == 4 && BinaryPrimitives.ReadUInt32BigEndian(boundary) != uint.MaxValue)
            Publication.Warnings.Add("双格式 MOBI：当前读取兼容正文，KF8 专属布局和局部放大信息未应用。");
        uint extra = length >= 228 ? (uint)Number(header, 240, 4) : 0;
        using var text = new MemoryStream();
        for (int i = 1; i <= textRecords; i++)
        {
            token.ThrowIfCancellationRequested();
            var record = StripTrailer(Record(i), extra);
            var decoded = compression == 2 ? Decompress(record) : record;
            if (decoded.Length > TextLimit - text.Length) throw Invalid("正文解压超过安全上限。");
            text.Write(decoded);
        }
        if (text.Length < textLength) throw Invalid("正文解压长度不足。");
        var document = new HtmlParser().ParseDocument(encoding.GetString(text.GetBuffer(), 0, textLength));
        var body = document.Body;
        if (body == null || !string.IsNullOrWhiteSpace(body.TextContent))
            throw new NotSupportedException("此 MOBI 含文字或混合排版，当前漫画模式不能完整呈现，请转换为 EPUB 阅读。");
        if (body.QuerySelector("svg, script, iframe, object, canvas, table, picture, video, audio") != null)
            throw new NotSupportedException("此 MOBI 含复杂页面，当前漫画模式不支持。请转换为 EPUB 阅读。");
        var images = body.QuerySelectorAll("img");
        if (images.Length == 0 || images.Length > 100_000) throw Invalid("没有可阅读的图片正文。");
        var refs = images.Select(image => int.TryParse(image.GetAttribute("recindex"), out int index) && index > 0 && index <= count ? (int?)(imageStart + index - 1) : null).ToList();
        int? cover = null;
        if (metadata.TryGetValue(201, out var coverData) && coverData.Length == 4)
        {
            uint relative = BinaryPrimitives.ReadUInt32BigEndian(coverData);
            if (relative < count - imageStart) cover = imageStart + (int)relative;
        }
        if (cover != null && !refs.Contains(cover)) Publication.Units.Add(MakeUnit(cover, -1, true));
        for (int i = 0; i < refs.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            Publication.Units.Add(MakeUnit(refs[i], i, refs[i] != null && refs[i] == cover));
        }
        if (Publication.Units.Any(x => x.Error != null)) Publication.Warnings.Add("部分图片缺失或无法解码，已保留原阅读位置。");
        Publication.Warnings.Add("MOBI 图片模式按正文图片顺序阅读，不还原 HTML/CSS 拼版。");
    }

    private ReadingUnit MakeUnit(int? index, int occurrence, bool cover)
    {
        string resource = index != null ? $"mobi:record:{index}" : "mobi:missing";
        (RasterInfo? Info, string? Error) result;
        if (index == null) result = (null, "MOBI 图片记录缺失。");
        else if (!infos.TryGetValue(index.Value, out result))
        {
            try { result = (RasterDecoder.Describe(Record(index.Value)).First(), null); }
            catch (Exception ex) when (ex is not OperationCanceledException) { result = (null, $"MOBI 图片无法读取：{ex.Message}"); }
            infos[index.Value] = result;
        }
        return new ReadingUnit
        {
            Locator = new SourceLocator(resource, occurrence), Title = cover ? "封面" : $"第 {occurrence + 1} 页",
            Width = result.Info?.Width ?? 0, Height = result.Info?.Height ?? 0, IsCover = cover, Error = result.Error
        };
    }
    private byte[] Record(int index)
    {
        if (index < 0 || index + 1 >= offsets.Length) throw Invalid("图片引用越界。");
        int length = offsets[index + 1] - offsets[index];
        if (length > BookArchive.ResourceLimit) throw Invalid("单个记录超过读取上限。");
        return data.AsSpan(offsets[index], length).ToArray();
    }
    private static byte[] StripTrailer(byte[] input, uint flags)
    {
        int end = input.Length;
        for (uint bits = flags >> 1; bits != 0; bits >>= 1)
        {
            if ((bits & 1) == 0) continue;
            int size = 0, shift = 0;
            bool terminated = false;
            for (int i = end - 1; i >= Math.Max(0, end - 4); i--)
            {
                int value = input[i]; size |= (value & 127) << shift; shift += 7;
                if ((value & 128) != 0) { terminated = true; break; }
            }
            if (!terminated || size <= 0 || size > end) throw Invalid("正文尾部索引损坏。");
            end -= size;
        }
        if ((flags & 1) != 0)
        {
            if (end == 0) throw Invalid("多字节尾部被截断。");
            int size = (input[end - 1] & 3) + 1;
            if (size > end) throw Invalid("多字节尾部越界。");
            end -= size;
        }
        return input.AsSpan(0, end).ToArray();
    }
    private static byte[] Decompress(byte[] input)
    {
        var output = new List<byte>();
        int i = 0;
        while (i < input.Length)
        {
            int value = input[i++];
            if (value is >= 1 and <= 8)
            {
                if (value > input.Length - i) throw Invalid("PalmDOC 字面量被截断。");
                for (int j = 0; j < value; j++) output.Add(input[i++]);
            }
            else if (value < 128) output.Add((byte)value);
            else if (value >= 192) { output.Add(32); output.Add((byte)(value ^ 128)); }
            else
            {
                if (i >= input.Length) throw Invalid("PalmDOC 回引用被截断。");
                int reference = (value << 8) | input[i++], distance = (reference >> 3) & 2047, length = (reference & 7) + 3;
                if (distance == 0 || distance > output.Count) throw Invalid("PalmDOC 回引用越界。");
                for (int j = 0; j < length; j++) output.Add(output[output.Count - distance]);
            }
            if (output.Count > 65536) throw Invalid("PalmDOC 记录解压超过上限。");
        }
        return output.ToArray();
    }
    private static int Number(byte[] bytes, int offset, int width)
    {
        if (offset < 0 || width <= 0 || offset > bytes.Length - width) throw Invalid("记录被截断。");
        uint value = 0;
        for (int i = 0; i < width; i++) value = (value << 8) | bytes[offset + i];
        if (value > int.MaxValue) throw Invalid("记录数值超出范围。");
        return (int)value;
    }
    private static InvalidDataException Invalid(string message) => new($"MOBI 文件无效：{message}");
    public override byte[]? Resource(string path)
    {
        ObjectDisposedException.ThrowIf(Disposed, this);
        return path.StartsWith("mobi:record:", StringComparison.Ordinal) && int.TryParse(path[12..], out int index) ? Record(index) : null;
    }
}

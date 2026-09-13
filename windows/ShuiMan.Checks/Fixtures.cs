using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ShuiMan.Checks;

/// <summary>Original vector artwork and minimal format documents. No user books.</summary>
internal static class Fixtures
{
    public static void Generate(string directory)
    {
        Directory.CreateDirectory(directory);
        var first = Image(480, 720, Colors.DarkSlateBlue, "01", "SHUIMAN / WINDOWS");
        var second = Image(480, 720, Colors.Teal, "02", "A QUIET AFTERNOON");
        var tenth = Image(960, 720, Colors.DarkOrange, "10", "A WIDER WORLD");
        Archive(Path.Combine(directory, "水漫 演示.zip"), new Dictionary<string, byte[]>
        {
            ["漫画/10.png"] = tenth,
            ["漫画/2.png"] = second,
            ["漫画/1.png"] = first,
            ["漫画/readme.txt"] = Encoding.UTF8.GetBytes("Original generated artwork for regression testing."),
            ["__MACOSX/._1.png"] = first
        });
        File.Copy(Path.Combine(directory, "水漫 演示.zip"), Path.Combine(directory, "demo.cbz"), true);
        var imageDirectory = Path.Combine(directory, "image-folder");
        Directory.CreateDirectory(Path.Combine(imageDirectory, "章节"));
        File.WriteAllBytes(Path.Combine(imageDirectory, "章节", "10.png"), tenth);
        File.WriteAllBytes(Path.Combine(imageDirectory, "章节", "2.png"), second);
        File.WriteAllBytes(Path.Combine(imageDirectory, "章节", "1.png"), first);
        File.WriteAllText(Path.Combine(imageDirectory, "notes.txt"), "Not an image.");
        File.WriteAllBytes(Path.Combine(directory, "single.png"), first);
        File.WriteAllBytes(Path.Combine(directory, "frames.tiff"), Tiff(first, second));
        Archive(Path.Combine(directory, "frames.zip"), new() { ["pages.tiff"] = Tiff(first, second) });
        Archive(Path.Combine(directory, "empty.zip"), new() { ["notes.txt"] = [1, 2, 3] });
        File.WriteAllText(Path.Combine(directory, "broken.zip"), "This is deliberately not a ZIP file.");
        Archive(Path.Combine(directory, "corrupt-page.zip"), new()
        {
            ["1.png"] = first,
            ["2.png"] = Encoding.UTF8.GetBytes("Broken image bytes"),
            ["3.png"] = second
        });
        Archive(Path.Combine(directory, "unsafe.zip"), new() { ["../outside.png"] = first, ["1.png"] = first });
        GenerateEpub(Path.Combine(directory, "spine.epub"), first, second);
        GeneratePdf(Path.Combine(directory, "sample.pdf"));
        GeneratePdf(Path.Combine(directory, "password.pdf"), "reading-pass");
        GenerateMobi(directory, first, second, tenth);
        File.WriteAllBytes(Path.Combine(directory, "single.jpg"), Reencode(first, new JpegBitmapEncoder { QualityLevel = 90 }));
        Archive(Path.Combine(directory, "mixed-images.zip"), new()
        {
            ["1.jpg"] = Reencode(first, new JpegBitmapEncoder { QualityLevel = 90 }),
            ["2.gif"] = Reencode(second, new GifBitmapEncoder()),
            ["3.bmp"] = Reencode(tenth, new BmpBitmapEncoder())
        });
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Archive(Path.Combine(directory, "legacy-filenames.zip"), new() { ["第一章/第2页.png"] = second, ["第一章/第1页.png"] = first }, Encoding.GetEncoding("GB18030"));
        Archive(Path.Combine(directory, "complex.epub"), new()
        {
            ["META-INF/container.xml"] = Utf8("<container xmlns=\"urn:oasis:names:tc:opendocument:xmlns:container\"><rootfiles><rootfile full-path=\"OPS/book.opf\"/></rootfiles></container>"),
            ["OPS/book.opf"] = Utf8("<package xmlns=\"http://www.idpf.org/2007/opf\" version=\"3.0\"><manifest><item id=\"story\" href=\"story.xhtml\" media-type=\"application/xhtml+xml\"/></manifest><spine><itemref idref=\"story\"/></spine></package>"),
            ["OPS/story.xhtml"] = Utf8("<html xmlns=\"http://www.w3.org/1999/xhtml\"><head><link rel=\"stylesheet\" href=\"style.css\"/></head><body><p>Original story text must remain visible.</p><img src=\"image.png\"/></body></html>"),
            ["OPS/style.css"] = Utf8("body { color: #164e63; }"),
            ["OPS/image.png"] = first
        });
    }

    private static void GenerateEpub(string path, byte[] first, byte[] second)
    {
        Archive(path, new()
        {
            ["mimetype"] = Encoding.ASCII.GetBytes("application/epub+zip"),
            ["META-INF/container.xml"] = Utf8("""
                <?xml version="1.0"?>
                <container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container"><rootfiles><rootfile full-path="OPS/book.opf" media-type="application/oebps-package+xml"/></rootfiles></container>
                """),
            ["OPS/book.opf"] = Utf8("""
                <?xml version="1.0" encoding="UTF-8"?>
                <package xmlns="http://www.idpf.org/2007/opf" version="3.0" unique-identifier="id">
                <metadata xmlns:dc="http://purl.org/dc/elements/1.1/"><dc:identifier id="id">generated-shuiman-checks</dc:identifier><dc:title>Generated spine order</dc:title><dc:language>en</dc:language></metadata>
                <manifest><item id="two" href="two.xhtml" media-type="application/xhtml+xml"/><item id="one" href="one.xhtml" media-type="application/xhtml+xml"/><item id="missing" href="missing.xhtml" media-type="application/xhtml+xml"/><item id="image1" href="images/1.png" media-type="image/png"/><item id="image2" href="images/2.png" media-type="image/png"/><item id="nav" href="nav.xhtml" media-type="application/xhtml+xml" properties="nav"/></manifest>
                <spine page-progression-direction="rtl"><itemref idref="two"/><itemref idref="one"/><itemref idref="two"/><itemref idref="missing"/></spine></package>
                """),
            ["OPS/one.xhtml"] = Utf8("<html xmlns=\"http://www.w3.org/1999/xhtml\"><head><title>One</title></head><body><img src=\"images/1.png\" alt=\"One\"/></body></html>"),
            ["OPS/two.xhtml"] = Utf8("<html xmlns=\"http://www.w3.org/1999/xhtml\"><head><title>Two</title></head><body><img src=\"images/2.png\" alt=\"Two\"/></body></html>"),
            ["OPS/nav.xhtml"] = Utf8("<html xmlns=\"http://www.w3.org/1999/xhtml\" xmlns:epub=\"http://www.idpf.org/2007/ops\"><body><nav epub:type=\"toc\"><ol><li><a href=\"two.xhtml\">Second first</a></li><li><a href=\"one.xhtml\">First second</a></li></ol></nav></body></html>"),
            ["OPS/images/1.png"] = first,
            ["OPS/images/2.png"] = second
        });
    }

    private static void GeneratePdf(string path, string? password = null)
    {
        const string drawing = "0.1 0.3 0.6 rg 0 0 300 400 re f 1 0.8 0.2 rg 60 80 180 240 re f";
        var content = Encoding.ASCII.GetBytes(drawing);
        string? encryption = null;
        var trailerEntries = "";
        if (password != null)
        {
            // Test fixture only: PDF 1.4 Standard security handler revision 2,
            // Adobe PDF Reference algorithms 3.1-3.4. Independent of the renderer.
            // https://opensource.adobe.com/dc-acrobat-sdk-docs/pdfstandards/pdfreference1.4.pdf
            var padding = Convert.FromHexString("28BF4E5E4E758A4164004E56FFFA01082E2E00B6D0683E802F0CA9FE6453697A");
            byte[] Pad(string value) => Encoding.ASCII.GetBytes(value).Concat(padding).Take(32).ToArray();
            var owner = Rc4(MD5.HashData(Pad("fixture-owner"))[..5], Pad(password));
            var id = MD5.HashData("ShuiMan generated encrypted PDF"u8);
            var key = MD5.HashData([.. Pad(password), .. owner, 252, 255, 255, 255, .. id])[..5];
            var user = Rc4(key, padding);
            var objectKey = MD5.HashData([.. key, 4, 0, 0, 0, 0])[..10];
            content = Rc4(objectKey, content);
            encryption = $"<< /Filter /Standard /V 1 /R 2 /Length 40 /O <{Convert.ToHexString(owner)}> /U <{Convert.ToHexString(user)}> /P -4 >>";
            trailerEntries = $" /Encrypt 6 0 R /ID [<{Convert.ToHexString(id)}> <{Convert.ToHexString(id)}>]";
        }
        List<byte[]> objects =
        [
            Utf8("<< /Type /Catalog /Pages 2 0 R >>"),
            Utf8("<< /Type /Pages /Kids [3 0 R 5 0 R] /Count 2 >>"),
            Utf8("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 400] /Contents 4 0 R /Resources << >> >>"),
            [.. Utf8($"<< /Length {content.Length} >>\nstream\n"), .. content, .. Utf8("\nendstream")],
            Utf8("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 600 400] /Contents 4 0 R /Resources << >> >>")
        ];
        if (encryption != null) objects.Add(Utf8(encryption));
        using var stream = File.Create(path);
        void Write(string value) => stream.Write(Encoding.ASCII.GetBytes(value));
        Write("%PDF-1.4\n");
        var offsets = new List<long> { 0 };
        for (var i = 0; i < objects.Count; i++)
        {
            offsets.Add(stream.Position);
            Write($"{i + 1} 0 obj\n");
            stream.Write(objects[i]);
            Write("\nendobj\n");
        }
        var xref = stream.Position;
        Write($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1)) Write($"{offset:0000000000} 00000 n \n");
        Write($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R{trailerEntries} >>\nstartxref\n{xref}\n%%EOF\n");
    }

    private static byte[] Rc4(byte[] key, byte[] input)
    {
        var state = Enumerable.Range(0, 256).ToArray();
        var j = 0;
        for (var i = 0; i < 256; i++)
        {
            j = (j + state[i] + key[i % key.Length]) & 255;
            (state[i], state[j]) = (state[j], state[i]);
        }
        var x = 0;
        j = 0;
        var output = new byte[input.Length];
        for (var i = 0; i < input.Length; i++)
        {
            x = (x + 1) & 255;
            j = (j + state[x]) & 255;
            (state[x], state[j]) = (state[j], state[x]);
            output[i] = (byte)(input[i] ^ state[(state[x] + state[j]) & 255]);
        }
        return output;
    }

    private static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);

    private static byte[] Reencode(byte[] image, BitmapEncoder encoder)
    {
        using var source = new MemoryStream(image);
        encoder.Frames.Add(BitmapFrame.Create(source, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad));
        using var output = new MemoryStream();
        encoder.Save(output);
        return output.ToArray();
    }

    private static void GenerateMobi(string directory, byte[] portrait, byte[] cover, byte[] wide)
    {
        static void Put(byte[] data, int value, int offset, int width)
        {
            for (var i = 0; i < width; i++) data[offset + i] = (byte)(value >> ((width - i - 1) * 8));
        }
        byte[] Book(string html, bool compressed = false)
        {
            var original = Utf8(html);
            using var text = new MemoryStream();
            var i = 0;
            while (i < original.Length)
            {
                if (compressed)
                {
                    var bestLength = 0;
                    var bestDistance = 0;
                    for (var distance = 1; distance <= Math.Min(i, 2047); distance++)
                    {
                        var length = 0;
                        while (length < 10 && i + length < original.Length && original[i + length] == original[i + length - distance]) length++;
                        if (length >= 3 && length > bestLength) { bestLength = length; bestDistance = distance; }
                    }
                    if (bestLength >= 3)
                    {
                        var value = 0x8000 | bestDistance << 3 | bestLength - 3;
                        text.WriteByte((byte)(value >> 8)); text.WriteByte((byte)value); i += bestLength;
                        continue;
                    }
                    if (original[i] >= 128 || original[i] is >= 1 and <= 8) text.WriteByte(1);
                }
                text.WriteByte(original[i++]);
            }
            if (compressed) { text.WriteByte(0); text.WriteByte(0x81); }
            var header = new byte[304];
            Put(header, compressed ? 2 : 1, 0, 2); Put(header, original.Length, 4, 4);
            Put(header, 1, 8, 2); Put(header, 4096, 10, 2);
            "MOBI"u8.CopyTo(header.AsSpan(16));
            Put(header, 264, 20, 4); Put(header, 2, 24, 4); Put(header, 65001, 28, 4);
            Put(header, 6, 36, 4); Put(header, 2, 108, 4); Put(header, 0x40, 128, 4);
            Put(header, compressed ? 3 : 0, 240, 4);
            "EXTH"u8.CopyTo(header.AsSpan(280));
            Put(header, 24, 284, 4); Put(header, 1, 288, 4); Put(header, 201, 292, 4); Put(header, 12, 296, 4); Put(header, 1, 300, 4);
            byte[][] records = [header, text.ToArray(), portrait, cover, wide];
            var result = new byte[78 + records.Length * 8 + 2 + records.Sum(r => r.Length)];
            "BOOKMOBI"u8.CopyTo(result.AsSpan(60));
            Put(result, records.Length, 76, 2);
            var cursor = 78 + records.Length * 8 + 2;
            for (var record = 0; record < records.Length; record++)
            {
                Put(result, cursor, 78 + record * 8, 4);
                records[record].CopyTo(result, cursor);
                cursor += records[record].Length;
            }
            return result;
        }
        const string body = "<html><body><img recindex='3'><mbp:pagebreak/><img recindex='1'><img recindex='3'><img recindex='99'></body></html>";
        var plain = Book(body);
        File.WriteAllBytes(Path.Combine(directory, "plain.mobi"), plain);
        File.WriteAllBytes(Path.Combine(directory, "compressed.mobi"), Book(body, true));
        File.WriteAllBytes(Path.Combine(directory, "body-cover.mobi"), Book("<img recindex='2'><img recindex='1'>"));
        foreach (var (name, offset, bytes, value) in new[]
        {
            ("drm", 132, 2, 1), ("kf8", 156, 4, 8), ("huff", 120, 2, 17480), ("bounds", 78, 4, 1), ("encoding", 148, 4, 42)
        })
        {
            var invalid = (byte[])plain.Clone();
            Put(invalid, value, offset, bytes);
            File.WriteAllBytes(Path.Combine(directory, "invalid-" + name + ".mobi"), invalid);
        }
        File.WriteAllBytes(Path.Combine(directory, "invalid-truncated.mobi"), plain[..90]);
        File.WriteAllBytes(Path.Combine(directory, "invalid-mixed.mobi"), Book("<p>Required story text</p><img recindex='1'>"));
    }

    internal static void Archive(string path, Dictionary<string, byte[]> entries, Encoding? encoding = null)
    {
        using var stream = File.Create(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create, false, encoding ?? Encoding.UTF8);
        foreach (var (name, bytes) in entries)
        {
            var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
            using var destination = entry.Open();
            destination.Write(bytes);
        }
    }

    private static byte[] Tiff(params byte[][] images)
    {
        var encoder = new TiffBitmapEncoder { Compression = TiffCompressOption.Zip };
        foreach (var image in images)
        {
            using var source = new MemoryStream(image);
            encoder.Frames.Add(BitmapFrame.Create(source, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad));
        }
        using var output = new MemoryStream();
        encoder.Save(output);
        return output.ToArray();
    }

    private static byte[] Image(int width, int height, Color background, string number, string caption)
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(new SolidColorBrush(background), null, new Rect(0, 0, width, height));
            context.DrawEllipse(new SolidColorBrush(Color.FromRgb(247, 221, 156)), null,
                new Point(width * .75, height * .25), height * .12, height * .12);
            context.DrawRectangle(new SolidColorBrush(Color.FromRgb(219, 235, 230)), null,
                new Rect(width * .1, height * .52, width * .8, height * .35));
            var pen = new Pen(new SolidColorBrush(Color.FromRgb(27, 63, 77)), 6);
            for (var i = 0; i < 5; i++)
                context.DrawLine(pen, new Point(width * .16, height * (.6 + i * .045)), new Point(width * .82, height * (.56 + i * .045)));
            var typeface = new Typeface("Segoe UI");
            context.DrawText(new FormattedText(number, CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, typeface, 84, Brushes.White, 1), new Point(36, 36));
            context.DrawText(new FormattedText(caption, CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, typeface, 18, Brushes.White, 1), new Point(38, height - 60));
        }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = new MemoryStream();
        encoder.Save(output);
        return output.ToArray();
    }
}

using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ShuiMan.UiChecks;

/// <summary>Original, code-drawn artwork for library appearance and navigation checks.</summary>
internal static class ShowcaseFixtures
{
    private sealed record Edition(string Series, string Title, string Subtitle, string Paper, string Ink, string Accent, int Scene);

    private static readonly Edition[] Editions =
    [
        new("潮汐来信", "潮汐来信 · 海的另一边", "LETTERS FROM THE TIDE  /  01", "#EDE7DA", "#235168", "#E59A65", 0),
        new("潮汐来信", "潮汐来信 · 风经过的岛", "LETTERS FROM THE TIDE  /  02", "#F1E8DD", "#507D87", "#DDA873", 1),
        new("潮汐来信", "潮汐来信 · 晚潮", "LETTERS FROM THE TIDE  /  03", "#F3E3D6", "#536479", "#D68E78", 0),
        new("山间慢行", "山间慢行 · 远山", "A WALK IN THE HILLS  /  01", "#E7E8DD", "#375B4D", "#C9B36D", 1),
        new("山间慢行", "山间慢行 · 雨后", "A WALK IN THE HILLS  /  02", "#E3E8E5", "#416874", "#A6B39A", 1),
        new("山间慢行", "山间慢行 · 秋日手记", "A WALK IN THE HILLS  /  03", "#F1E5CF", "#6C634A", "#BF754F", 1),
        new("夜航星图", "夜航星图 · 月亮邮局", "THE NIGHT ATLAS  /  01", "#DCDDE5", "#343D61", "#D6AC77", 2),
        new("夜航星图", "夜航星图 · 第七颗星", "THE NIGHT ATLAS  /  02", "#E0E5EA", "#364F70", "#B6C2C9", 2),
        new("城市漫游", "城市漫游 · 星期天", "A CITY TO WANDER  /  01", "#EEE1DD", "#6D5668", "#D08B72", 3),
        new("城市漫游", "城市漫游 · 窗里的光", "A CITY TO WANDER  /  02", "#F1E5DB", "#72594E", "#D6AD72", 3)
    ];

    public static IReadOnlyList<string> Generate(string directory)
    {
        Directory.CreateDirectory(directory);
        var files = new List<string>();
        foreach (var edition in Editions)
        {
            var seriesDirectory = Path.Combine(directory, edition.Series);
            Directory.CreateDirectory(seriesDirectory);
            var archivePath = Path.Combine(seriesDirectory, edition.Title + ".zip");
            using var zip = ZipFile.Open(archivePath, ZipArchiveMode.Create);
            for (var page = 0; page < 6; page++)
            {
                using var image = zip.CreateEntry($"{page + 1:000}.png", CompressionLevel.Optimal).Open();
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(Draw(edition, page)));
                using var encoded = new MemoryStream();
                encoder.Save(encoded);
                encoded.Position = 0;
                encoded.CopyTo(image);
            }
            files.Add(archivePath);
        }
        File.WriteAllText(Path.Combine(directory, "生成说明.txt"),
            "水漫界面检查用原创演示作品。封面、画面、书名与系列名均由仓库内代码生成；不含外部素材、真实出版物或用户漫画。\n" +
            "这些作品用于书库封面、阅读导航和视觉检查，不用于证明跨页或方向自动识别效果。\n", Encoding.UTF8);
        return files;
    }

    public static void GenerateSmallArchive(string path, string title, int pages = 4)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var edition = Editions[0] with { Title = title };
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        for (var page = 0; page < pages; page++)
        {
            using var image = zip.CreateEntry($"{page + 1:000}.png").Open();
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(Draw(edition, page)));
            using var encoded = new MemoryStream();
            encoder.Save(encoded);
            encoded.Position = 0;
            encoded.CopyTo(image);
        }
    }

    public static void GenerateImageFolder(string path)
    {
        Directory.CreateDirectory(path);
        for (var page = 0; page < 3; page++)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(Draw(Editions[3], page)));
            using var output = File.Create(Path.Combine(path, $"{page + 1:000}.png"));
            encoder.Save(output);
        }
    }

    private static RenderTargetBitmap Draw(Edition edition, int page)
    {
        const double width = 600, height = 900;
        var paper = Brush(edition.Paper); var ink = Brush(edition.Ink); var accent = Brush(edition.Accent);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(paper, null, new Rect(0, 0, width, height));
            if (page == 0)
            {
                Text(dc, "水漫原创绘本", 38, 42, 14, ink);
                Text(dc, edition.Subtitle, 38, 83, 11, ink);
                var parts = edition.Title.Split(" · ");
                Text(dc, parts[0], 35, 132, 45, ink, FontWeights.Light);
                Text(dc, parts.Length > 1 ? parts[1] : "绘本", 39, 200, 23, ink);
                dc.PushClip(new RectangleGeometry(new Rect(0, 286, width, 522)));
                Scene(dc, edition.Scene, ink, accent, paper, 0);
                dc.Pop();
                dc.DrawLine(new Pen(ink, 0.6), new Point(38, 842), new Point(562, 842));
                Text(dc, "慢一点，让故事发生。", 38, 860, 12, ink);
                Text(dc, "SHUIMAN  /  ORIGINAL", 403, 862, 9, ink);
            }
            else
            {
                Text(dc, edition.Series, 32, 22, 13, ink);
                Text(dc, $"{page + 1:00}", 545, 22, 13, ink);
                // Separate panels are illustration fixtures, not spread-detection evidence.
                dc.PushClip(new RectangleGeometry(new Rect(32, 76, 536, 354)));
                dc.PushTransform(new TranslateTransform(0, -210));
                Scene(dc, edition.Scene, ink, accent, paper, page);
                dc.Pop(); dc.Pop();
                dc.PushClip(new RectangleGeometry(new Rect(32, 464, 255, 345)));
                dc.PushTransform(new TranslateTransform(-90, 123));
                Scene(dc, edition.Scene, ink, accent, paper, page + 1);
                dc.Pop(); dc.Pop();
                dc.PushClip(new RectangleGeometry(new Rect(312, 464, 256, 345)));
                dc.PushTransform(new TranslateTransform(170, 132));
                Scene(dc, edition.Scene, accent, ink, paper, page + 2);
                dc.Pop(); dc.Pop();
                Text(dc, "在平常的日子里，也有值得停留的风景。", 34, 848, 16, ink);
            }
        }
        var bitmap = new RenderTargetBitmap((int)width, (int)height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual); bitmap.Freeze(); return bitmap;
    }

    private static void Scene(DrawingContext dc, int scene, SolidColorBrush ink, SolidColorBrush accent, SolidColorBrush paper, int variation)
    {
        dc.DrawRectangle(accent, null, new Rect(-200, 260, 1000, 600));
        switch (scene)
        {
            case 0:
                dc.DrawEllipse(paper, null, new Point(445 - variation * 9, 397), 79, 79);
                for (var i = 0; i < 7; i++)
                {
                    var y = 530 + i * 44;
                    var geometry = Geometry.Parse($"M -200,{y} C 100,{y - 80} 300,{y + 70} 800,{y - 8} L 800,880 -200,880 Z");
                    dc.DrawGeometry(i % 2 == 0 ? ink : paper, null, geometry);
                }
                dc.DrawGeometry(accent, null, Geometry.Parse("M 280,554 L 385,554 360,579 306,579 Z"));
                dc.DrawGeometry(paper, null, Geometry.Parse("M 334,454 L 334,546 286,546 Z"));
                break;
            case 1:
                dc.DrawEllipse(paper, null, new Point(436, 410), 62, 62);
                dc.DrawGeometry(ink, null, Geometry.Parse("M -100,740 L 93,404 178,559 265,378 477,676 620,553 800,820 Z"));
                dc.DrawGeometry(paper, null, Geometry.Parse("M 169,521 L 265,378 356,506 288,480 269,459 243,491 213,478 Z"));
                dc.DrawGeometry(paper, null, Geometry.Parse("M -100,770 Q 270,569 750,772 L 750,890 -100,890 Z"));
                dc.DrawGeometry(ink, null, Geometry.Parse("M -100,805 Q 300,647 750,784 L 750,890 -100,890 Z"));
                dc.DrawLine(new Pen(accent, 3), new Point(375, 702), new Point(345, 796));
                break;
            case 2:
                dc.DrawRectangle(ink, null, new Rect(-200, 260, 1000, 600));
                dc.DrawEllipse(paper, null, new Point(425, 393), 73, 73);
                dc.DrawEllipse(ink, null, new Point(455, 363), 66, 66);
                for (var i = 0; i < 36; i++)
                    dc.DrawEllipse(i % 3 == 0 ? accent : paper, null,
                        new Point((i * 83 + 27) % 610, 292 + (i * 57) % 408), i % 3 == 0 ? 2.5 : 1.4, i % 3 == 0 ? 2.5 : 1.4);
                dc.DrawGeometry(accent, null, Geometry.Parse("M 0,789 Q 162,633 334,751 Q 464,616 650,759 L 650,870 0,870 Z"));
                dc.DrawRectangle(paper, null, new Rect(166, 591, 78, 141));
                dc.DrawGeometry(accent, null, Geometry.Parse("M 152,591 L 205,531 258,591 Z"));
                dc.DrawRectangle(ink, null, new Rect(191, 664, 27, 68));
                dc.DrawEllipse(accent, null, new Point(205, 621), 12, 12);
                break;
            default:
                for (var i = 0; i < 5; i++)
                {
                    var x = i * 140 - 50; var top = 404 + (i * 73) % 163;
                    dc.DrawRectangle(i % 2 == 0 ? ink : paper, null, new Rect(x, top, 115, 425));
                    for (var row = 0; row < 4; row++)
                        for (var col = 0; col < 2; col++)
                            dc.DrawRoundedRectangle(i % 2 == 0 ? paper : accent, null, new Rect(x + 20 + col * 45, top + 24 + row * 55, 23, 33), 11, 11);
                }
                dc.DrawRectangle(ink, null, new Rect(-100, 795, 850, 70));
                dc.DrawLine(new Pen(paper, 2), new Point(0, 820), new Point(600, 820));
                break;
        }
    }

    private static SolidColorBrush Brush(string color) => new((Color)ColorConverter.ConvertFromString(color));
    private static void Text(DrawingContext dc, string value, double x, double y, double size, Brush brush, FontWeight? weight = null)
    {
        var typeface = new Typeface(new FontFamily("Microsoft YaHei UI, Segoe UI"), FontStyles.Normal, weight ?? FontWeights.Normal, FontStretches.Normal);
        dc.DrawText(new FormattedText(value, CultureInfo.GetCultureInfo("zh-CN"), FlowDirection.LeftToRight, typeface, size, brush, 1), new Point(x, y));
    }
}

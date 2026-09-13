using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ShuiMan.Core;

namespace ShuiMan.Checks;

internal static partial class Program
{
    private static void OrientationChecks()
    {
        Check("orientation honors explicit publication rotation on a cover", () =>
        {
            var image = DialogueFixture(true);
            var decision = SpreadAnalyzer.Analyze(image, new ReadingUnit { Locator = new("generated-cover"), IsCover = true, RotationHint = 90 });
            Equal(90, decision.Rotation, "cover publisher orientation");
        });
        Check("orientation leaves an unmarked cover unchanged without OCR", () =>
        {
            var pending = SpreadAnalyzer.AnalyzeAsync(RotateFixture(DialogueFixture(true), 90), new ReadingUnit { Locator = new("generated-unmarked-cover"), IsCover = true });
            True(pending.IsCompletedSuccessfully, "unmarked cover does not run OCR");
            Equal(0, pending.GetAwaiter().GetResult().Rotation, "unmarked cover orientation");
        });
        Check("orientation cancellation propagates without waiting for OCR", () =>
        {
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            bool stopped = false;
            try { SpreadAnalyzer.AnalyzeAsync(DialogueFixture(true), new ReadingUnit { Locator = new("generated-cancel") }, cancelled.Token).GetAwaiter().GetResult(); }
            catch (OperationCanceledException) { stopped = true; }
            True(stopped, "orientation cancellation");
        });
        Check("orientation honors explicit publication direction before any OCR evidence", () =>
        {
            var image = RotateFixture(DialogueFixture(true), 90);
            var pending = SpreadAnalyzer.AnalyzeAsync(image, new ReadingUnit { Locator = new("generated-publisher"), RotationHint = 90 });
            True(pending.IsCompletedSuccessfully, "explicit publication direction needs no asynchronous OCR");
            var decision = pending.GetAwaiter().GetResult();
            Equal(90, decision.Rotation, "publisher direction wins over different physical glyph direction");
            True(!decision.Uncertain && decision.Reason.Contains("出版物"), "publisher direction is definitive");
        });
        Check("orientation honors explicit zero-degree publication direction without OCR", () =>
        {
            var image = RotateFixture(DialogueFixture(true), 90);
            var pending = SpreadAnalyzer.AnalyzeAsync(image, new ReadingUnit { Locator = new("generated-explicit-zero"), RotationHint = 0 });
            True(pending.IsCompletedSuccessfully, "an explicit zero-degree declaration does not call asynchronous OCR");
            Equal(0, pending.GetAwaiter().GetResult().Rotation, "explicit zero is distinct from absent rotation metadata");
        });
        var languages = GlyphOrientationAnalyzer.AvailableLanguages;
        Console.WriteLine("OCR languages: " + string.Join(", ", languages));
        if (!languages.Any(language => language.StartsWith("zh", StringComparison.OrdinalIgnoreCase) || language.StartsWith("ja", StringComparison.OrdinalIgnoreCase)))
        {
            Console.WriteLine("SKIP CJK physical orientation: Windows Chinese/Japanese OCR language capability is not installed.");
            return;
        }
        foreach (int angle in new[] { 0, 90, 180, 270 })
        {
            int storedAngle = angle;
            Check($"CJK orientation preserves vertical flow and restores physical glyphs from {angle} degrees", () =>
            {
                var image = RotateFixture(DialogueFixture(true), storedAngle);
                var evidence = GlyphOrientationAnalyzer.AnalyzeAsync(image).GetAwaiter().GetResult();
                Console.WriteLine($"GLYPH input={storedAngle}, rotation={evidence.Rotation}, regions={evidence.Regions}, votes={evidence.Characters}, scores={string.Join(' ', evidence.Scores.Select(pair => $"{pair.Key}:{pair.Value:0.00}"))}");
                True(evidence.Rotation.HasValue, "physical glyph evidence reaches a definite orientation");
                Equal((360 - storedAngle) % 360, evidence.Rotation!.Value, "vertical glyph rotation");
            });
        }
        Check("CJK orientation recognizes horizontal dialogue in a second font through the actual spread analyzer", () =>
        {
            var image = RotateFixture(DialogueFixture(false, "SimSun"), 90);
            var decision = SpreadAnalyzer.Analyze(image, new ReadingUnit { Locator = new("generated-horizontal") });
            Equal(270, decision.Rotation, "sideways Chinese dialogue is corrected");
            True(decision.Standalone && decision.Reason.Contains("字形"), "physical CJK evidence actually used");
        });
        Check("CJK orientation rejects repeated symmetric decoration and textless artwork", () =>
        {
            var repeated = GlyphOrientationAnalyzer.AnalyzeAsync(DialogueFixture(true, "Microsoft YaHei", "田田田田田田田田")).GetAwaiter().GetResult();
            True(repeated.Rotation == null, "repeated symmetric glyphs are not independent orientation evidence");
            var visual = new DrawingVisual();
            using (var drawing = visual.RenderOpen())
            {
                drawing.DrawRectangle(Brushes.LightSlateGray, null, new Rect(0, 0, 700, 950));
                drawing.DrawEllipse(Brushes.White, null, new Point(220, 260), 130, 180);
                drawing.DrawEllipse(Brushes.White, null, new Point(480, 670), 120, 160);
            }
            var blank = new RenderTargetBitmap(700, 950, 96, 96, PixelFormats.Pbgra32); blank.Render(visual); blank.Freeze();
            var noText = GlyphOrientationAnalyzer.AnalyzeAsync(blank).GetAwaiter().GetResult();
            True(noText.Rotation == null && noText.Characters == 0, "no text creates no rotation evidence");
        });
    }

    private static BitmapSource DialogueFixture(bool vertical, string font = "Microsoft YaHei", string? replacement = null)
    {
        const int width = 900, height = 1200;
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(new SolidColorBrush(Color.FromRgb(83, 104, 115)), null, new Rect(0, 0, width, height));
            // Original separated dialogue balloons; letter flow is independent of physical glyph rotation.
            for (int region = 0; region < 2; region++)
            {
                var balloon = vertical ? new Rect(90 + region * 400, 110 + region * 490, 265, 360) : new Rect(65 + region * 50, 150 + region * 510, 690, 240);
                drawing.DrawRoundedRectangle(Brushes.White, new Pen(Brushes.Black, 3), balloon, 65, 65);
                string text = replacement ?? (region == 0 ? "明天我们继续出发" : "一起发现远方故事");
                for (int i = 0; i < text.Length; i++)
                {
                    double x = vertical ? balloon.X + 163 - i / 4 * 66 : balloon.X + 58 + i % 4 * 135;
                    double y = vertical ? balloon.Y + 51 + i % 4 * 62 : balloon.Y + 45 + i / 4 * 72;
                    drawing.DrawText(new FormattedText(text[i].ToString(), CultureInfo.GetCultureInfo("zh-CN"), FlowDirection.LeftToRight,
                        new Typeface(font), 43, Brushes.Black, 1), new Point(x, y));
                }
            }
        }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual); bitmap.Freeze(); return bitmap;
    }

    private static BitmapSource RotateFixture(BitmapSource source, int angle)
    {
        if (angle == 0) return source;
        var transformed = new TransformedBitmap(source, new RotateTransform(angle)); transformed.Freeze(); return transformed;
    }
}

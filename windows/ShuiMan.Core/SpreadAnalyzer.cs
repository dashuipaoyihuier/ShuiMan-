using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Windows.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace ShuiMan.Core;

/// <summary>Local Windows OCR with conservative evidence thresholds and explicit publisher hints.</summary>
public static class SpreadAnalyzer
{
    public const string AlgorithmVersion = "windows-ocr-conservative-v1";

    /// <remarks>CPU and OCR work: call from a worker, or prefer AnalyzeAsync. Input must be frozen across threads.</remarks>
    public static SpreadDecision Analyze(BitmapSource image, ReadingUnit unit) =>
        AnalyzeAsync(image, unit).GetAwaiter().GetResult();

    public static async Task<SpreadDecision> AnalyzeAsync(BitmapSource image, ReadingUnit unit,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (unit.IsCover || unit.Complex || unit.Error != null) return new();
        var ratio = (double)image.PixelWidth / image.PixelHeight;
        var baseline = new SpreadDecision(Standalone: ratio >= 1.2,
            Reason: ratio >= 1.2 ? "横向页面 · 完整展示" : "");
        var hint = LayoutEngine.NormalizeRotation(unit.RotationHint ?? 0);
        SpreadDecision WithHint(string fallbackReason = "") => hint is 90 or 180 or 270
            ? new(hint, true, Reason: "出版物旋转样式 · 完整展示")
            : baseline with { Reason = fallbackReason.Length > 0 ? fallbackReason : baseline.Reason };
        if (ratio is < .30 or > 3.2) return WithHint();

        // The Windows engine exposes no word confidence or physical CJK glyph orientation.
        // Four-angle Latin word evidence can be compared safely; vertical CJK flow alone
        // is deliberately never treated as proof that the artwork must be rotated.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(12));
        try
        {
            var engine = OcrEngine.TryCreateFromUserProfileLanguages();
            if (engine == null) return WithHint("未安装 Windows OCR 语言包；可手动旋转");
            var maxEdge = (int)Math.Min(1100, OcrEngine.MaxImageDimension);
            var pixels = AnalysisPixels.Bgra(image, maxEdge);
            var evidence = new List<OrientationEvidence>();
            foreach (var angle in new[] { 0, 90, 180, 270 })
            {
                timeout.Token.ThrowIfCancellationRequested();
                var rotated = Rotate(pixels.Bytes, pixels.Width, pixels.Height, angle);
                using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(rotated.Bytes.AsBuffer(), BitmapPixelFormat.Bgra8,
                    rotated.Width, rotated.Height, BitmapAlphaMode.Ignore);
                var result = await engine.RecognizeAsync(bitmap).AsTask(timeout.Token).ConfigureAwait(false);
                var score = 0;
                var words = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var lineCount = 0;
                foreach (var line in result.Lines)
                {
                    var qualifying = 0;
                    foreach (var word in line.Words)
                    {
                        // Readable multi-letter Latin words exclude numbers, isolated symbols,
                        // and CJK glyphs that OCR may accept in a sideways vertical column.
                        var letters = word.Text.Count(c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z');
                        var totalLetters = word.Text.Count(char.IsLetter);
                        if (letters < 3 || totalLetters != letters ||
                            word.BoundingRect.Width < word.BoundingRect.Height * 1.35) continue;
                        score += Math.Min(letters, 16);
                        words.Add(word.Text);
                        qualifying++;
                    }
                    if (qualifying > 0) lineCount++;
                }
                evidence.Add(new(angle, score, words.Count, lineCount));
            }
            var ranked = evidence.OrderByDescending(value => value.Score).ThenBy(value => value.Angle).ToList();
            var best = ranked[0];
            var runner = ranked[1].Score;
            var convincing = best.Score >= 28 && best.DistinctWords >= 4 && best.Lines >= 2 &&
                             best.Score >= Math.Max(1, runner) * 2.2 && best.Score - runner >= 18;
            if (convincing && best.Angle != 0)
            {
                if (hint != 0 && hint != best.Angle)
                    return baseline with { Uncertain = true, Reason = "文字与出版物朝向冲突，可手动旋转" };
                return new(best.Angle, true, Reason: "Windows 离线文字朝向分析 · 自动转正");
            }
            if (hint is 90 or 180 or 270) return WithHint();
            if (!convincing && best.Angle != 0 && best.Score >= 10)
                return baseline with { Uncertain = true, Reason = "朝向证据不足，可手动旋转" };
            return baseline;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { return WithHint("文字朝向分析超时，可手动旋转"); }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or UnauthorizedAccessException or TypeLoadException)
        { return WithHint("Windows OCR 暂不可用，可手动旋转"); }
    }

    private sealed record OrientationEvidence(int Angle, int Score, int DistinctWords, int Lines);
    private static (byte[] Bytes, int Width, int Height) Rotate(byte[] source, int width, int height, int angle)
    {
        if (angle == 0) return (source, width, height);
        var w = angle == 180 ? width : height;
        var h = angle == 180 ? height : width;
        var result = new byte[source.Length];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var nx = angle == 90 ? height - 1 - y : angle == 180 ? width - 1 - x : y;
                var ny = angle == 90 ? x : angle == 180 ? height - 1 - y : width - 1 - x;
                Buffer.BlockCopy(source, (y * width + x) * 4, result, (ny * w + nx) * 4, 4);
            }
        return (result, w, h);
    }
}

using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ShuiMan.Core;

/// <summary>Conservative seam evidence ported from ComicCore and Android seam-scale-offset-v2.</summary>
public static class PairAnalyzer
{
    public const string AlgorithmVersion = "seam-scale-offset-v4-antialias";
    private const int AnalysisHeight = 384;
    private record Features(double[] Left, double[] Right, double[] Thumbnail);
    private record Candidate(bool Swapped, double VerticalOffset = 0, double RightScale = 1,
        double Score = 0, double Correlation = 0, double DetailCorrelation = 0,
        double MeanError = 1, int MatchingBands = 0);

    public static PairDecision Analyze(BitmapSource first, BitmapSource second, int firstIndex)
    {
        var a = AnalysisPixels.Grayscale(first);
        var b = AnalysisPixels.Grayscale(second);
        return AnalyzePixels(a.Pixels, a.Width, a.Height, b.Pixels, b.Width, b.Height, firstIndex);
    }

    /// <summary>Pure deterministic entry point for generated regression fixtures; grayscale is in [0,1].</summary>
    public static PairDecision AnalyzePixels(double[] first, int firstWidth, int firstHeight,
        double[] second, int secondWidth, int secondHeight, int firstIndex = 0)
    {
        var rejected = new PairDecision(firstIndex);
        if (!Valid(first, firstWidth, firstHeight) || !Valid(second, secondWidth, secondHeight)) return rejected;
        var firstRatio = (double)firstWidth / firstHeight;
        var secondRatio = (double)secondWidth / secondHeight;
        if (firstRatio is < .45 or > .90 || secondRatio is < .45 or > .90 || Math.Abs(firstRatio - secondRatio) >= .18)
            return rejected;
        var a = ExtractFeatures(first, firstWidth, firstHeight);
        var b = ExtractFeatures(second, secondWidth, secondHeight);
        if (MeanError(a.Thumbnail, b.Thumbnail) < .025 || Ink(a.Thumbnail) < .07 || Ink(b.Thumbnail) < .07)
            return rejected;
        var forward = Placement(a.Right, b.Left, false);
        var reverse = Placement(b.Right, a.Left, true);
        var best = forward.Score >= reverse.Score ? forward : reverse;
        var margin = Math.Abs(forward.Score - reverse.Score);
        var automatic = best.Score >= .68 && best.Correlation >= .84 && best.DetailCorrelation >= .40 &&
                        best.MeanError <= .12 && best.MatchingBands >= 5 && margin >= .15;
        var suggested = automatic || best.Score >= .49 && best.Correlation >= .64 && best.DetailCorrelation >= .19 &&
                        best.MeanError <= .16 && best.MatchingBands >= 3 && margin >= .10;
        return new(firstIndex, automatic, best.Swapped, best.Score, best.VerticalOffset, best.RightScale,
            suggested, best.Correlation, best.DetailCorrelation, best.MeanError, best.MatchingBands, margin);
    }

    public static IReadOnlyDictionary<string, double> Diagnostics(BitmapSource first, BitmapSource second)
    {
        var pa = AnalysisPixels.Grayscale(first); var pb = AnalysisPixels.Grayscale(second);
        var a = ExtractFeatures(pa.Pixels, pa.Width, pa.Height); var b = ExtractFeatures(pb.Pixels, pb.Width, pb.Height);
        return new Dictionary<string, double>
        {
            ["firstOwnEdges"] = Correlate(a.Left, a.Right), ["secondOwnEdges"] = Correlate(b.Left, b.Right),
            ["firstInk"] = Ink(a.Thumbnail), ["secondInk"] = Ink(b.Thumbnail),
            ["firstLeftChanges"] = ChangeCount(a.Left), ["firstRightChanges"] = ChangeCount(a.Right),
            ["secondLeftChanges"] = ChangeCount(b.Left), ["secondRightChanges"] = ChangeCount(b.Right),
            ["firstRatio"] = (double)pa.Width / pa.Height, ["secondRatio"] = (double)pb.Width / pb.Height
        };
    }

    private static bool Valid(double[] pixels, int width, int height) => width > 0 && height > 0 &&
        (long)width * height == pixels.LongLength && pixels.All(value => double.IsFinite(value) && value is >= 0 and <= 1);

    private static Features ExtractFeatures(double[] pixels, int width, int height)
    {
        var scaledWidth = Math.Max(64, (int)Math.Round((double)width / height * AnalysisHeight));
        var page = Resample(pixels, width, height, scaledWidth, AnalysisHeight);
        var thumbnail = Resample(pixels, width, height, 24, 32);
        var margin = (int)(scaledWidth * .04);
        double EdgeInk(int x)
        {
            var count = 0;
            for (var y = 0; y < AnalysisHeight; y++) if (page[y * scaledWidth + x] < .85) count++;
            return (double)count / AnalysisHeight;
        }
        var left = 0;
        for (var offset = 0; offset <= margin; offset++)
            if (EdgeInk(offset) > .12) { left = offset; break; }
        var right = scaledWidth - 1;
        for (var offset = 0; offset <= margin; offset++)
        {
            var x = scaledWidth - 1 - offset;
            if (EdgeInk(x) > .12) { right = x; break; }
        }
        return new(Profile(page, scaledWidth, left, Math.Min(scaledWidth, left + 3)),
            Profile(page, scaledWidth, Math.Max(0, right - 2), right + 1), thumbnail);
    }

    private static double[] Profile(double[] pixels, int width, int start, int end)
    {
        var result = new double[AnalysisHeight];
        for (var y = 0; y < AnalysisHeight; y++)
        {
            for (var x = start; x < end; x++) result[y] += pixels[y * width + x];
            result[y] /= end - start;
        }
        // Comic screen tones carry unrelated high-frequency dots at a cut edge.
        // Compare the underlying linework after a small symmetric low-pass filter;
        // all texture, band, error and placement tests still apply to this profile.
        var filtered = new double[result.Length];
        for (int y = 0; y < result.Length; y++)
            filtered[y] = (result[Math.Max(0, y - 1)] + 2 * result[y] + result[Math.Min(result.Length - 1, y + 1)]) / 4;
        return filtered;
    }

    private static double[] Resample(double[] source, int sourceWidth, int sourceHeight, int targetWidth, int targetHeight)
    {
        // Point/bilinear sampling aliases scan dots when shrinking a full page to
        // 384 rows. Integrate every source pixel covered by each output pixel,
        // matching the anti-aliasing intent of CoreGraphics high-quality drawing.
        if (targetWidth < sourceWidth && targetHeight < sourceHeight)
        {
            var horizontal = new double[targetWidth * sourceHeight];
            for (int y = 0; y < sourceHeight; y++)
                for (int x = 0; x < targetWidth; x++)
                {
                    double begin = (double)x * sourceWidth / targetWidth;
                    double end = (double)(x + 1) * sourceWidth / targetWidth;
                    double sum = 0;
                    for (int sx = (int)begin; sx < (int)Math.Ceiling(end); sx++)
                        sum += source[y * sourceWidth + Math.Clamp(sx, 0, sourceWidth - 1)] *
                            Math.Max(0, Math.Min(end, sx + 1) - Math.Max(begin, sx));
                    horizontal[y * targetWidth + x] = sum / (end - begin);
                }
            var averaged = new double[targetWidth * targetHeight];
            for (int y = 0; y < targetHeight; y++)
                for (int x = 0; x < targetWidth; x++)
                {
                    double begin = (double)y * sourceHeight / targetHeight;
                    double end = (double)(y + 1) * sourceHeight / targetHeight;
                    double sum = 0;
                    for (int sy = (int)begin; sy < (int)Math.Ceiling(end); sy++)
                        sum += horizontal[Math.Clamp(sy, 0, sourceHeight - 1) * targetWidth + x] *
                            Math.Max(0, Math.Min(end, sy + 1) - Math.Max(begin, sy));
                    averaged[y * targetWidth + x] = sum / (end - begin);
                }
            return averaged;
        }
        var result = new double[targetWidth * targetHeight];
        for (var y = 0; y < targetHeight; y++)
            for (var x = 0; x < targetWidth; x++)
            {
                var sx = (x + .5) * sourceWidth / targetWidth - .5;
                var sy = (y + .5) * sourceHeight / targetHeight - .5;
                var floorX = (int)Math.Floor(sx);
                var floorY = (int)Math.Floor(sy);
                var x0 = Math.Clamp(floorX, 0, sourceWidth - 1);
                var y0 = Math.Clamp(floorY, 0, sourceHeight - 1);
                var x1 = Math.Clamp(floorX + 1, 0, sourceWidth - 1);
                var y1 = Math.Clamp(floorY + 1, 0, sourceHeight - 1);
                var tx = sx - Math.Floor(sx);
                var ty = sy - Math.Floor(sy);
                var top = source[y0 * sourceWidth + x0] * (1 - tx) + source[y0 * sourceWidth + x1] * tx;
                var bottom = source[y1 * sourceWidth + x0] * (1 - tx) + source[y1 * sourceWidth + x1] * tx;
                result[y * targetWidth + x] = Math.Clamp(top * (1 - ty) + bottom * ty, 0, 1);
            }
        return result;
    }

    private static Candidate Placement(double[] left, double[] right, bool swapped)
    {
        if (left.Length != AnalysisHeight || right.Length != AnalysisHeight ||
            Math.Min(Deviation(left), Deviation(right)) < .08 ||
            ChangeCount(left) < AnalysisHeight * .12 || ChangeCount(right) < AnalysisHeight * .12)
            return new(swapped);
        Candidate Measure(double scale, double offset)
        {
            Span<double> a = stackalloc double[AnalysisHeight];
            Span<double> b = stackalloc double[AnalysisHeight];
            var count = 0;
            for (var row = 0; row < AnalysisHeight; row++)
            {
                var source = ((double)row / AnalysisHeight - offset) / scale * AnalysisHeight;
                if (source < 0 || source >= AnalysisHeight - 1) continue;
                var lower = (int)source;
                var fraction = source - lower;
                a[count] = left[row];
                b[count] = right[lower] * (1 - fraction) + right[lower + 1] * fraction;
                count++;
            }
            if (count < (int)(AnalysisHeight * .80)) return new(swapped);
            a = a[..count]; b = b[..count];
            var correlation = Correlate(a, b);
            var error = MeanError(a, b);
            Span<double> da = stackalloc double[count - 1];
            Span<double> db = stackalloc double[count - 1];
            for (var j = 0; j < count - 1; j++) { da[j] = a[j + 1] - a[j]; db[j] = b[j + 1] - b[j]; }
            var detail = Correlate(da, db);
            var bands = 0;
            for (var band = 0; band < 12; band++)
            {
                var start = band * count / 12;
                var end = (band + 1) * count / 12;
                var ba = a[start..end];
                var bb = b[start..end];
                if (Math.Min(Deviation(ba), Deviation(bb)) >= .055 && Correlate(ba, bb) > .7 && MeanError(ba, bb) < .15)
                    bands++;
            }
            var score = .55 * Math.Max(0, correlation) + .20 * Math.Max(0, detail) + .25 * bands / 12;
            return new(swapped, offset, scale, score, correlation, detail, error, bands);
        }

        var best = new Candidate(swapped);
        for (var shift = -5; shift <= 5; shift++)
        {
            var candidate = Measure(1, (double)shift / AnalysisHeight);
            if (candidate.Score > best.Score) best = candidate;
        }
        var baseline = best;
        var coarse = best;
        for (var s = -5; s <= 5; s++)
            for (var t = -8; t <= 8; t++)
            {
                var candidate = Measure(1 + s * .02, t * .01);
                if (candidate.Score > coarse.Score) coarse = candidate;
            }
        for (var s = -3; s <= 3; s++)
            for (var t = -4; t <= 4; t++)
            {
                var candidate = Measure(coarse.RightScale + s * .002, coarse.VerticalOffset + t * .001);
                if (candidate.Score > best.Score) best = candidate;
            }
        if (best.Score < baseline.Score + .025) return baseline;
        if ((Math.Abs(best.RightScale - 1) > .015 || Math.Abs(best.VerticalOffset) > .015) &&
            (best.Correlation < .78 || best.DetailCorrelation < .28 || best.MatchingBands < 4)) return baseline;

        var seed = best;
        static double Fidelity(Candidate c) => c.Correlation + .25 * c.DetailCorrelation - .5 * c.MeanError;
        for (var s = -10; s <= 10; s++)
            for (var t = -6; t <= 6; t++)
            {
                var candidate = Measure(seed.RightScale + s * .0005, seed.VerticalOffset + t * .0005);
                if (candidate.MatchingBands >= seed.MatchingBands - 1 && candidate.Score >= seed.Score - .025 &&
                    Fidelity(candidate) > Fidelity(best)) best = candidate;
            }
        return best;
    }

    private static double Ink(double[] pixels) => (double)pixels.Count(value => value < .75) / pixels.Length;
    private static int ChangeCount(double[] values)
    {
        var count = 0;
        for (var i = 1; i < values.Length; i++) if (Math.Abs(values[i] - values[i - 1]) > .035) count++;
        return count;
    }
    private static double MeanError(ReadOnlySpan<double> a, ReadOnlySpan<double> b)
    {
        var count = Math.Min(a.Length, b.Length);
        if (count == 0) return 0;
        var sum = 0.0;
        for (var i = 0; i < count; i++) sum += Math.Abs(a[i] - b[i]);
        return sum / count;
    }
    private static double Deviation(ReadOnlySpan<double> values)
    {
        if (values.Length == 0) return 0;
        var mean = 0.0;
        foreach (var value in values) mean += value;
        mean /= values.Length;
        var sum = 0.0;
        foreach (var value in values) sum += (value - mean) * (value - mean);
        return Math.Sqrt(sum / values.Length);
    }
    private static double Correlate(ReadOnlySpan<double> a, ReadOnlySpan<double> b)
    {
        var count = Math.Min(a.Length, b.Length);
        if (count == 0) return 0;
        double ma = 0, mb = 0;
        for (var i = 0; i < count; i++) { ma += a[i]; mb += b[i]; }
        ma /= count; mb /= count;
        double cross = 0, va = 0, vb = 0;
        for (var i = 0; i < count; i++)
        {
            var x = a[i] - ma;
            var y = b[i] - mb;
            cross += x * y; va += x * x; vb += y * y;
        }
        return cross / Math.Max(1e-8, Math.Sqrt(va * vb));
    }
}

internal static class AnalysisPixels
{
    internal static (byte[] Bytes, int Width, int Height) Bgra(BitmapSource source, int maxEdge = 1100)
    {
        var scale = Math.Min(1, (double)maxEdge / Math.Max(source.PixelWidth, source.PixelHeight));
        BitmapSource sampled = scale < 1 ? new TransformedBitmap(source, new ScaleTransform(scale, scale)) : source;
        if (sampled.Format != PixelFormats.Bgra32) sampled = new FormatConvertedBitmap(sampled, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[checked(sampled.PixelWidth * sampled.PixelHeight * 4)];
        sampled.CopyPixels(pixels, sampled.PixelWidth * 4, 0);
        // OCR and seam evidence see transparency on white, as the original CoreGraphics implementation does.
        for (var i = 0; i < pixels.Length; i += 4)
        {
            var alpha = pixels[i + 3];
            if (alpha != 255)
                for (var channel = 0; channel < 3; channel++)
                    pixels[i + channel] = (byte)((pixels[i + channel] * alpha + 255 * (255 - alpha) + 127) / 255);
            pixels[i + 3] = 255;
        }
        return (pixels, sampled.PixelWidth, sampled.PixelHeight);
    }

    internal static (double[] Pixels, int Width, int Height) Grayscale(BitmapSource source)
    {
        var bgra = Bgra(source);
        var pixels = new double[bgra.Width * bgra.Height];
        for (var i = 0; i < pixels.Length; i++)
            pixels[i] = (.114 * bgra.Bytes[i * 4] + .587 * bgra.Bytes[i * 4 + 1] + .299 * bgra.Bytes[i * 4 + 2]) / 255;
        return (pixels, bgra.Width, bgra.Height);
    }
}

using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace ShuiMan.Core;

public sealed record GlyphOrientationEvidence(int? Rotation, int Regions, int Characters,
    IReadOnlyDictionary<int, double> Scores, string? Language);

/// <summary>
/// Reflows separated CJK glyphs from enclosed light dialogue regions into OCR rows, then verifies
/// their physical strokes against upright and rotated font templates. Text flow is never a rotation vote.
/// </summary>
public static class GlyphOrientationAnalyzer
{
    private sealed record Mask(int Width, int Height, byte[] Pixels, int X = 0, int Y = 0);
    private sealed class Component(bool ink, int x, int y)
    {
        public bool Ink = ink;
        public int X = x, Y = y, Right = x + 1, Bottom = y + 1;
        public List<int> Points = [];
        public int Area => (Right - X) * (Bottom - Y);
    }
    private sealed record Vote(int Region, Rect Bounds, int Rotation, double Score, double Margin, char Character);
    private static readonly ConcurrentDictionary<char, byte[][]> TemplateCache = new();
    private static readonly ConcurrentQueue<char> TemplateOrder = new();
    private static readonly object TemplateGate = new();

    public static IReadOnlyList<string> AvailableLanguages => OcrEngine.AvailableRecognizerLanguages.Select(language => language.LanguageTag).ToArray();

    public static Task<GlyphOrientationEvidence> AnalyzeAsync(BitmapSource image, CancellationToken cancellationToken = default) =>
        Task.Run(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var language = OcrEngine.AvailableRecognizerLanguages
                .Where(value => value.LanguageTag.StartsWith("zh", StringComparison.OrdinalIgnoreCase) || value.LanguageTag.StartsWith("ja", StringComparison.OrdinalIgnoreCase))
                .OrderBy(value => value.LanguageTag.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? 0 : 1).FirstOrDefault();
            var engine = language == null ? null : OcrEngine.TryCreateFromLanguage(language);
            if (engine == null) return new GlyphOrientationEvidence(null, 0, 0, new Dictionary<int, double>(), null);
            var masks = Regions(image, cancellationToken);
            var votes = new List<Vote>();
            if (masks.Count == 0) return new GlyphOrientationEvidence(null, 0, 0, new Dictionary<int, double>(), language!.LanguageTag);
            foreach (int angle in new[] { 0, 90, 180, 270 })
            {
                cancellationToken.ThrowIfCancellationRequested();
                var lines = masks.Select((mask, index) => (Region: index, Glyphs: Line(Rotate(mask, angle))))
                    .Where(line => line.Glyphs != null).Select(line => (line.Region, Glyphs: line.Glyphs!)).ToList();
                if (lines.Count == 0) continue;
                var sheet = Sheet(lines.Select(line => line.Glyphs).ToList());
                using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(sheet.Pixels.AsBuffer(), BitmapPixelFormat.Gray8,
                    sheet.Width, sheet.Height, BitmapAlphaMode.Ignore);
                var recognized = await engine.RecognizeAsync(bitmap).AsTask(cancellationToken).ConfigureAwait(false);
                foreach (var line in recognized.Lines)
                    foreach (var word in line.Words)
                    {
                        var characters = word.Text.Where(character => !char.IsWhiteSpace(character)).ToArray();
                        if (characters.Length == 0) continue;
                        for (int index = 0; index < characters.Length; index++)
                        {
                            char character = characters[index];
                            if (character is < '\u3400' or > '\u9fff') continue;
                            double centerX = word.BoundingRect.X + word.BoundingRect.Width * (index + .5) / characters.Length;
                            double centerY = word.BoundingRect.Y + word.BoundingRect.Height / 2;
                            int column = (int)Math.Floor((centerX - 12) / 48);
                            int row = (int)Math.Floor((centerY - 12) / 64);
                            if (row < 0 || row >= lines.Count || column < 0 || column >= lines[row].Glyphs.Count) continue;
                            var tile = lines[row].Glyphs[column];
                            var actual = Normalize(tile);
                            var templates = Templates(character);
                            if (templates.Length == 0) continue;
                            var matches = Enumerable.Range(0, 4).Select(turn => (Turn: turn,
                                Score: templates.Max(template => Similarity(actual, RotateSquare(template, turn)))))
                                .OrderByDescending(match => match.Score).ToArray();
                            double margin = matches[0].Score - matches[1].Score;
                            if (matches[0].Score < .55 || margin < .035) continue;
                            int region = lines[row].Region;
                            var original = masks[region];
                            var bounds = angle switch
                            {
                                90 => new Rect(tile.Y, original.Height - tile.X - tile.Width, tile.Height, tile.Width),
                                180 => new Rect(original.Width - tile.X - tile.Width, original.Height - tile.Y - tile.Height, tile.Width, tile.Height),
                                270 => new Rect(original.Width - tile.Y - tile.Height, tile.X, tile.Height, tile.Width),
                                _ => new Rect(tile.X, tile.Y, tile.Width, tile.Height)
                            };
                            var vote = new Vote(region, bounds, (angle - matches[0].Turn * 90 + 360) % 360, matches[0].Score, margin, character);
                            int existing = votes.FindIndex(value => value.Region == region && Overlaps(value.Bounds, bounds));
                            if (existing < 0) votes.Add(vote);
                            else if (vote.Score * vote.Margin > votes[existing].Score * votes[existing].Margin) votes[existing] = vote;
                        }
                    }
            }
            var scores = votes.GroupBy(vote => vote.Rotation).ToDictionary(group => group.Key, group => group.Sum(vote => vote.Score));
            var ranked = scores.OrderByDescending(pair => pair.Value).ToArray();
            int? rotation = null;
            if (ranked.Length > 0)
            {
                var best = ranked[0]; double runner = ranked.Length > 1 ? ranked[1].Value : 0;
                var supporters = votes.Where(vote => vote.Rotation == best.Key).ToArray();
                // Repeated decorative symbols cannot provide independent orientation evidence.
                if (supporters.Length >= 3 && supporters.Select(vote => vote.Character).Distinct().Count() >= 3 &&
                    best.Value >= 2.4 && best.Value - runner >= 1.8 && best.Value >= Math.Max(.1, runner) * 1.6)
                    rotation = best.Key;
            }
            return new GlyphOrientationEvidence(rotation, masks.Count, votes.Count, scores, language!.LanguageTag);
        }, cancellationToken);

    private static bool Overlaps(Rect first, Rect second)
    {
        var overlap = Rect.Intersect(first, second);
        return !overlap.IsEmpty && overlap.Width * overlap.Height > Math.Min(first.Width * first.Height, second.Width * second.Height) * .6;
    }

    private static byte[][] Templates(char character)
    {
        if (TemplateCache.TryGetValue(character, out var cached)) return cached;
        lock (TemplateGate)
        {
            if (TemplateCache.TryGetValue(character, out cached)) return cached;
            byte[][]? result = null;
            Exception? failure = null;
            // WPF font rasterization uses a private STA, never the application's dispatcher.
            var thread = new Thread(() =>
            {
                try
                {
                    var values = new List<byte[]>();
                    foreach (var family in new[] { "Microsoft YaHei", "SimSun", "SimHei", "KaiTi", "Yu Gothic" })
                    {
                        var typeface = new Typeface(new FontFamily(family), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
                        if (!typeface.TryGetGlyphTypeface(out var glyph) || !glyph.CharacterToGlyphMap.ContainsKey(character)) continue;
                        var visual = new DrawingVisual();
                        using (var drawing = visual.RenderOpen())
                        {
                            drawing.DrawRectangle(Brushes.White, null, new Rect(0, 0, 80, 80));
                            var text = new FormattedText(character.ToString(), CultureInfo.GetCultureInfo("zh-CN"), FlowDirection.LeftToRight,
                                typeface, 60, Brushes.Black, 1);
                            drawing.DrawText(text, new Point(5, 0));
                        }
                        var bitmap = new RenderTargetBitmap(80, 80, 96, 96, PixelFormats.Pbgra32);
                        bitmap.Render(visual); bitmap.Freeze();
                        var buffer = new byte[80 * 80 * 4]; bitmap.CopyPixels(buffer, 80 * 4, 0);
                        var mask = new byte[80 * 80];
                        for (int i = 0; i < mask.Length; i++) mask[i] = buffer[i * 4] < 205 ? (byte)0 : (byte)255;
                        values.Add(Normalize(new Mask(80, 80, mask)));
                    }
                    result = values.ToArray();
                }
                catch (Exception ex) { failure = ex; }
                finally { System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown(); }
            }) { IsBackground = true, Name = "ShuiMan glyph templates" };
            thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
            if (failure != null) throw new InvalidOperationException("无法生成文字方向参考字形。", failure);
            while (TemplateCache.Count >= 1024 && TemplateOrder.TryDequeue(out var oldest)) TemplateCache.TryRemove(oldest, out _);
            TemplateCache[character] = result ?? [];
            TemplateOrder.Enqueue(character);
            return result ?? [];
        }
    }

    private static byte[] Normalize(Mask mask)
    {
        int minX = mask.Width, minY = mask.Height, maxX = -1, maxY = -1;
        for (int y = 0; y < mask.Height; y++) for (int x = 0; x < mask.Width; x++)
            if (mask.Pixels[y * mask.Width + x] < 128) { minX = Math.Min(minX, x); minY = Math.Min(minY, y); maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y); }
        var result = new byte[32 * 32];
        if (maxX < minX || maxY < minY) return result;
        for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++)
        {
            int sx = minX + Math.Min(maxX - minX, (int)((x + .5) * (maxX - minX + 1) / 32));
            int sy = minY + Math.Min(maxY - minY, (int)((y + .5) * (maxY - minY + 1) / 32));
            result[y * 32 + x] = mask.Pixels[sy * mask.Width + sx] < 128 ? (byte)1 : (byte)0;
        }
        return result;
    }

    private static byte[] RotateSquare(byte[] source, int turn)
    {
        if (turn == 0) return source;
        var result = new byte[source.Length];
        for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++)
        {
            int nx = turn == 1 ? 31 - y : turn == 2 ? 31 - x : y;
            int ny = turn == 1 ? x : turn == 2 ? 31 - y : 31 - x;
            result[ny * 32 + nx] = source[y * 32 + x];
        }
        return result;
    }

    private static double Similarity(byte[] first, byte[] second)
    {
        int intersection = 0, total = 0;
        for (int i = 0; i < first.Length; i++) { intersection += first[i] & second[i]; total += first[i] + second[i]; }
        return 2.0 * intersection / Math.Max(1, total);
    }

    private static List<Mask> Regions(BitmapSource image, CancellationToken token)
    {
        var source = AnalysisPixels.Bgra(image, 1100);
        int width = source.Width, height = source.Height;
        var pixels = new byte[width * height];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = (byte)((source.Bytes[i * 4] * 29 + source.Bytes[i * 4 + 1] * 150 + source.Bytes[i * 4 + 2] * 77) / 256);
        var labels = new int[pixels.Length]; Array.Fill(labels, -1);
        var components = new List<Component>();
        for (int start = 0; start < pixels.Length; start++)
        {
            if (labels[start] >= 0) continue;
            token.ThrowIfCancellationRequested();
            if (components.Count >= 60000) return [];
            bool ink = pixels[start] < 205; int id = components.Count;
            var component = new Component(ink, start % width, start / width);
            component.Points.Add(start); labels[start] = id;
            for (int head = 0; head < component.Points.Count; head++)
            {
                if (head % 8192 == 0) token.ThrowIfCancellationRequested();
                int position = component.Points[head], x = position % width, y = position / width;
                component.X = Math.Min(component.X, x); component.Y = Math.Min(component.Y, y);
                component.Right = Math.Max(component.Right, x + 1); component.Bottom = Math.Max(component.Bottom, y + 1);
                Add(position - 1, x > 0); Add(position + 1, x + 1 < width); Add(position - width, y > 0); Add(position + width, y + 1 < height);
                void Add(int next, bool allowed)
                {
                    if (!allowed || labels[next] >= 0 || (pixels[next] < 205) != ink) return;
                    labels[next] = id; component.Points.Add(next);
                }
            }
            components.Add(component);
        }
        var groups = new Dictionary<int, List<Component>>();
        foreach (var component in components)
        {
            token.ThrowIfCancellationRequested();
            if (!component.Ink || component.Points.Count < 2 || component.Right - component.X >= width * .15 || component.Bottom - component.Y >= height * .15) continue;
            var adjacent = new Dictionary<int, int>();
            foreach (int position in component.Points)
            {
                int x = position % width, y = position / width;
                Add(position - 1, x > 0); Add(position + 1, x + 1 < width); Add(position - width, y > 0); Add(position + width, y + 1 < height);
                void Add(int next, bool allowed)
                {
                    if (!allowed || pixels[next] < 205) return;
                    adjacent[labels[next]] = adjacent.GetValueOrDefault(labels[next]) + 1;
                }
            }
            if (adjacent.Count == 0) continue;
            int parent = adjacent.MaxBy(pair => pair.Value).Key;
            var area = components[parent];
            if (area.Points.Count < 300 || (double)area.Points.Count / area.Area < .5 || area.Area >= width * height / 4 ||
                component.X <= area.X || component.Y <= area.Y || component.Right >= area.Right || component.Bottom >= area.Bottom) continue;
            if (!groups.TryGetValue(parent, out var values)) groups[parent] = values = [];
            values.Add(component);
        }
        var masks = new List<Mask>();
        foreach (var parts in groups.OrderBy(pair => pair.Key).Select(pair => pair.Value))
        {
            if (parts.Count < 4) continue;
            int x = parts.Min(part => part.X), y = parts.Min(part => part.Y), right = parts.Max(part => part.Right), bottom = parts.Max(part => part.Bottom);
            int w = right - x, h = bottom - y;
            if (w < 10 || h < 10) continue;
            var mask = new Mask(w, h, Enumerable.Repeat((byte)255, w * h).ToArray());
            foreach (var part in parts) foreach (int position in part.Points) mask.Pixels[(position / width - y) * w + position % width - x] = 0;
            if (Line(mask) != null) masks.Add(mask);
        }
        return masks.OrderByDescending(mask => mask.Width * mask.Height).Take(12).ToList();
    }

    private static Mask Rotate(Mask mask, int angle)
    {
        if (angle == 0) return mask;
        int width = angle == 180 ? mask.Width : mask.Height, height = angle == 180 ? mask.Height : mask.Width;
        var result = new byte[width * height];
        for (int y = 0; y < mask.Height; y++) for (int x = 0; x < mask.Width; x++)
        {
            int nx = angle == 90 ? mask.Height - 1 - y : angle == 180 ? mask.Width - 1 - x : y;
            int ny = angle == 90 ? x : angle == 180 ? mask.Height - 1 - y : mask.Width - 1 - x;
            result[ny * width + nx] = mask.Pixels[y * mask.Width + x];
        }
        return new Mask(width, height, result);
    }

    private static List<(int Start, int Length)> Bands(bool[] projection)
    {
        var values = new List<(int, int)>(); int start = -1;
        for (int i = 0; i <= projection.Length; i++)
        {
            if (i < projection.Length && projection[i]) { if (start < 0) start = i; }
            else if (start >= 0) { values.Add((start, i - start)); start = -1; }
        }
        return values;
    }

    private static List<Mask>? Line(Mask mask)
    {
        var xs = new bool[mask.Width]; var ys = new bool[mask.Height];
        for (int y = 0; y < mask.Height; y++) for (int x = 0; x < mask.Width; x++) if (mask.Pixels[y * mask.Width + x] == 0) { xs[x] = true; ys[y] = true; }
        var xb = Bands(xs); var yb = Bands(ys);
        var sizes = xb.Concat(yb).Select(band => band.Length).Where(length => length >= 5).Order().ToArray();
        if (sizes.Length == 0) return null;
        double pitch = sizes[sizes.Length / 2];
        List<(int Start, int Length)> Merge(List<(int Start, int Length)> bands)
        {
            var merged = new List<(int Start, int Length)>();
            foreach (var band in bands)
            {
                if (merged.Count > 0 && merged[^1].Length < pitch * .65 && band.Start + band.Length - merged[^1].Start <= pitch * 1.4)
                    merged[^1] = (merged[^1].Start, band.Start + band.Length - merged[^1].Start);
                else merged.Add(band);
            }
            return merged;
        }
        var glyphs = new List<Mask>();
        foreach (var x in Merge(xb).AsEnumerable().Reverse()) foreach (var y in Merge(yb))
        {
            double ratio = (double)x.Length / y.Length;
            if (ratio < .6 || ratio > 1.65 || Math.Min(x.Length, y.Length) < pitch * .6) continue;
            var pixels = new byte[x.Length * y.Length]; int ink = 0;
            for (int row = 0; row < y.Length; row++) for (int col = 0; col < x.Length; col++)
            { byte pixel = mask.Pixels[(y.Start + row) * mask.Width + x.Start + col]; pixels[row * x.Length + col] = pixel; if (pixel == 0) ink++; }
            double coverage = (double)ink / pixels.Length;
            if (coverage < .08 || coverage > .80) continue;
            glyphs.Add(new Mask(x.Length, y.Length, pixels, x.Start, y.Start));
        }
        return glyphs.Count >= 3 && glyphs.Count <= 24 ? glyphs : null;
    }

    private static Mask Sheet(List<List<Mask>> lines)
    {
        int width = 24 + 48 * lines.Max(line => line.Count), height = 24 + lines.Count * 64;
        var pixels = Enumerable.Repeat((byte)255, width * height).ToArray();
        for (int row = 0; row < lines.Count; row++) for (int col = 0; col < lines[row].Count; col++)
        {
            var glyph = lines[row][col];
            for (int y = 0; y < 40; y++) for (int x = 0; x < 40; x++)
                pixels[(12 + row * 64 + y) * width + 12 + col * 48 + x] =
                    glyph.Pixels[Math.Min(glyph.Height - 1, y * glyph.Height / 40) * glyph.Width + Math.Min(glyph.Width - 1, x * glyph.Width / 40)];
        }
        return new Mask(width, height, pixels);
    }
}

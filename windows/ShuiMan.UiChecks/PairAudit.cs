using System.IO;
using System.Text.Json;
using ShuiMan.Core;

namespace ShuiMan.UiChecks;

internal static partial class Program
{
    private static async Task PairAudit(string directory, int bookLimit, int pairLimit)
    {
        var candidates = Directory.EnumerateFiles(directory, "*.epub", SearchOption.AllDirectories)
            .Select(path => new FileInfo(path)).OrderBy(file => file.Length).ToArray();
        if (candidates.Length == 0) throw new InvalidOperationException("No EPUB files found.");
        bookLimit = Math.Clamp(bookLimit, 1, 12); pairLimit = Math.Clamp(pairLimit, 2, 600);
        var selected = Enumerable.Range(0, Math.Min(bookLimit, candidates.Length))
            .Select(index => candidates[(int)Math.Round((index + .5) * candidates.Length / Math.Min(bookLimit, candidates.Length) - .5)])
            .DistinctBy(file => file.FullName).ToArray();
        Console.WriteLine(JsonSerializer.Serialize(new { Inventory = candidates.Length, SampledBooks = selected.Length, PairLimit = pairLimit, ReadOnly = true }));
        int ordinal = 0;
        foreach (var file in selected)
        {
            string alias = $"B{++ordinal:00}";
            try
            {
                using var book = await DocumentEngine.OpenAsync(file.FullName);
                var units = book.Publication.Units;
                var pairs = new Dictionary<int, PairDecision>();
                int failures = 0, ratioRejected = 0, textureRejected = 0, inkRejected = 0;
                for (int index = 0; index < Math.Min(units.Count - 1, pairLimit); index++)
                {
                    if (units[index].Error != null || units[index + 1].Error != null) { failures++; continue; }
                    try
                    {
                        var first = await book.RenderAsync(index, BookAnalysisService.RenderEdge); var second = await book.RenderAsync(index + 1, BookAnalysisService.RenderEdge);
                        var decision = await Task.Run(() => PairAnalyzer.Analyze(first, second, index));
                        pairs[index] = decision;
                        if (decision.Score == 0)
                        {
                            var details = PairAnalyzer.Diagnostics(first, second);
                            if (details["firstRatio"] is < .45 or > .90 || details["secondRatio"] is < .45 or > .90 || Math.Abs(details["firstRatio"] - details["secondRatio"]) >= .18) ratioRejected++;
                            else if (details["firstInk"] < .07 || details["secondInk"] < .07) inkRejected++;
                            else if (Math.Min(details["firstRightChanges"], details["secondLeftChanges"]) < 384 * .12 && Math.Min(details["secondRightChanges"], details["firstLeftChanges"]) < 384 * .12) textureRejected++;
                        }
                    }
                    catch { failures++; }
                }
                bool UnmarkedUpright(int index) => !units[index].IsCover && units[index].Error == null && units[index].RotationHint is null or 0;
                bool Eligible(PairDecision pair, bool suggestions)
                {
                    var rival = Math.Max(pairs.GetValueOrDefault(pair.FirstIndex - 1)?.Score ?? 0, pairs.GetValueOrDefault(pair.FirstIndex + 1)?.Score ?? 0);
                    return UnmarkedUpright(pair.FirstIndex) && UnmarkedUpright(pair.FirstIndex + 1) &&
                        (pair.Automatic || suggestions && pair.Suggested) && pair.Score - rival >= (suggestions ? .04 : .08);
                }
                var ordinary = LayoutEngine.Groups(book.Publication, new SavedBook { Preferences = new ReaderPreferences { Layout = "double", SmartSpreads = false, AutomaticOrientation = false } });
                var orientation = new Dictionary<string, SpreadDecision>();
                var orientationPages = pairs.Values.Where(pair => Eligible(pair, true)).SelectMany(pair => new[] { pair.FirstIndex, pair.FirstIndex + 1 }).Distinct().Order().ToArray();
                int orientationCompleted = 0;
                foreach (int page in orientationPages)
                {
                    try
                    {
                        var image = await book.RenderAsync(page, BookAnalysisService.RenderEdge);
                        orientation[units[page].Id] = await Task.Run(() => SpreadAnalyzer.AnalyzeAsync(image, units[page]));
                    }
                    catch { orientation[units[page].Id] = new(Uncertain: true); }
                    if (++orientationCompleted % 8 == 0) Console.WriteLine(JsonSerializer.Serialize(new { Book = alias, OrientationCompleted = orientationCompleted, OrientationCandidates = orientationPages.Length }));
                }
                var policy = new SavedBook { Preferences = new ReaderPreferences { Layout = "single", AggressivePairs = true } };
                var validated = LayoutEngine.Groups(book.Publication, policy, orientation, pairs).Where(group => group.Spread && group.Indices.Length == 2).ToArray();
                policy.Preferences.AggressivePairs = false;
                var validatedStrict = LayoutEngine.Groups(book.Publication, policy, orientation, pairs).Where(group => group.Spread && group.Indices.Length == 2).ToArray();
                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    Book = alias, Pages = units.Count, Direction = book.Publication.Direction ?? "unmarked",
                    NativePages = units.Count(unit => !unit.Complex && unit.Error == null), Hints = units.Count(unit => unit.RotationHint.HasValue),
                    PairsMeasured = pairs.Count, Failures = failures, Strong = pairs.Values.Count(pair => pair.Automatic),
                    Suggested = pairs.Values.Count(pair => pair.Suggested), OldEligibleBeforeOcr = pairs.Values.Count(pair => Eligible(pair, false)),
                    MacEligibleBeforeOcr = pairs.Values.Count(pair => Eligible(pair, true)),
                    OrientationPages = orientationPages.Length, OrientationUncertain = orientation.Values.Count(value => value.Uncertain),
                    AcceptedAfterOrientation = validated.Length, StrictAfterOrientation = validatedStrict.Length,
                    AcceptedAnonymousPairs = validated.Select(group => group.Indices.Order().Select(index => index + 1)),
                    OrdinaryDoubleGroups = ordinary.Count(group => group.Indices.Length == 2 && !group.Spread),
                    ZeroScoreRatio = ratioRejected, ZeroScoreInk = inkRejected, ZeroScoreTexture = textureRejected,
                    Candidates = pairs.Values.Where(pair => pair.Score >= .40).OrderByDescending(pair => pair.Score).Take(16).Select(pair => new
                    {
                        Pages = new[] { pair.FirstIndex + 1, pair.FirstIndex + 2 }, pair.Automatic, pair.Suggested,
                        Score = Math.Round(pair.Score, 3), Correlation = Math.Round(pair.Correlation, 3),
                        Detail = Math.Round(pair.DetailCorrelation, 3), Error = Math.Round(pair.MeanError, 3),
                        pair.MatchingBands, Margin = Math.Round(pair.PlacementMargin, 3), pair.Swapped,
                        OldEligible = Eligible(pair, false), MacEligible = Eligible(pair, true)
                    })
                }));
            }
            catch (Exception exception) { Console.WriteLine(JsonSerializer.Serialize(new { Book = alias, Failure = exception.GetType().Name })); }
        }
    }
}

using System.Diagnostics;
using System.IO;
using System.Text.Json;
using ShuiMan.Core;

namespace ShuiMan.UiChecks;

internal static partial class Program
{
    private static async Task WholeBookAudit(string directory, int sampleCount, int ordinal, string cacheDirectory)
    {
        var candidates = Directory.EnumerateFiles(directory, "*.epub", SearchOption.AllDirectories)
            .Select(path => new FileInfo(path)).OrderBy(file => file.Length).ToArray();
        sampleCount = Math.Clamp(sampleCount, 1, Math.Min(12, candidates.Length));
        ordinal = Math.Clamp(ordinal, 1, sampleCount);
        var selected = candidates[(int)Math.Round((ordinal - .5) * candidates.Length / sampleCount - .5)];
        var cache = new AnalysisCache(cacheDirectory);
        var service = new BookAnalysisService(cache);
        var timer = Stopwatch.StartNew();
        int renderCalls = 0;
        using var io = new SemaphoreSlim(1, 1);
        BookAnalysisSnapshot snapshot;
        using (var book = await DocumentEngine.OpenAsync(selected.FullName))
        {
            int lastReported = -1;
            var progress = new AuditProgress<BookAnalysisProgress>(update =>
            {
                int bucket = update.CompletedPages / 25;
                if (bucket == lastReported && !update.IsComplete) return;
                lastReported = bucket;
                Console.WriteLine(JsonSerializer.Serialize(new { Book = $"B{ordinal:00}", Completed = update.CompletedPages, Total = update.TotalPages, update.IsComplete, update.FromCache }));
            });
            snapshot = await service.AnalyzeAsync(book.Publication, async (index, edge, token) =>
            {
                await io.WaitAsync(token);
                try { Interlocked.Increment(ref renderCalls); return await book.RenderAsync(index, edge, token); }
                finally { io.Release(); }
            }, progress);
            var state = new SavedBook { Preferences = new ReaderPreferences { Layout = "single", Direction = book.Publication.Direction ?? "ltr", AggressivePairs = true } };
            var accepted = LayoutEngine.Groups(book.Publication, state, snapshot.Decisions, snapshot.Pairs)
                .Where(group => group.Spread && group.Indices.Length == 2).ToArray();
            state.Preferences.AggressivePairs = false;
            var strict = LayoutEngine.Groups(book.Publication, state, snapshot.Decisions, snapshot.Pairs)
                .Where(group => group.Spread && group.Indices.Length == 2).ToArray();
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                Book = $"B{ordinal:00}", Pages = book.Publication.Units.Count, snapshot.IsComplete,
                CompletedPages = snapshot.CompletedPages.Count, CompletedPairs = snapshot.CompletedPairs.Count,
                FailedPages = snapshot.FailedPages.Count, AnalysisRenderCalls = renderCalls,
                ElapsedSeconds = Math.Round(timer.Elapsed.TotalSeconds, 1),
                StrongSeams = snapshot.Pairs.Values.Count(pair => pair.Automatic), SuggestedSeams = snapshot.Pairs.Values.Count(pair => pair.Suggested),
                ActualSpreadGroups = accepted.Length, StrictSpreadGroups = strict.Length,
                ActualAnonymousPairs = accepted.Select(group => group.Indices.Order().Select(index => index + 1)),
                OrientationCounts = snapshot.Decisions.Values.GroupBy(decision => decision.Rotation).ToDictionary(group => group.Key, group => group.Count()),
                UncertainOrientations = snapshot.Decisions.Values.Count(decision => decision.Uncertain)
            }));
        }
        using var reopened = await DocumentEngine.OpenAsync(selected.FullName);
        int reopenedRenders = 0;
        var reloaded = await service.AnalyzeAsync(reopened.Publication, async (index, edge, token) =>
        {
            Interlocked.Increment(ref reopenedRenders);
            return await reopened.RenderAsync(index, edge, token);
        });
        bool sameDecisions = reloaded.Decisions.Count == snapshot.Decisions.Count &&
            snapshot.Decisions.All(entry => reloaded.Decisions.TryGetValue(entry.Key, out var decision) && decision == entry.Value) &&
            reloaded.Pairs.Count == snapshot.Pairs.Count &&
            snapshot.Pairs.All(entry => reloaded.Pairs.TryGetValue(entry.Key, out var pair) && pair == entry.Value);
        Console.WriteLine(JsonSerializer.Serialize(new { Book = $"B{ordinal:00}", ReopenedComplete = reloaded.IsComplete,
            ReopenedAnalysisRenderCalls = reopenedRenders, CachedPages = reloaded.CompletedPages.Count,
            CachedPairs = reloaded.CompletedPairs.Count, SameCachedDecisions = sameDecisions }));
        if (!snapshot.IsComplete || !reloaded.IsComplete || reopenedRenders != 0 || !sameDecisions)
            throw new InvalidOperationException("Whole-book analysis or complete-cache reuse did not finish with identical decisions.");
    }

    private sealed class AuditProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}

using System.IO;
using System.Text.Json;
using System.Windows.Media.Imaging;
using ShuiMan.Core;

namespace ShuiMan.Checks;

internal static partial class Program
{
    private static void BookAnalysisChecks(string root)
    {
        Check("whole-book analysis scans generated ZIP to the last page and reuses its completed cache", () =>
        {
            using var book = Open(Path.Combine(root, "水漫 演示.zip"));
            foreach (var unit in book.Publication.Units) unit.RotationHint = 0;
            var cache = new AnalysisCache(Path.Combine(root, "whole-book-zip-cache"));
            var service = new BookAnalysisService(cache);
            var renders = new List<int>(); var progress = new List<BookAnalysisProgress>();
            var result = service.AnalyzeAsync(book.Publication, async (index, edge, token) =>
            {
                renders.Add(index);
                True(edge == BookAnalysisService.RenderEdge, "analysis decode is bounded independently of display zoom");
                return await book.RenderAsync(index, edge, token);
            }, new InlineProgress<BookAnalysisProgress>(progress.Add)).GetAwaiter().GetResult();
            Equal(book.Publication.Units.Count, result.CompletedPages.Count, "whole generated publication analyzed");
            Equal(book.Publication.Units.Count - 1, result.CompletedPairs.Count, "every adjacent position considered");
            True(result.IsComplete && result.Decisions.ContainsKey(book.Publication.Units[^1].Id), "last page completed before it is displayed");
            True(renders.Contains(book.Publication.Units.Count - 1) && renders.Distinct().Count() == renders.Count, "eligible pages decode once in the sequential pass");
            True(progress.Last().IsComplete && progress.Where(value => !value.FromCache).All(value => value.Decisions.Count <= 1 && value.Pairs.Count <= 1), "bounded incremental results report completed progress");
            bool renderedAgain = false;
            var reopened = service.AnalyzeAsync(book.Publication, (_, _, _) => { renderedAgain = true; throw new InvalidOperationException("Completed cache must not render again."); }).GetAwaiter().GetResult();
            True(reopened.IsComplete && !renderedAgain, "reopening a completed book does no image/OCR work");
        });

        Check("whole-book analysis cancellation checkpoints work without changing reading records and resumes", () =>
        {
            var publication = AnalysisPublication("cancel-book", 5);
            var bitmap = DialogueFixture(true);
            var data = Path.Combine(root, "analysis-with-reading-state");
            var store = new LibraryStore(data);
            store.Save(new SavedBook { Id = publication.Identity, Path = "generated-read-only-book", Title = "原创短册", Position = 3,
                LocatorKey = publication.Units[2].Id, Favorite = true, Bookmarks = [publication.Units[1].Id],
                Overrides = new() { [publication.Units[1].Id] = new PageOverride { Rotation = 90 } } });
            string before = File.ReadAllText(Path.Combine(data, "library.json"));
            var cache = new AnalysisCache(Path.Combine(data, "analysis"));
            var service = new BookAnalysisService(cache);
            using var stop = new CancellationTokenSource();
            bool cancelled = false;
            try
            {
                service.AnalyzeAsync(publication, (_, _, token) => { token.ThrowIfCancellationRequested(); return Task.FromResult(bitmap); },
                    new InlineProgress<BookAnalysisProgress>(value => { if (!value.FromCache && value.CompletedPages >= 2) stop.Cancel(); }), stop.Token).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException) { cancelled = true; }
            True(cancelled, "caller cancellation stops the whole-book worker");
            var partial = cache.LoadAsync(publication).GetAwaiter().GetResult();
            Equal(2, partial.CompletedPages.Count, "completed pages survive cancellation");
            True(!partial.IsComplete && partial.CompletedPages.SetEquals([0, 1]), "unfinished pages are not marked complete");
            Equal(before, File.ReadAllText(Path.Combine(data, "library.json")), "progress, bookmarks and manual overrides are untouched");
            var resumeProgress = new List<BookAnalysisProgress>();
            var resumed = service.AnalyzeAsync(publication, (_, _, _) => Task.FromResult(bitmap),
                new InlineProgress<BookAnalysisProgress>(resumeProgress.Add)).GetAwaiter().GetResult();
            True(resumed.IsComplete && resumeProgress[0].FromCache && resumeProgress[0].Decisions.Count == 2, "reopening resumes the partial automatic result");
            True(resumeProgress.Skip(1).All(value => !value.Decisions.ContainsKey(publication.Units[0].Id) && !value.Decisions.ContainsKey(publication.Units[1].Id)), "finished orientation entries are not rerun");
        });

        Check("whole-book analysis rejects stale source, locator, publisher and algorithm cache identities", () =>
        {
            var publication = AnalysisPublication("versioned-book", 2);
            string directory = Path.Combine(root, "versioned-analysis-cache");
            var cache = new AnalysisCache(directory);
            var bitmap = DialogueFixture(false);
            var result = new BookAnalysisService(cache).AnalyzeAsync(publication, (_, _, _) => Task.FromResult(bitmap)).GetAwaiter().GetResult();
            True(result.IsComplete, "original version completed");
            publication.Revision = "changed-source";
            True(cache.LoadAsync(publication).GetAwaiter().GetResult().CompletedPages.Count == 0, "source revision invalidates cache");
            publication.Revision = "generated-1";
            publication.Units[1].RotationHint = 90;
            True(cache.LoadAsync(publication).GetAwaiter().GetResult().CompletedPages.Count == 0, "changed publisher direction invalidates cache");
            publication.Units[1].RotationHint = 0;
            publication.Units[1] = new ReadingUnit { Locator = new("a-new-spine-occurrence", 1), RotationHint = 0 };
            True(cache.LoadAsync(publication).GetAwaiter().GetResult().CompletedPages.Count == 0, "changed source locator invalidates cache");
            publication = AnalysisPublication("versioned-book", 2);
            result.OrientationVersion = "obsolete-algorithm";
            File.WriteAllText(Path.Combine(directory, result.CacheKey + ".json"), JsonSerializer.Serialize(result));
            True(cache.LoadAsync(publication).GetAwaiter().GetResult().CompletedPages.Count == 0, "obsolete algorithm result is rejected");
        });

        Check("whole-book analysis isolates a failed page, continues later pages and retries incomplete pairs", () =>
        {
            var publication = AnalysisPublication("broken-then-recovered", 4);
            var cache = new AnalysisCache(Path.Combine(root, "retry-analysis-cache"));
            var service = new BookAnalysisService(cache);
            var bitmap = DialogueFixture(false);
            var rendered = new List<int>();
            var partial = service.AnalyzeAsync(publication, (index, _, _) =>
            {
                rendered.Add(index);
                if (index == 1) throw new InvalidOperationException("Generated decoder failure");
                return Task.FromResult(bitmap);
            }).GetAwaiter().GetResult();
            True(!partial.IsComplete && partial.FailedPages.ContainsKey(1), "a bad page remains retryable");
            True(rendered.Contains(3) && partial.CompletedPages.Contains(3) && partial.CompletedPairs.Contains(2), "a decoder failure does not stop later pages and neighbors");
            Equal(1, rendered.Count(index => index == 1), "a failed page is not repeatedly decoded in the same scan");
            var stored = cache.LoadAsync(publication).GetAwaiter().GetResult();
            True(stored.FailedPages.ContainsKey(1) && stored.CompletedPairs.Contains(2), "successful and failed state checkpoint independently");
            var recovered = service.AnalyzeAsync(publication, (_, _, _) => Task.FromResult(bitmap)).GetAwaiter().GetResult();
            True(recovered.IsComplete && recovered.FailedPages.Count == 0, "a later successful scan fills only incomplete work");
            cache.InvalidateAsync(publication).GetAwaiter().GetResult();
            True(cache.LoadAsync(publication).GetAwaiter().GetResult().CompletedPages.Count == 0, "explicit reanalysis discards only automatic analysis");
        });

        if (GlyphOrientationAnalyzer.AvailableLanguages.Any(language => language.StartsWith("zh", StringComparison.OrdinalIgnoreCase) || language.StartsWith("ja", StringComparison.OrdinalIgnoreCase)))
            Check("whole-book analysis identifies an unmarked sideways final page before it is displayed", () =>
            {
                var publication = AnalysisPublication("undisplayed-cjk-page", 3);
                publication.Units[2].RotationHint = null;
                var upright = DialogueFixture(true);
                var sideways = RotateFixture(upright, 90);
                var increments = new List<BookAnalysisProgress>();
                var result = new BookAnalysisService(new AnalysisCache(Path.Combine(root, "unseen-cjk-analysis")))
                    .AnalyzeAsync(publication, (index, _, _) => Task.FromResult(index == 2 ? sideways : upright),
                        new InlineProgress<BookAnalysisProgress>(increments.Add)).GetAwaiter().GetResult();
                True(result.IsComplete, "background scan reaches the undisplayed final page");
                Equal(270, result.Decisions[publication.Units[2].Id].Rotation, "whole-book service actually invokes physical CJK orientation");
                True(increments.Any(value => value.Decisions.TryGetValue(publication.Units[2].Id, out var decision) && decision.Rotation == 270), "final page's automatic correction is published incrementally");
            });
        else Console.WriteLine("SKIP whole-book unmarked CJK analysis: Windows Chinese/Japanese OCR capability is not installed.");
    }

    private static Publication AnalysisPublication(string identity, int count) => new()
    {
        Identity = identity, Revision = "generated-1", Title = "原创分析短册",
        Units = Enumerable.Range(0, count).Select(index => new ReadingUnit
        { Locator = new($"generated-{index}.png"), Width = 900, Height = 1200, IsCover = index == 0, RotationHint = 0 }).ToList()
    };

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T> { public void Report(T value) => report(value); }
}

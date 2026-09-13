using System.IO;
using System.Text.Json;
using System.Windows.Media;
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

        Check("whole-book priority starts at the current page and publishes forward work before filling earlier pages", () =>
        {
            var publication = AnalysisPublication("priority-forward-book", 6);
            var images = Enumerable.Range(0, 6).Select(AnalysisPageFixture).ToArray();
            var renders = new List<int>();
            var reportedPages = new List<int>();
            var reportedPairs = new List<int>();
            var cache = new AnalysisCache(Path.Combine(root, "priority-forward-cache"));
            var result = new BookAnalysisService(cache).AnalyzeAsync(publication, (index, _, _) =>
            {
                if (index == 4) Sequence([3], reportedPages, "the current-page decision arrives before decoding the next page");
                renders.Add(index);
                return Task.FromResult(images[index]);
            }, new InlineProgress<BookAnalysisProgress>(value =>
            {
                reportedPages.AddRange(value.Decisions.Keys.Select(id => publication.Units.FindIndex(unit => unit.Id == id)));
                reportedPairs.AddRange(value.Pairs.Keys);
            }), startIndex: 3).GetAwaiter().GetResult();
            Sequence([3, 2, 4, 5], renders.Take(4), "decode current page first, its required physical predecessor, then following pages");
            Sequence([3, 4, 5, 0, 1, 2], reportedPages, "forward pages are published individually before earlier pages, without skipping any source position");
            Sequence(Enumerable.Range(0, 5), reportedPairs.Order(), "each real adjacent seam is reported once, never last-to-first");
            True(result.IsComplete && result.CompletedPages.SetEquals(Enumerable.Range(0, 6)), "priority scanning finishes the whole source");
            for (int index = 1; index < 5; index++)
                Equal(PairAnalyzer.Analyze(images[index], images[index + 1], index), result.Pairs[index], "priority boundaries retain the correct physical image pair");
        });

        Check("whole-book priority resumes a partial checkpoint from a new reading position without recalculating completed work", () =>
        {
            var publication = AnalysisPublication("priority-resume-book", 6);
            var images = Enumerable.Range(0, 6).Select(AnalysisPageFixture).ToArray();
            var cache = new AnalysisCache(Path.Combine(root, "priority-resume-cache"));
            var service = new BookAnalysisService(cache);
            using var stop = new CancellationTokenSource();
            bool cancelled = false;
            try
            {
                service.AnalyzeAsync(publication, (index, _, _) => Task.FromResult(images[index]),
                    new InlineProgress<BookAnalysisProgress>(value => { if (value.CompletedPages == 2) stop.Cancel(); }),
                    stop.Token, startIndex: 4).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException) { cancelled = true; }
            var partial = cache.LoadAsync(publication).GetAwaiter().GetResult();
            True(cancelled && partial.CompletedPages.SetEquals([4, 5]) && partial.CompletedPairs.SetEquals([3, 4]), "cancellation stores actual completed source indices, not a sequential prefix");
            var renders = new List<int>();
            var changes = new List<int>();
            var resumed = service.AnalyzeAsync(publication, (index, _, _) =>
            {
                renders.Add(index); return Task.FromResult(images[index]);
            }, new InlineProgress<BookAnalysisProgress>(value =>
            {
                if (!value.FromCache) changes.AddRange(value.Decisions.Keys.Select(id => publication.Units.FindIndex(unit => unit.Id == id)));
            }), startIndex: 2).GetAwaiter().GetResult();
            Equal(2, renders[0], "resuming prioritizes the newly requested page");
            Sequence([2, 3, 0, 1], changes, "resume only publishes unfinished pages in the new priority order");
            True(!renders.Contains(4) && !renders.Contains(5), "completed tail pages and seams require no further image decode");
            True(resumed.IsComplete && resumed.CacheKey == partial.CacheKey && partial.Pairs.All(item => resumed.Pairs[item.Key] == item.Value), "priority preserves cached seam decisions and cache identity");
            var reopened = service.AnalyzeAsync(publication, (_, _, _) => throw new InvalidOperationException("Changing scan priority must not invalidate a completed cache."), startIndex: 5).GetAwaiter().GetResult();
            True(reopened.IsComplete && reopened.CacheKey == resumed.CacheKey, "a completed cache remains reusable from any reading position");
        });

        Check("whole-book dynamic navigation promotes a newly requested neighborhood while an older page is being decoded", () =>
        {
            var publication = AnalysisPublication("dynamic-navigation-book", 12);
            var images = Enumerable.Range(0, 12).Select(AnalysisPageFixture).ToArray();
            var priority = new BookAnalysisPriority();
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var renders = new List<int>(); var pages = new List<int>(); var pairs = new List<int>();
            using var stop = new CancellationTokenSource();
            var analysis = new BookAnalysisService(new AnalysisCache(Path.Combine(root, "dynamic-navigation-cache")))
                .AnalyzeAsync(publication, async (index, _, token) =>
                {
                    renders.Add(index);
                    if (index == 6) { entered.TrySetResult(); await release.Task.WaitAsync(token); }
                    return images[index];
                }, new InlineProgress<BookAnalysisProgress>(value =>
                {
                    pages.AddRange(value.Decisions.Keys.Select(id => publication.Units.FindIndex(unit => unit.Id == id)));
                    pairs.AddRange(value.Pairs.Keys);
                }), stop.Token, startIndex: 6, priority: priority);
            try
            {
                // Navigation is issued from this thread while the worker is inside a real render callback.
                entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
                priority.Promote(2);
                release.SetResult();
                var result = analysis.GetAwaiter().GetResult();
                Sequence([6, 5, 2], renders.Take(3), "the in-flight source pair finishes, then the newly requested page decodes next");
                Sequence([6, 2, 1, 3, 0, 4, 5, 7, 8, 9, 10, 11], pages, "the new local neighborhood precedes the old tail and every page is published once");
                Sequence(Enumerable.Range(0, 11), pairs.Order(), "dynamic queue changes neither omit nor repeat physical seams");
                for (int index = 1; index < 11; index++)
                    Equal(PairAnalyzer.Analyze(images[index], images[index + 1], index), result.Pairs[index], "dynamic jumps retain actual predecessor/successor image evidence");
                True(result.IsComplete, "all remaining pages eventually complete after the promoted neighborhood");
            }
            finally
            {
                release.TrySetResult(); stop.Cancel();
                try { analysis.GetAwaiter().GetResult(); } catch (OperationCanceledException) { }
            }
        });

        Check("whole-book dynamic navigation checkpoints noncontiguous work and reports failed neighbor pages before resuming", () =>
        {
            var publication = AnalysisPublication("dynamic-checkpoint-book", 9);
            var images = Enumerable.Range(0, 9).Select(AnalysisPageFixture).ToArray();
            var cache = new AnalysisCache(Path.Combine(root, "dynamic-checkpoint-cache"));
            var service = new BookAnalysisService(cache);
            var priority = new BookAnalysisPriority();
            var progress = new List<BookAnalysisProgress>();
            using var stop = new CancellationTokenSource();
            bool cancelled = false;
            try
            {
                service.AnalyzeAsync(publication, (index, _, _) =>
                {
                    if (index == 7) priority.Promote(3);
                    if (index == 2) throw new IOException("Generated failed predecessor");
                    return Task.FromResult(images[index]);
                }, new InlineProgress<BookAnalysisProgress>(value =>
                {
                    progress.Add(value);
                    if (value.CompletedPages == 2) stop.Cancel();
                }), stop.Token, startIndex: 7, priority: priority).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException) { cancelled = true; }
            var partial = cache.LoadAsync(publication).GetAwaiter().GetResult();
            True(cancelled && partial.CompletedPages.SetEquals([7, 3]) && partial.CompletedPairs.SetEquals([6]), "the checkpoint preserves noncontiguous completed source positions and only completed seams");
            True(progress.Any(value => value.Decisions.ContainsKey(publication.Units[3].Id) && value.FailedPages?.ContainsKey(2) == true), "a failed predecessor is reported with the current-page increment, before the rest of the book");
            True(partial.FailedPages.ContainsKey(2), "failed neighboring work is durable and remains retryable");
            var resumePriority = new BookAnalysisPriority(); resumePriority.Promote(5, precedingPages: 1, followingPages: 1);
            var renders = new List<int>(); var resumedProgress = new List<BookAnalysisProgress>();
            var resumed = service.AnalyzeAsync(publication, (index, _, _) =>
            { renders.Add(index); return Task.FromResult(images[index]); },
                new InlineProgress<BookAnalysisProgress>(resumedProgress.Add), priority: resumePriority).GetAwaiter().GetResult();
            Equal(5, renders[0], "a reopened worker starts at the newly promoted reading position");
            True(resumedProgress[0].FromCache && resumedProgress[0].FailedPages?.ContainsKey(2) == true, "initial cached progress includes failed neighbors for first-paint readiness");
            True(resumedProgress.Skip(1).All(value => !value.Decisions.ContainsKey(publication.Units[3].Id) && !value.Decisions.ContainsKey(publication.Units[7].Id)), "completed automatic page decisions are reused after dynamic cancellation");
            True(resumed.IsComplete && resumed.CacheKey == partial.CacheKey && resumed.FailedPages.Count == 0 && resumedProgress[^1].FailedPages?.Count == 0, "successful retry clears failure snapshots while completing the same cache identity");
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
            var priority = new BookAnalysisPriority();
            var partial = service.AnalyzeAsync(publication, (index, _, _) =>
            {
                rendered.Add(index);
                if (index == 1) throw new InvalidOperationException("Generated decoder failure");
                return Task.FromResult(bitmap);
            }, new InlineProgress<BookAnalysisProgress>(_ => priority.Promote(1)), priority: priority).GetAwaiter().GetResult();
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

    private static BitmapSource AnalysisPageFixture(int page)
    {
        const int width = 384, height = 512;
        var pixels = new byte[width * height];
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
        {
            double phase = (x + page * width) * .008;
            double value = .50 + .17 * Math.Sin(y * .049 + phase) + .15 * Math.Sin(y * .117 + phase * .8) + .13 * Math.Cos(y * .189 - phase * 1.2);
            pixels[y * width + x] = (byte)Math.Round(Math.Clamp(value, 0, 1) * 255);
        }
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Gray8, null, pixels, width);
        bitmap.Freeze(); return bitmap;
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T> { public void Report(T value) => report(value); }
}

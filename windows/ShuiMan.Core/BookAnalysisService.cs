using System.Diagnostics;
using System.IO;
using System.Windows.Media.Imaging;

namespace ShuiMan.Core;

public sealed record BookAnalysisProgress(int CompletedPages, int TotalPages,
    IReadOnlyDictionary<string, SpreadDecision> Decisions, IReadOnlyDictionary<int, PairDecision> Pairs,
    bool IsComplete = false, bool FromCache = false, string? Warning = null, IReadOnlyDictionary<int, string>? FailedPages = null);

/// <summary>
/// Scans from the requested reading position to the end, then fills preceding pages on a worker.
/// Only the current and previous analysis images are held; callers serialize rendering with their
/// publication lifetime, releasing their lock before OCR. Priority does not change cache identities.
/// </summary>
public sealed class BookAnalysisService(AnalysisCache cache)
{
    public const int RenderEdge = 1100;

    public Task<BookAnalysisSnapshot> AnalyzeAsync(Publication publication,
        Func<int, int, CancellationToken, Task<BitmapSource>> render,
        IProgress<BookAnalysisProgress>? progress = null, CancellationToken cancellationToken = default, int startIndex = 0,
        BookAnalysisPriority? priority = null)
    {
        ArgumentNullException.ThrowIfNull(publication); ArgumentNullException.ThrowIfNull(render);
        return Task.Run(async () =>
        {
            var result = await cache.LoadAsync(publication, cancellationToken).ConfigureAwait(false);
            progress?.Report(new(result.CompletedPages.Count, result.TotalPages, new Dictionary<string, SpreadDecision>(result.Decisions),
                new Dictionary<int, PairDecision>(result.Pairs), result.IsComplete,
                result.CompletedPages.Count > 0 || result.CompletedPairs.Count > 0, cache.Warning, new Dictionary<int, string>(result.FailedPages)));
            if (result.IsComplete) return result;
            var checkpoint = Stopwatch.StartNew();
            int changesSinceCheckpoint = 0;
            BitmapSource? previous = null;
            int previousIndex = -1;
            bool dirty = false;
            var failedThisRun = new HashSet<int>();
            try
            {
                int processed = 0;
                foreach (int index in (priority ?? new BookAnalysisPriority()).VisitOrder(publication.Units.Count, startIndex, cancellationToken))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    // Visiting a page out of source order never makes it adjacent to the prior
                    // visited page. The seam always uses this page and its physical predecessor.
                    var unit = publication.Units[index];
                    var orientationDelta = new Dictionary<string, SpreadDecision>();
                    var pairDelta = new Dictionary<int, PairDecision>();
                    bool previousPairPending = index > 0 && !result.CompletedPairs.Contains(index - 1);
                    bool nextPairPending = index + 1 < result.TotalPages && !result.CompletedPairs.Contains(index);
                    bool needsOcr = !result.CompletedPages.Contains(index) && unit.RotationHint == null && !unit.IsCover && IsRaster(unit);
                    bool needsImage = needsOcr || previousPairPending && EligiblePair(publication, index - 1) || nextPairPending && EligiblePair(publication, index);
                    BitmapSource? current = needsImage ? await ReadImage(index).ConfigureAwait(false) : null;
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!result.CompletedPages.Contains(index))
                    {
                        SpreadDecision? decision;
                        if (unit.RotationHint is int angle)
                        {
                            angle = LayoutEngine.NormalizeRotation(angle);
                            decision = new(angle, angle != 0 || unit.Height > 0 && unit.Width / unit.Height >= 1.2,
                                Reason: angle == 0 ? "出版物明确原方向" : "出版物旋转样式 · 完整展示");
                        }
                        else if (!IsRaster(unit)) decision = new(Standalone: true, Uncertain: true, Reason: unit.Error ?? "此页面无法进行图片分析");
                        else if (unit.IsCover) decision = new();
                        else
                        {
                            decision = null;
                            if (current != null)
                            {
                                try { decision = await SpreadAnalyzer.AnalyzeAsync(current, unit, cancellationToken).ConfigureAwait(false); }
                                catch (Exception ex) when (Recoverable(ex))
                                { cancellationToken.ThrowIfCancellationRequested(); result.FailedPages[index] = ex.Message; dirty = true; }
                            }
                        }
                        cancellationToken.ThrowIfCancellationRequested();
                        if (decision != null)
                        {
                            result.Decisions[unit.Id] = decision; orientationDelta[unit.Id] = decision;
                            result.CompletedPages.Add(index);
                            dirty = true; changesSinceCheckpoint++;
                        }
                    }
                    if (previousPairPending)
                    {
                        PairDecision? pair = null;
                        if (!EligiblePair(publication, index - 1)) pair = new(index - 1);
                        else
                        {
                            if (previousIndex != index - 1 || previous == null)
                            {
                                // A navigation promotion can leave a bitmap from an unrelated page.
                                // Release it before decoding the actual neighbor to retain only two images.
                                previous = null; previousIndex = -1;
                                previous = await ReadImage(index - 1).ConfigureAwait(false); previousIndex = index - 1;
                            }
                            if (previous != null && current != null)
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                                try { pair = PairAnalyzer.Analyze(previous, current, index - 1); }
                                catch (Exception ex) when (Recoverable(ex))
                                { cancellationToken.ThrowIfCancellationRequested(); result.FailedPages[index] = ex.Message; dirty = true; }
                                cancellationToken.ThrowIfCancellationRequested();
                            }
                        }
                        if (pair != null)
                        {
                            result.Pairs[index - 1] = pair; result.CompletedPairs.Add(index - 1); pairDelta[index - 1] = pair;
                            dirty = true; changesSinceCheckpoint++;
                        }
                    }
                    previous = nextPairPending && EligiblePair(publication, index) ? current : null;
                    previousIndex = previous == null ? -1 : index;
                    // Checkpoint early, periodically, and on exit; avoid quadratic disk writes for long books.
                    if (dirty && (processed == 0 || changesSinceCheckpoint >= 8 || checkpoint.Elapsed >= TimeSpan.FromSeconds(2)))
                    {
                        await cache.SaveAsync(publication, result, cancellationToken).ConfigureAwait(false);
                        dirty = false; changesSinceCheckpoint = 0; checkpoint.Restart();
                    }
                    progress?.Report(new(result.CompletedPages.Count, result.TotalPages, orientationDelta, pairDelta,
                        result.IsComplete, Warning: result.FailedPages.GetValueOrDefault(index) ?? cache.Warning,
                        FailedPages: new Dictionary<int, string>(result.FailedPages)));
                    processed++;
                }
                return result;
            }
            finally
            {
                previous = null;
                // Cancellation invalidates unfinished work only; completed checkpoints remain usable on reopen.
                if (dirty) await cache.SaveAsync(publication, result, CancellationToken.None).ConfigureAwait(false);
            }

            async Task<BitmapSource?> ReadImage(int index)
            {
                if (failedThisRun.Contains(index)) return null;
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var image = await render(index, RenderEdge, cancellationToken).ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!image.IsFrozen) throw new InvalidOperationException("后台分析渲染回调必须返回已冻结的图片。");
                    if (result.FailedPages.Remove(index)) dirty = true;
                    return image;
                }
                catch (Exception ex) when (Recoverable(ex))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    result.FailedPages[index] = ex.Message; failedThisRun.Add(index); dirty = true;
                    return null;
                }
            }
        }, cancellationToken);
    }

    private static bool IsRaster(ReadingUnit unit) => !unit.Complex && unit.Error == null;
    private static bool Recoverable(Exception exception) => exception is not OperationCanceledException and not OutOfMemoryException and not AccessViolationException and not StackOverflowException;
    private static bool EligiblePair(Publication publication, int index) => index >= 0 && index + 1 < publication.Units.Count &&
        IsRaster(publication.Units[index]) && IsRaster(publication.Units[index + 1]) &&
        !publication.Units[index].IsCover && !publication.Units[index + 1].IsCover;
}

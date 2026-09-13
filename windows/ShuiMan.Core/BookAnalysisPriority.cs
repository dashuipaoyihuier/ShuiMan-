namespace ShuiMan.Core;

/// <summary>Thread-safe navigation requests for a running whole-book analysis.</summary>
public sealed class BookAnalysisPriority
{
    private readonly object gate = new();
    private Request? latest;
    private long revision;

    /// <summary>
    /// Prioritizes the current page and nearby orientation/seam evidence at the next page boundary.
    /// The remaining source pages still run afterwards; completed work is never invalidated.
    /// </summary>
    public void Promote(int index, int precedingPages = 2, int followingPages = 3)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(precedingPages);
        ArgumentOutOfRangeException.ThrowIfNegative(followingPages);
        lock (gate) latest = new(++revision, index, precedingPages, followingPages);
    }

    private Request? Read() { lock (gate) return latest; }

    internal IEnumerable<int> VisitOrder(int count, int startIndex, CancellationToken cancellationToken)
    {
        if (count == 0) yield break;
        var visited = new bool[count];
        Request? active = Read();
        IEnumerator<int> pending = Order(active).GetEnumerator();
        try
        {
            for (int processed = 0; processed < count; processed++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var requested = Read();
                if (requested != active)
                {
                    pending.Dispose();
                    active = requested;
                    pending = Order(active).GetEnumerator();
                }
                while (pending.MoveNext())
                {
                    int index = pending.Current;
                    if (visited[index]) continue;
                    visited[index] = true;
                    yield return index;
                    break;
                }
            }
        }
        finally { pending.Dispose(); }

        IEnumerable<int> Order(Request? request)
        {
            int first = Math.Clamp(request?.Index ?? startIndex, 0, count - 1);
            if (request is not Request navigation)
            {
                for (int index = first; index < count; index++) yield return index;
                for (int index = 0; index < first; index++) yield return index;
                yield break;
            }
            int before = Math.Min(navigation.Before, first);
            int after = Math.Min(navigation.After, count - first - 1);
            yield return first;
            for (int distance = 1; distance <= Math.Max(before, after); distance++)
            {
                if (distance <= before) yield return first - distance;
                if (distance <= after) yield return first + distance;
            }
            for (int index = first + after + 1; index < count; index++) yield return index;
            for (int index = 0; index < first - before; index++) yield return index;
        }
    }

    private readonly record struct Request(long Revision, int Index, int Before, int After);
}

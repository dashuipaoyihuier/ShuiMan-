using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShuiMan.Core;

/// <summary>Only automatic analysis is cached; reading position, preferences, and manual overrides are never stored here.</summary>
public sealed class BookAnalysisSnapshot
{
    public string CacheKey { get; set; } = "";
    public string BookId { get; set; } = "";
    public string Revision { get; set; } = "";
    public string OrientationVersion { get; set; } = "";
    public string PairVersion { get; set; } = "";
    public int TotalPages { get; set; }
    public Dictionary<string, SpreadDecision> Decisions { get; set; } = [];
    public Dictionary<int, PairDecision> Pairs { get; set; } = [];
    public HashSet<int> CompletedPages { get; set; } = [];
    public HashSet<int> CompletedPairs { get; set; } = [];
    public Dictionary<int, string> FailedPages { get; set; } = [];
    [JsonIgnore] public bool IsComplete => CompletedPages.Count == TotalPages && CompletedPairs.Count == Math.Max(0, TotalPages - 1);
}

/// <summary>Disposable automatic-analysis checkpoints, invalidated by source revision, page identities, and algorithm versions.</summary>
public sealed class AnalysisCache
{
    private const string Schema = "shuiman-book-analysis-1";
    private static readonly JsonSerializerOptions JsonOptions = new();
    private readonly string directory;
    public string? Warning { get; private set; }

    public AnalysisCache(string directory) => this.directory = Path.GetFullPath(directory);

    /// <summary>Call after cancelling/awaiting this book's running analysis so it cannot republish an old checkpoint.</summary>
    public Task InvalidateAsync(Publication publication, CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        var expected = CreateSnapshot(publication);
        var path = Path.Combine(directory, expected.CacheKey + ".json");
        using var mutex = new Mutex(false, "Local\\ShuiMan.Analysis." + expected.CacheKey);
        bool entered;
        try { entered = mutex.WaitOne(TimeSpan.FromSeconds(10)); }
        catch (AbandonedMutexException) { entered = true; }
        if (!entered) throw new IOException("另一窗口正在保存自动分析缓存。");
        try { cancellationToken.ThrowIfCancellationRequested(); if (File.Exists(path)) File.Delete(path); }
        finally { mutex.ReleaseMutex(); }
    }, cancellationToken);

    public Task<BookAnalysisSnapshot> LoadAsync(Publication publication, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var empty = CreateSnapshot(publication);
            try { return Read(Path.Combine(directory, empty.CacheKey + ".json"), empty, publication) ?? empty; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
            { Warning = "自动分析缓存暂时无法读取，将重新分析；阅读记录和手工修正保留。"; return empty; }
        }, cancellationToken);

    public Task SaveAsync(Publication publication, BookAnalysisSnapshot snapshot, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (snapshot.BookId != publication.Identity || snapshot.Revision != publication.Revision || snapshot.TotalPages != publication.Units.Count)
                throw new ArgumentException("分析结果不属于当前出版物。", nameof(snapshot));
            if (snapshot.CacheKey.Length != 64 || snapshot.CacheKey.Any(character => !char.IsAsciiHexDigit(character)))
                throw new ArgumentException("分析缓存标识无效。", nameof(snapshot));
            string path = Path.Combine(directory, snapshot.CacheKey + ".json");
            try
            {
                Directory.CreateDirectory(directory);
                using var mutex = new Mutex(false, "Local\\ShuiMan.Analysis." + snapshot.CacheKey);
                bool entered;
                try { entered = mutex.WaitOne(TimeSpan.FromSeconds(10)); }
                catch (AbandonedMutexException) { entered = true; }
                if (!entered) throw new IOException("另一窗口正在保存自动分析缓存。");
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    BookAnalysisSnapshot? existing = null;
                    try { existing = Read(path, snapshot, publication); }
                    catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException) { }
                    var merged = Merge(existing, snapshot);
                    var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    try
                    {
                        using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 16384, FileOptions.WriteThrough))
                        { JsonSerializer.Serialize(stream, merged, JsonOptions); stream.Flush(true); }
                        if (File.Exists(path)) File.Replace(temporary, path, null);
                        else File.Move(temporary, path);
                    }
                    finally { if (File.Exists(temporary)) File.Delete(temporary); }
                }
                finally { mutex.ReleaseMutex(); }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { Warning = "自动分析结果暂时无法写入缓存；本次阅读继续，下次打开会重新分析。"; }
        }, cancellationToken);

    private static BookAnalysisSnapshot CreateSnapshot(Publication publication)
    {
        ArgumentNullException.ThrowIfNull(publication);
        string languages;
        try { languages = string.Join(',', GlyphOrientationAnalyzer.AvailableLanguages.Order(StringComparer.Ordinal)); }
        catch { languages = "unavailable"; }
        var signature = JsonSerializer.SerializeToUtf8Bytes(new
        {
            Schema, publication.Identity, publication.Revision, SpreadAnalyzer.AlgorithmVersion,
            Pair = PairAnalyzer.AlgorithmVersion, Languages = languages,
            Units = publication.Units.Select(unit => new { unit.Id, unit.Width, unit.Height, unit.IsCover, unit.RotationHint, unit.Complex, unit.Error }).ToArray()
        });
        return new BookAnalysisSnapshot
        {
            CacheKey = Convert.ToHexStringLower(SHA256.HashData(signature)), BookId = publication.Identity,
            Revision = publication.Revision, TotalPages = publication.Units.Count,
            OrientationVersion = SpreadAnalyzer.AlgorithmVersion, PairVersion = PairAnalyzer.AlgorithmVersion
        };
    }

    private static BookAnalysisSnapshot? Read(string path, BookAnalysisSnapshot expected, Publication publication)
    {
        if (!File.Exists(path)) return null;
        if (new FileInfo(path).Length > 64L * 1024 * 1024) throw new InvalidDataException("分析缓存超过大小上限。");
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        var value = JsonSerializer.Deserialize<BookAnalysisSnapshot>(input, JsonOptions);
        if (value == null || value.CacheKey != expected.CacheKey || value.BookId != expected.BookId || value.Revision != expected.Revision ||
            value.OrientationVersion != SpreadAnalyzer.AlgorithmVersion || value.PairVersion != PairAnalyzer.AlgorithmVersion || value.TotalPages != expected.TotalPages ||
            value.Decisions == null || value.Pairs == null || value.CompletedPages == null || value.CompletedPairs == null || value.FailedPages == null)
            return null;
        var ids = publication.Units.Select(unit => unit.Id).ToHashSet(StringComparer.Ordinal);
        if (value.Decisions.Any(pair => !ids.Contains(pair.Key) || pair.Value == null || pair.Value.Rotation is not (0 or 90 or 180 or 270)) ||
            value.CompletedPages.Any(index => index < 0 || index >= value.TotalPages || !value.Decisions.ContainsKey(publication.Units[index].Id)) ||
            value.CompletedPairs.Any(index => index < 0 || index >= value.TotalPages - 1 || !value.Pairs.ContainsKey(index)) ||
            value.Pairs.Any(pair => pair.Key < 0 || pair.Key >= value.TotalPages - 1 || pair.Value == null || pair.Value.FirstIndex != pair.Key ||
                !double.IsFinite(pair.Value.Score) || !double.IsFinite(pair.Value.VerticalOffset) || !double.IsFinite(pair.Value.RightScale)) ||
            value.FailedPages.Any(pair => pair.Key < 0 || pair.Key >= value.TotalPages || string.IsNullOrWhiteSpace(pair.Value))) return null;
        return value;
    }

    private static BookAnalysisSnapshot Merge(BookAnalysisSnapshot? existing, BookAnalysisSnapshot incoming)
    {
        var result = new BookAnalysisSnapshot
        {
            CacheKey = incoming.CacheKey, BookId = incoming.BookId, Revision = incoming.Revision,
            OrientationVersion = incoming.OrientationVersion, PairVersion = incoming.PairVersion, TotalPages = incoming.TotalPages,
            Decisions = existing == null ? [] : new(existing.Decisions), Pairs = existing == null ? [] : new(existing.Pairs),
            CompletedPages = existing == null ? [] : new(existing.CompletedPages), CompletedPairs = existing == null ? [] : new(existing.CompletedPairs),
            FailedPages = existing == null ? [] : new(existing.FailedPages)
        };
        foreach (var pair in incoming.Decisions) result.Decisions[pair.Key] = pair.Value;
        foreach (var pair in incoming.Pairs) result.Pairs[pair.Key] = pair.Value;
        result.CompletedPages.UnionWith(incoming.CompletedPages); result.CompletedPairs.UnionWith(incoming.CompletedPairs);
        foreach (var pair in incoming.FailedPages) result.FailedPages[pair.Key] = pair.Value;
        foreach (int index in incoming.CompletedPages)
            if (!incoming.FailedPages.ContainsKey(index)) result.FailedPages.Remove(index);
        return result;
    }
}

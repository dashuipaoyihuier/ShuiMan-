using System.IO;
using System.Text.Json;

namespace ShuiMan.Core;

/// <summary>Atomic, durable local snapshots. Book source files are never changed.</summary>
public sealed class LibraryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly object gate = new();
    private readonly string filePath;
    private readonly string mutexName;
    public string DirectoryPath { get; }
    public string? Warning { get; private set; }

    public LibraryStore(string? directory = null)
    {
        DirectoryPath = System.IO.Path.GetFullPath(directory ?? System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ShuiMan"));
        Directory.CreateDirectory(DirectoryPath);
        filePath = System.IO.Path.Combine(DirectoryPath, "library.json");
        mutexName = "Local\\ShuiMan.Library." + LibraryScanner.IdentityForPath(DirectoryPath);
        using (AcquireFileLock()) _ = Load();
    }

    public IReadOnlyList<SavedBook> Books
    {
        get
        {
            lock (gate)
            {
                using var fileLock = AcquireFileLock();
                // Return fresh independent records, including changes saved by another app window.
                return Load().OrderByDescending(book => book.OpenedAt).ToList();
            }
        }
    }

    public void Save(SavedBook book)
    {
        ArgumentNullException.ThrowIfNull(book);
        SaveMany([book]);
    }

    /// <summary>Imports or updates a batch with one durable write. Callers merge any existing reading state first.</summary>
    public void SaveMany(IEnumerable<SavedBook> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var incoming = values.ToList();
        if (incoming.Count == 0) return;
        if (incoming.Any(book => book == null || string.IsNullOrWhiteSpace(book.Id)))
            throw new ArgumentException("书籍必须有稳定的标识。", nameof(values));
        lock (gate)
        {
            using var fileLock = AcquireFileLock();
            // A file association can launch another process. Always merge into the
            // latest disk state while holding the shared mutex; never overwrite it
            // with the snapshot that happened to exist when this window started.
            var byId = Load().ToDictionary(book => book.Id, StringComparer.Ordinal);
            foreach (var snapshot in Clone(incoming)) byId[snapshot.Id] = snapshot;
            var merged = byId.Values.ToList();
            Persist(merged);
        }
    }

    public void Remove(string id)
    {
        lock (gate)
        {
            using var fileLock = AcquireFileLock();
            var books = Load();
            var next = books.Where(book => book.Id != id).ToList();
            if (next.Count == books.Count) return;
            Persist(next);
        }
    }

    private IDisposable AcquireFileLock() => new FileLock(mutexName);
    private sealed class FileLock : IDisposable
    {
        private readonly Mutex mutex;
        public FileLock(string name)
        {
            mutex = new Mutex(false, name);
            try
            {
                bool entered;
                try { entered = mutex.WaitOne(TimeSpan.FromSeconds(10)); }
                catch (AbandonedMutexException) { entered = true; }
                if (!entered) throw new IOException("另一水漫窗口正在保存书库，请稍后重试。");
            }
            catch { mutex.Dispose(); throw; }
        }
        public void Dispose()
        {
            try { mutex.ReleaseMutex(); }
            finally { mutex.Dispose(); }
        }
    }

    private List<SavedBook> Load()
    {
        if (!File.Exists(filePath))
        {
            if (!File.Exists(filePath + ".bak")) return [];
            try
            {
                var recovered = ReadSnapshot(filePath + ".bak");
                Warning = "书库主文件缺失，已从上次保存的备份恢复。";
                return recovered;
            }
            catch (Exception ex) when (ex is JsonException or InvalidDataException)
            {
                Warning = "书库主文件缺失，备份也无法读取。已保留备份，请重新导入书籍；原漫画文件不受影响。";
                return [];
            }
        }
        try { return ReadSnapshot(filePath); }
        catch (Exception ex) when (ex is JsonException or InvalidDataException)
        {
            var preserved = filePath + $".corrupt-{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}";
            File.Move(filePath, preserved);
            Warning = $"书库记录损坏，原文件已保存在 {preserved}。";
            if (File.Exists(filePath + ".bak"))
            {
                try
                {
                    var recovered = ReadSnapshot(filePath + ".bak");
                    Warning += " 已从上次保存的备份恢复；最近一次更改可能需要重做。";
                    return recovered;
                }
                catch (Exception backupError) when (backupError is JsonException or InvalidDataException)
                { Warning += " 备份也无法读取，请重新导入书籍；原漫画文件不受影响。"; }
            }
            return [];
        }
    }

    private static List<SavedBook> ReadSnapshot(string path)
    {
        using var stream = File.OpenRead(path);
        var values = JsonSerializer.Deserialize<List<SavedBook>>(stream, JsonOptions)
            ?? throw new InvalidDataException("书库记录为空。");
        if (values.Any(book => book == null || string.IsNullOrWhiteSpace(book.Id) ||
                book.Path == null || book.Title == null || book.Series == null || book.Revision == null || book.ReadState == null) ||
            values.Select(book => book.Id).Distinct(StringComparer.Ordinal).Count() != values.Count)
            throw new InvalidDataException("书库记录包含无效或重复的书籍标识。");
        foreach (var book in values)
        {
            book.Preferences ??= new();
            book.Overrides ??= [];
            book.Bookmarks ??= [];
            foreach (var key in book.Overrides.Where(pair => pair.Value == null).Select(pair => pair.Key).ToArray())
                book.Overrides.Remove(key);
            book.Position = Math.Max(0, book.Position);
            book.Total = Math.Max(0, book.Total);
        }
        return values;
    }

    private void Persist(List<SavedBook> next)
    {
        var temporary = System.IO.Path.Combine(DirectoryPath, $"library-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                       16384, FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, next, JsonOptions);
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(filePath)) File.Replace(temporary, filePath, filePath + ".bak");
            else File.Move(temporary, filePath);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions), JsonOptions)!;
}

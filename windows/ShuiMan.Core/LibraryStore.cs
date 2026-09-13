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

    /// <summary>Updates only library metadata against the latest disk record, never the reader's position or settings.</summary>
    public SavedBook? UpdateMetadata(string id, string? title = null, string? series = null,
        IEnumerable<string>? tags = null, bool? favorite = null, string? readState = null)
    {
        if (title != null && string.IsNullOrWhiteSpace(title)) throw new ArgumentException("书名不能为空。", nameof(title));
        if (readState != null && readState is not ("未读" or "在读" or "已读")) throw new ArgumentException("阅读状态无效。", nameof(readState));
        lock (gate)
        {
            using var fileLock = AcquireFileLock();
            var books = Load();
            var book = books.FirstOrDefault(value => value.Id == id);
            if (book == null) return null;
            if (title != null) book.Title = title.Trim();
            if (series != null) book.Series = series.Trim();
            if (tags != null) book.Tags = tags.Select(tag => tag.Trim()).Where(tag => tag.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase).Take(40).ToList();
            if (favorite != null) book.Favorite = favorite.Value;
            if (readState != null) book.ReadState = readState;
            book.MetadataVersion++;
            Persist(books);
            return Clone(book);
        }
    }

    /// <summary>Persists reader-owned fields while preserving metadata edited in another window.</summary>
    public SavedBook SaveReadingState(SavedBook snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (string.IsNullOrWhiteSpace(snapshot.Id)) throw new ArgumentException("书籍必须有稳定的标识。", nameof(snapshot));
        lock (gate)
        {
            using var fileLock = AcquireFileLock();
            var books = Load();
            var current = books.FirstOrDefault(value => value.Id == snapshot.Id);
            if (current == null)
            {
                current = Clone(snapshot);
                if (current.AddedAt == DateTime.MinValue) current.AddedAt = DateTime.UtcNow;
                books.Add(current);
            }
            else
            {
                current.Path = snapshot.Path;
                current.Revision = snapshot.Revision;
                current.LocatorKey = snapshot.LocatorKey;
                current.Position = Math.Max(0, snapshot.Position);
                current.Total = Math.Max(0, snapshot.Total);
                current.OpenedAt = snapshot.OpenedAt;
                current.Preferences = Clone(snapshot.Preferences);
                current.Overrides = Clone(snapshot.Overrides);
                current.Bookmarks = Clone(snapshot.Bookmarks);
                // A newly edited manual state wins over an older open-reader snapshot.
                if (snapshot.MetadataVersion >= current.MetadataVersion) current.ReadState = snapshot.ReadState;
            }
            Persist(books);
            return Clone(current);
        }
    }

    /// <summary>Merges scanner results without replacing titles, favorites, tags, or any saved reading state.</summary>
    public int ImportDiscovered(IEnumerable<SavedBook> discovered)
    {
        ArgumentNullException.ThrowIfNull(discovered);
        var incoming = discovered.ToList();
        if (incoming.Any(book => string.IsNullOrWhiteSpace(book.Id))) throw new ArgumentException("书籍必须有稳定的标识。", nameof(discovered));
        lock (gate)
        {
            using var fileLock = AcquireFileLock();
            var books = Load().ToDictionary(book => book.Id, StringComparer.Ordinal);
            int added = 0;
            bool changed = false;
            foreach (var candidate in incoming)
            {
                if (books.TryGetValue(candidate.Id, out var existing))
                {
                    if (existing.Revision != candidate.Revision || existing.Path != candidate.Path)
                    {
                        existing.Path = candidate.Path;
                        existing.Revision = candidate.Revision;
                        changed = true;
                    }
                }
                else
                {
                    var book = Clone(candidate);
                    book.AddedAt = DateTime.UtcNow;
                    books.Add(book.Id, book);
                    added++; changed = true;
                }
            }
            if (changed) Persist(books.Values.ToList());
            return added;
        }
    }

    public IReadOnlyList<string> SourceFolders
    {
        get { lock (gate) { using var fileLock = AcquireFileLock(); return LoadFolders(); } }
    }

    public void AddSourceFolder(string path) => ChangeSourceFolder(path, true);
    public void RemoveSourceFolder(string path) => ChangeSourceFolder(path, false);

    private void ChangeSourceFolder(string path, bool add)
    {
        path = System.IO.Path.GetFullPath(path).TrimEnd(System.IO.Path.DirectorySeparatorChar);
        // Keep drive roots rooted (C:\\ must not become the relative path C:).
        if (path.EndsWith(':')) path += System.IO.Path.DirectorySeparatorChar;
        lock (gate)
        {
            using var fileLock = AcquireFileLock();
            var folders = LoadFolders();
            if (add && !folders.Contains(path, StringComparer.OrdinalIgnoreCase)) folders.Add(path);
            if (!add) folders.RemoveAll(value => value.Equals(path, StringComparison.OrdinalIgnoreCase));
            var settingsPath = System.IO.Path.Combine(DirectoryPath, "folders.json");
            var temporary = settingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                { JsonSerializer.Serialize(stream, folders, JsonOptions); stream.Flush(true); }
                if (File.Exists(settingsPath)) File.Replace(temporary, settingsPath, settingsPath + ".bak");
                else File.Move(temporary, settingsPath);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }

    private List<string> LoadFolders()
    {
        var path = System.IO.Path.Combine(DirectoryPath, "folders.json");
        foreach (var candidate in new[] { path, path + ".bak" })
        {
            if (!File.Exists(candidate)) continue;
            try
            {
                var values = JsonSerializer.Deserialize<List<string>>(File.ReadAllBytes(candidate), JsonOptions);
                if (values == null || values.Any(string.IsNullOrWhiteSpace)) throw new JsonException("文件夹列表无效。");
                return values.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            { Warning = "保存的漫画目录暂时无法读取；书籍和阅读记录已保留，可重新添加目录。"; }
        }
        return [];
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
            book.Tags ??= [];
            book.Tags = book.Tags.Where(tag => !string.IsNullOrWhiteSpace(tag)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (book.AddedAt == DateTime.MinValue && book.OpenedAt != DateTime.MinValue) book.AddedAt = book.OpenedAt;
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

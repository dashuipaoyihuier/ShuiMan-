using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace ShuiMan.Core;

/// <summary>Discovers books using directory entries and metadata only; never decodes pages.</summary>
public static class LibraryScanner
{
    public static IReadOnlySet<string> ImageExtensions => DocumentEngine.ImageExtensions;
    public static readonly IReadOnlySet<string> BookExtensions = new HashSet<string>(
        DocumentEngine.SupportedExtensions.Except(DocumentEngine.ImageExtensions), StringComparer.OrdinalIgnoreCase);

    public static string IdentityForPath(string path) => Hash(System.IO.Path.GetFullPath(path).TrimEnd('\\', '/').ToUpperInvariant());

    public static List<SavedBook> Scan(string root, CancellationToken cancellationToken = default,
        Action<string>? warning = null)
    {
        var fullRoot = System.IO.Path.GetFullPath(root);
        cancellationToken.ThrowIfCancellationRequested();
        var rootInfo = new DirectoryInfo(fullRoot);
        if (!rootInfo.Exists) throw new DirectoryNotFoundException($"找不到书库文件夹：{fullRoot}");
        if ((rootInfo.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            warning?.Invoke($"已跳过链接文件夹：{fullRoot}");
            return [];
        }
        var rootNode = new ScanNode(rootInfo);
        var visited = new List<ScanNode>();
        var pending = new Stack<ScanNode>();
        pending.Push(rootNode);
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var node = pending.Pop();
            visited.Add(node);
            var directory = node.Directory;
            FileSystemInfo[] entries;
            try { entries = directory.GetFileSystemInfos(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                warning?.Invoke($"无法扫描 {directory.FullName}：{ex.Message}");
                continue;
            }
            Array.Sort(entries, (a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name));
            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if ((entry.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Hidden | FileAttributes.System)) != 0 ||
                        entry.Name.StartsWith('.') || entry.Name.Equals("__MACOSX", StringComparison.OrdinalIgnoreCase)) continue;
                    if (entry is DirectoryInfo child)
                    {
                        var childNode = new ScanNode(child);
                        node.Children.Add(childNode);
                        pending.Push(childNode);
                    }
                    else if (entry is FileInfo file && BookExtensions.Contains(file.Extension))
                    {
                        node.Books.Add(Create(file.FullName, System.IO.Path.GetFileNameWithoutExtension(file.Name),
                            file.Directory?.Name ?? "", $"{file.Length}:{file.LastWriteTimeUtc.Ticks}"));
                    }
                    else if (entry is FileInfo image && ImageExtensions.Contains(image.Extension))
                        node.Images.Add($"{image.Name}:{image.Length}:{image.LastWriteTimeUtc.Ticks}");
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { warning?.Invoke($"无法读取 {entry.FullName}：{ex.Message}"); }
            }
        }
        // Resolve volume boundaries from leaves toward the root. Opening an image
        // folder includes its descendants, so emitting both parent and child books
        // would duplicate every child page in the library. A parent cover must also
        // never hide actual ZIP/EPUB/PDF books further down the directory tree.
        for (var i = visited.Count - 1; i >= 0; i--)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var node = visited[i];
            node.ContainsBooks = node.Books.Count > 0 || node.Children.Any(child => child.ContainsBooks);
            node.ImageCount = node.Images.Count + node.Children.Sum(child => child.ImageCount);
            node.Revision = Hash(string.Join('\n', node.Images.Concat(node.Children.Where(child => child.ImageCount > 0)
                .Select(child => $"{child.Directory.Name}/{child.Revision}"))));
            if (node.Images.Count > 0 && !node.ContainsBooks)
            {
                var directory = node.Directory;
                var book = Create(directory.FullName, directory.Name, directory.Parent?.Name ?? "", node.Revision);
                book.Total = node.ImageCount;
                node.Volumes = [book];
            }
            else node.Volumes = node.Books.Concat(node.Children.SelectMany(child => child.Volumes)).ToList();
        }
        return rootNode.Volumes.OrderBy(book => book.Series, NaturalPathComparer.Instance)
            .ThenBy(book => book.Title, NaturalPathComparer.Instance)
            .ThenBy(book => book.Path, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private sealed class ScanNode(DirectoryInfo directory)
    {
        public DirectoryInfo Directory { get; } = directory;
        public List<ScanNode> Children { get; } = [];
        public List<string> Images { get; } = [];
        public List<SavedBook> Books { get; } = [];
        public List<SavedBook> Volumes { get; set; } = [];
        public bool ContainsBooks { get; set; }
        public int ImageCount { get; set; }
        public string Revision { get; set; } = "";
    }

    private static SavedBook Create(string path, string title, string series, string revision) => new()
    {
        Id = IdentityForPath(path), Path = path, Title = title, Series = series, Revision = revision,
        OpenedAt = DateTime.MinValue
    };
    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

using System.ComponentModel;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ShuiMan.Core;

namespace ShuiMan.Windows;

/// <summary>WPF Border does not round-clip its child image; apply the matching geometry explicitly.</summary>
public sealed class LibraryCoverFrame : Border
{
    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        Clip = new RectangleGeometry(new Rect(sizeInfo.NewSize), CornerRadius.TopLeft, CornerRadius.TopLeft);
    }
}

public sealed class LibraryBookCard : INotifyPropertyChanged
{
    public required SavedBook Book { get; init; }
    public string Id => Book.Id;
    public string Title => IsSeries ? SeriesName : Book.Title;
    public string SeriesName => string.IsNullOrWhiteSpace(Book.Series) ? "未分类" : Book.Series;
    public bool IsSeries { get; init; }
    public int SeriesCount { get; init; }
    public bool Available { get; init; }
    public double CoverOpacity => Available ? 1 : .5;
    public string Format => Directory.Exists(Book.Path) ? "图片集" : Path.GetExtension(Book.Path).TrimStart('.').ToUpperInvariant();
    public string Initial => string.IsNullOrWhiteSpace(Title) ? "漫" : Title[..1];
    public string FavoriteGlyph => Book.Favorite ? "\uE735" : "\uE734";
    public string FavoriteHint => Book.Favorite ? "取消收藏" : "收藏";
    public string Metadata => IsSeries ? $"{SeriesCount} 卷 · 点击浏览" : !Available ? "原文件暂不可用" :
        Book.Total > 0 ? $"{Book.ReadState} · {Math.Min(Book.Position, Book.Total)} / {Book.Total} 页" : Book.ReadState;
    public string Detail => IsSeries ? "系列" : !string.IsNullOrWhiteSpace(Book.Series) ? Book.Series : Format;
    public string TagsText => string.Join(" · ", Book.Tags);
    public string Description => $"{Title}\n{Metadata}\n{Book.Path}" + (_coverError == null ? "" : $"\n封面：{_coverError}");
    public double Progress => Book.Total > 0 ? Math.Clamp(100.0 * Book.Position / Book.Total, 0, 100) : 0;
    public Visibility ProgressVisibility => !IsSeries && Book.Total > 0 && Book.Position > 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility BookActionsVisibility => IsSeries ? Visibility.Collapsed : Visibility.Visible;
    public Visibility MissingVisibility => Available ? Visibility.Collapsed : Visibility.Visible;
    public Brush PlaceholderBrush
    {
        get
        {
            string[] colors = ["#DEE5EE", "#E9DFD9", "#DDE7E0", "#E5DFEC", "#E5E3D8", "#DAE6E9"];
            int index = (int)((uint)Title.Aggregate(17, (value, c) => unchecked(value * 31 + c)) % colors.Length);
            var brush = (SolidColorBrush)new BrushConverter().ConvertFrom(colors[index])!;
            brush.Freeze(); return brush;
        }
    }
    private BitmapSource? _cover;
    private string? _coverError;
    public BitmapSource? Cover { get => _cover; set { _cover = value; Notify(nameof(Cover)); Notify(nameof(PlaceholderVisibility)); } }
    public Visibility PlaceholderVisibility => Cover == null ? Visibility.Visible : Visibility.Collapsed;
    public string? CoverError { get => _coverError; set { _coverError = value; Notify(nameof(Description)); } }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify(string property) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}

/// <summary>Two bounded decoders, one rendered source image per cover, and revision-keyed disposable PNG previews.</summary>
internal sealed class LibraryCoverCache(string directory) : IDisposable
{
    private readonly SemaphoreSlim workers = new(2, 2);
    private readonly CancellationTokenSource lifetime = new();
    private bool disposed;
    private int pruneCounter;

    public async Task<BitmapSource?> GetAsync(SavedBook entry, CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
        var token = linked.Token;
        await workers.WaitAsync(token);
        try
        {
            token.ThrowIfCancellationRequested();
            return await Task.Run(async () =>
            {
                Directory.CreateDirectory(directory);
                var file = new FileInfo(entry.Path);
                var stamp = file.Exists ? $"{file.Length}:{file.LastWriteTimeUtc.Ticks}" : entry.Revision;
                var key = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"v1:{entry.Id}:{stamp}:{entry.Revision}")));
                var cachePath = Path.Combine(directory, key + ".png");
                if (File.Exists(cachePath))
                {
                    try { return Read(cachePath); }
                    catch (Exception ex) when (ex is IOException or NotSupportedException or FileFormatException) { File.Delete(cachePath); }
                }
                using var book = await DocumentEngine.OpenAsync(entry.Path, cancellationToken: token);
                int firstImage = book.Publication.Units.FindIndex(unit => !unit.Complex && unit.Error == null);
                if (firstImage < 0) return null;
                var bitmap = await book.RenderAsync(firstImage, 480, token);
                token.ThrowIfCancellationRequested();
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                var temporary = cachePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    using (var output = File.Create(temporary)) encoder.Save(output);
                    File.Move(temporary, cachePath, true);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
                if (Interlocked.Increment(ref pruneCounter) % 24 == 0) Prune();
                return bitmap;
            }, token);
        }
        finally { workers.Release(); }
    }

    private static BitmapSource Read(string path)
    {
        using var input = File.OpenRead(path);
        var bitmap = new BitmapImage();
        bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = input; bitmap.EndInit();
        bitmap.Freeze(); return bitmap;
    }

    private void Prune()
    {
        try
        {
            var entries = new DirectoryInfo(directory).GetFiles("*.png").OrderByDescending(file => file.LastWriteTimeUtc).ToList();
            long total = 0;
            foreach (var file in entries)
            {
                total += file.Length;
                if (total > 256L * 1024 * 1024) { try { file.Delete(); } catch (IOException) { } }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true; lifetime.Cancel();
        // Outstanding decoders release their own slots after disposing their publication.
    }
}

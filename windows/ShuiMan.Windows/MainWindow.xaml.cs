using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using Microsoft.Win32;
using ShuiMan.Core;

namespace ShuiMan.Windows;

public sealed class PageRow : INotifyPropertyChanged
{
    public int Index { get; init; }
    public int Number => Index + 1;
    public string Title { get; init; } = "";
    private BitmapSource? _thumbnail;
    public BitmapSource? Thumbnail { get => _thumbnail; set { _thumbnail = value; PropertyChanged?.Invoke(this, new(nameof(Thumbnail))); } }
    public event PropertyChangedEventHandler? PropertyChanged;
}

public partial class MainWindow : Window
{
    private readonly LibraryStore _store;
    private readonly string _dataDirectory;
    private readonly SemaphoreSlim _io = new(1, 1);
    private readonly AnalysisCache _analysisCache;
    private CancellationTokenSource _analysisCancellation = new();
    private Task _analysisTask = Task.CompletedTask;
    private int _foregroundReaders;
    private string? _displaySignature;
    private bool _analysisRefreshQueued;
    private IOpenBook? _book;
    private SavedBook? _saved;
    private readonly Dictionary<string, SpreadDecision> _decisions = [];
    private readonly Dictionary<int, PairDecision> _pairs = [];
    private List<DisplayGroup> _groups = [];
    private ObservableCollection<PageRow> _pages = [];
    private readonly List<BitmapSource> _displayImages = [];
    private DisplayGroup? _displayGroup;
    private CancellationTokenSource _renderCancellation = new();
    private CancellationTokenSource _openCancellation = new();
    private bool _binding = true, _closing, _closed, _fullScreen;
    private int _index, _openGeneration, _busyCount, _lastProgressEnd;
    private double _zoom = 1;
    private Point? _dragStart;
    private double _dragX, _dragY;
    private WindowState _previousWindowState;

    public MainWindow(string? dataDirectory, string? initialBook)
    {
        _dataDirectory = dataDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ShuiMan");
        _store = new LibraryStore(_dataDirectory);
        _analysisCache = new AnalysisCache(Path.Combine(_dataDirectory, "analysis"));
        InitializeComponent();
        Width = Math.Min(1320, SystemParameters.WorkArea.Width - 20);
        Height = Math.Min(880, SystemParameters.WorkArea.Height - 20);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        LibraryHome.Initialize(_store);
        LibraryHome.OpenRequested += async path => await Run(() => OpenAsync(path));
        LibraryHome.BookChanged += book =>
        {
            if (_saved?.Id != book.Id) return;
            _saved.Title = book.Title; _saved.Series = book.Series; _saved.Tags = [.. book.Tags];
            _saved.Favorite = book.Favorite; _saved.ReadState = book.ReadState; _saved.MetadataVersion = book.MetadataVersion;
            BookTitle.Text = book.Title; Title = $"{book.Title} — 水漫";
        };
        LibraryHome.BookRemoved += async id => await Run(() => ForgetOpenedBookAsync(id));
        LibraryHome.StatusChanged += text => StatusText.Text = text;
        StateChanged += (_, _) => MaximizeButton.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
        _binding = false;
        Loaded += async (_, _) => await Run(async () =>
        {
            await LibraryHome.RefreshAsync();
            if (!string.IsNullOrEmpty(_store.Warning)) StatusText.Text = _store.Warning;
            if (initialBook != null) await OpenAsync(initialBook);
        });
    }
    private async Task Run(Func<Task> action)
    {
        try { await action(); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!_closing) { StatusText.Text = ex.Message; MessageBox.Show(this, ex.Message, "水漫", MessageBoxButton.OK, MessageBoxImage.Warning); } }
    }
    private void Busy(string? text)
    {
        _busyCount = Math.Max(0, _busyCount + (text == null ? -1 : 1));
        if (text != null) BusyText.Text = text;
        BusyBanner.Visibility = _busyCount > 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    private async Task OpenAsync(string path)
    {
        int generation = ++_openGeneration;
        _openCancellation.Cancel(); _openCancellation.Dispose(); _openCancellation = new();
        var token = _openCancellation.Token;
        _renderCancellation.Cancel(); Busy("正在打开书籍…");
        IOpenBook? next = null;
        try
        {
            try { next = await DocumentEngine.OpenAsync(path, cancellationToken: token); }
            catch (PasswordRequiredException)
            {
                token.ThrowIfCancellationRequested();
                var password = Dialogs.Text(this, "PDF 密码", "请输入此 PDF 的打开密码。密码不会保存。", password: true);
                if (password == null) return;
                next = await DocumentEngine.OpenAsync(path, password, token);
            }
            token.ThrowIfCancellationRequested();
            await _io.WaitAsync(token);
            try
            {
                token.ThrowIfCancellationRequested();
                _analysisCancellation.Cancel();
                SaveCurrent(); _book?.Dispose(); _book = next; next = null;
                var publication = _book.Publication;
                _saved = _store.Books.FirstOrDefault(b => b.Id == publication.Identity) ?? new SavedBook
                {
                    Id = publication.Identity, Path = publication.SourcePath, Title = publication.Title,
                    Series = Path.GetFileName(Path.GetDirectoryName(publication.SourcePath)) ?? "本地书籍",
                    Preferences = new ReaderPreferences { Direction = publication.Direction ?? "ltr" }
                };
                _saved.Revision = publication.Revision; _saved.Total = publication.Units.Count; _saved.OpenedAt = DateTime.UtcNow;
                var located = publication.Units.FindIndex(p => p.Id == _saved.LocatorKey);
                _index = located >= 0 ? located : Math.Clamp(_saved.Position - 1, 0, publication.Units.Count - 1);
                _lastProgressEnd = _saved.Position;
                _decisions.Clear(); _pairs.Clear(); _displayImages.Clear(); _displayGroup = null; _displaySignature = null; _zoom = 1;
                var cachedAnalysis = await _analysisCache.LoadAsync(publication, token);
                token.ThrowIfCancellationRequested();
                if (cachedAnalysis != null)
                {
                    foreach (var item in cachedAnalysis.Decisions) _decisions[item.Key] = item.Value;
                    foreach (var item in cachedAnalysis.Pairs) _pairs[item.Key] = item.Value;
                }
                _pages = new(publication.Units.Select((u, i) => new PageRow { Index = i, Title = u.Title }));
                _binding = true; PageList.ItemsSource = _pages; TocList.ItemsSource = publication.Navigation;
                BindPreferences(); _binding = false;
                BookTitle.Text = _saved.Title; Title = $"{_saved.Title} — 水漫";
                Welcome.Visibility = Visibility.Collapsed; Sidebar.SelectedIndex = 0;
                ShowReader();
                RebuildGroups(); RefreshBookmarks(); SaveCurrent();
            }
            finally { _io.Release(); }
            StartBookAnalysis(_book!);
            await ShowPageAsync(_index);
            if (generation == _openGeneration && _book?.Publication.Warnings.Count > 0)
                MessageBox.Show(this, string.Join("\n", _book.Publication.Warnings.Distinct()), "出版物提示", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        finally { next?.Dispose(); Busy(null); }
    }
    private void BindPreferences()
    {
        var p = _saved!.Preferences;
        LayoutBox.SelectedIndex = p.Layout == "double" ? 1 : 0; DirectionBox.SelectedIndex = p.Direction == "rtl" ? 1 : 0;
        FitBox.SelectedIndex = p.Fit == "width" ? 1 : p.Fit == "actual" ? 2 : 0;
        SmartMenu.IsChecked = p.SmartSpreads; PairsMenu.IsChecked = p.AutomaticPairs; AggressiveMenu.IsChecked = p.AggressivePairs; CoverMenu.IsChecked = p.CoverAlone;
        OrientationMenu.IsChecked = p.AutomaticOrientation; DarkMenu.IsChecked = p.DarkBackground;
        ReadingArea.Background = new SolidColorBrush(p.DarkBackground ? Color.FromRgb(32, 36, 43) : Color.FromRgb(236, 238, 243));
        SmartStatusButton.Content = p.SmartSpreads ? "智能跨页：开" : "智能跨页：关";
        OrientationStatusButton.Content = p.AutomaticOrientation ? "智能转向：开" : "智能转向：关";
        SmartStatusButton.Foreground = (Brush)FindResource(p.SmartSpreads ? "Accent" : "MutedBrush");
        OrientationStatusButton.Foreground = (Brush)FindResource(p.AutomaticOrientation ? "Accent" : "MutedBrush");
    }
    private void SaveCurrent()
    {
        if (_saved == null || _book == null) return;
        int visibleEnd = _displayGroup?.Indices.Max() + 1 ?? _index + 1;
        bool moved = _saved.Position != _index + 1 || _saved.Total != _book.Publication.Units.Count || _lastProgressEnd != visibleEnd;
        _saved.LocatorKey = _book.Publication.Units[_index].Id; _saved.Position = _index + 1;
        _saved.Total = _book.Publication.Units.Count;
        if (moved || _saved.ReadState == "未读") _saved.ReadState = _displayGroup?.Indices.Contains(_saved.Total - 1) == true || _index == _saved.Total - 1 ? "已读" : "在读";
        _lastProgressEnd = visibleEnd;
        _saved = _store.SaveReadingState(_saved);
    }
    private void RebuildGroups()
    {
        if (_book != null && _saved != null) _groups = LayoutEngine.Groups(_book.Publication, _saved, _decisions, ReadyPairs());
    }
    private Dictionary<int, PairDecision> ReadyPairs() => _pairs.ToDictionary(item => item.Key, item =>
        (item.Key == 0 || _pairs.ContainsKey(item.Key - 1)) &&
        (item.Key >= _book!.Publication.Units.Count - 2 || _pairs.ContainsKey(item.Key + 1))
            ? item.Value : item.Value with { Automatic = false, Suggested = false });
    private Task ShowPageAsync(int index) => ShowPageCoreAsync(index, preserveDisplayedPage: false);
    private async Task ShowPageCoreAsync(int index, bool preserveDisplayedPage)
    {
        if (_book == null || _saved == null || _closing) return;
        _index = Math.Clamp(index, 0, _book.Publication.Units.Count - 1);
        _renderCancellation.Cancel(); _renderCancellation.Dispose(); _renderCancellation = new();
        var token = _renderCancellation.Token; var targetBook = _book;
        Interlocked.Increment(ref _foregroundReaders);
        if (!preserveDisplayedPage) { Surface.Visibility = Visibility.Hidden; _displayGroup = null; }
        CorrectionButton.IsEnabled = false; BookmarkButton.IsEnabled = false;
        StatusText.Text = $"正在准备第 {_index + 1} 页…";
        Busy("正在绘制页面…");
        try
        {
            await _io.WaitAsync(token);
            try
            {
                token.ThrowIfCancellationRequested();
                if (targetBook != _book) return;
                var publication = targetBook.Publication;
                RebuildGroups();
                // Navigation consumes existing whole-book results; it never starts page-by-page OCR.
                // Explicit EPUB direction is available immediately through LayoutEngine.
                token.ThrowIfCancellationRequested();
                var group = _groups.First(value => value.Indices.Contains(_index));
                BusyText.Text = "正在显示页面…";
                await RenderGroupAsync(group, targetBook, token);
                _binding = true; PageList.SelectedIndex = _index; PageList.ScrollIntoView(_pages[_index]); _binding = false;
                PageNumber.Text = (_index + 1).ToString(); PageTotal.Text = $" / {publication.Units.Count}";
                BookmarkButton.Content = _saved.Bookmarks.Contains(publication.Units[_index].Id) ? "★" : "☆";
                var nums = group.Indices.Order().Select(i => i + 1);
                string reason = group.Spread && group.Indices.Length == 2 ? (_saved.Overrides.GetValueOrDefault(publication.Units[group.FirstSourceIndex].Id)?.JoinNext == true ? "完整双图 · 手动修正" : "完整双图 · 自动识别") : group.Spread ? "大跨页 · 完整适配" : "";
                var orientation = _decisions.GetValueOrDefault(publication.Units[_index].Id);
                if (group.Indices.Length == 1 && publication.Units[_index].IsCover) reason = "封面";
                if (_saved.Preferences.AutomaticOrientation && group.Indices.Length == 1 && !string.IsNullOrWhiteSpace(orientation?.Reason)) reason = orientation.Reason;
                if (group.Indices.Length == 1 && publication.Units[_index].RotationHint is int publisher && _saved.Overrides.GetValueOrDefault(publication.Units[_index].Id)?.Rotation == null)
                    reason = publisher == 0 ? "EPUB 标记 · 原始方向" : $"EPUB 旋转标记 · {publisher}°";
                StatusText.Text = $"{publication.Kind.ToUpperInvariant()} · 第 {string.Join("–", nums)} 页  {reason}";
                SaveCurrent();
                // Only nearby thumbnails are retained; the full book is never decoded for a sidebar.
                foreach (var row in _pages.Where(p => p.Thumbnail != null && Math.Abs(p.Index - _index) > 24)) row.Thumbnail = null;
                for (int i = Math.Max(0, _index - 3); i < Math.Min(_pages.Count, _index + 7); i++)
                {
                    if (_pages[i].Thumbnail != null || publication.Units[i].Complex || publication.Units[i].Error != null) continue;
                    try { _pages[i].Thumbnail = await targetBook.RenderAsync(i, 140, token); }
                    catch (Exception ex) when (ex is not OperationCanceledException) { /* Broken pages keep their position. */ }
                }
            }
            finally { _io.Release(); }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!token.IsCancellationRequested)
            {
                _displayImages.Clear(); DrawPages();
                PageErrorText.Text = $"第 {_index + 1} 页\n\n{ex.Message}\n\n可以继续翻页。"; PageError.Visibility = Visibility.Visible;
                PageNumber.Text = (_index + 1).ToString(); PageTotal.Text = $" / {_book.Publication.Units.Count}";
                StatusText.Text = "此页无法显示，原始页面位置已保留"; SaveCurrent();
            }
        }
        finally
        {
            Interlocked.Decrement(ref _foregroundReaders);
            if (!token.IsCancellationRequested && targetBook == _book && !_closing)
            {
                Surface.Visibility = Visibility.Visible; CorrectionButton.IsEnabled = true; BookmarkButton.IsEnabled = true;
                ApplyIncrementalAnalysis();
            }
            Busy(null);
        }
    }
    private async Task RenderGroupAsync(DisplayGroup group, IOpenBook book, CancellationToken token)
    {
        PageError.Visibility = Visibility.Collapsed;
        var images = new List<BitmapSource>();
        var rotations = group.Indices.ToDictionary(index => index, index => LayoutEngine.Rotation(book.Publication.Units[index], _saved!, _decisions));
        var signature = GroupSignature(group, rotations);
        foreach (int pageIndex in group.Indices)
        {
            int edge = _saved!.Preferences.Fit == "actual" ? 8192 : (int)Math.Clamp(3200 * _zoom, 3200, 8192);
            var bitmap = await book.RenderAsync(pageIndex, edge, token);
            int rotation = rotations[pageIndex];
            if (rotation % 360 != 0) { var rotated = new TransformedBitmap(bitmap, new RotateTransform(rotation)); rotated.Freeze(); bitmap = rotated; }
            images.Add(bitmap);
        }
        token.ThrowIfCancellationRequested();
        _displayGroup = group; _displaySignature = signature; _displayImages.Clear(); _displayImages.AddRange(images); DrawPages();
        Surface.Visibility = Visibility.Visible; CorrectionButton.IsEnabled = true; BookmarkButton.IsEnabled = true;
    }
    private void DrawPages()
    {
        if (Surface == null || _saved == null) return;
        Surface.SetPages(_displayImages, Math.Max(100, Viewport.ActualWidth - 18), Math.Max(100, Viewport.ActualHeight - 18), _saved.Preferences.Fit, _zoom, _displayGroup?.Spread == true, _displayGroup?.VerticalOffset ?? 0, _displayGroup?.RightScale ?? 1);
        ZoomText.Text = $"{_zoom * 100:0}%";
    }
    private async Task Turn(int delta)
    {
        if (_book == null || _groups.Count == 0) return;
        RebuildGroups();
        int current = _groups.FindIndex(g => g.Indices.Contains(_index));
        int next = current + delta;
        if (next >= 0 && next < _groups.Count) { Viewport.ScrollToTop(); Viewport.ScrollToLeftEnd(); await ShowPageAsync(_groups[next].FirstSourceIndex); }
        else StatusText.Text = delta > 0 ? "已到本卷末页，可点击“下一卷”" : "已到第一页";
    }
    private void ShowReader()
    {
        if (_book == null) return;
        LibraryHome.Visibility = Visibility.Collapsed; ReaderHost.Visibility = Visibility.Visible;
        ResumeReaderButton.IsEnabled = true;
        ResumeReaderButton.Background = Brushes.White; BackToLibraryButton.Background = Brushes.Transparent;
        BookTitle.Text = _saved?.Title ?? "阅读";
    }
    private async Task ShowLibraryAsync()
    {
        if (_fullScreen) FullScreen();
        ++_openGeneration; _openCancellation.Cancel();
        _renderCancellation.Cancel();
        await _io.WaitAsync();
        try { SaveCurrent(); }
        finally { _io.Release(); }
        if (_closing) return;
        ReaderHost.Visibility = Visibility.Collapsed; LibraryHome.Visibility = Visibility.Visible;
        BackToLibraryButton.Background = Brushes.White; ResumeReaderButton.Background = Brushes.Transparent;
        BookTitle.Text = "你的私人书库";
        await LibraryHome.RefreshAsync();
    }
    private async void BackToLibraryClick(object sender, RoutedEventArgs e) => await Run(ShowLibraryAsync);
    private async void ResumeReaderClick(object sender, RoutedEventArgs e) => await Run(async () => { ShowReader(); await ShowPageAsync(_index); });
    private void MinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void MaximizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void CloseClick(object sender, RoutedEventArgs e) => Close();
    private async Task ForgetOpenedBookAsync(string id)
    {
        if (_saved?.Id != id) return;
        ++_openGeneration; _openCancellation.Cancel(); _renderCancellation.Cancel(); _analysisCancellation.Cancel();
        // Clear before waiting so a finishing render cannot reinsert a removed record.
        _saved = null;
        await _io.WaitAsync();
        try
        {
            _book?.Dispose(); _book = null; _displayGroup = null; _displayImages.Clear(); _groups.Clear();
            _binding = true; PageList.ItemsSource = null; TocList.ItemsSource = null; BookmarkList.ItemsSource = null; _binding = false;
            Surface.SetPages([], Viewport.ActualWidth, Viewport.ActualHeight, "page", 1, false, 0, 1);
            Viewport.Visibility = Visibility.Visible; PageError.Visibility = Visibility.Collapsed;
            Welcome.Visibility = Visibility.Visible; ResumeReaderButton.IsEnabled = false;
            Title = "水漫 · 你的私人书库"; PageNumber.Text = "1"; PageTotal.Text = " / 0";
        }
        finally { _io.Release(); }
        await ShowLibraryAsync();
    }
    private void RefreshBookmarks()
    {
        if (_saved == null || _book == null) return;
        _binding = true;
        BookmarkList.ItemsSource = _book.Publication.Units.Select((p, i) => new { Page = p, Index = i }).Where(p => _saved.Bookmarks.Contains(p.Page.Id)).Select(p => new NavigationItem($"第 {p.Index + 1} 页 · {p.Page.Title}", p.Index)).ToList();
        _binding = false;
    }
    private async void OpenFileClick(object sender, RoutedEventArgs e) => await Run(async () => { var dialog = new OpenFileDialog { Filter = "漫画与图片|*.zip;*.cbz;*.pdf;*.epub;*.mobi;*.jpg;*.jpeg;*.png;*.gif;*.bmp;*.tif;*.tiff;*.webp;*.avif;*.heic;*.heif|所有文件|*.*" }; if (dialog.ShowDialog(this) == true) await OpenAsync(dialog.FileName); });
    private async void OpenFolderClick(object sender, RoutedEventArgs e) => await Run(async () => { var dialog = new OpenFolderDialog { Title = "选择图片文件夹" }; if (dialog.ShowDialog(this) == true) await OpenAsync(dialog.FolderName); });
    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Handled || e.Data.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0) return;
        e.Handled = true;
        await Run(async () =>
        {
            if (ReaderHost.Visibility == Visibility.Visible && paths.Length == 1) await OpenAsync(paths[0]);
            else { await ShowLibraryAsync(); await LibraryHome.ImportPathsAsync(paths); }
        });
    }
    private async void PageSelected(object sender, SelectionChangedEventArgs e) { if (!_binding && PageList.SelectedItem is PageRow p) await Run(() => ShowPageAsync(p.Index)); }
    private async void TocSelected(object sender, SelectionChangedEventArgs e) { if (!_binding && TocList.SelectedItem is NavigationItem p) await Run(() => ShowPageAsync(p.Index)); }
    private async void BookmarkSelected(object sender, SelectionChangedEventArgs e) { if (!_binding && BookmarkList.SelectedItem is NavigationItem p) await Run(() => ShowPageAsync(p.Index)); }
    private async void PreviousClick(object sender, RoutedEventArgs e) => await Run(() => Turn(-1));
    private async void NextClick(object sender, RoutedEventArgs e) => await Run(() => Turn(1));
    private async void NextBookClick(object sender, RoutedEventArgs e) => await Run(async () => { if (_saved == null) return; var volumes = _store.Books.Where(b => b.Series == _saved.Series && (File.Exists(b.Path) || Directory.Exists(b.Path))).Order(LibraryBookComparer.NaturalVolume).ToList(); int i = volumes.FindIndex(b => b.Id == _saved.Id); if (i >= 0 && i + 1 < volumes.Count) await OpenAsync(volumes[i + 1].Path); else StatusText.Text = "书库中没有下一卷，请先导入同一系列"; });
    private async void JumpKeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter && int.TryParse(PageNumber.Text, out int n)) { e.Handled = true; await Run(() => ShowPageAsync(n - 1)); } }
    private async void PreferenceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_binding || _saved == null) return;
        _saved.Preferences.Layout = LayoutBox.SelectedIndex == 1 ? "double" : "single"; _saved.Preferences.Direction = DirectionBox.SelectedIndex == 1 ? "rtl" : "ltr";
        _saved.Preferences.Fit = FitBox.SelectedIndex == 1 ? "width" : FitBox.SelectedIndex == 2 ? "actual" : "page";
        _zoom = 1; await Run(() => ShowPageAsync(_index));
    }
    private async void SettingClick(object sender, RoutedEventArgs e)
    {
        if (_saved == null) return;
        var p = _saved.Preferences; p.SmartSpreads = SmartMenu.IsChecked; p.AutomaticPairs = PairsMenu.IsChecked; p.AggressivePairs = AggressiveMenu.IsChecked; p.AutomaticOrientation = OrientationMenu.IsChecked; p.CoverAlone = CoverMenu.IsChecked; p.DarkBackground = DarkMenu.IsChecked;
        _binding = true; BindPreferences(); _binding = false; await Run(() => ShowPageAsync(_index));
    }
    private void SmartStatusClick(object sender, RoutedEventArgs e)
    {
        if (_saved == null) return;
        if (ReferenceEquals(sender, SmartStatusButton)) SmartMenu.IsChecked = !SmartMenu.IsChecked;
        else OrientationMenu.IsChecked = !OrientationMenu.IsChecked;
        SettingClick(sender, e);
    }
    private async void ReanalyzeClick(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        if (_book == null || _closing) return;
        var targetBook = _book;
        int generation = _openGeneration;
        _analysisCancellation.Cancel();
        try { await _analysisTask; } catch (OperationCanceledException) { }
        if (_closing || targetBook != _book || generation != _openGeneration) return;
        await _analysisCache.InvalidateAsync(targetBook.Publication);
        if (_closing || targetBook != _book || generation != _openGeneration) return;
        _decisions.Clear(); _pairs.Clear(); StartBookAnalysis(targetBook);
        StatusText.Text = "已重新开始整本后台分析，阅读可以继续";
    });
    private string GroupSignature(DisplayGroup group, IReadOnlyDictionary<int, int>? rotations = null) =>
        string.Join(",", group.Indices.Select(index => $"{index}:{rotations?.GetValueOrDefault(index) ?? LayoutEngine.Rotation(_book!.Publication.Units[index], _saved!, _decisions)}")) +
        $"/{group.Spread}/{group.VerticalOffset:R}/{group.RightScale:R}";
    private void ApplyIncrementalAnalysis()
    {
        if (_closing || _book == null || _saved == null || _displayGroup == null || _foregroundReaders != 0) return;
        RebuildGroups();
        var updated = _groups.First(group => group.Indices.Contains(_index));
        if (GroupSignature(updated) == _displaySignature || _analysisRefreshQueued) return;
        _analysisRefreshQueued = true;
        var targetBook = _book;
        _ = Dispatcher.InvokeAsync(async () =>
        {
            _analysisRefreshQueued = false;
            if (_closing || targetBook != _book || _saved == null || _foregroundReaders != 0 || _displayGroup == null) return;
            RebuildGroups();
            var current = _groups.First(group => group.Indices.Contains(_index));
            if (GroupSignature(current) != _displaySignature)
                await Run(() => ShowPageCoreAsync(_index, preserveDisplayedPage: true));
        }, System.Windows.Threading.DispatcherPriority.Background);
    }
    private void StartBookAnalysis(IOpenBook targetBook)
    {
        if (_closing || targetBook != _book) return;
        _analysisCancellation.Cancel(); _analysisCancellation.Dispose(); _analysisCancellation = new();
        var token = _analysisCancellation.Token;
        int analysisStartIndex = _index;
        AnalysisStatusText.Text = $"整本分析 0 / {targetBook.Publication.Units.Count}";
        var progress = new Progress<BookAnalysisProgress>(update =>
        {
            if (_closing || token.IsCancellationRequested || targetBook != _book || _saved == null) return;
            foreach (var item in update.Decisions) _decisions[item.Key] = item.Value;
            foreach (var item in update.Pairs) _pairs[item.Key] = item.Value;
            AnalysisStatusText.Text = update.IsComplete ? $"整本分析完成 · {update.TotalPages} 页" : $"整本分析 {update.CompletedPages} / {update.TotalPages}";
            AnalysisStatusText.ToolTip = update.Warning ?? "从当前页往后逐页识别并立即应用，随后补齐前面的页面；结果缓存于本机。";
            if (update.Decisions.Count > 0 || update.Pairs.Count > 0) ApplyIncrementalAnalysis();
        });
        _analysisTask = Task.Run(async () =>
        {
            try
            {
                var completed = await new BookAnalysisService(_analysisCache).AnalyzeAsync(targetBook.Publication, async (page, edge, cancellation) =>
                {
                    // Foreground drawing wins; OCR itself never owns the publication I/O gate.
                    while (Volatile.Read(ref _foregroundReaders) > 0) await Task.Delay(25, cancellation);
                    await _io.WaitAsync(cancellation);
                    try
                    {
                        cancellation.ThrowIfCancellationRequested();
                        if (targetBook != _book) throw new OperationCanceledException(cancellation);
                        return await targetBook.RenderAsync(page, edge, cancellation);
                    }
                    finally { _io.Release(); }
                }, progress, token, startIndex: analysisStartIndex);
                if (!completed.IsComplete)
                    await Dispatcher.InvokeAsync(() =>
                    {
                        if (!_closing && targetBook == _book && !token.IsCancellationRequested)
                        {
                            AnalysisStatusText.Text = $"扫描结束 · {completed.FailedPages.Count} 页待重试";
                            AnalysisStatusText.ToolTip = "已保存完成的结果；重新打开或点击重新分析整本可重试未完成页面。";
                        }
                    });
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    if (!_closing && targetBook == _book && !token.IsCancellationRequested)
                    { AnalysisStatusText.Text = "整本分析暂未完成"; AnalysisStatusText.ToolTip = ex.Message; }
                });
            }
        }, token);
    }
    private void ToggleBookmarkClick(object sender, RoutedEventArgs e)
    {
        if (_saved == null || _book == null || !BookmarkButton.IsEnabled) return; string key = _book.Publication.Units[_index].Id;
        if (!_saved.Bookmarks.Add(key)) _saved.Bookmarks.Remove(key);
        BookmarkButton.Content = _saved.Bookmarks.Contains(key) ? "★" : "☆"; SaveCurrent(); RefreshBookmarks();
    }
    private void CorrectionsClick(object sender, RoutedEventArgs e) { if (sender is Button b && b.ContextMenu != null) { b.ContextMenu.PlacementTarget = b; b.ContextMenu.IsOpen = true; } }
    private PageOverride? Correction(int? index = null)
    {
        if (_saved == null || _book == null) return null;
        string key = _book.Publication.Units[index ?? _index].Id;
        if (!_saved.Overrides.TryGetValue(key, out var result)) _saved.Overrides[key] = result = new(); return result;
    }
    private async Task Rotate(int delta)
    {
        if (_saved == null || _book == null || !CorrectionButton.IsEnabled) return; int angle = LayoutEngine.Rotation(_book.Publication.Units[_index], _saved, _decisions);
        var correction = Correction()!; correction.Rotation = ((angle + delta) % 360 + 360) % 360; await ShowPageAsync(_index);
    }
    private async void RotateRightClick(object sender, RoutedEventArgs e) => await Run(() => Rotate(90));
    private async void RotateLeftClick(object sender, RoutedEventArgs e) => await Run(() => Rotate(-90));
    private async void PairClick(object sender, RoutedEventArgs e) => await Run(async () => { if (_saved == null || _book == null || _index + 1 >= _book.Publication.Units.Count) return; var c = Correction()!; c.JoinNext = true; c.Standalone = false; c.EarlierOnRight = _saved.Preferences.Direction == "rtl"; var next = Correction(_index + 1)!; next.JoinNext = null; next.PairingBreak = false; if (_index > 0) Correction(_index - 1)!.JoinNext = false; await ShowPageAsync(_index); });
    private PageOverride? ConfirmCurrentPair()
    {
        if (_displayGroup?.Indices.Length != 2 || _book == null) return null;
        var c = Correction(_displayGroup.FirstSourceIndex)!;
        c.JoinNext = true; c.Standalone = false; c.EarlierOnRight = _displayGroup.Indices[0] > _displayGroup.Indices[1]; c.PairOffset = _displayGroup.VerticalOffset; c.PairScale = _displayGroup.RightScale;
        return c;
    }
    private async void SwapClick(object sender, RoutedEventArgs e) => await Run(async () => { var c = ConfirmCurrentPair(); if (c == null) return; c.EarlierOnRight = !c.EarlierOnRight; c.PairOffset = -c.PairOffset / c.PairScale; c.PairScale = 1 / c.PairScale; await ShowPageAsync(_index); });
    private async void UnpairClick(object sender, RoutedEventArgs e) => await Run(async () => { if (_book == null || _saved == null) return; int i = _displayGroup?.FirstSourceIndex ?? _index; var c = Correction(i)!; c.JoinNext = false; c.Standalone = true; if (i + 1 < _book.Publication.Units.Count) Correction(i + 1)!.PairingBreak = true; await ShowPageAsync(_index); });
    private async void SeamClick(object sender, RoutedEventArgs e) => await Run(async () => { if (_displayGroup?.Indices.Length != 2) return; var input = Dialogs.Seam(this, _displayGroup.VerticalOffset, _displayGroup.RightScale); if (input == null) return; var c = ConfirmCurrentPair()!; c.PairOffset = input.Value.Offset; c.PairScale = input.Value.Scale; await ShowPageAsync(_index); });
    private async void ResetCorrectionClick(object sender, RoutedEventArgs e) => await Run(async () => { if (_saved == null || _book == null) return; int first = _displayGroup?.FirstSourceIndex ?? _index; foreach (int i in _displayGroup?.Indices ?? [_index]) _saved.Overrides.Remove(_book.Publication.Units[i].Id); if (first + 1 < _book.Publication.Units.Count && _saved.Overrides.TryGetValue(_book.Publication.Units[first + 1].Id, out var c)) c.PairingBreak = false; await ShowPageAsync(_index); });
    private void SidebarClick(object sender, RoutedEventArgs e) { bool show = SideColumn.Width.Value == 0; SideColumn.Width = new GridLength(show ? 230 : 0); Sidebar.Visibility = show ? Visibility.Visible : Visibility.Collapsed; }
    private void FullScreenClick(object sender, RoutedEventArgs e) => FullScreen();
    private void FullScreen()
    {
        _fullScreen = !_fullScreen;
        if (_fullScreen)
        {
            _previousWindowState = WindowState; WindowState = WindowState.Normal;
            WindowChrome.SetWindowChrome(this, null); ResizeMode = ResizeMode.NoResize;
            TitleBar.Visibility = Visibility.Collapsed; WindowState = WindowState.Maximized;
        }
        else
        {
            WindowState = WindowState.Normal; ResizeMode = ResizeMode.CanResize;
            WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 56, ResizeBorderThickness = new Thickness(6), CornerRadius = new CornerRadius(8), GlassFrameThickness = new Thickness(0), UseAeroCaptionButtons = false });
            TitleBar.Visibility = Visibility.Visible; WindowState = _previousWindowState;
        }
    }
    private async void ZoomInClick(object sender, RoutedEventArgs e) { _zoom = Math.Min(8, _zoom * 1.25); DrawPages(); await Run(() => ShowPageAsync(_index)); }
    private async void ZoomOutClick(object sender, RoutedEventArgs e) { _zoom = Math.Max(.25, _zoom / 1.25); DrawPages(); await Run(() => ShowPageAsync(_index)); }
    private void ViewportSizeChanged(object sender, SizeChangedEventArgs e) => DrawPages();
    private async void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { _zoom = Math.Clamp(_zoom * (e.Delta > 0 ? 1.15 : 1 / 1.15), .25, 8); DrawPages(); e.Handled = true; await Run(() => ShowPageAsync(_index)); }
        else if (_saved?.Preferences.Fit == "page" && _zoom <= 1) { e.Handled = true; await Run(() => Turn(e.Delta < 0 ? 1 : -1)); }
    }
    private void CanvasMouseDown(object sender, MouseButtonEventArgs e) { _dragStart = e.GetPosition(Viewport); _dragX = Viewport.HorizontalOffset; _dragY = Viewport.VerticalOffset; Surface.CaptureMouse(); }
    private void CanvasMouseMove(object sender, MouseEventArgs e) { if (_dragStart is Point start && e.LeftButton == MouseButtonState.Pressed) { var p = e.GetPosition(Viewport); Viewport.ScrollToHorizontalOffset(_dragX + start.X - p.X); Viewport.ScrollToVerticalOffset(_dragY + start.Y - p.Y); } }
    private void CanvasMouseUp(object sender, MouseButtonEventArgs e) { _dragStart = null; Surface.ReleaseMouseCapture(); }
    private async void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.O && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { OpenFileClick(this, e); e.Handled = true; return; }
        if (e.Key == Key.F11 || e.Key == Key.Escape && _fullScreen) { FullScreen(); e.Handled = true; return; }
        if (e.Key == Key.L && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { e.Handled = true; await Run(ShowLibraryAsync); return; }
        if (ReaderHost.Visibility != Visibility.Visible || Keyboard.FocusedElement is TextBox or PasswordBox or ComboBox) return;
        if (e.Key == Key.B && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { ToggleBookmarkClick(this, e); e.Handled = true; return; }
        int delta = e.Key == Key.Right ? (_saved?.Preferences.Direction == "rtl" ? -1 : 1) : e.Key == Key.Left ? (_saved?.Preferences.Direction == "rtl" ? 1 : -1) : e.Key is Key.PageDown or Key.Space ? 1 : e.Key == Key.PageUp ? -1 : 0;
        if (delta != 0) { e.Handled = true; await Run(() => Turn(delta)); }
        else if (e.Key is Key.Home or Key.End && _book != null) { e.Handled = true; await Run(() => ShowPageAsync(e.Key == Key.Home ? 0 : _book.Publication.Units.Count - 1)); }
    }
    private void HelpClick(object sender, RoutedEventArgs e) => MessageBox.Show(this, "水漫 Windows 0.7.0\n\n打开：Ctrl+O。返回书库：Ctrl+L。\n书库可拖入多个文件或目录，支持封面、系列、标签和收藏。\n翻页：方向键、Page Up/Down、空格；方向键遵循阅读方向。\n跳页：底部输入页码，按 Enter。\n缩放：Ctrl+滚轮；放大后拖动画面。\n书签：Ctrl+B。全屏：F11，Esc 退出。\n\nZIP/CBZ 按文件名自然排序，支持嵌套目录及中文文件名。\nEPUB 遵循 spine 顺序，以原生画布显示漫画图片。\nMOBI 支持无 DRM 的 MOBI 6 图片漫画。\n\n自动跨页分析较为保守，可通过“页面修正”纠正。\n本地数据目录：" + _dataDirectory, "关于水漫", MessageBoxButton.OK, MessageBoxImage.Information);
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_closed) return; e.Cancel = true; if (_closing) return;
        _closing = true; LibraryHome.Dispose(); _openCancellation.Cancel(); _renderCancellation.Cancel(); _analysisCancellation.Cancel();
        try { await _analysisTask; } catch (OperationCanceledException) { }
        await _io.WaitAsync();
        try { SaveCurrent(); _book?.Dispose(); }
        catch (Exception ex) { MessageBox.Show(this, "保存阅读记录失败：" + ex.Message, "水漫"); }
        finally { _io.Release(); _closed = true; _ = Dispatcher.BeginInvoke(Close); }
    }
}

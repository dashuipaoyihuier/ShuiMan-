using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
    private readonly RestrictedBookView _web;

    public MainWindow(string? dataDirectory, string? initialBook)
    {
        _dataDirectory = dataDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ShuiMan");
        _store = new LibraryStore(_dataDirectory);
        InitializeComponent();
        Width = Math.Min(1280, SystemParameters.WorkArea.Width - 20);
        Height = Math.Min(850, SystemParameters.WorkArea.Height - 20);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        _web = new RestrictedBookView(BookWeb, Path.Combine(_dataDirectory, "WebView2"), _io);
        _web.NavigateRequested += async resource => await Run(async () =>
        {
            if (_book == null) return;
            var candidates = _book.Publication.Units.Select((u, i) => (u, i)).Where(x => x.u.Locator.Resource == resource).OrderBy(x => Math.Abs(x.i - _index)).ToList();
            if (candidates.Count > 0) await ShowPageAsync(candidates[0].i);
            else StatusText.Text = "该链接不在出版物阅读顺序中，请通过目录选择页面";
        });
        _binding = false; RefreshLibrary();
        Loaded += async (_, _) => { if (!string.IsNullOrEmpty(_store.Warning)) StatusText.Text = _store.Warning; if (initialBook != null) await Run(() => OpenAsync(initialBook)); };
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
                SaveCurrent(); _web.Clear(); _book?.Dispose(); _book = next; next = null;
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
                _decisions.Clear(); _pairs.Clear(); _displayImages.Clear(); _displayGroup = null; _zoom = 1;
                _pages = new(publication.Units.Select((u, i) => new PageRow { Index = i, Title = u.Title }));
                _binding = true; PageList.ItemsSource = _pages; TocList.ItemsSource = publication.Navigation;
                BindPreferences(); _binding = false;
                BookTitle.Text = _saved.Title; Title = $"{_saved.Title} — 水漫";
                Welcome.Visibility = Visibility.Collapsed; Sidebar.SelectedIndex = 1;
                RebuildGroups(); RefreshBookmarks(); SaveCurrent(); RefreshLibrary();
            }
            finally { _io.Release(); }
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
        SmartMenu.IsChecked = p.SmartSpreads; PairsMenu.IsChecked = p.AutomaticPairs; CoverMenu.IsChecked = p.CoverAlone;
        OrientationMenu.IsChecked = p.AutomaticOrientation; DarkMenu.IsChecked = p.DarkBackground;
        ReadingArea.Background = new SolidColorBrush(p.DarkBackground ? Color.FromRgb(21, 38, 45) : Color.FromRgb(225, 235, 233));
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
        _store.Save(_saved);
    }
    private void RebuildGroups()
    {
        if (_book != null && _saved != null) _groups = LayoutEngine.Groups(_book.Publication, _saved, _decisions, _pairs);
    }
    private async Task ShowPageAsync(int index)
    {
        if (_book == null || _saved == null || _closing) return;
        _index = Math.Clamp(index, 0, _book.Publication.Units.Count - 1);
        _renderCancellation.Cancel(); _renderCancellation.Dispose(); _renderCancellation = new();
        var token = _renderCancellation.Token; var targetBook = _book;
        Busy("正在绘制页面…");
        try
        {
            await _io.WaitAsync(token);
            try
            {
                token.ThrowIfCancellationRequested();
                if (targetBook != _book) return;
                var publication = targetBook.Publication;
                // Show the current layout immediately; OCR can continue while the page is visible.
                RebuildGroups();
                await RenderGroupAsync(_groups.First(g => g.Indices.Contains(_index)), targetBook, token);
                PageNumber.Text = (_index + 1).ToString(); PageTotal.Text = $" / {publication.Units.Count}";
                StatusText.Text = $"{publication.Kind.ToUpperInvariant()} · 第 {_index + 1} 页";
                BusyText.Text = "页面已显示，正在分析跨页…";
                // Analyze only a small neighborhood; all decoding runs away from the dispatcher.
                var samples = new Dictionary<int, BitmapSource>();
                if (!publication.Units[_index].IsCover && !publication.Units[_index].Complex && (_saved.Preferences.SmartSpreads || _saved.Preferences.AutomaticOrientation))
                {
                    for (int i = Math.Max(0, _index - 2); i < Math.Min(publication.Units.Count, _index + 4); i++)
                    {
                        token.ThrowIfCancellationRequested(); var unit = publication.Units[i];
                        if (unit.Complex || unit.Error != null) continue;
                        try
                        {
                            if (!_decisions.ContainsKey(unit.Id) || (_saved.Preferences.AutomaticPairs && !_pairs.ContainsKey(i)))
                            {
                                var bitmap = await targetBook.RenderAsync(i, 768, token); samples[i] = bitmap;
                                if (!_decisions.ContainsKey(unit.Id)) _decisions[unit.Id] = await Task.Run(() => SpreadAnalyzer.AnalyzeAsync(bitmap, unit, token), token);
                            }
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException) { _decisions[unit.Id] = new(Uncertain: true, Reason: ex.Message); }
                    }
                    if (_saved.Preferences.AutomaticPairs)
                        for (int i = Math.Max(0, _index - 2); i < Math.Min(publication.Units.Count - 1, _index + 3); i++)
                        {
                            if (_pairs.ContainsKey(i) || publication.Units[i].Complex || publication.Units[i + 1].Complex || publication.Units[i].Error != null || publication.Units[i + 1].Error != null) continue;
                            try
                            {
                                if (!samples.TryGetValue(i, out var firstSample)) firstSample = await targetBook.RenderAsync(i, 768, token);
                                if (!samples.TryGetValue(i + 1, out var secondSample)) secondSample = await targetBook.RenderAsync(i + 1, 768, token);
                                int firstIndex = i;
                                _pairs[i] = await Task.Run(() => PairAnalyzer.Analyze(firstSample, secondSample, firstIndex), token);
                            }
                            catch (Exception ex) when (ex is not OperationCanceledException) { _pairs[i] = new(i); }
                        }
                }
                RebuildGroups();
                var group = _groups.First(g => g.Indices.Contains(_index));
                await RenderGroupAsync(group, targetBook, token);
                _binding = true; PageList.SelectedIndex = _index; PageList.ScrollIntoView(_pages[_index]); _binding = false;
                PageNumber.Text = (_index + 1).ToString(); PageTotal.Text = $" / {publication.Units.Count}";
                BookmarkButton.Content = _saved.Bookmarks.Contains(publication.Units[_index].Id) ? "★ 已收藏此页" : "☆ 书签";
                var nums = group.Indices.Order().Select(i => i + 1);
                string reason = group.Spread && group.Indices.Length == 2 ? (_saved.Overrides.GetValueOrDefault(publication.Units[group.FirstSourceIndex].Id)?.JoinNext == true ? "完整双图 · 手动修正" : "完整双图 · 自动识别") : group.Spread ? "大跨页 · 完整适配" : "";
                var orientation = _decisions.GetValueOrDefault(publication.Units[_index].Id);
                if (group.Indices.Length == 1 && publication.Units[_index].IsCover) reason = "封面";
                if (publication.Units[_index].Complex) reason = "原版式 · 可滚动阅读";
                if (group.Indices.Length == 1 && !string.IsNullOrWhiteSpace(orientation?.Reason)) reason = orientation.Reason;
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
                _displayImages.Clear(); DrawPages(); BookWeb.Visibility = Visibility.Collapsed;
                PageErrorText.Text = $"第 {_index + 1} 页\n\n{ex.Message}\n\n可以继续翻页。"; PageError.Visibility = Visibility.Visible;
                PageNumber.Text = (_index + 1).ToString(); PageTotal.Text = $" / {_book.Publication.Units.Count}";
                StatusText.Text = "此页无法显示，原始页面位置已保留"; SaveCurrent();
            }
        }
        finally { Busy(null); }
    }
    private async Task RenderGroupAsync(DisplayGroup group, IOpenBook book, CancellationToken token)
    {
        _displayGroup = group;
        var first = book.Publication.Units[group.Indices[0]];
        CorrectionButton.IsEnabled = !first.Complex;
        PageError.Visibility = Visibility.Collapsed;
        if (first.Complex && first.Error == null)
        {
            Viewport.Visibility = Visibility.Collapsed; BookWeb.Visibility = Visibility.Visible;
            await _web.ShowAsync(book, first.Locator.Resource, token);
            DrawPages();
        }
        else
        {
            var images = new List<BitmapSource>();
            foreach (int pageIndex in group.Indices)
            {
                int edge = _saved!.Preferences.Fit == "actual" ? 8192 : (int)Math.Clamp(3200 * _zoom, 3200, 8192);
                var bitmap = await book.RenderAsync(pageIndex, edge, token);
                int rotation = LayoutEngine.Rotation(book.Publication.Units[pageIndex], _saved!, _decisions);
                if (rotation % 360 != 0) { var rotated = new TransformedBitmap(bitmap, new RotateTransform(rotation)); rotated.Freeze(); bitmap = rotated; }
                images.Add(bitmap);
            }
            token.ThrowIfCancellationRequested();
            _web.Clear(); BookWeb.Visibility = Visibility.Collapsed; Viewport.Visibility = Visibility.Visible;
            _displayImages.Clear(); _displayImages.AddRange(images); DrawPages();
        }
    }
    private void DrawPages()
    {
        if (Surface == null || _saved == null) return;
        if (_book != null && _book.Publication.Units[_index].Complex)
        {
            var unit = _book.Publication.Units[_index];
            double fit = 1;
            if (unit.Width > 0 && unit.Height > 0 && _saved.Preferences.Fit != "actual")
                fit = _saved.Preferences.Fit == "width" ? ReadingArea.ActualWidth / unit.Width : Math.Min(ReadingArea.ActualWidth / unit.Width, ReadingArea.ActualHeight / unit.Height);
            _web.SetZoom(fit * _zoom);
        }
        Surface.SetPages(_displayImages, Math.Max(100, Viewport.ActualWidth - 18), Math.Max(100, Viewport.ActualHeight - 18), _saved.Preferences.Fit, _zoom, _displayGroup?.Spread == true, _displayGroup?.VerticalOffset ?? 0, _displayGroup?.RightScale ?? 1);
        ZoomText.Text = $"{_zoom * 100:0}%";
    }
    private async Task Turn(int delta)
    {
        if (_book == null || _groups.Count == 0) return;
        int current = _groups.FindIndex(g => g.Indices.Contains(_index));
        int next = current + delta;
        if (next >= 0 && next < _groups.Count) { Viewport.ScrollToTop(); Viewport.ScrollToLeftEnd(); await ShowPageAsync(_groups[next].FirstSourceIndex); }
        else StatusText.Text = delta > 0 ? "已到本卷末页，可点击“下一卷”" : "已到第一页";
    }
    private void RefreshLibrary()
    {
        if (LibraryList == null) return;
        string query = LibrarySearch.Text.Trim(); int filter = LibraryFilter.SelectedIndex;
        var books = _store.Books.Where(b => (b.Title.Contains(query, StringComparison.OrdinalIgnoreCase) || b.Series.Contains(query, StringComparison.OrdinalIgnoreCase)) && (filter == 1 ? b.Favorite : filter == 2 ? b.ReadState == "在读" : filter == 3 ? b.ReadState == "已读" : true)).OrderByDescending(b => b.OpenedAt).ToList();
        LibraryList.ItemsSource = books; LibraryCount.Text = $"{books.Count} 本书 · 双击继续阅读";
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
    private async void ImportClick(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        var dialog = new OpenFolderDialog { Title = "选择书库根目录（扫描子文件夹）" }; if (dialog.ShowDialog(this) != true) return;
        Busy("正在扫描书库…");
        try
        {
            var scanToken = _openCancellation.Token;
            var warnings = new List<string>();
            var found = await Task.Run(() => LibraryScanner.Scan(dialog.FolderName, scanToken, warnings.Add));
            var existing = _store.Books.Select(b => b.Id).ToHashSet();
            var additions = found.Where(b => !existing.Contains(b.Id)).ToList();
            await Task.Run(() => _store.SaveMany(additions));
            RefreshLibrary(); Sidebar.SelectedIndex = 0;
            StatusText.Text = $"书库扫描完成：发现 {found.Count} 本书，新增 {additions.Count} 本";
            if (warnings.Count > 0) MessageBox.Show(this, string.Join("\n", warnings.Take(12)), "部分目录未能扫描");
        }
        finally { Busy(null); }
    });
    private async void OnDrop(object sender, DragEventArgs e) { if (e.Data.GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0) await Run(() => OpenAsync(paths[0])); }
    private async void LibraryOpen(object sender, RoutedEventArgs e) { if (LibraryList.SelectedItem is SavedBook b) await Run(() => OpenAsync(b.Path)); }
    private void SearchChanged(object sender, RoutedEventArgs e) { if (!_binding) RefreshLibrary(); }
    private void FavoriteClick(object sender, RoutedEventArgs e) { if (LibraryList.SelectedItem is SavedBook b) { b.Favorite = !b.Favorite; if (_saved?.Id == b.Id) _saved.Favorite = b.Favorite; _store.Save(b); RefreshLibrary(); } }
    private void FinishedClick(object sender, RoutedEventArgs e) { if (LibraryList.SelectedItem is SavedBook b) { b.ReadState = "已读"; if (_saved?.Id == b.Id) _saved.ReadState = b.ReadState; _store.Save(b); RefreshLibrary(); } }
    private void RenameClick(object sender, RoutedEventArgs e) { if (LibraryList.SelectedItem is SavedBook b) { var text = Dialogs.Text(this, "修改书名", "书库显示名称（原始文件保持不变）", b.Title); if (!string.IsNullOrWhiteSpace(text)) { b.Title = text.Trim(); _store.Save(b); if (_saved?.Id == b.Id) { _saved.Title = b.Title; BookTitle.Text = b.Title; } RefreshLibrary(); } } }
    private async void RemoveClick(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        if (LibraryList.SelectedItem is not SavedBook b || MessageBox.Show(this, $"从书库移除“{b.Title}”及其本地阅读记录？原始文件保留。", "移除书籍", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        if (_saved?.Id == b.Id)
        {
            _renderCancellation.Cancel(); await _io.WaitAsync();
            try
            {
                _web.Clear(); _saved = null; _book?.Dispose(); _book = null; _displayGroup = null; _displayImages.Clear(); _groups.Clear();
                _binding = true; PageList.ItemsSource = null; TocList.ItemsSource = null; BookmarkList.ItemsSource = null; _binding = false;
                Surface.SetPages([], Viewport.ActualWidth, Viewport.ActualHeight, "page", 1, false, 0, 1);
                BookWeb.Visibility = Visibility.Collapsed; Viewport.Visibility = Visibility.Visible; PageError.Visibility = Visibility.Collapsed;
                Welcome.Visibility = Visibility.Visible; BookTitle.Text = "本地阅读，自在翻页"; Title = "水漫 · 本地漫画阅读器";
                PageNumber.Text = "1"; PageTotal.Text = " / 0"; StatusText.Text = "书籍已从书库移除，原文件保留";
            }
            finally { _io.Release(); }
        }
        _store.Remove(b.Id); RefreshLibrary();
    });
    private async void PageSelected(object sender, SelectionChangedEventArgs e) { if (!_binding && PageList.SelectedItem is PageRow p) await Run(() => ShowPageAsync(p.Index)); }
    private async void TocSelected(object sender, SelectionChangedEventArgs e) { if (!_binding && TocList.SelectedItem is NavigationItem p) await Run(() => ShowPageAsync(p.Index)); }
    private async void BookmarkSelected(object sender, SelectionChangedEventArgs e) { if (!_binding && BookmarkList.SelectedItem is NavigationItem p) await Run(() => ShowPageAsync(p.Index)); }
    private async void PreviousClick(object sender, RoutedEventArgs e) => await Run(() => Turn(-1));
    private async void NextClick(object sender, RoutedEventArgs e) => await Run(() => Turn(1));
    private async void NextBookClick(object sender, RoutedEventArgs e) => await Run(async () => { if (_saved == null) return; var volumes = _store.Books.Where(b => b.Series == _saved.Series && string.Equals(Path.GetDirectoryName(b.Path), Path.GetDirectoryName(_saved.Path), StringComparison.OrdinalIgnoreCase)).OrderBy(b => b.Title, NaturalPathComparer.Instance).ToList(); int i = volumes.FindIndex(b => b.Id == _saved.Id); if (i >= 0 && i + 1 < volumes.Count) await OpenAsync(volumes[i + 1].Path); else StatusText.Text = "书库中没有下一卷，请先导入同一系列"; });
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
        var p = _saved.Preferences; p.SmartSpreads = SmartMenu.IsChecked; p.AutomaticPairs = PairsMenu.IsChecked; p.AutomaticOrientation = OrientationMenu.IsChecked; p.CoverAlone = CoverMenu.IsChecked; p.DarkBackground = DarkMenu.IsChecked;
        _binding = true; BindPreferences(); _binding = false; await Run(() => ShowPageAsync(_index));
    }
    private void ToggleBookmarkClick(object sender, RoutedEventArgs e)
    {
        if (_saved == null || _book == null) return; string key = _book.Publication.Units[_index].Id;
        if (!_saved.Bookmarks.Add(key)) _saved.Bookmarks.Remove(key);
        BookmarkButton.Content = _saved.Bookmarks.Contains(key) ? "★ 已收藏此页" : "☆ 书签"; SaveCurrent(); RefreshBookmarks();
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
        if (_saved == null || _book == null) return; int angle = LayoutEngine.Rotation(_book.Publication.Units[_index], _saved, _decisions);
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
    private void SidebarClick(object sender, RoutedEventArgs e) { bool show = SideColumn.Width.Value == 0; SideColumn.Width = new GridLength(show ? 280 : 0); Sidebar.Visibility = show ? Visibility.Visible : Visibility.Collapsed; }
    private void FullScreenClick(object sender, RoutedEventArgs e) => FullScreen();
    private void FullScreen()
    {
        _fullScreen = !_fullScreen;
        if (_fullScreen) { _previousWindowState = WindowState; WindowState = WindowState.Normal; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; WindowState = WindowState.Maximized; }
        else { WindowStyle = WindowStyle.SingleBorderWindow; ResizeMode = ResizeMode.CanResize; WindowState = _previousWindowState; }
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
        if (Keyboard.FocusedElement is TextBox or PasswordBox or ComboBox) return;
        if (e.Key == Key.B && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { ToggleBookmarkClick(this, e); e.Handled = true; return; }
        int delta = e.Key == Key.Right ? (_saved?.Preferences.Direction == "rtl" ? -1 : 1) : e.Key == Key.Left ? (_saved?.Preferences.Direction == "rtl" ? 1 : -1) : e.Key is Key.PageDown or Key.Space ? 1 : e.Key == Key.PageUp ? -1 : 0;
        if (delta != 0) { e.Handled = true; await Run(() => Turn(delta)); }
        else if (e.Key is Key.Home or Key.End && _book != null) { e.Handled = true; await Run(() => ShowPageAsync(e.Key == Key.Home ? 0 : _book.Publication.Units.Count - 1)); }
    }
    private void HelpClick(object sender, RoutedEventArgs e) => MessageBox.Show(this, "水漫 Windows 0.6.0\n\n打开：Ctrl+O，也可拖入文件。\n翻页：方向键、Page Up/Down、空格；方向键遵循阅读方向。\n跳页：底部输入页码，按 Enter。\n缩放：Ctrl+滚轮；放大后拖动画面。\n书签：Ctrl+B。全屏：F11，Esc 退出。\n\nZIP/CBZ 按文件名自然排序，支持嵌套目录及中文文件名。\nEPUB 遵循 spine 顺序，复杂页面使用离线原版式显示。\nMOBI 支持无 DRM 的 MOBI 6 图片漫画。\n\n自动跨页分析较为保守，可通过“页面修正”纠正。\n本地数据目录：" + _dataDirectory, "关于水漫", MessageBoxButton.OK, MessageBoxImage.Information);
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_closed) return; e.Cancel = true; if (_closing) return;
        _closing = true; _openCancellation.Cancel(); _renderCancellation.Cancel();
        await _io.WaitAsync();
        try { SaveCurrent(); _web.Dispose(); _book?.Dispose(); }
        catch (Exception ex) { MessageBox.Show(this, "保存阅读记录失败：" + ex.Message, "水漫"); }
        finally { _io.Release(); _closed = true; _ = Dispatcher.BeginInvoke(Close); }
    }
}

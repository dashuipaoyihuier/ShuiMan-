using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using ShuiMan.Core;

namespace ShuiMan.Windows;

/// <summary>A cover-first local library. Disk scanning and cover decoding run outside the WPF dispatcher.</summary>
public partial class LibraryView : UserControl, IDisposable
{
    public const int BooksPerPage = 48;
    private LibraryStore? store;
    private LibraryCoverCache? coverCache;
    private readonly DispatcherTimer monitor = new() { Interval = TimeSpan.FromMinutes(1) };
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource coversCancellation = new();
    private CancellationTokenSource? importCancellation;
    private readonly SemaphoreSlim operations = new(1, 1);
    private List<SavedBook> books = [];
    private Dictionary<string, bool> availability = [];
    private List<LibraryBookCard> cards = [];
    private List<string> folders = [];
    private LibraryBookCard? resume;
    private string section = "series", search = "", state = "全部", sort = "recent";
    private string? selectedSeries;
    private bool listMode, initialized, updatingControls, disposed;
    private int refreshGeneration, pageIndex, cardCount;
    private string? snapshotSignature;

    public event Action<string>? OpenRequested;
    public event Action<SavedBook>? BookChanged;
    public event Action<string>? BookRemoved;
    public event Action<string>? StatusChanged;

    public IReadOnlyList<SavedBook> VisibleBooks => cards.Select(card => card.Book).ToArray();
    public IReadOnlyList<LibraryBookCard> VisibleCards => cards;
    public IReadOnlyList<string> SourceFolders => folders;
    public int TotalFilteredBooks { get; private set; }
    public int PageIndex => pageIndex;
    public bool IsBusy { get; private set; }
    public Task CoversReady { get; private set; } = Task.CompletedTask;

    public LibraryView()
    {
        InitializeComponent();
        monitor.Tick += async (_, _) => { if (!disposed && !IsBusy && folders.Count > 0) await Run(() => RefreshSourcesAsync(true)); };
        IsVisibleChanged += (_, _) =>
        {
            if (!initialized || disposed) return;
            if (IsVisible) StartCovers();
            else coversCancellation.Cancel();
        };
    }

    public void Initialize(LibraryStore libraryStore)
    {
        ArgumentNullException.ThrowIfNull(libraryStore);
        if (initialized) throw new InvalidOperationException("书库已经初始化。");
        store = libraryStore;
        coverCache = new LibraryCoverCache(Path.Combine(store.DirectoryPath, "covers"));
        initialized = true;
        UpdateNavigation();
        monitor.Start();
    }

    public Task RefreshAsync() => RefreshAsync(false, false);
    private async Task RefreshAsync(bool preserveViewport, bool skipUnchanged)
    {
        if (disposed || store == null) return;
        int generation = ++refreshGeneration;
        var snapshot = await Task.Run(() =>
        {
            var values = store.Books.ToList();
            var roots = store.SourceFolders.ToList();
            var present = values.ToDictionary(book => book.Id, book => IsAvailable(book.Path));
            var signature = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { values, roots, present })));
            return (Books: values, Folders: roots, Availability: present, Signature: signature);
        }, lifetime.Token);
        if (disposed || generation != refreshGeneration) return;
        if (skipUnchanged && snapshotSignature == snapshot.Signature) return;
        snapshotSignature = snapshot.Signature;
        books = snapshot.Books; folders = snapshot.Folders; availability = snapshot.Availability;
        FolderList.ItemsSource = folders;
        NoFoldersText.Visibility = folders.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ApplyView(preserveViewport);
        if (!string.IsNullOrWhiteSpace(store.Warning)) ShowMessage(store.Warning, true);
    }

    public void SetSection(string value)
    {
        if (value is not ("all" or "reading" or "favorites" or "series" or "folders")) throw new ArgumentException("未知的书库分区。", nameof(value));
        section = value; selectedSeries = null; pageIndex = 0;
        ApplyView();
    }

    public void SetSearch(string value)
    {
        search = value ?? ""; pageIndex = 0;
        updatingControls = true; SearchBox.Text = search; updatingControls = false;
        ApplyView();
    }

    public void SetReadStateFilter(string value)
    {
        if (value is not ("全部" or "未读" or "在读" or "已读")) throw new ArgumentException("阅读状态无效。", nameof(value));
        state = value; pageIndex = 0;
        updatingControls = true;
        StateBox.SelectedItem = StateBox.Items.OfType<ComboBoxItem>().First(item => (string)item.Tag == value);
        updatingControls = false; ApplyView();
    }

    public void SetSort(string value)
    {
        if (value is not ("recent" or "title" or "series" or "added")) throw new ArgumentException("排列方式无效。", nameof(value));
        sort = value; pageIndex = 0;
        updatingControls = true;
        SortBox.SelectedItem = SortBox.Items.OfType<ComboBoxItem>().First(item => (string)item.Tag == value);
        updatingControls = false; ApplyView();
    }

    public void SetSeries(string? value)
    {
        section = "series"; selectedSeries = value; pageIndex = 0;
        if (value != null) SetSort("title"); else ApplyView();
    }

    public void SetListMode(bool value) { listMode = value; ApplyView(); }
    public void GoToPage(int zeroBased) { pageIndex = Math.Clamp(zeroBased, 0, Math.Max(0, (cardCount - 1) / BooksPerPage)); ApplyView(); }

    private void ApplyView(bool preserveViewport = false)
    {
        if (!initialized || disposed) return;
        var previousOffset = LibraryScroll.VerticalOffset;
        UpdateNavigation();
        SearchPlaceholder.Visibility = string.IsNullOrEmpty(search) ? Visibility.Visible : Visibility.Collapsed;
        bool sources = section == "folders";
        FoldersPanel.Visibility = sources ? Visibility.Visible : Visibility.Collapsed;
        FilterBar.Visibility = sources ? Visibility.Collapsed : Visibility.Visible;
        PaginationPanel.Visibility = sources ? Visibility.Collapsed : Visibility.Visible;
        LibraryScroll.Visibility = sources ? Visibility.Collapsed : Visibility.Visible;
        SeriesBack.Visibility = section == "series" && selectedSeries != null ? Visibility.Visible : Visibility.Collapsed;
        SectionTitle.Text = selectedSeries != null ? DisplaySeries(selectedSeries) : section switch
        { "reading" => "正在阅读", "favorites" => "我的收藏", "series" => "系列书库", "folders" => "漫画来源", _ => "所有分卷" };

        IEnumerable<SavedBook> filtered = books;
        if (section == "reading") filtered = filtered.Where(book => book.ReadState == "在读");
        if (section == "favorites") filtered = filtered.Where(book => book.Favorite);
        if (selectedSeries != null) filtered = filtered.Where(book => book.Series.Equals(selectedSeries, StringComparison.OrdinalIgnoreCase));
        if (state != "全部") filtered = filtered.Where(book => book.ReadState == state);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var terms = search.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            filtered = filtered.Where(book => terms.All(term =>
                $"{book.Title} {book.Series} {string.Join(' ', book.Tags)}".Contains(term, StringComparison.CurrentCultureIgnoreCase)));
        }
        var ordered = (sort switch
        {
            "title" => filtered.Order(LibraryBookComparer.NaturalVolume),
            "series" => filtered.OrderBy(book => book.Series, NaturalPathComparer.Instance).ThenBy(book => book, LibraryBookComparer.NaturalVolume),
            "added" => filtered.OrderByDescending(book => book.AddedAt).ThenBy(book => book, LibraryBookComparer.NaturalVolume),
            _ => filtered.OrderByDescending(book => book.OpenedAt).ThenBy(book => book, LibraryBookComparer.NaturalVolume)
        }).ToList();
        TotalFilteredBooks = ordered.Count;
        bool seriesOverview = section == "series" && selectedSeries == null;
        var seriesGroups = seriesOverview ? ordered.GroupBy(book => book.Series, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, NaturalPathComparer.Instance).ToList() : null;
        cardCount = seriesGroups?.Count ?? ordered.Count;
        pageIndex = Math.Clamp(pageIndex, 0, Math.Max(0, (cardCount - 1) / BooksPerPage));
        cards = seriesGroups != null
            ? seriesGroups.Skip(pageIndex * BooksPerPage).Take(BooksPerPage)
                .Select(group => CreateCard(group.Order(LibraryBookComparer.NaturalVolume).FirstOrDefault(book => availability.GetValueOrDefault(book.Id)) ?? group.Order(LibraryBookComparer.NaturalVolume).First(), true, group.Count())).ToList()
            : ordered.Skip(pageIndex * BooksPerPage).Take(BooksPerPage).Select(book => CreateCard(book)).ToList();
        BookGrid.ItemsSource = cards; BookList.ItemsSource = cards;
        bool showList = listMode && !seriesOverview;
        ListModeButton.IsEnabled = !seriesOverview;
        BookGrid.Visibility = !showList ? Visibility.Visible : Visibility.Collapsed;
        BookList.Visibility = showList ? Visibility.Visible : Visibility.Collapsed;
        GridModeButton.Background = !showList ? Brushes.White : Brushes.Transparent;
        ListModeButton.Background = showList ? Brushes.White : Brushes.Transparent;
        SectionSubtitle.Text = sources ? $"{folders.Count} 个漫画目录 · 自动更新本地收藏" :
            ordered.Count == 0 && books.Count == 0 ? "为喜欢的故事，留一个位置。" :
            $"{ordered.Count} 本漫画 · {ordered.Select(book => book.Series).Distinct(StringComparer.OrdinalIgnoreCase).Count()} 个系列";
        int pageCount = Math.Max(1, (cardCount + BooksPerPage - 1) / BooksPerPage);
        PageLabel.Text = $"{pageIndex + 1} / {pageCount}";
        PageInfo.Text = cardCount == 0 ? "仅在本地保存书库与阅读进度" :
            $"{pageIndex * BooksPerPage + 1}–{Math.Min((pageIndex + 1) * BooksPerPage, cardCount)} / {cardCount} {(seriesOverview ? "个系列" : "本漫画")}";
        PreviousPage.IsEnabled = pageIndex > 0; NextPage.IsEnabled = pageIndex + 1 < pageCount;
        EmptyPanel.Visibility = !sources && cards.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (cards.Count == 0)
        {
            bool first = books.Count == 0;
            EmptyTitle.Text = first ? "你的下一段故事，从这里开始" : section switch
            { "favorites" when string.IsNullOrWhiteSpace(search) => "把喜欢的故事，收藏在这里", "reading" when string.IsNullOrWhiteSpace(search) => "还没有未读完的故事", _ => "没有找到匹配的漫画" };
            EmptyDescription.Text = first ? "添加漫画文件，或选择一个收藏目录。\nZIP / CBZ、EPUB、PDF 与图片集，都能安放于此。" :
                section == "favorites" && string.IsNullOrWhiteSpace(search) ? "点击书籍下方的星标，即可加入收藏。\n也可以调整阅读状态或搜索条件。" : "试试其他书名、系列、标签或阅读状态。\n所有漫画都保留在你的书库里。";
        }
        resume = !sources && (section == "all" || section == "series" && selectedSeries == null) && pageIndex == 0 && string.IsNullOrWhiteSpace(search) && state == "全部"
            ? books.Where(book => book.ReadState == "在读" && book.Position > 0 && availability.GetValueOrDefault(book.Id))
                .OrderByDescending(book => book.OpenedAt).Select(book => CreateCard(book)).FirstOrDefault() : null;
        ResumePanel.Visibility = resume != null ? Visibility.Visible : Visibility.Collapsed;
        ResumeCover.Content = resume;
        if (resume != null) { ResumeTitle.Text = resume.Title; ResumeProgress.Text = resume.Metadata + " · " + resume.SeriesName; }
        if (preserveViewport) LibraryScroll.ScrollToVerticalOffset(previousOffset);
        else LibraryScroll.ScrollToTop();
        StartCovers();
    }

    private static string DisplaySeries(string value) => string.IsNullOrWhiteSpace(value) ? "未分类" : value;
    private static bool IsAvailable(string path) => File.Exists(path) || Directory.Exists(path);
    private LibraryBookCard CreateCard(SavedBook book, bool isSeries = false, int count = 0) =>
        new() { Book = book, Available = availability.GetValueOrDefault(book.Id), IsSeries = isSeries, SeriesCount = count };

    private void UpdateNavigation()
    {
        AllCount.Text = books.Count.ToString();
        ReadingCount.Text = books.Count(book => book.ReadState == "在读").ToString();
        FavoriteCount.Text = books.Count(book => book.Favorite).ToString();
        foreach (var button in new[] { AllNav, ReadingNav, FavoritesNav, SeriesNav, FoldersNav })
        {
            bool selected = (string)button.Tag == section;
            button.Background = selected ? new SolidColorBrush(Color.FromRgb(223, 230, 250)) : Brushes.Transparent;
            button.Foreground = selected ? new SolidColorBrush(Color.FromRgb(71, 100, 188)) : new SolidColorBrush(Color.FromRgb(84, 91, 109));
            button.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
        }
    }

    private void StartCovers()
    {
        coversCancellation.Cancel(); coversCancellation.Dispose(); coversCancellation = new();
        if (disposed || coverCache == null || section == "folders" || !IsVisible) { CoversReady = Task.CompletedTask; return; }
        var token = coversCancellation.Token;
        var requests = cards.Concat(resume == null ? [] : new[] { resume }).ToList();
        CoversReady = Task.WhenAll(requests.Select(async card =>
        {
            if (!card.Available || card.Cover != null) return;
            try
            {
                var image = await coverCache.GetAsync(card.Book, token);
                if (!token.IsCancellationRequested && !disposed) card.Cover = image;
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (!token.IsCancellationRequested && !disposed) card.CoverError = ex is PasswordRequiredException ? "密码保护的 PDF；打开时输入密码" : ex.Message; }
        }));
    }

    public async Task ImportPathsAsync(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (disposed || store == null) return;
        var requested = paths.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (requested.Length == 0) return;
        await operations.WaitAsync(lifetime.Token);
        try
        {
            BeginImport("正在查找漫画…");
            var token = importCancellation!.Token;
            var result = await Task.Run(() =>
            {
                var candidates = new List<SavedBook>(); var addedFolders = new List<string>(); var warnings = new List<string>();
                foreach (var input in requested)
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        string path = Path.GetFullPath(input);
                        if (Directory.Exists(path))
                        {
                            candidates.AddRange(LibraryScanner.Scan(path, token, warnings.Add));
                            addedFolders.Add(path);
                        }
                        else if (File.Exists(path) && DocumentEngine.IsSupported(path))
                        {
                            var info = new FileInfo(path);
                            candidates.Add(new SavedBook
                            {
                                Id = LibraryScanner.IdentityForPath(path), Path = path,
                                Title = Path.GetFileNameWithoutExtension(path), Series = info.Directory?.Name ?? "",
                                Revision = $"{info.Length}:{info.LastWriteTimeUtc.Ticks}", OpenedAt = DateTime.MinValue
                            });
                        }
                        else warnings.Add($"无法导入 {Path.GetFileName(path)}：文件不存在或格式不受支持。");
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
                    { warnings.Add($"无法导入 {input}：{ex.Message}"); }
                }
                token.ThrowIfCancellationRequested();
                int added = store.ImportDiscovered(candidates);
                foreach (var folder in addedFolders) store.AddSourceFolder(folder);
                return (Added: added, Found: candidates.Select(book => book.Id).Distinct().Count(), Warnings: warnings);
            }, token);
            section = "series"; selectedSeries = null; pageIndex = 0; search = ""; state = "全部";
            updatingControls = true; SearchBox.Text = ""; StateBox.SelectedIndex = 0; updatingControls = false;
            await RefreshAsync();
            string message = result.Found == 0 ? "没有找到可导入的漫画。请选择 ZIP/CBZ、EPUB、PDF、MOBI 或包含图片的目录。" :
                $"已添加 {result.Added} 本漫画，{result.Found - result.Added} 本已在书库。原文件保留在原处。";
            if (result.Warnings.Count > 0) message += $"\n{result.Warnings.Count} 项提示：{string.Join("；", result.Warnings.Take(3))}";
            ShowMessage(message, result.Warnings.Count > 0);
        }
        catch (OperationCanceledException) { if (!disposed) ShowMessage("已停止导入。现有书库和原漫画文件已保留。"); }
        finally { EndImport(); operations.Release(); }
    }

    public Task RefreshSourcesAsync() => RefreshSourcesAsync(false);
    private async Task RefreshSourcesAsync(bool quiet)
    {
        if (disposed || store == null || IsBusy) return;
        var roots = await Task.Run(() => store.SourceFolders.ToList(), lifetime.Token);
        if (roots.Count == 0) { await RefreshAsync(); if (!quiet) ShowMessage("书库已更新。添加漫画目录后，可以自动发现新书。 "); return; }
        await operations.WaitAsync(lifetime.Token);
        try
        {
            BeginImport(quiet ? null : "正在更新漫画目录…");
            var token = importCancellation!.Token;
            var result = await Task.Run(() =>
            {
                var found = new List<SavedBook>(); var warnings = new List<string>();
                foreach (var root in roots)
                {
                    token.ThrowIfCancellationRequested();
                    try { found.AddRange(LibraryScanner.Scan(root, token, warnings.Add)); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    { warnings.Add($"{root}：{ex.Message}"); }
                }
                token.ThrowIfCancellationRequested();
                return (Added: store.ImportDiscovered(found), Warnings: warnings);
            }, token);
            await RefreshAsync(preserveViewport: true, skipUnchanged: quiet);
            if (!quiet || result.Added > 0 || result.Warnings.Count > 0)
                ShowMessage($"目录已更新，新增 {result.Added} 本漫画。" + (result.Warnings.Count > 0 ? "\n部分来源暂时不可用，已保留书库与进度。" + string.Join("；", result.Warnings.Take(2)) : ""), result.Warnings.Count > 0);
        }
        catch (OperationCanceledException) { if (!disposed && !quiet) ShowMessage("已停止更新，现有书库已保留。"); }
        finally { EndImport(); operations.Release(); }
    }

    public async Task UpdateBookAsync(string id, string? title = null, string? series = null,
        IEnumerable<string>? tags = null, bool? favorite = null, string? readState = null)
    {
        if (disposed || store == null) return;
        var updated = await Task.Run(() => store.UpdateMetadata(id, title, series, tags, favorite, readState), lifetime.Token);
        if (updated != null && !disposed) BookChanged?.Invoke(updated);
        await RefreshAsync();
    }

    public async Task RemoveBookAsync(string id)
    {
        if (disposed || store == null) return;
        await Task.Run(() => store.Remove(id), lifetime.Token);
        if (!disposed) BookRemoved?.Invoke(id);
        await RefreshAsync();
        ShowMessage("已移除书库记录，原漫画文件保留。来源目录中的漫画会在下次扫描时重新发现；可在“漫画来源”停止扫描。");
    }

    public async Task RemoveSourceFolderAsync(string path)
    {
        if (disposed || store == null) return;
        await operations.WaitAsync(lifetime.Token);
        try { await Task.Run(() => store.RemoveSourceFolder(path), lifetime.Token); await RefreshAsync(); ShowMessage("已停止扫描这个目录。书库、阅读进度和原漫画文件均已保留。"); }
        finally { operations.Release(); }
    }

    private void BeginImport(string? message)
    {
        IsBusy = true;
        importCancellation?.Dispose(); importCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        CancelImportButton.Visibility = message != null ? Visibility.Visible : Visibility.Collapsed;
        if (message != null) ShowMessage(message);
    }
    private void EndImport()
    {
        IsBusy = false;
        if (!disposed) CancelImportButton.Visibility = Visibility.Collapsed;
        importCancellation?.Dispose(); importCancellation = null;
    }
    private void ShowMessage(string message, bool warning = false)
    {
        if (disposed) return;
        MessageText.Text = message; MessageBanner.Visibility = Visibility.Visible;
        MessageBanner.Background = new SolidColorBrush(warning ? Color.FromRgb(250, 241, 225) : Color.FromRgb(234, 240, 252));
        StatusChanged?.Invoke(message);
    }
    private async Task Run(Func<Task> action)
    {
        try { await action(); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { ShowMessage(ex.Message, true); }
    }

    private void NavigationClick(object sender, RoutedEventArgs e) => SetSection((string)((Button)sender).Tag);
    private void SearchChanged(object sender, TextChangedEventArgs e) { if (!initialized || updatingControls) return; search = SearchBox.Text; pageIndex = 0; ApplyView(); }
    private void FilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!initialized || updatingControls) return;
        state = (StateBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "全部";
        sort = (SortBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "recent";
        pageIndex = 0; ApplyView();
    }
    private void SeriesBackClick(object sender, RoutedEventArgs e) => SetSeries(null);
    private void GridModeClick(object sender, RoutedEventArgs e) => SetListMode(false);
    private void ListModeClick(object sender, RoutedEventArgs e) => SetListMode(true);
    private void PreviousPageClick(object sender, RoutedEventArgs e) => GoToPage(pageIndex - 1);
    private void NextPageClick(object sender, RoutedEventArgs e) => GoToPage(pageIndex + 1);
    private void CancelImportClick(object sender, RoutedEventArgs e) => importCancellation?.Cancel();
    private async void ChooseFilesClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "添加漫画到书库", Multiselect = true,
            Filter = "漫画与图片|*.zip;*.cbz;*.epub;*.pdf;*.mobi;*.jpg;*.jpeg;*.jpe;*.png;*.gif;*.bmp;*.tif;*.tiff;*.webp;*.avif;*.heic;*.heif;*.jxl|所有文件|*.*" };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) await Run(() => ImportPathsAsync(dialog.FileNames));
    }
    private async void ChooseFolderClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "选择漫画来源目录", Multiselect = true };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) await Run(() => ImportPathsAsync(dialog.FolderNames));
    }
    private async void RefreshSourcesClick(object sender, RoutedEventArgs e) => await Run(RefreshSourcesAsync);
    private async void RemoveFolderClick(object sender, RoutedEventArgs e)
    { if (((FrameworkElement)sender).DataContext is string path) await Run(() => RemoveSourceFolderAsync(path)); }
    private void LibraryDragOver(object sender, DragEventArgs e)
    { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; }
    private async void LibraryDrop(object sender, DragEventArgs e)
    { e.Handled = true; if (e.Data.GetData(DataFormats.FileDrop) is string[] paths) await Run(() => ImportPathsAsync(paths)); }
    private void OpenBookClick(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not LibraryBookCard card) return;
        if (card.IsSeries) { SetSeries(card.Book.Series); return; }
        RequestOpen(card);
    }
    private void RequestOpen(LibraryBookCard card)
    {
        if (!IsAvailable(card.Book.Path)) { ShowMessage("找不到原漫画文件。重新连接所在磁盘或恢复文件到原位置，然后刷新书库。", true); return; }
        OpenRequested?.Invoke(card.Book.Path);
    }
    private void ResumeClick(object sender, RoutedEventArgs e) { if (resume != null) RequestOpen(resume); }
    private async void FavoriteClick(object sender, RoutedEventArgs e)
    { if (((FrameworkElement)sender).DataContext is LibraryBookCard card) await Run(() => UpdateBookAsync(card.Id, favorite: !card.Book.Favorite)); }
    private void MoreClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: LibraryBookCard card } button) return;
        var menu = new ContextMenu();
        AddMenu(menu, "编辑书籍信息…", () => EditBookAsync(card.Book));
        AddMenu(menu, card.Book.Favorite ? "取消收藏" : "加入收藏", () => UpdateBookAsync(card.Id, favorite: !card.Book.Favorite));
        menu.Items.Add(new Separator());
        foreach (var value in new[] { "未读", "在读", "已读" })
        {
            string readState = value;
            var item = new MenuItem { Header = "标记为" + value, IsCheckable = true, IsChecked = card.Book.ReadState == value };
            item.Click += async (_, _) => await Run(() => UpdateBookAsync(card.Id, readState: readState)); menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());
        AddMenu(menu, "从书库移除（保留原文件）", () => RemoveBookAsync(card.Id));
        menu.PlacementTarget = button; menu.IsOpen = true;
    }
    private void AddMenu(ContextMenu menu, string text, Func<Task> action)
    { var item = new MenuItem { Header = text }; item.Click += async (_, _) => await Run(action); menu.Items.Add(item); }

    private async Task EditBookAsync(SavedBook book)
    {
        var editor = new LibraryMetadataEditor(book) { Owner = Window.GetWindow(this) };
        if (editor.ShowDialog() == true)
            await UpdateBookAsync(book.Id, editor.BookTitle, editor.Series, editor.Tags, editor.Favorite, editor.ReadState);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true; monitor.Stop(); lifetime.Cancel(); importCancellation?.Cancel(); coversCancellation.Cancel();
        coverCache?.Dispose();
    }
}

internal sealed class LibraryMetadataEditor : Window
{
    private readonly TextBox titleInput, seriesInput, tagsInput;
    private readonly CheckBox favoriteInput;
    private readonly ComboBox stateInput;
    public string BookTitle => titleInput.Text.Trim();
    public string Series => seriesInput.Text.Trim();
    public IEnumerable<string> Tags => tagsInput.Text.Replace('，', ',').Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    public bool Favorite => favoriteInput.IsChecked == true;
    public string ReadState => (string)((ComboBoxItem)stateInput.SelectedItem).Content;

    public LibraryMetadataEditor(SavedBook book)
    {
        Title = "编辑书籍信息"; Width = 500; SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(247, 247, 250));
        var panel = new StackPanel { Margin = new Thickness(30) }; Content = panel;
        panel.Children.Add(new TextBlock { Text = "编辑书籍信息", FontSize = 23, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 24) });
        titleInput = AddInput(panel, "书名", book.Title);
        seriesInput = AddInput(panel, "系列", book.Series);
        tagsInput = AddInput(panel, "标签（用逗号分隔）", string.Join(", ", book.Tags));
        panel.Children.Add(new TextBlock { Text = "阅读状态", Foreground = Brushes.Gray, Margin = new Thickness(0, 6, 0, 8), FontSize = 12 });
        stateInput = new ComboBox { Margin = new Thickness(0, 0, 0, 15), Height = 38 };
        foreach (var value in new[] { "未读", "在读", "已读" })
        { var item = new ComboBoxItem { Content = value }; stateInput.Items.Add(item); if (value == book.ReadState) stateInput.SelectedItem = item; }
        if (stateInput.SelectedIndex < 0) stateInput.SelectedIndex = 0;
        panel.Children.Add(stateInput);
        favoriteInput = new CheckBox { Content = "加入我的收藏", IsChecked = book.Favorite, Margin = new Thickness(0, 0, 0, 20) }; panel.Children.Add(favoriteInput);
        panel.Children.Add(new TextBlock { Text = "同名系列会归在一起。修改仅保存在书库，不会重命名或移动原漫画文件。", TextWrapping = TextWrapping.Wrap, FontSize = 11, Foreground = Brushes.Gray, LineHeight = 20 });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 25, 0, 0) };
        var cancel = new Button { Content = "取消", IsCancel = true, Margin = new Thickness(0, 0, 10, 0), Padding = new Thickness(18, 9, 18, 9) };
        cancel.SetResourceReference(StyleProperty, "GhostButton"); buttons.Children.Add(cancel);
        var save = new Button { Content = "保存更改", IsDefault = true, Padding = new Thickness(18, 9, 18, 9) };
        save.SetResourceReference(StyleProperty, "PrimaryButton"); buttons.Children.Add(save); panel.Children.Add(buttons);
        save.Click += (_, _) => { if (string.IsNullOrWhiteSpace(titleInput.Text)) { titleInput.Focus(); return; } DialogResult = true; };
        Loaded += (_, _) => { titleInput.Focus(); titleInput.SelectAll(); };
    }
    private static TextBox AddInput(Panel panel, string label, string value)
    {
        panel.Children.Add(new TextBlock { Text = label, Foreground = Brushes.Gray, FontSize = 12, Margin = new Thickness(0, 0, 0, 8) });
        var input = new TextBox { Text = value, Margin = new Thickness(0, 0, 0, 15), Padding = new Thickness(10, 9, 10, 9) }; panel.Children.Add(input); return input;
    }
}

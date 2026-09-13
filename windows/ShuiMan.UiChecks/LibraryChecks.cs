using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ShuiMan.Core;
using ShuiMan.Windows;

namespace ShuiMan.UiChecks;

internal static partial class Program
{
    private static async Task LibraryChecks(string root)
    {
        var sources = Path.Combine(root, "original-library-books");
        var firstPath = Path.Combine(sources, "潮汐来信", "海的另一边.zip");
        var secondPath = Path.Combine(sources, "潮汐来信", "晚潮.cbz");
        var folderPath = Path.Combine(sources, "山间慢行", "雨后");
        ShowcaseFixtures.GenerateSmallArchive(firstPath, "海的另一边");
        ShowcaseFixtures.GenerateSmallArchive(secondPath, "晚潮");
        ShowcaseFixtures.GenerateImageFolder(folderPath);
        await LibraryStateRaceChecks(root, firstPath);
        var data = Path.Combine(root, "library-app-data");
        var store = new LibraryStore(data);
        var library = new LibraryView();
        library.Initialize(store);
        library.SetSection("all");
        var window = OffscreenWindow("ShuiMan real library integration", library);
        try
        {
            window.Show();
            await library.RefreshAsync();
            window.UpdateLayout();
            Check("new library displays an actual empty state", library.TotalFilteredBooks == 0 && library.VisibleBooks.Count == 0 && ((FrameworkElement)library.FindName("EmptyPanel")).IsVisible);
            await library.ImportPathsAsync([firstPath, folderPath]);
            library.SetSection("all");
            await library.CoversReady.WaitAsync(TimeSpan.FromSeconds(30));
            window.UpdateLayout();
            Check("ZIP and recursive image folder import create two durable books", library.VisibleBooks.Count == 2 && new LibraryStore(data).Books.Count == 2);
            Check("imported covers decode into real, nonempty bitmap content", library.VisibleCards.Count == 2 && library.VisibleCards.All(card => HasArtwork(card.Cover)));
            Check("grid renders decoded cover images in its WPF visual tree", Descendants<Image>((DependencyObject)library.FindName("BookGrid")).Any(image => image.IsVisible && image.Source is BitmapSource bitmap && HasArtwork(bitmap)));

            await library.ImportPathsAsync([sources]);
            Check("directory import opens the series overview with one card per series", library.TotalFilteredBooks == 3 && library.VisibleCards.Count == 2 && library.VisibleCards.All(card => card.IsSeries));
            library.SetSection("all");
            Check("folder scan adds missing books without duplicating existing paths", library.VisibleBooks.Count == 3 && store.Books.Select(book => book.Id).Distinct().Count() == 3);
            var firstId = LibraryScanner.IdentityForPath(firstPath);
            var secondId = LibraryScanner.IdentityForPath(secondPath);
            await library.CoversReady.WaitAsync(TimeSpan.FromSeconds(30));
            window.UpdateLayout();
            FavoriteButton(library, firstId).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntil(() => store.Books.Single(book => book.Id == firstId).Favorite && library.VisibleBooks.Single(book => book.Id == firstId).Favorite, "favorite button updates stored and displayed state");
            Check("clicking the real favorite button persists a favorite", store.Books.Single(book => book.Id == firstId).Favorite);
            await library.RefreshAsync();
            window.UpdateLayout();
            FavoriteButton(library, firstId).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntil(() => !store.Books.Single(book => book.Id == firstId).Favorite && !library.VisibleBooks.Single(book => book.Id == firstId).Favorite, "favorite button removes stored and displayed favorite");
            Check("clicking the favorite button again removes the favorite", !store.Books.Single(book => book.Id == firstId).Favorite);
            await library.UpdateBookAsync(firstId, title: "海的另一边（珍藏）", series: "潮汐来信", tags: ["旅行", "绘本"], favorite: true, readState: "在读");
            await library.UpdateBookAsync(secondId, readState: "已读");
            var persisted = new LibraryStore(data).Books.Single(book => book.Id == firstId);
            Check("edited title, series, tags, favorite and read status persist", persisted.Title == "海的另一边（珍藏）" && persisted.Series == "潮汐来信" && persisted.Tags.SequenceEqual(["旅行", "绘本"]) && persisted.Favorite && persisted.ReadState == "在读");

            var search = (TextBox)library.FindName("SearchBox");
            search.Text = "旅行";
            await WaitUntil(() => library.TotalFilteredBooks == 1, "tag search updates visible library");
            Check("typing into the real search box searches stored tags", library.VisibleBooks.Single().Id == firstId);
            search.Text = "雨后";
            await WaitUntil(() => library.TotalFilteredBooks == 1 && library.VisibleBooks[0].Path == folderPath, "title search updates visible library");
            Check("typing into the real search box searches book titles", library.VisibleBooks.Single().Path == folderPath);
            search.Text = "";
            ((Button)library.FindName("FavoritesNav")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check("favorites navigation only displays favorited books", library.TotalFilteredBooks == 1 && library.VisibleBooks.Single().Id == firstId);
            ((Button)library.FindName("ReadingNav")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check("continue-reading navigation only displays books in progress", library.TotalFilteredBooks == 1 && library.VisibleBooks.Single().Id == firstId);
            ((Button)library.FindName("AllNav")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var readStateFilter = (ComboBox)library.FindName("StateBox");
            readStateFilter.SelectedItem = readStateFilter.Items.OfType<ComboBoxItem>().Single(item => item.Tag?.ToString() == "已读");
            Check("read-state filter displays completed books", library.TotalFilteredBooks == 1 && library.VisibleBooks.Single().Id == secondId);
            library.SetReadStateFilter("全部");
            ((Button)library.FindName("SeriesNav")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await library.CoversReady.WaitAsync(TimeSpan.FromSeconds(30));
            window.UpdateLayout();
            var seriesButton = Descendants<Button>((DependencyObject)library.FindName("BookGrid"))
                .FirstOrDefault(button => AutomationProperties.GetName(button) == "潮汐来信")
                ?? throw new InvalidOperationException("The series cover button was not rendered.");
            seriesButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntil(() => library.TotalFilteredBooks == 2 && library.VisibleBooks.All(book => book.Series == "潮汐来信"), "series cover navigation updates shelf");
            Check("clicking a series cover displays the two volumes in that series", library.TotalFilteredBooks == 2 && library.VisibleBooks.All(book => book.Series == "潮汐来信"));
            library.SetSeries(null);
            library.SetSection("all");
            var expectedIds = library.VisibleBooks.Select(book => book.Id).Order().ToArray();
            ((Button)library.FindName("ListModeButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.UpdateLayout();
            Check("list mode renders the same books through its real list control", ((FrameworkElement)library.FindName("BookList")).IsVisible && !((FrameworkElement)library.FindName("BookGrid")).IsVisible && library.VisibleBooks.Select(book => book.Id).Order().SequenceEqual(expectedIds));
            ((Button)library.FindName("GridModeButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.UpdateLayout();
            Check("grid mode restores the cover shelf", ((FrameworkElement)library.FindName("BookGrid")).IsVisible && !((FrameworkElement)library.FindName("BookList")).IsVisible);

            Check("imported source folder is retained for refresh", library.SourceFolders.Any(path => string.Equals(path, sources, StringComparison.OrdinalIgnoreCase)));
            var addedPath = Path.Combine(sources, "潮汐来信", "新到的信.zip");
            ShowcaseFixtures.GenerateSmallArchive(addedPath, "新到的信");
            await library.RefreshSourcesAsync();
            Check("refreshing a source folder discovers a newly added ZIP", library.TotalFilteredBooks == 4 && library.VisibleBooks.Any(book => book.Path == addedPath));
            var afterRefresh = new LibraryStore(data).Books.Single(book => book.Id == firstId);
            Check("source refresh preserves user metadata and favorite state", afterRefresh.Title == persisted.Title && afterRefresh.Favorite && afterRefresh.Tags.SequenceEqual(persisted.Tags));
            await library.RemoveBookAsync(LibraryScanner.IdentityForPath(addedPath));
            Check("removing a library entry retains the original archive", library.TotalFilteredBooks == 3 && File.Exists(addedPath) && new LibraryStore(data).Books.Count == 3);

            var temporarilyUnavailable = secondPath + ".unavailable";
            File.Move(secondPath, temporarilyUnavailable);
            try
            {
                await library.RefreshAsync();
                window.UpdateLayout();
                bool requestedMissingBook = false;
                void MissingOpen(string _) => requestedMissingBook = true;
                library.OpenRequested += MissingOpen;
                try { OpenCover(library, secondId); }
                finally { library.OpenRequested -= MissingOpen; }
                Check("a missing source is marked unavailable and its cover does not open a reader", !library.VisibleCards.Single(card => card.Book.Id == secondId).Available && !requestedMissingBook && store.Books.Count == 3);
            }
            finally { File.Move(temporarilyUnavailable, secondPath); }
            await library.RefreshAsync();
            Check("reconnecting the source restores its existing library entry", library.VisibleCards.Single(card => card.Book.Id == secondId).Available && store.Books.Single(book => book.Id == secondId).ReadState == "已读");

            foreach (var source in library.SourceFolders.ToArray()) await library.RemoveSourceFolderAsync(source);
            var noLongerScanned = Path.Combine(sources, "潮汐来信", "停止扫描后的新书.zip");
            ShowcaseFixtures.GenerateSmallArchive(noLongerScanned, "停止扫描后的新书");
            await library.RefreshSourcesAsync();
            Check("stopping all source folders persists and prevents later discovery", new LibraryStore(data).SourceFolders.Count == 0 && library.TotalFilteredBooks == 3 && !store.Books.Any(book => book.Path == noLongerScanned));

            var reloaded = new LibraryView();
            reloaded.Initialize(new LibraryStore(data));
            reloaded.SetSection("all");
            library.Dispose();
            window.Content = reloaded;
            await reloaded.RefreshAsync();
            await reloaded.CoversReady.WaitAsync(TimeSpan.FromSeconds(30));
            Check("a fresh library view reloads books, favorite and series", reloaded.TotalFilteredBooks == 3 && reloaded.VisibleBooks.Single(book => book.Id == firstId) is { Favorite: true, Series: "潮汐来信" } && reloaded.VisibleCards.All(card => HasArtwork(card.Cover)));
            reloaded.Dispose();
        }
        finally { library.Dispose(); window.Close(); }
        await PagedLibraryChecks(root, firstPath);
        await MainWindowLibraryChecks(data, firstPath);
    }

    private static async Task PagedLibraryChecks(string root, string originalFixture)
    {
        var source = Path.Combine(root, "paged-original-books");
        Directory.CreateDirectory(source);
        // Independent paths backed by original generated content exercise the real scanner and cache.
        for (var index = 53; index > 0; index--) File.Copy(originalFixture, Path.Combine(source, $"Volume {index}.zip"));
        var library = new LibraryView();
        library.Initialize(new LibraryStore(Path.Combine(root, "paged-library-data")));
        library.SetSection("all");
        var window = OffscreenWindow("ShuiMan pagination integration", library);
        try
        {
            window.Show();
            await library.ImportPathsAsync([source]);
            library.SetSection("all");
            var sort = (ComboBox)library.FindName("SortBox");
            sort.SelectedItem = sort.Items.OfType<ComboBoxItem>().Single(item => item.Tag?.ToString() == "title");
            window.UpdateLayout();
            Check("a real 53-book import only creates 48 cover cards on its first page", library.TotalFilteredBooks == 53 && library.VisibleCards.Count == 48 && library.PageIndex == 0 && ((ItemsControl)library.FindName("BookGrid")).Items.Count == 48);
            Check("the title sorting control uses natural volume order", library.VisibleBooks.Take(3).Select(book => book.Title).SequenceEqual(["Volume 1", "Volume 2", "Volume 3"]) && library.VisibleBooks.Last().Title == "Volume 48");
            ((Button)library.FindName("NextPage")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await library.CoversReady.WaitAsync(TimeSpan.FromSeconds(30));
            window.UpdateLayout();
            Check("the next-page button shows the final five books with decoded covers", library.PageIndex == 1 && library.VisibleCards.Count == 5 && library.VisibleBooks.First().Title == "Volume 49" && library.VisibleBooks.Last().Title == "Volume 53" && library.VisibleCards.All(card => HasArtwork(card.Cover)));
            ((Button)library.FindName("PreviousPage")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check("the previous-page button returns to the original first 48 books", library.PageIndex == 0 && library.VisibleBooks.Count == 48 && library.VisibleBooks.First().Title == "Volume 1");
        }
        finally { library.Dispose(); window.Close(); }
    }

    private static async Task LibraryStateRaceChecks(string root, string sourcePath)
    {
        var data = Path.Combine(root, "interleaved-library-state");
        var readerStore = new LibraryStore(data);
        var metadataStore = new LibraryStore(data);
        var id = LibraryScanner.IdentityForPath(sourcePath);
        readerStore.Save(new SavedBook { Id = id, Path = sourcePath, Title = "原书名", Series = "原系列", Total = 4 });
        var staleReader = readerStore.Books.Single();
        staleReader.Position = 2; staleReader.LocatorKey = "0:0:002.png"; staleReader.ReadState = "在读";
        staleReader.Preferences.Direction = "rtl";
        staleReader.Bookmarks.Add("0:0:001.png");
        metadataStore.UpdateMetadata(id, title: "另一窗口修改的书名", series: "新的系列", tags: ["已整理"], favorite: true, readState: "已读");
        var merged = readerStore.SaveReadingState(staleReader);
        Check("an older reader snapshot preserves metadata and manual read state edited in another window", merged is { Title: "另一窗口修改的书名", Series: "新的系列", Favorite: true, ReadState: "已读", Position: 2 } && merged.Tags.SequenceEqual(["已整理"]) && merged.Preferences.Direction == "rtl" && merged.Bookmarks.Contains("0:0:001.png"));

        metadataStore.ImportDiscovered([new SavedBook { Id = id, Path = sourcePath, Title = "扫描得到的文件名", Series = "磁盘目录", Revision = "new-disk-revision" }]);
        var reimported = new LibraryStore(data).Books.Single();
        Check("reimporting a changed source preserves metadata, page locator, bookmarks and reader settings", reimported.Title == merged.Title && reimported.Tags.SequenceEqual(merged.Tags) && reimported is { Position: 2, Favorite: true, ReadState: "已读", Revision: "new-disk-revision" } && reimported.LocatorKey == merged.LocatorKey && reimported.Preferences.Direction == "rtl" && reimported.Bookmarks.SetEquals(merged.Bookmarks));

        var concurrentSnapshot = readerStore.Books.Single();
        concurrentSnapshot.Position = 3;
        concurrentSnapshot.ReadState = "在读";
        using var start = new ManualResetEventSlim(false);
        var reading = Task.Run(() => { start.Wait(); readerStore.SaveReadingState(concurrentSnapshot); });
        var editing = Task.Run(() => { start.Wait(); metadataStore.UpdateMetadata(id, title: "并发编辑后的书名", favorite: false, readState: "未读"); });
        start.Set();
        await Task.WhenAll(reading, editing);
        var concurrent = new LibraryStore(data).Books.Single();
        Check("concurrent same-book progress and metadata saves keep both changes", concurrent is { Position: 3, Title: "并发编辑后的书名", Favorite: false, ReadState: "未读" } && concurrent.Tags.SequenceEqual(["已整理"]));
    }

    private static async Task MainWindowLibraryChecks(string data, string sourcePath)
    {
        var id = LibraryScanner.IdentityForPath(sourcePath);
        var store = new LibraryStore(data);
        var state = store.Books.Single(book => book.Id == id);
        state.Preferences.AutomaticOrientation = false;
        state.Preferences.AutomaticPairs = false;
        state.Preferences.SmartSpreads = false;
        store.Save(state);
        var reader = NewMainWindow(data);
        try
        {
            reader.Show();
            var library = (LibraryView)reader.FindName("LibraryHome");
            await library.RefreshAsync();
            await library.CoversReady.WaitAsync(TimeSpan.FromSeconds(30));
            reader.UpdateLayout();
            OpenCover(library, id);
            var pageNumber = (TextBox)reader.FindName("PageNumber");
            var pageTotal = (TextBlock)reader.FindName("PageTotal");
            await WaitUntil(() => pageNumber.IsVisible && pageTotal.Text.Contains("4"), "cover click opens the reader");
            Check("clicking a real library cover opens the corresponding reader", ((TextBlock)reader.FindName("BookTitle")).Text == state.Title && !library.IsVisible);
            pageNumber.Text = "2";
            pageNumber.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(pageNumber), Environment.TickCount, Key.Enter)
            {
                RoutedEvent = Keyboard.KeyDownEvent
            });
            await WaitUntil(() => store.Books.Single(book => book.Id == id).Position == 2, "page navigation persists progress");
            ((Button)reader.FindName("BackToLibraryButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntil(() => library.IsVisible && library.VisibleBooks.Any(book => book.Id == id && book.Position == 2), "returning to library refreshes progress");
            Check("returning to library shows saved reading progress and keeps metadata", library.VisibleBooks.Single(book => book.Id == id) is { Position: 2, Favorite: true, Title: "海的另一边（珍藏）" });
        }
        finally { await CloseWindow(reader); }

        var reopened = NewMainWindow(data);
        try
        {
            reopened.Show();
            var library = (LibraryView)reopened.FindName("LibraryHome");
            await library.RefreshAsync();
            await library.CoversReady.WaitAsync(TimeSpan.FromSeconds(30));
            reopened.UpdateLayout();
            OpenCover(library, id);
            var pageNumber = (TextBox)reopened.FindName("PageNumber");
            await WaitUntil(() => pageNumber.IsVisible && pageNumber.Text == "2", "reopened application resumes the saved page");
            Check("a new application window resumes the selected book at page two", pageNumber.Text == "2" && ((TextBlock)reopened.FindName("BookTitle")).Text == state.Title);
        }
        finally { await CloseWindow(reopened); }
    }

    private static void OpenCover(LibraryView library, string id)
    {
        library.SetSearch(""); library.SetSection("all"); library.SetListMode(false); library.UpdateLayout();
        var button = Descendants<Button>((DependencyObject)library.FindName("BookGrid"))
            .FirstOrDefault(candidate => candidate.DataContext is LibraryBookCard card && card.Book?.Id == id && AutomationProperties.GetName(candidate) == card.Title)
            ?? throw new InvalidOperationException("The target library cover button was not rendered.");
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    private static Button FavoriteButton(LibraryView library, string id) =>
        Descendants<Button>((DependencyObject)library.FindName("BookGrid"))
            .FirstOrDefault(candidate => candidate.DataContext is LibraryBookCard card && card.Book?.Id == id && candidate.ToolTip?.ToString() == card.FavoriteHint)
        ?? throw new InvalidOperationException("The target library favorite button was not rendered.");

    private static MainWindow NewMainWindow(string data) => new(data, null)
    {
        WindowStartupLocation = WindowStartupLocation.Manual,
        Left = -12000, Top = -12000, ShowActivated = false, ShowInTaskbar = false
    };

    private static Window OffscreenWindow(string title, object content) => new()
    {
        Title = title, Width = 1200, Height = 850, Content = content,
        Left = -12000, Top = -12000, ShowActivated = false, ShowInTaskbar = false
    };

    private static async Task CloseWindow(Window window)
    {
        if (!window.IsLoaded) return;
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        window.Closed += (_, _) => closed.TrySetResult();
        window.Close();
        await closed.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    private static async Task WaitUntil(Func<bool> predicate, string description)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            if (DispatcherErrors.Count != 0) throw new AggregateException(description, DispatcherErrors);
            if (predicate()) return;
            await Task.Delay(80);
        }
        throw new TimeoutException(description);
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T found) yield return found;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private static bool HasArtwork(BitmapSource? bitmap)
    {
        if (bitmap is null || bitmap.PixelWidth < 40 || bitmap.PixelHeight < 60) return false;
        var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[converted.PixelWidth * converted.PixelHeight * 4];
        converted.CopyPixels(pixels, converted.PixelWidth * 4, 0);
        var samples = new HashSet<int>();
        for (var offset = 0; offset < pixels.Length; offset += 4 * 61)
            samples.Add((pixels[offset] << 16) | (pixels[offset + 1] << 8) | pixels[offset + 2]);
        return samples.Count > 6;
    }
}

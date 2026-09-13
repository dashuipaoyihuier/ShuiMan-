using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ShuiMan.Core;
using ShuiMan.Windows;

namespace ShuiMan.UiChecks;

internal static partial class Program
{
    private static async Task BackgroundReaderChecks(string root)
    {
        var shortBook = Path.Combine(root, "background-six-pages.epub");
        GenerateBackgroundBook(shortBook, 6);
        var data = Path.Combine(root, "background-reader-data");
        Publication publication;
        using (var book = await DocumentEngine.OpenAsync(shortBook)) publication = book.Publication;
        var cache = new AnalysisCache(Path.Combine(data, "analysis"));
        var reader = BackgroundWindow(data, shortBook);
        BookAnalysisSnapshot completed;
        DateTime cacheWritten;
        byte[] cacheHash;
        string cachePath;
        bool cjkAvailable = GlyphOrientationAnalyzer.AvailableLanguages.Any(language => language.StartsWith("zh", StringComparison.OrdinalIgnoreCase) || language.StartsWith("ja", StringComparison.OrdinalIgnoreCase));
        try
        {
            reader.Show();
            await WaitReaderPage(reader, 1, 6);
            await WaitUntil(() => ((TextBlock)reader.FindName("AnalysisStatusText")).Text.Contains("整本分析完成"), "the reader finishes the entire book while remaining on its first page");
            await ReaderField<Task>(reader, "_analysisTask").WaitAsync(TimeSpan.FromSeconds(10));
            completed = await cache.LoadAsync(publication);
            Check("a real reader finishes every page and adjacent pair while the user stays on page one", ((TextBox)reader.FindName("PageNumber")).Text == "1" && completed.IsComplete && completed.CompletedPages.Count == 6 && completed.CompletedPairs.Count == 5);
            Check("whole-book background work preserves the displayed reading position", new LibraryStore(data).Books.Single(book => book.Id == publication.Identity).Position == 1);
            Check("background analysis discovers a real generated continuous-image spread outside the displayed page", completed.Pairs[1].Automatic && completed.Decisions.ContainsKey(publication.Units[5].Id));

            await JumpReaderPage(reader, 2, 6);
            var spread = ReaderField<DisplayGroup>(reader, "_displayGroup");
            var images = ReaderField<List<BitmapSource>>(reader, "_displayImages");
            Check("turning to an analyzed spread immediately draws both real source images with automatic grouping", spread.Spread && spread.Indices.SequenceEqual([1, 2]) && images.Count == 2 && ((TextBlock)reader.FindName("StatusText")).Text.Contains("自动"));
            await JumpReaderPage(reader, 6, 6);
            images = ReaderField<List<BitmapSource>>(reader, "_displayImages");
            if (cjkAvailable)
                Check("an undisplayed sideways final page is already corrected when first navigated to", completed.Decisions[publication.Units[5].Id].Rotation == 270 && images.Count == 1 && images[0].PixelHeight > images[0].PixelWidth);
            else Console.WriteLine("SKIP UI final-page CJK correction: Windows Chinese/Japanese OCR capability is not installed.");
            cachePath = Path.Combine(data, "analysis", completed.CacheKey + ".json");
            cacheWritten = File.GetLastWriteTimeUtc(cachePath);
            cacheHash = SHA256.HashData(File.ReadAllBytes(cachePath));
        }
        finally { await CloseWindow(reader); }

        var reopened = BackgroundWindow(data, shortBook);
        try
        {
            reopened.Show();
            await WaitReaderPage(reopened, 6, 6);
            await WaitUntil(() => ((TextBlock)reopened.FindName("AnalysisStatusText")).Text.Contains("整本分析完成"), "a reopened reader publishes its completed automatic cache");
            await ReaderField<Task>(reopened, "_analysisTask").WaitAsync(TimeSpan.FromSeconds(10));
            Check("reopening a fully analyzed book reuses its durable cache without creating a new analysis checkpoint", File.GetLastWriteTimeUtc(cachePath) == cacheWritten && SHA256.HashData(File.ReadAllBytes(cachePath)).SequenceEqual(cacheHash));
            if (cjkAvailable)
            {
                var images = ReaderField<List<BitmapSource>>(reopened, "_displayImages");
                Check("a reopened reader displays the corrected saved page directly from cached orientation", images.Count == 1 && images[0].PixelHeight > images[0].PixelWidth && ((TextBox)reopened.FindName("PageNumber")).Text == "6");
            }
        }
        finally { await CloseWindow(reopened); }

        var longBook = Path.Combine(root, "background-cancellation-book.epub");
        GenerateBackgroundBook(longBook, 96);
        Publication longPublication;
        // Close this diagnostic source before checking that reader ownership releases the source handle.
        using (var source = await DocumentEngine.OpenAsync(longBook)) longPublication = source.Publication;
        await BackgroundSwitchCheck(root, longBook, longPublication, shortBook);
        await BackgroundCloseCheck(root, longBook, longPublication);
        await BackgroundReanalysisSwitchCheck(root, data, shortBook);
        await BackgroundDrawingCompletionCheck(data, shortBook);
        await BackgroundIncrementalCheck(data, shortBook, rotation: false);
        if (cjkAvailable) await BackgroundIncrementalCheck(data, shortBook, rotation: true);
    }

    private static async Task BackgroundReanalysisSwitchCheck(string root, string data, string initialBook)
    {
        var target = Path.Combine(root, "background-reanalysis-target.epub");
        GenerateBackgroundBook(target, 6);
        var reader = BackgroundWindow(data, initialBook);
        var pendingOldAnalysis = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            reader.Show();
            await WaitReaderPage(reader, 6, 6);
            await ReaderField<Task>(reader, "_analysisTask").WaitAsync(TimeSpan.FromSeconds(10));
            SetReaderField(reader, "_analysisTask", pendingOldAnalysis.Task);
            var settings = ItemsControl.ItemsControlFromItemContainer((MenuItem)reader.FindName("SmartMenu"));
            var reanalyze = settings.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "重新分析整本漫画"));
            // Observe completion of the real async-void menu handler, including both await boundaries.
            // The gate deliberately outlives the old book, as a slow OCR/checkpoint can in production.
            var menuFinished = RaiseBackgroundMenu(reanalyze);
            if (menuFinished.IsCompleted) throw new InvalidOperationException("Reanalysis must await the controlled old worker before switching books.");
            await ((Task)typeof(MainWindow).GetMethod("OpenAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(reader, [target])!).WaitAsync(TimeSpan.FromSeconds(15));
            await WaitReaderPage(reader, 1, 6);
            await WaitUntil(() => ((TextBlock)reader.FindName("AnalysisStatusText")).Text.Contains("整本分析完成"), "the replacement book completes its own background worker before old reanalysis resumes");
            var targetWorker = ReaderField<Task>(reader, "_analysisTask");
            await targetWorker.WaitAsync(TimeSpan.FromSeconds(10));
            var targetToken = ReaderField<CancellationTokenSource>(reader, "_analysisCancellation").Token;
            var publication = ReaderField<IOpenBook>(reader, "_book").Publication;
            var cache = new AnalysisCache(Path.Combine(data, "analysis"));
            var before = await cache.LoadAsync(publication);
            var path = Path.Combine(data, "analysis", before.CacheKey + ".json");
            var hash = SHA256.HashData(File.ReadAllBytes(path));
            var written = File.GetLastWriteTimeUtc(path);

            pendingOldAnalysis.SetResult();
            await menuFinished.WaitAsync(TimeSpan.FromSeconds(10));
            var decisions = ReaderField<Dictionary<string, SpreadDecision>>(reader, "_decisions");
            var pairs = ReaderField<Dictionary<int, PairDecision>>(reader, "_pairs");
            Check("a delayed reanalysis menu request cannot restart or erase the replacement book's worker and completed cache",
                ReferenceEquals(targetWorker, ReaderField<Task>(reader, "_analysisTask")) && !targetToken.IsCancellationRequested &&
                before.IsComplete && decisions.Count == before.Decisions.Count && before.Decisions.All(item => decisions.GetValueOrDefault(item.Key) == item.Value) &&
                pairs.Count == before.Pairs.Count && before.Pairs.All(item => pairs.GetValueOrDefault(item.Key) == item.Value) &&
                File.Exists(path) && File.GetLastWriteTimeUtc(path) == written && SHA256.HashData(File.ReadAllBytes(path)).SequenceEqual(hash));
        }
        finally { pendingOldAnalysis.TrySetResult(); await CloseWindow(reader); }
    }

    private static async Task BackgroundDrawingCompletionCheck(string data, string initialBook)
    {
        var reader = BackgroundWindow(data, initialBook);
        BackgroundGatedBook? gated = null;
        try
        {
            reader.Show();
            await WaitReaderPage(reader, 6, 6);
            await ReaderField<Task>(reader, "_analysisTask").WaitAsync(TimeSpan.FromSeconds(10));
            gated = new BackgroundGatedBook(ReaderField<IOpenBook>(reader, "_book"), 1);
            SetReaderField(reader, "_book", gated);
            ReaderField<Dictionary<string, SpreadDecision>>(reader, "_decisions").Clear();
            ReaderField<Dictionary<int, PairDecision>>(reader, "_pairs").Clear();
            // Hold the actual foreground render after its one-page grouping/signature has been selected.
            var render = (Task)typeof(MainWindow).GetMethod("ShowPageAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(reader, [1])!;
            await gated.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            typeof(MainWindow).GetMethod("StartBookAnalysis", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(reader, [gated]);
            await ReaderField<Task>(reader, "_analysisTask").WaitAsync(TimeSpan.FromSeconds(10));
            await WaitUntil(() => ReaderField<Dictionary<int, PairDecision>>(reader, "_pairs").GetValueOrDefault(1)?.Automatic == true &&
                ((TextBlock)reader.FindName("AnalysisStatusText")).Text.Contains("整本分析完成"), "actual completed analysis is delivered while the foreground image remains gated");
            if (render.IsCompleted || ReaderField<int>(reader, "_foregroundReaders") == 0)
                throw new InvalidOperationException("The final analysis result must arrive before the controlled foreground draw completes.");
            gated.Release.TrySetResult();
            await render.WaitAsync(TimeSpan.FromSeconds(10));
            await WaitReaderPage(reader, 2, 6);
            await WaitUntil(() => ReaderField<DisplayGroup>(reader, "_displayGroup") is { Spread: true } &&
                ReaderField<List<BitmapSource>>(reader, "_displayImages").Count == 2 && !((FrameworkElement)reader.FindName("BusyBanner")).IsVisible,
                "analysis arriving during a draw is applied automatically after foreground drawing finishes");
            await WaitReaderPage(reader, 2, 6);
            var displayed = ReaderField<DisplayGroup>(reader, "_displayGroup");
            Check("analysis finishing during a foreground draw automatically displays the completed spread without a user action",
                displayed.Spread && displayed.Indices.SequenceEqual([1, 2]) &&
                ReaderField<List<BitmapSource>>(reader, "_displayImages").Count == 2 && ((TextBox)reader.FindName("PageNumber")).Text == "2");
        }
        finally { gated?.Release.TrySetResult(); await CloseWindow(reader); }
    }

    private static void SetReaderField(MainWindow reader, string name, object value) =>
        typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(reader, value);

    private static async Task BackgroundIncrementalCheck(string data, string initialBook, bool rotation)
    {
        var reader = BackgroundWindow(data, initialBook);
        BackgroundProgressGate? paused = null;
        try
        {
            reader.Show();
            await WaitUntil(() => !((FrameworkElement)reader.FindName("BusyBanner")).IsVisible &&
                ((TextBlock)reader.FindName("PageTotal")).Text == " / 6", "incremental reader opens its six-page source");
            await ReaderField<Task>(reader, "_analysisTask").WaitAsync(TimeSpan.FromSeconds(20));
            var source = ReaderField<IOpenBook>(reader, "_book");
            await new AnalysisCache(Path.Combine(data, "analysis")).InvalidateAsync(source.Publication);
            ReaderField<Dictionary<string, SpreadDecision>>(reader, "_decisions").Clear();
            ReaderField<Dictionary<int, PairDecision>>(reader, "_pairs").Clear();
            int page = rotation ? 5 : 1;
            var draw = (Task)typeof(MainWindow).GetMethod("ShowPageAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(reader, [page])!;
            await draw.WaitAsync(TimeSpan.FromSeconds(10));
            var dispatcher = SynchronizationContext.Current ?? throw new InvalidOperationException("Expected the UI dispatcher context.");
            paused = new BackgroundProgressGate(dispatcher, update => rotation
                ? update.Decisions.ContainsKey(source.Publication.Units[page].Id) : update.Pairs.ContainsKey(2));
            SynchronizationContext.SetSynchronizationContext(paused);
            try { typeof(MainWindow).GetMethod("StartBookAnalysis", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(reader, [source]); }
            finally { SynchronizationContext.SetSynchronizationContext(dispatcher); }
            await paused.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
            await WaitUntil(() =>
            {
                var images = ReaderField<List<BitmapSource>>(reader, "_displayImages");
                return !((FrameworkElement)reader.FindName("BusyBanner")).IsVisible && (rotation
                    ? images.Count == 1 && images[0].PixelHeight > images[0].PixelWidth
                    : images.Count == 2 && ReaderField<DisplayGroup>(reader, "_displayGroup").Spread);
            }, "completed page evidence updates the displayed pixels while later analysis is still blocked");
            Check(rotation
                    ? "a newly recognized sideways page visibly rotates before the unfinished book scan completes"
                    : "a newly recognized spread visibly joins before the unfinished book scan completes",
                !ReaderField<Task>(reader, "_analysisTask").IsCompleted &&
                !((TextBlock)reader.FindName("AnalysisStatusText")).Text.Contains("整本分析完成") &&
                ((TextBox)reader.FindName("PageNumber")).Text == (page + 1).ToString(CultureInfo.InvariantCulture));
        }
        finally { paused?.Release.TrySetResult(); await CloseWindow(reader); }
    }

    // Pause the worker between progress delivery and its next page, outside document I/O.
    // The real dispatcher still handles the actual incremental page result and drawing.
    private sealed class BackgroundProgressGate(SynchronizationContext dispatcher, Func<BookAnalysisProgress, bool> pause) : SynchronizationContext
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int remaining = 1;
        public override void Post(SendOrPostCallback callback, object? state)
        {
            dispatcher.Post(callback, state);
            if (state is BookAnalysisProgress update && pause(update) && Interlocked.Exchange(ref remaining, 0) == 1)
            { Entered.TrySetResult(); Release.Task.GetAwaiter().GetResult(); }
        }
    }

    private static Task RaiseBackgroundMenu(MenuItem item)
    {
        var previous = SynchronizationContext.Current ?? throw new InvalidOperationException("UI checks require the dispatcher context.");
        var context = new BackgroundEventContext(previous);
        SynchronizationContext.SetSynchronizationContext(context);
        try { item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
        return context.Completed.Task;
    }

    private sealed class BackgroundEventContext(SynchronizationContext dispatcher) : SynchronizationContext
    {
        public TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int active;
        public override void OperationStarted() => Interlocked.Increment(ref active);
        public override void OperationCompleted() { if (Interlocked.Decrement(ref active) == 0) Completed.TrySetResult(); }
        public override SynchronizationContext CreateCopy() => this;
        public override void Post(SendOrPostCallback callback, object? state) => dispatcher.Post(_ =>
        {
            var previous = Current;
            SetSynchronizationContext(this);
            try { callback(state); }
            finally { SetSynchronizationContext(previous); }
        }, null);
    }

    private sealed class BackgroundGatedBook(IOpenBook source, int page) : IOpenBook
    {
        public Publication Publication => source.Publication;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int remaining = 1;
        public async Task<BitmapSource> RenderAsync(int index, int maxEdge = 2400, CancellationToken cancellationToken = default)
        {
            if (index == page && maxEdge >= 3200 && Interlocked.Exchange(ref remaining, 0) == 1)
            {
                Entered.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }
            return await source.RenderAsync(index, maxEdge, cancellationToken);
        }
        public byte[]? Resource(string path) => source.Resource(path);
        public void Dispose() => source.Dispose();
    }

    private static async Task BackgroundSwitchCheck(string root, string longBook, Publication publication, string target)
    {
        var data = Path.Combine(root, "background-switch-data");
        var reader = BackgroundWindow(data, longBook);
        try
        {
            reader.Show();
            await WaitReaderPage(reader, 1, 96);
            await WaitUntil(() => ReaderField<Dictionary<string, SpreadDecision>>(reader, "_decisions").Count >= 2, "the old book has real partial background progress before switching");
            var oldTask = ReaderField<Task>(reader, "_analysisTask");
            var oldToken = ReaderField<CancellationTokenSource>(reader, "_analysisCancellation").Token;
            bool wasRunning = !oldTask.IsCompleted;
            var open = typeof(MainWindow).GetMethod("OpenAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            await ((Task)open.Invoke(reader, [target])!).WaitAsync(TimeSpan.FromSeconds(15));
            await WaitReaderPage(reader, 1, 6);
            await oldTask.WaitAsync(TimeSpan.FromSeconds(10));
            Check("switching books cancels an active whole-book worker and leaves the new reader usable", wasRunning && oldToken.IsCancellationRequested && oldTask.IsCompleted && ((TextBlock)reader.FindName("PageTotal")).Text.Contains("6"));
            var partial = await new AnalysisCache(Path.Combine(data, "analysis")).LoadAsync(publication);
            Check("switching books saves completed automatic work without falsely completing the old book", partial.CompletedPages.Count >= 2 && partial.CompletedPages.Count < 96 && !partial.IsComplete);
            using var exclusive = new FileStream(longBook, FileMode.Open, FileAccess.Read, FileShare.None);
            Check("switching books releases the old publication source handle", exclusive.Length > 0);
        }
        finally { await CloseWindow(reader); }
    }

    private static async Task BackgroundCloseCheck(string root, string longBook, Publication publication)
    {
        var data = Path.Combine(root, "background-close-data");
        var reader = BackgroundWindow(data, longBook);
        Task? analysis = null;
        CancellationToken token = default;
        bool wasRunning = false;
        try
        {
            reader.Show();
            await WaitReaderPage(reader, 1, 96);
            await WaitUntil(() => ReaderField<Dictionary<string, SpreadDecision>>(reader, "_decisions").Count >= 2, "the closing book has checkpointable background progress");
            analysis = ReaderField<Task>(reader, "_analysisTask");
            token = ReaderField<CancellationTokenSource>(reader, "_analysisCancellation").Token;
            wasRunning = !analysis.IsCompleted;
        }
        finally { await CloseWindow(reader); }
        if (analysis != null) await analysis.WaitAsync(TimeSpan.FromSeconds(10));
        var partial = await new AnalysisCache(Path.Combine(data, "analysis")).LoadAsync(publication);
        Check("a single close cancels and joins active whole-book analysis without hanging the window", wasRunning && token.IsCancellationRequested && analysis?.IsCompleted == true && !reader.IsVisible);
        Check("closing a reader preserves its partial automatic cache and actual reading position", partial.CompletedPages.Count >= 2 && partial.CompletedPages.Count < 96 && new LibraryStore(data).Books.Single(book => book.Id == publication.Identity).Position == 1);
    }

    private static MainWindow BackgroundWindow(string data, string initialBook) => new(data, initialBook)
    { WindowStartupLocation = WindowStartupLocation.Manual, Left = -12000, Top = -12000, ShowActivated = false, ShowInTaskbar = false };

    private static T ReaderField<T>(MainWindow reader, string name) => (T)(typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(reader)
        ?? throw new InvalidOperationException($"Reader field {name} is unavailable."));

    private static Task WaitReaderPage(MainWindow reader, int page, int total) => WaitUntil(() =>
        ((TextBox)reader.FindName("PageNumber")).Text == page.ToString(CultureInfo.InvariantCulture) &&
        ((TextBlock)reader.FindName("PageTotal")).Text == $" / {total}" &&
        ((FrameworkElement)reader.FindName("Surface")).IsVisible && !((FrameworkElement)reader.FindName("BusyBanner")).IsVisible,
        $"the real reader finishes displaying page {page} of {total}");

    private static async Task JumpReaderPage(MainWindow reader, int page, int total)
    {
        var input = (TextBox)reader.FindName("PageNumber");
        input.Text = page.ToString(CultureInfo.InvariantCulture);
        input.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(input), Environment.TickCount, Key.Enter) { RoutedEvent = Keyboard.KeyDownEvent });
        await WaitReaderPage(reader, page, total);
    }

    private static void GenerateBackgroundBook(string path, int pages)
    {
        var dialogue = BackgroundDialogue();
        var sideways = new TransformedBitmap(dialogue, new RotateTransform(90)); sideways.Freeze();
        var images = pages == 6 ? new[] { BackgroundPlain(Colors.SlateBlue), BackgroundSeam(false), BackgroundSeam(true), BackgroundPlain(Colors.Silver), BackgroundPlain(Colors.SteelBlue), sideways }
            : new[] { BackgroundPlain(Colors.SlateBlue), dialogue };
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        void Add(string name, byte[] bytes) { using var output = zip.CreateEntry(name).Open(); output.Write(bytes); }
        void Text(string name, string value) => Add(name, Encoding.UTF8.GetBytes(value));
        Text("META-INF/container.xml", "<container xmlns='urn:oasis:names:tc:opendocument:xmlns:container'><rootfiles><rootfile full-path='OPS/book.opf'/></rootfiles></container>");
        var manifest = new StringBuilder("<item id='cover' href='image-0.png' media-type='image/png' properties='cover-image'/>");
        for (int index = 1; index < images.Length; index++) manifest.Append($"<item id='image-{index}' href='image-{index}.png' media-type='image/png'/>");
        var spine = new StringBuilder();
        for (int index = 0; index < pages; index++)
        {
            manifest.Append($"<item id='page-{index}' href='page-{index}.xhtml' media-type='application/xhtml+xml'/>");
            spine.Append($"<itemref idref='page-{index}'/>");
            bool explicitDirection = pages == 6 ? index < 5 : index == 0;
            int image = pages == 6 ? index : index == 0 ? 0 : 1;
            Text($"OPS/page-{index}.xhtml", $"<html><body><img {(explicitDirection ? "style='transform:rotate(0deg)'" : "")} src='image-{image}.png'/></body></html>");
        }
        Text("OPS/book.opf", $"<package xmlns='http://www.idpf.org/2007/opf' version='3.0'><manifest>{manifest}</manifest><spine>{spine}</spine></package>");
        for (int index = 0; index < images.Length; index++)
        {
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(images[index]));
            using var output = new MemoryStream(); encoder.Save(output); Add($"OPS/image-{index}.png", output.ToArray());
        }
    }

    private static BitmapSource BackgroundPlain(Color color)
    {
        var pixels = new byte[600 * 900 * 4];
        for (int i = 0; i < pixels.Length; i += 4) { pixels[i] = color.B; pixels[i + 1] = color.G; pixels[i + 2] = color.R; pixels[i + 3] = 255; }
        var bitmap = BitmapSource.Create(600, 900, 96, 96, PixelFormats.Bgra32, null, pixels, 600 * 4); bitmap.Freeze(); return bitmap;
    }

    private static BitmapSource BackgroundSeam(bool right)
    {
        const int width = 768, height = 1024;
        var pixels = new byte[width * height];
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
        {
            double phase = (x + (right ? width : 0)) * .008;
            double value = Math.Clamp(.50 + .17 * Math.Sin(y * .049 + phase) + .15 * Math.Sin(y * .117 + phase * .8) + .13 * Math.Cos(y * .189 - phase * 1.2), 0, 1);
            pixels[y * width + x] = (byte)Math.Round(value * 255);
        }
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Gray8, null, pixels, width); bitmap.Freeze(); return bitmap;
    }

    private static BitmapSource BackgroundDialogue()
    {
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(new SolidColorBrush(Color.FromRgb(83, 104, 115)), null, new Rect(0, 0, 900, 1200));
            for (int region = 0; region < 2; region++)
            {
                var balloon = new Rect(90 + region * 400, 110 + region * 490, 265, 360);
                drawing.DrawRoundedRectangle(Brushes.White, new Pen(Brushes.Black, 3), balloon, 65, 65);
                string text = region == 0 ? "明天我们继续出发" : "一起发现远方故事";
                for (int i = 0; i < text.Length; i++)
                    drawing.DrawText(new FormattedText(text[i].ToString(), CultureInfo.GetCultureInfo("zh-CN"), FlowDirection.LeftToRight,
                        new Typeface("Microsoft YaHei"), 43, Brushes.Black, 1),
                        new Point(balloon.X + 163 - i / 4 * 66, balloon.Y + 51 + i % 4 * 62));
            }
        }
        var bitmap = new RenderTargetBitmap(900, 1200, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual); bitmap.Freeze(); return bitmap;
    }
}

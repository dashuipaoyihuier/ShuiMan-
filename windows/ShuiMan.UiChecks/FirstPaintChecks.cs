using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Reflection;
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
    private static async Task FirstPaintChecks(string root)
    {
        var source = Path.Combine(root, "first-paint-original.epub");
        GenerateFirstPaintBook(source);
        Publication publication;
        using (var book = await DocumentEngine.OpenAsync(source)) publication = book.Publication;
        var data = Path.Combine(root, "first-paint-reader-data");
        SaveFirstPaintPosition(data, source, publication);
        var reader = FirstPaintWindow(data);
        var gate = new FirstPaintProgressGate(SynchronizationContext.Current!, publication,
            () => ReaderField<CancellationTokenSource>(reader, "_analysisCancellation").Token,
            (_, update) => update.CompletedPages == 0,
            (pages, _) => Enumerable.Range(2, 6).All(pages.Contains),
            (pages, _) => Enumerable.Range(28, 6).All(pages.Contains));
        var frames = new List<(int[] Indices, int Images, bool Spread)>();
        var surface = (PageSurface)reader.FindName("Surface");
        surface.IsVisibleChanged += (_, _) =>
        {
            if (!surface.IsVisible || typeof(MainWindow).GetField("_displayGroup", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(reader) is not DisplayGroup group) return;
            var images = ReaderField<List<BitmapSource>>(reader, "_displayImages");
            if (images.Count > 0) frames.Add(([.. group.Indices], images.Count, group.Spread));
        };
        try
        {
            reader.Show();
            await WaitUntil(() => !((FrameworkElement)reader.FindName("BusyBanner")).IsVisible, "the empty first-paint test window is ready");
            var opening = WithFirstPaintContext(gate, () => OpenFirstPaintBook(reader, source));
            await gate.Points[0].Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await WaitUntil(() => !surface.IsVisible, "the reader keeps its initial current page hidden while seam evidence is pending");
            Check("first paint waits for current-page seam evidence without holding document I/O or starving analysis",
                !opening.IsCompleted && frames.Count == 0 && ReaderField<int>(reader, "_foregroundReaders") == 0 &&
                ReaderField<SemaphoreSlim>(reader, "_io").CurrentCount == 1);
            gate.Points[0].Release.TrySetResult();
            await gate.Points[1].Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            await opening.WaitAsync(TimeSpan.FromSeconds(10));
            await WaitReaderPage(reader, 5, 48);
            Check("the first visible current-page image is already a complete spread before the whole book finishes",
                frames.Count > 0 && frames[0].Indices.SequenceEqual([4, 5]) && frames[0] is { Images: 2, Spread: true } &&
                !ReaderField<Task>(reader, "_analysisTask").IsCompleted && gate.SeenPages.Count < 48 &&
                CaptureSurface(surface).Bounds.Width > CaptureSurface(surface).Bounds.Height);
            var initialGroup = ReaderField<DisplayGroup>(reader, "_displayGroup");
            Check("automatic seam alignment starts disabled while ordinary intelligent pairing remains active",
                !new ReaderPreferences().AutomaticSeamAlignment && !((MenuItem)reader.FindName("SeamAlignmentMenu")).IsChecked &&
                !new LibraryStore(data).Books.Single().Preferences.AutomaticSeamAlignment &&
                initialGroup is { Spread: true, VerticalOffset: 0, RightScale: 1 });
            var alignment = (MenuItem)reader.FindName("SeamAlignmentMenu");
            alignment.IsChecked = true;
            await RaiseBackgroundMenu(alignment).WaitAsync(TimeSpan.FromSeconds(10));
            bool enabledSaved = new LibraryStore(data).Books.Single().Preferences.AutomaticSeamAlignment;
            alignment.IsChecked = false;
            await RaiseBackgroundMenu(alignment).WaitAsync(TimeSpan.FromSeconds(10));
            var alignmentState = new LibraryStore(data).Books.Single();
            Check("the actual seam-alignment menu explicitly enables and disables its independent persisted preference",
                enabledSaved && !alignmentState.Preferences.AutomaticSeamAlignment && alignmentState.Preferences.AutomaticPairs &&
                alignmentState.Overrides.Count == 0 && ReaderField<DisplayGroup>(reader, "_displayGroup") is { Spread: true, VerticalOffset: 0, RightScale: 1 });

            int observations = gate.ObservedOrder.Count;
            int earlierFrames = frames.Count;
            var jump = FirstPaintJump(reader, 31);
            await WaitUntil(() => !surface.IsVisible, "jumping to an unexamined distant page hides the pending picture");
            Check("a distant-page jump does not expose a provisional split page while its promoted neighborhood is pending",
                !jump.IsCompleted && frames.Count == earlierFrames && ReaderField<int>(reader, "_foregroundReaders") == 0);
            gate.Points[1].Release.TrySetResult();
            await gate.Points[2].Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            await jump.WaitAsync(TimeSpan.FromSeconds(10));
            await WaitReaderPage(reader, 31, 48);
            var newlyAnalyzed = gate.ObservedOrder.Skip(observations).ToArray();
            var distantFrames = frames.Skip(earlierFrames).ToArray();
            Check("jumping to an unexamined distant page promotes its real analysis ahead of the old scan tail",
                newlyAnalyzed.Length > 0 && newlyAnalyzed[0] == 30 && gate.SeenPages.Count < 20 && !ReaderField<Task>(reader, "_analysisTask").IsCompleted);
            Check("a promoted distant page first appears as the correctly ordered two-image spread",
                distantFrames.Length > 0 && distantFrames[0].Indices.SequenceEqual([30, 31]) && distantFrames[0] is { Images: 2, Spread: true } &&
                distantFrames.All(frame => frame.Images == 2 && frame.Spread));

            var waiting = FirstPaintJump(reader, 16);
            await WaitUntil(() => !surface.IsVisible, "the next unexamined page waits for evidence before a controlled book switch");
            var oldWorker = ReaderField<Task>(reader, "_analysisTask");
            var oldToken = ReaderField<CancellationTokenSource>(reader, "_analysisCancellation").Token;
            var replacement = Path.Combine(root, "first-paint-replacement.epub");
            GenerateBackgroundBook(replacement, 6);
            await OpenFirstPaintBook(reader, replacement).WaitAsync(TimeSpan.FromSeconds(15));
            await waiting.WaitAsync(TimeSpan.FromSeconds(10));
            await oldWorker.WaitAsync(TimeSpan.FromSeconds(10));
            await WaitReaderPage(reader, 1, 6);
            Check("switching books cancels a pending first-paint neighborhood wait without a document-I/O deadlock",
                oldToken.IsCancellationRequested && oldWorker.IsCompleted && ReaderField<IOpenBook>(reader, "_book").Publication.Units.Count == 6);
        }
        finally { gate.ReleaseAll(); await CloseWindow(reader); }
        await FirstPaintCloseCheck(root, source, publication);
    }

    private static async Task FirstPaintCloseCheck(string root, string source, Publication publication)
    {
        var data = Path.Combine(root, "first-paint-close-data");
        SaveFirstPaintPosition(data, source, publication);
        var reader = FirstPaintWindow(data);
        var gate = new FirstPaintProgressGate(SynchronizationContext.Current!, publication,
            () => ReaderField<CancellationTokenSource>(reader, "_analysisCancellation").Token,
            (_, update) => update.CompletedPages == 0);
        try
        {
            reader.Show();
            await WaitUntil(() => !((FrameworkElement)reader.FindName("BusyBanner")).IsVisible, "the first-paint cancellation window is ready");
            var opening = WithFirstPaintContext(gate, () => OpenFirstPaintBook(reader, source));
            await gate.Points[0].Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var analysis = ReaderField<Task>(reader, "_analysisTask");
            await CloseWindow(reader);
            try { await opening.WaitAsync(TimeSpan.FromSeconds(10)); } catch (OperationCanceledException) { }
            await analysis.WaitAsync(TimeSpan.FromSeconds(10));
            Check("closing during the initial seam check cancels both the display wait and worker after one close request",
                !reader.IsVisible && opening.IsCompleted && analysis.IsCompleted);
        }
        finally { gate.ReleaseAll(); if (reader.IsVisible) await CloseWindow(reader); }
    }

    private static MainWindow FirstPaintWindow(string data) => new(data, null)
    { WindowStartupLocation = WindowStartupLocation.Manual, Left = -12000, Top = -12000, ShowActivated = false, ShowInTaskbar = false };

    private static void SaveFirstPaintPosition(string data, string source, Publication publication) => new LibraryStore(data).Save(new SavedBook
    {
        Id = publication.Identity, Path = source, Total = publication.Units.Count, Position = 5,
        LocatorKey = publication.Units[4].Id, Preferences = new ReaderPreferences { Layout = "single" }
    });

    private static Task OpenFirstPaintBook(MainWindow reader, string source) =>
        (Task)typeof(MainWindow).GetMethod("OpenAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(reader, [source])!;

    private static Task WithFirstPaintContext(SynchronizationContext context, Func<Task> action)
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(context);
        try { return action(); }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }

    private static Task FirstPaintJump(MainWindow reader, int page)
    {
        var dispatcher = SynchronizationContext.Current!;
        var context = new BackgroundEventContext(dispatcher);
        var input = (TextBox)reader.FindName("PageNumber");
        input.Text = page.ToString(CultureInfo.InvariantCulture);
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            input.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(input), Environment.TickCount, Key.Enter)
            { RoutedEvent = Keyboard.KeyDownEvent });
        }
        finally { SynchronizationContext.SetSynchronizationContext(dispatcher); }
        return context.Completed.Task;
    }

    private sealed class FirstPaintProgressGate : SynchronizationContext
    {
        internal sealed class PausePoint(Func<IReadOnlySet<int>, BookAnalysisProgress, bool> condition)
        {
            public Func<IReadOnlySet<int>, BookAnalysisProgress, bool> Condition { get; } = condition;
            public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        private readonly SynchronizationContext dispatcher;
        private readonly Func<CancellationToken> cancellation;
        private readonly Dictionary<string, int> positions;
        private readonly HashSet<int> seen = [];
        private readonly List<int> order = [];
        private readonly object sync = new();
        private int nextPoint;
        public PausePoint[] Points { get; }
        public IReadOnlySet<int> SeenPages { get { lock (sync) return seen.ToHashSet(); } }
        public IReadOnlyList<int> ObservedOrder { get { lock (sync) return order.ToArray(); } }
        public FirstPaintProgressGate(SynchronizationContext dispatcher, Publication publication, Func<CancellationToken> cancellation,
            params Func<IReadOnlySet<int>, BookAnalysisProgress, bool>[] conditions)
        {
            this.dispatcher = dispatcher; this.cancellation = cancellation;
            positions = publication.Units.Select((unit, index) => (unit.Id, index)).ToDictionary(item => item.Id, item => item.index);
            Points = conditions.Select(condition => new PausePoint(condition)).ToArray();
        }
        public override SynchronizationContext CreateCopy() => this;
        public override void Post(SendOrPostCallback callback, object? state)
        {
            PausePoint? point = null;
            if (state is BookAnalysisProgress update)
                lock (sync)
                {
                    foreach (var id in update.Decisions.Keys)
                        if (positions.TryGetValue(id, out var index) && seen.Add(index)) order.Add(index);
                    if (nextPoint < Points.Length && Points[nextPoint].Condition(seen, update)) point = Points[nextPoint++];
                }
            dispatcher.Post(_ =>
            {
                var previous = Current; SetSynchronizationContext(this);
                try { callback(state); }
                finally { SetSynchronizationContext(previous); }
            }, null);
            if (point != null)
            {
                var token = cancellation();
                point.Entered.TrySetResult();
                point.Release.Task.WaitAsync(token).GetAwaiter().GetResult();
            }
        }
        public void ReleaseAll() { foreach (var point in Points) point.Release.TrySetResult(); }
    }

    private static void GenerateFirstPaintBook(string path)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        void Text(string name, string text) { using var output = new StreamWriter(zip.CreateEntry(name).Open(), Encoding.UTF8); output.Write(text); }
        Text("META-INF/container.xml", "<container xmlns='urn:oasis:names:tc:opendocument:xmlns:container'><rootfiles><rootfile full-path='OPS/book.opf'/></rootfiles></container>");
        var images = new[] { BackgroundPlain(Colors.SlateBlue), BackgroundSeam(false), BackgroundSeam(true), BackgroundPlain(Colors.Silver) };
        var manifest = new StringBuilder(); var spine = new StringBuilder();
        for (int i = 0; i < images.Length; i++)
        {
            manifest.Append($"<item id='image-{i}' href='image-{i}.png' media-type='image/png' {(i == 0 ? "properties='cover-image'" : "")}/>");
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(images[i]));
            using var bytes = new MemoryStream(); encoder.Save(bytes); using var output = zip.CreateEntry($"OPS/image-{i}.png").Open(); output.Write(bytes.ToArray());
        }
        for (int i = 0; i < 48; i++)
        {
            int image = i == 0 ? 0 : i is 4 or 30 ? 1 : i is 5 or 31 ? 2 : 3;
            manifest.Append($"<item id='page-{i}' href='page-{i}.xhtml' media-type='application/xhtml+xml'/>"); spine.Append($"<itemref idref='page-{i}'/>");
            Text($"OPS/page-{i}.xhtml", $"<html><body><img src='image-{image}.png'/></body></html>");
        }
        Text("OPS/book.opf", $"<package xmlns='http://www.idpf.org/2007/opf' version='3.0'><manifest>{manifest}</manifest><spine>{spine}</spine></package>");
    }
}

using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ShuiMan.Core;
using ShuiMan.Windows;

namespace ShuiMan.UiChecks;

internal static partial class Program
{
    private static async Task RightRivalFirstPaintChecks(string root)
    {
        var source = Path.Combine(root, "right-rival-first-paint.epub");
        GenerateBackgroundBook(source, 6);
        // Keep five explicitly upright pages, with actual continuous artwork on pages 3–4.
        using (var zip = ZipFile.Open(source, ZipArchiveMode.Update))
        {
            string Read(string name) { using var input = new StreamReader(zip.GetEntry(name)!.Open()); return input.ReadToEnd(); }
            void Write(string name, string content)
            {
                zip.GetEntry(name)!.Delete();
                using var output = new StreamWriter(zip.CreateEntry(name).Open(), Encoding.UTF8); output.Write(content);
            }
            Write("OPS/book.opf", Read("OPS/book.opf").Replace("<itemref idref='page-5'/>", ""));
            Write("OPS/page-1.xhtml", "<html><body><img style='transform:rotate(0deg)' src='image-3.png'/></body></html>");
            Write("OPS/page-2.xhtml", "<html><body><img style='transform:rotate(0deg)' src='image-1.png'/></body></html>");
            Write("OPS/page-3.xhtml", "<html><body><img style='transform:rotate(0deg)' src='image-2.png'/></body></html>");
        }
        foreach (bool manualZero in new[] { false, true })
        {
            var data = Path.Combine(root, manualZero ? "right-rival-manual-zero" : "right-rival-ordinary");
            var reader = BackgroundWindow(data, source);
            var shown = new List<(int[] Indices, int Images)>();
            var surface = (PageSurface)reader.FindName("Surface");
            try
            {
                reader.Show();
                await WaitReaderPage(reader, 1, 5);
                await ReaderField<Task>(reader, "_analysisTask").WaitAsync(TimeSpan.FromSeconds(10));
                await reader.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                var book = ReaderField<IOpenBook>(reader, "_book");
                var saved = ReaderField<SavedBook>(reader, "_saved");
                saved.Preferences.Layout = "double";
                saved.Overrides.Clear();
                if (manualZero) saved.Overrides[book.Publication.Units[1].Id] = new PageOverride { Rotation = 0 };
                var pairs = ReaderField<Dictionary<int, PairDecision>>(reader, "_pairs");
                pairs.Clear();
                pairs[0] = new(0); pairs[1] = new(1);
                pairs[2] = new(2, Automatic: true, Score: .95, Suggested: true);
                ReaderField<Dictionary<int, string>>(reader, "_analysisFailures").Clear();
                SetReaderField(reader, "_analysisFinished", false);
                surface.IsVisibleChanged += (_, _) =>
                {
                    if (surface.IsVisible && typeof(MainWindow).GetField("_displayGroup", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(reader) is DisplayGroup group)
                        shown.Add(([.. group.Indices], ReaderField<List<BitmapSource>>(reader, "_displayImages").Count));
                };
                var navigation = FirstPaintJump(reader, 2);
                await reader.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                Check(manualZero
                        ? "a manually upright ordinary page waits for the next spread's unresolved right rival before first paint"
                        : "ordinary double-page first paint waits for the next spread's unresolved right rival even with publisher direction hints",
                    !navigation.IsCompleted && !surface.IsVisible && shown.Count == 0 && ReaderField<int>(reader, "_foregroundReaders") == 0);

                pairs[3] = new(3);
                typeof(MainWindow).GetMethod("SignalAnalysisChanged", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(reader, null);
                await navigation.WaitAsync(TimeSpan.FromSeconds(10));
                await WaitReaderPage(reader, 2, 5);
                var first = shown.ToArray();
                var rawFirstBounds = CaptureSurface(surface).Bounds;
                var firstBounds = await CaptureRivalBounds(reader, surface);
                bool correctFirst = first.Length > 0 && first.All(frame => frame.Images == 1 && frame.Indices.SequenceEqual([1])) &&
                    firstBounds.Width < firstBounds.Height;
                ((Button)reader.FindName("NextPageButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await WaitReaderPage(reader, 3, 5);
                var next = ReaderField<DisplayGroup>(reader, "_displayGroup");
                int nextImages = ReaderField<List<BitmapSource>>(reader, "_displayImages").Count;
                var rawNextBounds = CaptureSurface(surface).Bounds;
                var nextBounds = await CaptureRivalBounds(reader, surface);
                Console.WriteLine($"RIVAL FRAME manualZero={manualZero}; first={string.Join(";", first.Select(frame => $"[{string.Join(',', frame.Indices)}]/{frame.Images}"))}; " +
                    $"firstBoundsBeforeLayout={rawFirstBounds}; firstBounds={firstBounds}; next=[{string.Join(',', next.Indices)}]/{nextImages}; " +
                    $"nextSpread={next.Spread}; nextBoundsBeforeLayout={rawNextBounds}; nextBounds={nextBounds}; surface={surface.ActualWidth}x{surface.ActualHeight}");
                Check(manualZero
                        ? "resolving the right rival preserves the manually upright page and then displays the next true spread"
                        : "resolving the right rival first paints the correct single page and then displays the next true spread",
                    correctFirst && next.Spread && next.Indices.SequenceEqual([2, 3]) &&
                    nextImages == 2 && nextBounds.Width > nextBounds.Height);
            }
            finally
            {
                SetReaderField(reader, "_analysisFinished", true);
                typeof(MainWindow).GetMethod("SignalAnalysisChanged", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(reader, null);
                await CloseWindow(reader);
            }
        }
    }

    private static async Task<Rect> CaptureRivalBounds(MainWindow reader, PageSurface surface)
    {
        // Navigation completion updates PageSurface's model and invalidates layout; WPF
        // may not have committed its drawing yet. Drain that render turn once, while
        // keeping the first-visible group observations made before this fence intact.
        reader.UpdateLayout();
        await reader.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        surface.UpdateLayout();
        return CaptureSurface(surface).Bounds;
    }
}

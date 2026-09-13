using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ShuiMan.Core;
using ShuiMan.Windows;

namespace ShuiMan.UiChecks;

internal static partial class Program
{
    private static async Task DoublePageChecks(string root)
    {
        var source = Path.Combine(root, "original-double-pages.zip");
        ShowcaseFixtures.GenerateSmallArchive(source, "Original ordinary two-page reading", 4);
        var data = Path.Combine(root, "double-page-data");
        var store = new LibraryStore(data);
        using (var book = await DocumentEngine.OpenAsync(source))
            store.Save(new SavedBook
            {
                Id = book.Publication.Identity, Path = source, Title = "Original double-page check", Total = 4,
                Preferences = new ReaderPreferences { Layout = "single", SmartSpreads = false, AutomaticPairs = false, AutomaticOrientation = false }
            });
        var reader = new MainWindow(data, source)
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -12000, Top = -12000, ShowActivated = false, ShowInTaskbar = false
        };
        try
        {
            reader.Show();
            var surface = (PageSurface)reader.FindName("Surface");
            var busy = (FrameworkElement)reader.FindName("BusyBanner");
            var status = (TextBlock)reader.FindName("StatusText");
            await WaitUntil(() => surface.IsVisible && ((TextBlock)reader.FindName("PageTotal")).Text.Contains("4") && !busy.IsVisible, "ordinary reader loads its generated ZIP");
            var layout = (ComboBox)reader.FindName("LayoutBox");
            layout.SelectedItem = layout.Items.OfType<ComboBoxItem>().Single(item => item.Content?.ToString() == "双页");
            await WaitUntil(() => store.Books.Single().Preferences.Layout == "double" && !busy.IsVisible, "double-page selector saves its preference");
            ((Button)reader.FindName("NextPageButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntil(() => status.Text.Contains("2–3") && !busy.IsVisible, "ordinary double mode displays pages two and three");
            reader.UpdateLayout();
            var doubleCapture = CaptureSurface(surface);
            Check("the actual double-page selector renders two ordinary pages with a visible gutter", doubleCapture.Bounds.Width > doubleCapture.Bounds.Height && HasPageGutter(surface));
            Check("ordinary double-page viewing persists independently of intelligent joining", store.Books.Single() is { Position: 2, Preferences: { Layout: "double", SmartSpreads: false }, Overrides.Count: 0 });

            var correction = (Button)reader.FindName("CorrectionButton");
            correction.ContextMenu!.Items.OfType<MenuItem>().Single(item => item.Header?.ToString() == "与下一页配对").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await WaitUntil(() => status.Text.Contains("2–3") && status.Text.Contains("手动") && !busy.IsVisible, "manual seam confirmation replaces ordinary two-page layout");
            reader.UpdateLayout();
            Check("confirming an ordinary pair as a spread removes its physical gutter", !HasPageGutter(surface));
        }
        finally { await CloseWindow(reader); }
    }

    private static bool HasPageGutter(PageSurface surface)
    {
        var bitmap = new RenderTargetBitmap(Math.Max(1, (int)Math.Ceiling(surface.ActualWidth)), Math.Max(1, (int)Math.Ceiling(surface.ActualHeight)), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(surface);
        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        int separated = 0, total = 0;
        for (int y = bitmap.PixelHeight / 3; y < bitmap.PixelHeight * 2 / 3; y++)
        {
            total++;
            // RenderTargetBitmap includes the visual's parent offset. Find the actual two
            // opaque page regions, rather than assuming their seam is at the bitmap midpoint.
            var runs = new List<(int Start, int End)>();
            int start = -1;
            for (int x = 0; x <= bitmap.PixelWidth; x++)
            {
                bool opaque = x < bitmap.PixelWidth && pixels[(y * bitmap.PixelWidth + x) * 4 + 3] > 0;
                if (opaque && start < 0) start = x;
                else if (!opaque && start >= 0) { runs.Add((start, x - 1)); start = -1; }
            }
            if (runs.Count == 2 && runs.All(run => run.End - run.Start > bitmap.PixelWidth / 5) &&
                runs[1].Start - runs[0].End > 2) separated++;
        }
        return total > 0 && separated == total;
    }
}

using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ShuiMan.Core;
using ShuiMan.Windows;

namespace ShuiMan.UiChecks;

internal static partial class Program
{
    private static async Task NativeEpubChecks(string root)
    {
        var epub = Path.Combine(root, "original-native-canvas.epub");
        GenerateNativeEpub(epub);
        var data = Path.Combine(root, "native-epub-data");
        var store = new LibraryStore(data);
        string firstId;
        using (var book = await DocumentEngine.OpenAsync(epub))
        {
            firstId = book.Publication.Units[0].Id;
            store.Save(new SavedBook
            {
                Id = book.Publication.Identity, Path = epub, Title = "Original native EPUB", Total = 3,
                Preferences = new ReaderPreferences { SmartSpreads = false, AutomaticPairs = false, AutomaticOrientation = false }
            });
        }
        var reader = new MainWindow(data, epub)
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -12000, Top = -12000, ShowActivated = false, ShowInTaskbar = false
        };
        try
        {
            reader.Show();
            var surface = (PageSurface)reader.FindName("Surface");
            var status = (TextBlock)reader.FindName("StatusText");
            var correction = (Button)reader.FindName("CorrectionButton");
            var busy = (FrameworkElement)reader.FindName("BusyBanner");
            await WaitUntil(() => surface.IsVisible && ((TextBlock)reader.FindName("PageTotal")).Text.Contains("3") && !busy.IsVisible, "EPUB images appear on the native page canvas");
            reader.UpdateLayout();
            var before = CaptureSurface(surface);
            Check("EPUB publisher orientation renders on a native canvas with usable correction controls", before.Bounds.Width > before.Bounds.Height && correction.IsEnabled && correction.IsVisible && reader.FindName("BookWeb") == null);
            void Menu(string label) => correction.ContextMenu!.Items.OfType<MenuItem>().Single(item => item.Header?.ToString() == label).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

            Menu("向右旋转 90°");
            await WaitUntil(() => store.Books.Single().Overrides.GetValueOrDefault(firstId)?.Rotation == 180 && !busy.IsVisible, "native EPUB manual rotation is saved");
            reader.UpdateLayout();
            Check("the real EPUB rotation command overrides its publisher and visibly turns the page", CaptureSurface(surface).Bounds.Width < CaptureSurface(surface).Bounds.Height);

            Menu("与下一页配对");
            try { await WaitUntil(() => store.Books.Single().Overrides.GetValueOrDefault(firstId)?.JoinNext == true && status.Text.Contains("1–2") && !busy.IsVisible, "native EPUB pairing displays both source images"); }
            catch { Console.WriteLine($"Generated pair fixture: status={status.Text}, busy={busy.IsVisible}, corrections={System.Text.Json.JsonSerializer.Serialize(store.Books.Single().Overrides)}"); throw; }
            reader.UpdateLayout();
            var paired = CaptureSurface(surface);
            Check("the real EPUB pair command displays a persisted two-image spread", status.Text.Contains("手动") && !paired.Pixels.SequenceEqual(before.Pixels));

            ((ComboBox)reader.FindName("DirectionBox")).SelectedIndex = 1;
            await WaitUntil(() => store.Books.Single().Preferences.Direction == "rtl" && !busy.IsVisible, "RTL preference redraw completes");
            reader.UpdateLayout();
            Check("changing EPUB reading direction preserves confirmed physical image sides", CaptureSurface(surface).Pixels.SequenceEqual(paired.Pixels) && store.Books.Single().Overrides[firstId].EarlierOnRight == false);

            Menu("交换双图左右");
            await WaitUntil(() => store.Books.Single().Overrides[firstId].EarlierOnRight && !busy.IsVisible, "native EPUB pair swap is saved");
            reader.UpdateLayout();
            Check("the EPUB swap command visibly exchanges the two source images", !CaptureSurface(surface).Pixels.SequenceEqual(paired.Pixels));

            Menu("取消配对 / 单独显示");
            await WaitUntil(() => store.Books.Single().Overrides[firstId].JoinNext == false && !busy.IsVisible, "native EPUB unpair completes");
            Check("the EPUB unpair command restores an individual native page", !status.Text.Contains("1–2") && store.Books.Single().Overrides[firstId].Standalone == true);
            Menu("恢复当前页自动识别");
            await WaitUntil(() => !store.Books.Single().Overrides.ContainsKey(firstId) && !busy.IsVisible, "native EPUB manual corrections reset");
            ((Button)reader.FindName("OrientationStatusButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntil(() => store.Books.Single().Preferences.AutomaticOrientation && !busy.IsVisible, "publisher EPUB rotation hints are applied by the reader");
            reader.UpdateLayout();
            Check("enabling native EPUB orientation applies the matching publisher quarter-turn", CaptureSurface(surface).Bounds.Width > CaptureSurface(surface).Bounds.Height);

            ((Button)reader.FindName("SmartStatusButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntil(() => store.Books.Single().Preferences.SmartSpreads && !busy.IsVisible, "smart spread toggle persists");
            Check("the visible intelligent-spread control changes real EPUB reader preferences", ((MenuItem)reader.FindName("SmartMenu")).IsChecked && store.Books.Single().Preferences.SmartSpreads);
        }
        finally { await CloseWindow(reader); }
        Check("the native EPUB window closes after a single request", !reader.IsVisible);
    }

    private static (Rect Bounds, byte[] Pixels) CaptureSurface(PageSurface surface)
    {
        var bitmap = new RenderTargetBitmap(Math.Max(1, (int)Math.Ceiling(surface.ActualWidth)), Math.Max(1, (int)Math.Ceiling(surface.ActualHeight)), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(surface);
        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        int left = bitmap.PixelWidth, top = bitmap.PixelHeight, right = 0, bottom = 0;
        for (int y = 0; y < bitmap.PixelHeight; y++)
            for (int x = 0; x < bitmap.PixelWidth; x++)
                if (pixels[(y * bitmap.PixelWidth + x) * 4 + 3] > 0)
                { left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x); bottom = Math.Max(bottom, y); }
        return (right >= left && bottom >= top ? new Rect(left, top, right - left + 1, bottom - top + 1) : Rect.Empty, SHA256.HashData(pixels));
    }

    private static void GenerateNativeEpub(string path)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        void Add(string name, byte[] bytes) { using var output = zip.CreateEntry(name).Open(); output.Write(bytes); }
        void Text(string name, string text) => Add(name, Encoding.UTF8.GetBytes(text));
        Text("META-INF/container.xml", "<container xmlns='urn:oasis:names:tc:opendocument:xmlns:container'><rootfiles><rootfile full-path='OPS/book.opf'/></rootfiles></container>");
        Text("OPS/book.opf", "<package xmlns='http://www.idpf.org/2007/opf' version='3.0'><manifest><item id='one' href='one.xhtml' media-type='application/xhtml+xml'/></manifest><spine><itemref idref='one'/></spine></package>");
        Text("OPS/one.xhtml", "<html><head><link rel='stylesheet' href='style.css'/></head><body><div class='page'><img src='one.png'/></div><div class='page'><svg xmlns='http://www.w3.org/2000/svg'><image href='two.png'/></svg></div><div class='page'><img src='three.png'/></div></body></html>");
        Text("OPS/style.css", ".page { position:absolute; transform:rotate(90deg); } .unrelated { transform:rotate(180deg); }");
        foreach (var (name, color) in new[] { ("one.png", Colors.SteelBlue), ("two.png", Colors.SeaGreen), ("three.png", Colors.IndianRed) })
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(new SolidColorBrush(color), null, new Rect(0, 0, 320, 480));
                dc.DrawEllipse(Brushes.Bisque, null, new Point(220, 130), 55, 55);
                dc.DrawRectangle(Brushes.WhiteSmoke, null, new Rect(35, 310, 105, 125));
            }
            var bitmap = new RenderTargetBitmap(320, 480, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var output = new MemoryStream(); encoder.Save(output); Add("OPS/" + name, output.ToArray());
        }
    }
}

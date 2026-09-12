using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using ShuiMan.Core;
using ShuiMan.Windows;

namespace ShuiMan.UiChecks;

internal static class Program
{
    private static readonly List<Exception> DispatcherErrors = [];
    private static int passed;
    private static int result;

    [STAThread]
    private static int Main()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.DispatcherUnhandledException += (_, e) =>
        {
            DispatcherErrors.Add(e.Exception);
            Console.Error.WriteLine($"UNHANDLED DISPATCHER: {e.Exception}");
            e.Handled = true;
        };
        app.Startup += async (_, _) =>
        {
            try { await RunAsync(); }
            catch (Exception ex) { result = 1; Console.Error.WriteLine($"FAIL WebView2 integration: {ex}"); }
            finally { app.Shutdown(result); }
        };
        app.Run();
        return result;
    }

    private static async Task RunAsync()
    {
        string version;
        try { version = CoreWebView2Environment.GetAvailableBrowserVersionString(); }
        catch (WebView2RuntimeNotFoundException)
        {
            Console.WriteLine("SKIP Windows UI checks: WebView2 Evergreen Runtime is not installed.");
            return;
        }
        Console.WriteLine($"WebView2 Runtime: {version}");
        var root = Path.Combine(Path.GetTempPath(), "ShuiMan-UiChecks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "original-ui.epub");
        GenerateEpub(path);
        using var book = await DocumentEngine.OpenAsync(path);
        var view = new WebView2();
        var window = new Window
        {
            Title = "ShuiMan automated WebView2 checks", Width = 800, Height = 650,
            Left = -12000, Top = -12000, ShowActivated = false, ShowInTaskbar = false,
            Content = view
        };
        using var io = new SemaphoreSlim(1, 1);
        var host = new RestrictedBookView(view, Path.Combine(root, "webview"), io);
        try
        {
            window.Show();
            await host.ShowAsync(book, "OPS/one.xhtml", CancellationToken.None);
            await WaitForDom(view, "document.readyState === 'complete' && document.getElementById('story') !== null && document.getElementById('picture').naturalWidth > 0 && document.getElementById('tiff').naturalWidth > 0");
            Check("complex EPUB body and local CSS render", await EvaluateBool(view,
                "document.getElementById('story').textContent === 'Original story text' && getComputedStyle(document.getElementById('story')).color === 'rgb(18, 52, 86)'"));
            Check("PNG and TIFF adaptation load through deferred resource handler", await EvaluateBool(view,
                "document.getElementById('picture').naturalWidth === 24 && document.getElementById('tiff').naturalWidth === 24"));
            Check("book scripts remain disabled while host diagnostics work", await EvaluateBool(view,
                "typeof window.bookScriptRan === 'undefined' && document.getElementById('story').textContent === 'Original story text'") && !view.CoreWebView2.Settings.IsScriptEnabled);
            Check("external image has no loaded content", await EvaluateBool(view, "document.getElementById('externalImage').naturalWidth === 0"));

            var external = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnExternal(object? sender, CoreWebView2NavigationStartingEventArgs e)
            {
                if (e.Uri.StartsWith("https://outside.invalid/", StringComparison.Ordinal)) external.TrySetResult(e.Cancel);
            }
            view.CoreWebView2.NavigationStarting += OnExternal;
            try
            {
                await view.ExecuteScriptAsync("document.getElementById('externalLink').click()");
                Check("external navigation is cancelled before leaving publication", await external.Task.WaitAsync(TimeSpan.FromSeconds(8)));
                Check("blocked navigation retains original page", view.Source.AbsolutePath == "/OPS/one.xhtml");
            }
            finally { view.CoreWebView2.NavigationStarting -= OnExternal; }

            var local = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnLocal(string resource) => local.TrySetResult(resource);
            host.NavigateRequested += OnLocal;
            try
            {
                await view.ExecuteScriptAsync("document.getElementById('next').click()");
                var resource = await local.Task.WaitAsync(TimeSpan.FromSeconds(8));
                var position = book.Publication.Units.FindIndex(unit => unit.Locator.Resource == resource);
                Check("local EPUB link reports the correct spine position", resource == "OPS/two.xhtml" && position == 1);
                await host.ShowAsync(book, resource, CancellationToken.None);
                await WaitForDom(view, "document.readyState === 'complete' && document.getElementById('second') !== null");
                Check("reported local navigation displays next EPUB page", await EvaluateBool(view, "document.getElementById('second').textContent === 'Second spine page'") && view.Source.AbsolutePath == "/OPS/two.xhtml");
            }
            finally { host.NavigateRequested -= OnLocal; }

            host.Clear();
            await WaitForDom(view, "location.href === 'about:blank'");
            Check("clearing the book resets browser content", view.Source.AbsoluteUri == "about:blank");
            await MainWindowChecks(root, path);
            await Task.Delay(250);
            Check("resource deferrals complete without dispatcher exceptions", DispatcherErrors.Count == 0);
            Console.WriteLine($"UI RESULT: {passed} passed, 0 failed");
        }
        finally
        {
            host.Dispose();
            window.Close();
            book.Dispose();
            // WebView2 browser processes can briefly retain their own profile after close.
            // Only this generated per-run directory is eligible for cleanup.
            for (var attempt = 0; attempt < 10; attempt++)
            {
                try { Directory.Delete(root, true); break; }
                catch (IOException) { await Task.Delay(250); }
                catch (UnauthorizedAccessException) { await Task.Delay(250); }
            }
            if (Directory.Exists(root)) Console.WriteLine($"WebView2 profile still closing; generated test data remains at {root}");
        }
    }

    private static async Task MainWindowChecks(string root, string bookPath)
    {
        // MainWindow needs its two named app brushes; visual styling is checked manually.
        Application.Current.Resources["Accent"] = new SolidColorBrush(Color.FromRgb(8, 127, 140));
        Application.Current.Resources["Ink"] = new SolidColorBrush(Color.FromRgb(21, 63, 72));
        var reader = new MainWindow(Path.Combine(root, "app-data"), bookPath)
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -12000, Top = -12000, ShowActivated = false, ShowInTaskbar = false
        };
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        reader.Closed += (_, _) => closed.TrySetResult();
        try
        {
            reader.Show();
            var browser = (WebView2)reader.FindName("BookWeb");
            await WaitForDom(browser, "document.readyState === 'complete' && document.getElementById('next') !== null");
            await browser.ExecuteScriptAsync("document.getElementById('next').click()");
            await WaitForDom(browser, "document.readyState === 'complete' && document.getElementById('second') !== null");
            var pageNumber = (System.Windows.Controls.TextBox)reader.FindName("PageNumber");
            Check("real MainWindow EPUB hyperlink synchronizes displayed page number", pageNumber.Text == "2");
            reader.Close();
            await closed.Task.WaitAsync(TimeSpan.FromSeconds(8));
            Check("real MainWindow closes after one Close request", !reader.IsVisible);
        }
        finally { if (!closed.Task.IsCompleted) reader.Close(); }
    }

    private static async Task<bool> EvaluateBool(WebView2 view, string expression)
    {
        var json = await view.ExecuteScriptAsync($"Boolean({expression})");
        return JsonSerializer.Deserialize<bool>(json);
    }
    private static async Task WaitForDom(WebView2 view, string expression)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        do
        {
            if (DispatcherErrors.Count != 0) throw new AggregateException("WebView resource event failed", DispatcherErrors);
            try { if (await EvaluateBool(view, expression)) return; }
            catch (InvalidOperationException) { }
            await Task.Delay(100);
        } while (DateTime.UtcNow < deadline);
        throw new TimeoutException($"Browser condition timed out: {expression}");
    }
    private static void Check(string name, bool condition)
    {
        if (!condition) throw new InvalidOperationException(name);
        passed++;
        Console.WriteLine($"PASS {name}");
    }
    private static void GenerateEpub(string path)
    {
        byte[] Image(BitmapEncoder encoder)
        {
            var bitmap = BitmapSource.Create(24, 36, 96, 96, PixelFormats.Bgra32, null,
                Enumerable.Repeat(new byte[] { 180, 120, 30, 255 }, 24 * 36).SelectMany(pixel => pixel).ToArray(), 24 * 4);
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var output = new MemoryStream();
            encoder.Save(output);
            return output.ToArray();
        }
        using var stream = File.Create(path);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);
        void Add(string name, byte[] bytes)
        {
            using var resource = zip.CreateEntry(name).Open();
            resource.Write(bytes);
        }
        void Text(string name, string value) => Add(name, Encoding.UTF8.GetBytes(value));
        Text("META-INF/container.xml", "<container xmlns=\"urn:oasis:names:tc:opendocument:xmlns:container\"><rootfiles><rootfile full-path=\"OPS/book.opf\"/></rootfiles></container>");
        Text("OPS/book.opf", "<package xmlns=\"http://www.idpf.org/2007/opf\" version=\"3.0\"><manifest><item id=\"one\" href=\"one.xhtml\" media-type=\"application/xhtml+xml\"/><item id=\"two\" href=\"two.xhtml\" media-type=\"application/xhtml+xml\"/></manifest><spine><itemref idref=\"one\"/><itemref idref=\"two\"/></spine></package>");
        Text("OPS/one.xhtml", """
            <html xmlns="http://www.w3.org/1999/xhtml"><head><link rel="stylesheet" href="style.css"/></head>
            <body><p id="story">Original story text</p><img id="picture" src="picture.png"/><img id="tiff" src="picture.tiff"/>
            <img id="externalImage" src="https://outside.invalid/picture.png"/>
            <a id="next" href="two.xhtml">Next page</a><a id="externalLink" href="https://outside.invalid/escape">External</a>
            <script>window.bookScriptRan=true;document.getElementById('story').textContent='Changed by book script';</script></body></html>
            """);
        Text("OPS/two.xhtml", "<html xmlns=\"http://www.w3.org/1999/xhtml\"><body><p id=\"second\">Second spine page</p></body></html>");
        Text("OPS/style.css", "#story { color: rgb(18, 52, 86); }");
        Add("OPS/picture.png", Image(new PngBitmapEncoder()));
        Add("OPS/picture.tiff", Image(new TiffBitmapEncoder()));
    }
}

using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Security.Cryptography;
using ShuiMan.Core;

namespace ShuiMan.UiChecks;

internal static partial class Program
{
    private static readonly List<Exception> DispatcherErrors = [];
    private static int passed;
    private static int result;

    [STAThread]
    private static int Main(string[] args)
    {
        if (args is ["--whole-book-audit", var sourceDirectory, var sampleCount, var bookOrdinal, var cacheDirectory] &&
            int.TryParse(sampleCount, out int sampleBooks) && int.TryParse(bookOrdinal, out int sampleBook))
        {
            WholeBookAudit(Path.GetFullPath(sourceDirectory), sampleBooks, sampleBook, Path.GetFullPath(cacheDirectory)).GetAwaiter().GetResult();
            return 0;
        }
        if (args is ["--pair-audit", var auditDirectory, var bookCount, var pairCount] && int.TryParse(bookCount, out int books) && int.TryParse(pairCount, out int pairs))
        {
            PairAudit(Path.GetFullPath(auditDirectory), books, pairs).GetAwaiter().GetResult();
            return 0;
        }
        if (args is ["--showcase", var destination])
        {
            var folder = Path.GetFullPath(destination);
            var generated = ShowcaseFixtures.Generate(folder);
            Console.WriteLine($"Generated {generated.Count} original comic ZIP files in {folder}");
            return 0;
        }
        if (args is ["--epub-statistics", var publication])
        {
            using var book = DocumentEngine.OpenAsync(publication).GetAwaiter().GetResult();
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                Pages = book.Publication.Units.Count,
                NativeImages = book.Publication.Units.Count(unit => unit.ImagePath != null && unit.Error == null && !unit.Complex),
                WebPages = book.Publication.Units.Count(unit => unit.Complex),
                ErrorPositions = book.Publication.Units.Count(unit => unit.Error != null),
                RotationHints = book.Publication.Units.Count(unit => unit.RotationHint != null),
                HintDistribution = book.Publication.Units.GroupBy(unit => unit.RotationHint?.ToString() ?? "unmarked").ToDictionary(group => group.Key, group => group.Count()),
                HintedPageRender = RenderHintDiagnostic(book)
            }));
            return 0;
        }
        if (args.Length != 0)
        {
            Console.Error.WriteLine("Usage: ShuiMan.UiChecks [--showcase <new-output-folder> | --epub-statistics <local-book>]");
            return 2;
        }
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/ShuiMan;component/Theme.xaml", UriKind.Absolute)
        });
        app.DispatcherUnhandledException += (_, e) =>
        {
            if (!DispatcherErrors.Any(prior => prior.GetType() == e.Exception.GetType() && prior.Message == e.Exception.Message))
            {
                DispatcherErrors.Add(e.Exception);
                Console.Error.WriteLine($"UNHANDLED DISPATCHER: {e.Exception}");
            }
            e.Handled = true;
        };
        app.Startup += async (_, _) =>
        {
            try { await RunAsync(); }
            catch (Exception ex) { result = 1; Console.Error.WriteLine($"FAIL Windows UI integration: {ex}"); }
            finally { app.Shutdown(result); }
        };
        app.Run();
        return result;
    }

    private static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "ShuiMan-UiChecks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await LibraryChecks(root);
            await NativeEpubChecks(root);
            await BackgroundReaderChecks(root);
            await DoublePageChecks(root);
            Check("the Windows reader has no browser or WebView assembly dependency", !typeof(ShuiMan.Windows.MainWindow).Assembly.GetReferencedAssemblies().Any(assembly => assembly.Name?.Contains("WebView", StringComparison.OrdinalIgnoreCase) == true));
            await Task.Delay(200);
            Check("native reader and library finish without dispatcher exceptions", DispatcherErrors.Count == 0);
            Console.WriteLine($"UI RESULT: {passed} passed, 0 failed");
        }
        finally
        {
            // This exact per-run directory contains only generated test books and data.
            try { Directory.Delete(root, true); }
            catch (IOException) { Console.WriteLine($"Generated test data is still in use: {root}"); }
        }
    }

    private static void Check(string name, bool condition)
    {
        if (!condition) throw new InvalidOperationException(name);
        passed++;
        Console.WriteLine($"PASS {name}");
    }

    private static object? RenderHintDiagnostic(IOpenBook book)
    {
        var index = book.Publication.Units.FindIndex(unit => unit.RotationHint.HasValue && unit.Error == null);
        if (index < 0) return null;
        var original = book.RenderAsync(index, 1600).GetAwaiter().GetResult();
        int angle = book.Publication.Units[index].RotationHint!.Value;
        var rotated = new TransformedBitmap(original, new RotateTransform(angle));
        byte[] Fingerprint(BitmapSource bitmap)
        {
            var image = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
            var pixels = new byte[image.PixelWidth * image.PixelHeight * 4];
            image.CopyPixels(pixels, image.PixelWidth * 4, 0);
            return SHA256.HashData(pixels);
        }
        return new { Angle = angle, OriginalWidth = original.PixelWidth, OriginalHeight = original.PixelHeight,
            RotatedWidth = rotated.PixelWidth, RotatedHeight = rotated.PixelHeight,
            PixelsChanged = !Fingerprint(original).SequenceEqual(Fingerprint(rotated)) };
    }
}

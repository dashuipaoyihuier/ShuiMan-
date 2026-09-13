using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ShuiMan.Core;

namespace ShuiMan.Checks;

internal static class Program
{
    private static int passed;
    private static int failed;

    [STAThread]
    private static int Main(string[] args)
    {
        // Fixtures use WPF rendering, which creates a dispatcher and native render
        // resources even without a window. Complete WPF shutdown before process exit.
        var dispatcher = Dispatcher.CurrentDispatcher;
        var exitCode = 1;
        try { exitCode = RunChecks(args); }
        catch (Exception ex) { Console.Error.WriteLine($"FAIL harness: {ex}"); }
        finally
        {
            // The synchronous harness has no dispatcher frame or synchronization
            // context. Shutdown therefore completes here, on the owning STA thread.
            dispatcher.InvokeShutdown();
        }
        if (!dispatcher.HasShutdownFinished)
        {
            Console.Error.WriteLine("FAIL harness: WPF dispatcher did not complete shutdown.");
            return 1;
        }
        Console.WriteLine("HOST: WPF dispatcher shutdown completed.");
        return exitCode;
    }

    private static int RunChecks(string[] args)
    {
        if (args.Length == 2 && args[0] == "--fixtures")
        {
            var destination = Path.GetFullPath(args[1]);
            Fixtures.Generate(destination);
            Console.WriteLine($"Original fixtures generated: {destination}");
            return 0;
        }
        if (args.Length != 0)
        {
            Console.Error.WriteLine("Usage: ShuiMan.Checks [--fixtures <directory>]");
            return 2;
        }
        var root = Path.Combine(Path.GetTempPath(), "ShuiMan-Checks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Fixtures.Generate(root);
            FormatChecks(root);
            LayoutChecks();
            PairChecks();
            StorageChecks(root);
        }
        catch (Exception ex)
        {
            failed++;
            Console.Error.WriteLine($"FAIL harness: {ex}");
        }
        finally
        {
            // This exact per-run directory is created above; never touches user books.
            try { Directory.Delete(root, true); }
            catch (IOException ex) { Console.Error.WriteLine($"Fixture cleanup: {ex.Message}"); }
        }
        Console.WriteLine($"RESULT: {passed} passed, {failed} failed");
        return failed == 0 ? 0 : 1;
    }

    private static IOpenBook Open(string path) => DocumentEngine.OpenAsync(path).GetAwaiter().GetResult();

    private static void FormatChecks(string root)
    {
        Check("ZIP nested Unicode natural ordering and metadata exclusion", () =>
        {
            using var book = Open(Path.Combine(root, "水漫 演示.zip"));
            Equal(3, book.Publication.Units.Count, "ZIP page count");
            Sequence(["1.png", "2.png", "10.png"], book.Publication.Units.Select(p => Path.GetFileName(p.ImagePath ?? p.Locator.Resource)), "ZIP order");
            Equal(3, book.Publication.Units.Select(p => p.Id).Distinct().Count(), "unique source identities");
            var rendered = book.RenderAsync(0, 400).GetAwaiter().GetResult();
            True(rendered.PixelWidth > 0 && rendered.PixelHeight > rendered.PixelWidth, "portrait decodes");
            True(Math.Max(rendered.PixelWidth, rendered.PixelHeight) <= 400, "decode size bounded");
            var wide = book.RenderAsync(2).GetAwaiter().GetResult();
            True(wide.PixelWidth > wide.PixelHeight, "landscape decodes");
        });
        Check("CBZ has ZIP image semantics", () =>
        {
            using var book = Open(Path.Combine(root, "demo.cbz"));
            Equal(3, book.Publication.Units.Count, "CBZ pages");
            True(book.RenderAsync(1).GetAwaiter().GetResult().PixelWidth > 0, "CBZ render");
        });
        Check("ZIP renders JPEG GIF and BMP pages", () =>
        {
            using var book = Open(Path.Combine(root, "mixed-images.zip"));
            Equal(3, book.Publication.Units.Count, "mixed image count");
            for (var index = 0; index < 3; index++)
                True(book.RenderAsync(index).GetAwaiter().GetResult().PixelWidth > 0, $"mixed image {index} renders");
        });
        Check("ZIP legacy GB18030 Chinese filenames", () =>
        {
            using var book = Open(Path.Combine(root, "legacy-filenames.zip"));
            Equal(2, book.Publication.Units.Count, "legacy ZIP count");
            Sequence(["第1页.png", "第2页.png"], book.Publication.Units.Select(p => Path.GetFileName(p.ImagePath ?? p.Locator.Resource)), "Chinese filename decoding and sorting");
            True(book.RenderAsync(1).GetAwaiter().GetResult().PixelWidth > 0, "legacy path rendering");
        });
        Check("recursive image folder natural ordering", () =>
        {
            using var book = Open(Path.Combine(root, "image-folder"));
            Equal(3, book.Publication.Units.Count, "folder pages");
            Sequence(["1.png", "2.png", "10.png"], book.Publication.Units.Select(p => Path.GetFileName(p.ImagePath ?? p.Locator.Resource)), "folder order");
        });
        Check("image source IDs survive earlier folder or ZIP insertion", () =>
        {
            var path = Path.Combine(root, "stable-folder");
            Directory.CreateDirectory(path);
            var image = File.ReadAllBytes(Path.Combine(root, "single.png"));
            File.WriteAllBytes(Path.Combine(path, "2.png"), image);
            string folderId;
            using (var before = Open(path)) folderId = before.Publication.Units.Single().Id;
            File.WriteAllBytes(Path.Combine(path, "1.png"), image);
            using (var after = Open(path)) Equal(folderId, after.Publication.Units.Single(u => u.Locator.Resource == "2.png").Id, "folder locator stable after insertion");
            var archive = Path.Combine(root, "stable.zip");
            Fixtures.Archive(archive, new() { ["2.png"] = image });
            string zipId;
            using (var before = Open(archive)) zipId = before.Publication.Units.Single().Id;
            Fixtures.Archive(archive, new() { ["1.png"] = image, ["2.png"] = image });
            using var reopened = Open(archive);
            Equal(zipId, reopened.Publication.Units.Single(u => u.Locator.Resource == "2.png").Id, "ZIP locator stable after insertion");
        });
        Check("single image rendering", () =>
        {
            using var book = Open(Path.Combine(root, "single.png"));
            Equal(1, book.Publication.Units.Count, "single image count");
            Equal(480, book.RenderAsync(0).GetAwaiter().GetResult().PixelWidth, "original width");
        });
        Check("TIFF frames remain distinct pages", () =>
        {
            using var book = Open(Path.Combine(root, "frames.tiff"));
            Equal(2, book.Publication.Units.Count, "TIFF frame count");
            True(book.Publication.Units[0].Id != book.Publication.Units[1].Id, "frame source locators differ");
            True(!Pixels(book.RenderAsync(0).GetAwaiter().GetResult()).SequenceEqual(Pixels(book.RenderAsync(1).GetAwaiter().GetResult())), "TIFF renders distinct frame content");
        });
        Check("TIFF frames inside ZIP", () =>
        {
            using var book = Open(Path.Combine(root, "frames.zip"));
            Equal(2, book.Publication.Units.Count, "archived TIFF frames");
            True(book.RenderAsync(1).GetAwaiter().GetResult().PixelWidth > 0, "second archived TIFF frame");
        });
        Check("TIFF browser adaptation produces a valid PNG", () =>
        {
            var png = DocumentEngine.RasterToPng(File.ReadAllBytes(Path.Combine(root, "frames.tiff")), 400);
            Sequence(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, png.Take(8), "PNG signature");
            using var stream = new MemoryStream(png);
            var decoded = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            True(decoded.PixelWidth > 0 && decoded.PixelHeight <= 400, "browser PNG decodes within size bound");
        });
        Check("render cache reuses size-specific frozen bitmap", () =>
        {
            using var book = Open(Path.Combine(root, "水漫 演示.zip"));
            var original = book.RenderAsync(0, 400).GetAwaiter().GetResult();
            var cached = book.RenderAsync(0, 400).GetAwaiter().GetResult();
            var larger = book.RenderAsync(0, 600).GetAwaiter().GetResult();
            True(ReferenceEquals(original, cached) && cached.IsFrozen, "same render reused safely across threads");
            True(larger.PixelHeight > cached.PixelHeight, "larger request gets a distinct adequate decode");
        });
        Check("EPUB spine duplicates and missing positions", () =>
        {
            using var book = Open(Path.Combine(root, "spine.epub"));
            var publication = book.Publication;
            Equal(4, publication.Units.Count, "spine positions including missing page");
            Equal("rtl", publication.Direction, "EPUB direction");
            Equal(4, publication.Units.Select(p => p.Id).Distinct().Count(), "duplicate references keep distinct IDs");
            var a = Pixels(book.RenderAsync(0).GetAwaiter().GetResult());
            var b = Pixels(book.RenderAsync(1).GetAwaiter().GetResult());
            var repeated = Pixels(book.RenderAsync(2).GetAwaiter().GetResult());
            True(a.SequenceEqual(repeated), "repeated spine resource repeats its image");
            True(!a.SequenceEqual(b), "spine takes priority over filename order");
            True(!string.IsNullOrEmpty(publication.Units[3].Error), "missing spine resource has error placeholder");
            True(publication.Navigation.Any(p => p.Index == 0), "navigation maps to spine positions");
        });
        Check("PDF count dimensions and raster render", () =>
        {
            using var book = Open(Path.Combine(root, "sample.pdf"));
            Equal(2, book.Publication.Units.Count, "PDF count");
            var first = book.RenderAsync(0, 400).GetAwaiter().GetResult();
            var second = book.RenderAsync(1, 400).GetAwaiter().GetResult();
            True(first.PixelHeight > first.PixelWidth, "PDF first page portrait");
            True(second.PixelWidth > second.PixelHeight, "PDF second page landscape");
            True(Pixels(first).Distinct().Count() > 2, "PDF contains rendered artwork");
        });
        Check("complex EPUB preserves HTML CSS and image resources", () =>
        {
            using var book = Open(Path.Combine(root, "complex.epub"));
            Equal(1, book.Publication.Units.Count, "complex spine count");
            True(book.Publication.Units[0].Complex, "mixed text remains a complex page");
            True(book.Publication.Units[0].Error == null, "valid complex page has no missing-resource error");
            var html = System.Text.Encoding.UTF8.GetString(book.Resource("OPS/story.xhtml") ?? []);
            True(html.Contains("Original story text must remain visible."), "full HTML available to browser");
            True(book.Resource("OPS/style.css")?.Length > 0 && book.Resource("OPS/image.png")?.Length > 0, "linked stylesheet and image available");
        });
        foreach (var filename in new[] { "plain.mobi", "compressed.mobi" })
            Check($"MOBI image order duplicates missing position ({filename})", () =>
            {
                using var book = Open(Path.Combine(root, filename));
                Equal(5, book.Publication.Units.Count, "cover plus body positions");
                Sequence(["mobi:record:3", "mobi:record:4", "mobi:record:2", "mobi:record:4", "mobi:missing"],
                    book.Publication.Units.Select(u => u.Locator.Resource), "MOBI resource order");
                Equal(5, book.Publication.Units.Select(u => u.Id).Distinct().Count(), "MOBI occurrence identities");
                True(book.Publication.Units[0].IsCover && book.Publication.Units[4].Error != null, "cover and missing placeholder");
                var wide = book.RenderAsync(1).GetAwaiter().GetResult();
                True(wide.PixelWidth > wide.PixelHeight, "MOBI referenced image renders");
                using var reopened = Open(Path.Combine(root, filename));
                Sequence(book.Publication.Units.Select(u => u.Id), reopened.Publication.Units.Select(u => u.Id), "MOBI stable reopen locators");
            });
        Check("MOBI body cover is not inserted twice", () =>
        {
            using var book = Open(Path.Combine(root, "body-cover.mobi"));
            Equal(2, book.Publication.Units.Count, "cover already in body");
            True(book.Publication.Units[0].IsCover, "body cover identified");
        });
        Check("MOBI unsupported and malformed containers fail clearly", () =>
        {
            foreach (var file in Directory.EnumerateFiles(root, "invalid-*.mobi")) ExpectOpenFailure(file);
        });
        Check("malformed ZIP fails clearly", () => ExpectOpenFailure(Path.Combine(root, "broken.zip")));
        Check("archive without images fails clearly", () => ExpectOpenFailure(Path.Combine(root, "empty.zip")));
        Check("corrupt image preserves following page position", () =>
        {
            using var book = Open(Path.Combine(root, "corrupt-page.zip"));
            Equal(3, book.Publication.Units.Count, "corrupt image keeps its slot");
            True(book.RenderAsync(2).GetAwaiter().GetResult().PixelWidth > 0, "subsequent page renders");
            try { book.RenderAsync(1).GetAwaiter().GetResult(); }
            catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException) { return; }
            True(!string.IsNullOrEmpty(book.Publication.Units[1].Error), "invalid image is reported");
        });
        Check("ZIP traversal archive is rejected without extraction", () =>
        {
            var rejected = false;
            try { using var book = Open(Path.Combine(root, "unsafe.zip")); }
            catch (InvalidDataException) { rejected = true; }
            True(rejected, "traversal archive rejected");
            True(!File.Exists(Path.Combine(root, "outside.png")), "no filesystem extraction");
        });
    }

    private static void LayoutChecks()
    {
        Check("dual layout cover alone consumes every page exactly once", () =>
        {
            var publication = Pages(7);
            var saved = Settings("double", "ltr");
            var groups = LayoutEngine.Groups(publication, saved);
            Equal(1, groups[0].Indices.Length, "cover alone");
            Sequence(Enumerable.Range(0, 7), groups.SelectMany(g => g.Indices).Order(), "all pages once");
        });
        Check("confirmed physical spread stays in place in both directions", () =>
        {
            var publication = Pages(6);
            var saved = Settings("double", "ltr");
            saved.Overrides[publication.Units[1].Id] = new PageOverride { JoinNext = true, EarlierOnRight = true, PairOffset = .04, PairScale = 1.08 };
            var leftToRight = LayoutEngine.Groups(publication, saved);
            saved.Preferences.Direction = "rtl";
            var rightToLeft = LayoutEngine.Groups(publication, saved);
            var first = leftToRight.Single(g => g.Indices.Contains(1));
            var second = rightToLeft.Single(g => g.Indices.Contains(1));
            Sequence([2, 1], first.Indices, "manual physical placement");
            Sequence(first.Indices, second.Indices, "RTL physical placement preserved");
            True(first.Spread && second.Spread, "confirmed joins have no gutter");
            True(Math.Abs(first.VerticalOffset - .04) < .0001 && Math.Abs(first.RightScale - 1.08) < .0001, "seam alignment retained");
            Sequence(Enumerable.Range(0, 6), rightToLeft.SelectMany(g => g.Indices).Order(), "no duplicate consumption");
        });
        Check("manual standalone overrides automatic pairing", () =>
        {
            var publication = Pages(5);
            var saved = Settings("double", "ltr");
            saved.Overrides[publication.Units[1].Id] = new PageOverride { Standalone = true };
            var automatic = new Dictionary<int, PairDecision> { [1] = new(1, true, false, .95) };
            var groups = LayoutEngine.Groups(publication, saved, pairs: automatic);
            Equal(1, groups.Single(g => g.Indices.Contains(1)).Indices.Length, "manual standalone wins");
            Sequence(Enumerable.Range(0, 5), groups.SelectMany(g => g.Indices).Order(), "all pages still consumed once");
        });
        Check("manual rotation overrides automatic decision", () =>
        {
            var publication = Pages(2);
            var saved = Settings("single", "ltr");
            var page = publication.Units[1];
            var decisions = new Dictionary<string, SpreadDecision> { [page.Id] = new(90, true, false, "fixture") };
            saved.Overrides[page.Id] = new PageOverride { Rotation = 270 };
            Equal(270, LayoutEngine.Rotation(page, saved, decisions), "manual rotation");
            saved.Overrides[page.Id].Rotation = 0;
            Equal(0, LayoutEngine.Rotation(page, saved, decisions), "explicit zero overrides analysis");
        });
        Check("layout invariants across preferences and corrections", () =>
        {
            foreach (var direction in new[] { "ltr", "rtl" })
            foreach (var layout in new[] { "single", "double", "auto" })
            for (var length = 1; length <= 12; length++)
            {
                var publication = Pages(length);
                var saved = Settings(layout, direction);
                if (length > 3) saved.Overrides[publication.Units[1].Id] = new() { JoinNext = true };
                if (length > 4) saved.Overrides[publication.Units[3].Id] = new() { Standalone = true };
                if (length > 6) saved.Overrides[publication.Units[5].Id] = new() { PairingBreak = true };
                var groups = LayoutEngine.Groups(publication, saved);
                Sequence(Enumerable.Range(0, length), groups.SelectMany(g => g.Indices).Order(), $"coverage {direction}/{layout}/{length}");
                True(groups.All(g => g.Indices.Length is 1 or 2), "one or two pages in a display group");
            }
        });
    }

    private static void PairChecks()
    {
        const int width = 768, height = 1024;
        double[] Page(bool right, int shift = 0, double scale = 1, double frequency = 1) =>
            Enumerable.Range(0, width * height).Select(index =>
            {
                var x = index % width;
                var y = index / width;
                var row = (y - (right ? shift : 0)) / (right ? scale : 1) * frequency;
                var phase = (x + (right ? width : 0)) * .008;
                return Math.Clamp(.50 + .17 * Math.Sin(row * .049 + phase) +
                    .15 * Math.Sin(row * .117 + phase * .8) + .13 * Math.Cos(row * .189 - phase * 1.2), 0, 1);
            }).ToArray();
        PairDecision Analyze(double[] first, double[] second) => PairAnalyzer.AnalyzePixels(first, width, height, second, width, height);
        Check("automatic pair accepts rich seam in either physical order", () =>
        {
            var left = Page(false);
            var right = Page(true);
            var forward = Analyze(left, right);
            var reverse = Analyze(right, left);
            True(forward.Automatic && !forward.Swapped, "continuous seam accepted in source order");
            True(reverse.Automatic && reverse.Swapped, "reverse source order restores physical placement");
        });
        Check("pair alignment recovers generated vertical offset and scale", () =>
        {
            var left = Page(false);
            var shifted = Analyze(left, Page(true, shift: 8));
            True(Math.Abs(shifted.VerticalOffset + 8.0 / height) < .003, "vertical offset recovered");
            var scaled = Analyze(left, Page(true, shift: 35, scale: .94));
            True(Math.Abs(scaled.RightScale - 1 / .94) < .009, "scale recovered");
            True(Math.Abs(scaled.VerticalOffset + 35.0 / height / .94) < .009, "scaled offset recovered");
        });
        Check("automatic pair rejects blank duplicate smooth and invalid inputs", () =>
        {
            var left = Page(false);
            var blank = Enumerable.Repeat(1.0, width * height).ToArray();
            True(!Analyze(left, left).Automatic, "duplicate rejected");
            True(!Analyze(blank, blank).Automatic, "blank rejected");
            True(!Analyze(Page(false, frequency: .05), Page(true, frequency: .05)).Automatic, "smooth border rejected");
            True(!PairAnalyzer.AnalyzePixels([.1, .2, .3], 2, 2, [.1, .2, .3, .4], 2, 2).Automatic, "invalid dimensions rejected");
        });
    }

    private static void StorageChecks(string root)
    {
        Check("library persists progress bookmarks preferences and corrections", () =>
        {
            var directory = Path.Combine(root, "library");
            var store = new LibraryStore(directory);
            var saved = Settings("double", "rtl");
            saved.Id = "generated-book";
            saved.Path = Path.Combine(root, "水漫 演示.zip");
            saved.Title = "水漫 测试";
            saved.Position = 2;
            saved.Total = 8;
            saved.LocatorKey = "2:0:漫画/2.png";
            saved.Favorite = true;
            saved.Bookmarks.Add(saved.LocatorKey);
            saved.Overrides[saved.LocatorKey] = new() { Rotation = 90, Standalone = true, PairOffset = -.03, PairScale = .96 };
            store.Save(saved);
            var restored = new LibraryStore(directory).Books.Single();
            Equal(2, restored.Position, "position");
            Equal(saved.LocatorKey, restored.LocatorKey, "stable locator");
            Equal("rtl", restored.Preferences.Direction, "direction");
            Equal("double", restored.Preferences.Layout, "layout");
            True(restored.Favorite && restored.Bookmarks.Contains(saved.LocatorKey), "favorite and bookmark");
            Equal(90, restored.Overrides[saved.LocatorKey].Rotation, "manual rotation");
            store.Remove(saved.Id);
            Equal(0, new LibraryStore(directory).Books.Count, "remove persisted");
            True(File.Exists(saved.Path), "removing library record preserves original comic");
        });
        Check("corrupt library recovers backup with warning", () =>
        {
            var directory = Path.Combine(root, "library-recovery");
            var store = new LibraryStore(directory);
            store.Save(new SavedBook { Id = "first", Path = "generated-first.zip", Title = "First" });
            store.Save(new SavedBook { Id = "second", Path = "generated-second.zip", Title = "Second" });
            File.WriteAllText(Path.Combine(directory, "library.json"), "{broken json");
            var recovered = new LibraryStore(directory);
            True(recovered.Books.Any(b => b.Id == "first"), "previous valid snapshot recovered");
            True(!string.IsNullOrWhiteSpace(recovered.Warning), "recovery warning exposed");
        });
        Check("semantically corrupt library recovers usable backup", () =>
        {
            var directory = Path.Combine(root, "library-null-record");
            var store = new LibraryStore(directory);
            store.Save(new SavedBook { Id = "first", Title = "First" });
            store.Save(new SavedBook { Id = "second", Title = "Second" });
            File.WriteAllText(Path.Combine(directory, "library.json"), "[{\"Id\":\"bad\",\"Title\":null}]");
            var recovered = new LibraryStore(directory);
            True(recovered.Books.Any(b => b.Id == "first"), "invalid null record falls back to valid backup");
            True(!string.IsNullOrWhiteSpace(recovered.Warning), "semantic corruption warning exposed");
        });
        Check("library scanner discovers ZIP and CBZ", () =>
        {
            var books = LibraryScanner.Scan(root);
            True(books.Any(b => b.Path.EndsWith("水漫 演示.zip", StringComparison.OrdinalIgnoreCase)), "ZIP discovered");
            True(books.Any(b => b.Path.EndsWith("demo.cbz", StringComparison.OrdinalIgnoreCase)), "CBZ discovered");
            Equal(books.Count, books.Select(b => b.Id).Distinct().Count(), "scanner deduplicates identities");
        });
        Check("library batch import merges and isolates mutable snapshots", () =>
        {
            var directory = Path.Combine(root, "library-batch");
            var store = new LibraryStore(directory);
            store.Save(new SavedBook { Id = "first", Title = "Original" });
            store.SaveMany([new SavedBook { Id = "first", Title = "Updated" }, new SavedBook { Id = "second", Title = "Second" }]);
            Equal(2, store.Books.Count, "batch merge count");
            Equal("Updated", store.Books.Single(b => b.Id == "first").Title, "matching record updated");
            store.Books.Single(b => b.Id == "first").Title = "Unsaved external mutation";
            Equal("Updated", store.Books.Single(b => b.Id == "first").Title, "snapshot does not mutate store");
            Equal(2, new LibraryStore(directory).Books.Count, "batch persisted");
        });
        Check("scanner avoids overlapping recursive image volumes", () =>
        {
            var directory = Path.Combine(root, "scan-image-volume");
            Directory.CreateDirectory(Path.Combine(directory, "pages"));
            File.Copy(Path.Combine(root, "single.png"), Path.Combine(directory, "cover.png"));
            File.Copy(Path.Combine(root, "single.png"), Path.Combine(directory, "pages", "1.png"));
            var books = LibraryScanner.Scan(directory);
            Equal(1, books.Count, "parent image volume excludes duplicate child volume");
            Equal(Path.GetFullPath(directory), books[0].Path, "parent becomes recursive image volume");
            Equal(2, books[0].Total, "recursive image file count");
        });
        Check("independent library instances merge saves without resurrection", () =>
        {
            var directory = Path.Combine(root, "library-multiple-windows");
            var first = new LibraryStore(directory);
            var second = new LibraryStore(directory);
            first.Save(new SavedBook { Id = "a", Title = "First" });
            second.Save(new SavedBook { Id = "b", Title = "Second" });
            first.Save(new SavedBook { Id = "a", Title = "Updated first" });
            Equal(2, first.Books.Count, "first instance preserves other window record");
            Equal(2, second.Books.Count, "second instance reads fresh records");
            Equal("Updated first", second.Books.Single(b => b.Id == "a").Title, "other instance sees update");
            first.Remove("b");
            second.Save(new SavedBook { Id = "a", Title = "Updated again" });
            Equal(1, first.Books.Count, "stale instance does not resurrect removed record");
            Equal("a", first.Books.Single().Id, "intended remaining record");
        });
        Check("scanner preserves nested books beneath loose cover image", () =>
        {
            var directory = Path.Combine(root, "scan-book-volume");
            Directory.CreateDirectory(Path.Combine(directory, "books"));
            File.Copy(Path.Combine(root, "single.png"), Path.Combine(directory, "cover.png"));
            var epub = Path.Combine(directory, "books", "chapter.epub");
            File.Copy(Path.Combine(root, "spine.epub"), epub);
            var books = LibraryScanner.Scan(directory);
            Equal(1, books.Count, "loose cover does not hide actual book");
            Equal(epub, books[0].Path, "nested EPUB discovered");
        });
    }

    private static Publication Pages(int count) => new()
    {
        Units = Enumerable.Range(0, count).Select(i => new ReadingUnit
        {
            Locator = new($"{i}.png"), Width = 600, Height = 900, IsCover = i == 0
        }).ToList()
    };
    private static SavedBook Settings(string layout, string direction) => new()
    {
        Preferences = new() { Layout = layout, Direction = direction, CoverAlone = true, SmartSpreads = true, AutomaticPairs = true }
    };
    private static byte[] Pixels(BitmapSource source)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var bytes = new byte[converted.PixelWidth * converted.PixelHeight * 4];
        converted.CopyPixels(bytes, converted.PixelWidth * 4, 0);
        return bytes;
    }
    private static void ExpectOpenFailure(string path)
    {
        try { using var book = Open(path); }
        catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
        {
            True(!string.IsNullOrWhiteSpace(ex.Message), "useful error message");
            return;
        }
        throw new InvalidOperationException("Expected a clear import error.");
    }
    private static void Check(string name, Action body)
    {
        try { body(); passed++; Console.WriteLine($"PASS {name}"); }
        catch (Exception ex) { failed++; Console.Error.WriteLine($"FAIL {name}: {ex}"); }
    }
    private static void True(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{message}: expected {expected}, actual {actual}");
    }
    private static void Sequence<T>(IEnumerable<T> expected, IEnumerable<T> actual, string message)
    {
        var a = expected.ToArray();
        var b = actual.ToArray();
        if (!a.SequenceEqual(b)) throw new InvalidOperationException($"{message}: expected [{string.Join(",", a)}], actual [{string.Join(",", b)}]");
    }
}

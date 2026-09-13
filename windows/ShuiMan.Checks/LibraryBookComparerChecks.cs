using ShuiMan.Core;

namespace ShuiMan.Checks;

internal static partial class Program
{
    private static void LibraryBookComparerChecks()
    {
        Check("catalog volume order follows mac markers and decimal installments", () =>
        {
            Equal(1d, LibraryBookComparer.VolumeOrder("原创漫画卷01"), "Chinese volume marker");
            Equal(2d, LibraryBookComparer.VolumeOrder("Original Comic VOL. 02"), "case-insensitive abbreviated volume marker");
            Equal(3.5d, LibraryBookComparer.VolumeOrder("Original Comic volume 3.5"), "decimal full volume marker");
            Equal(0d, LibraryBookComparer.VolumeOrder("原创漫画 第十册"), "unmatched names use the mac zero fallback");
        });

        Check("catalog volume number takes precedence over unrelated filename digits", () =>
        {
            var books = new[]
            {
                NamedVolume("[Edition 1999]原创漫画卷10"),
                NamedVolume("[Edition 2024]原创漫画卷02"),
                NamedVolume("[Edition 2010]原创漫画卷1.5"),
                NamedVolume("[Edition 2020]原创漫画卷01")
            };
            Sequence(new[] { 3, 2, 1, 0 }, books.Order(LibraryBookComparer.NaturalVolume).Select(book => Array.IndexOf(books, book)),
                "explicit volume order wins over publisher/year prefixes");
        });

        Check("catalog volume order survives display rename and decimal image-folder names", () =>
        {
            var first = NamedVolume("原创漫画卷01", "后来改的书名 Z");
            var second = NamedVolume("原创漫画卷02", "后来改的书名 A");
            True(LibraryBookComparer.NaturalVolume.Compare(first, second) < 0, "manual display title keeps original installment position");
            var folder = new SavedBook { Path = @"D:\generated-catalog\Volume 1.5\", Title = "图片卷" };
            True(LibraryBookComparer.NaturalVolume.Compare(first, folder) < 0 && LibraryBookComparer.NaturalVolume.Compare(folder, second) < 0,
                "a folder's decimal suffix is not treated as a file extension");
            Equal("后来改的书名 Z", first.Title, "comparison never edits metadata");
        });

        Check("catalog volume ties use natural display title and stable source path", () =>
        {
            var second = NamedVolume("无卷号 A", "特别篇 2");
            var tenth = NamedVolume("无卷号 B", "特别篇 10");
            True(LibraryBookComparer.NaturalVolume.Compare(second, tenth) < 0, "natural title fallback");
            var a = NamedVolume("来源 A 卷02", "同名分卷");
            var b = NamedVolume("来源 B 卷02", "同名分卷");
            True(LibraryBookComparer.NaturalVolume.Compare(a, b) < 0 && LibraryBookComparer.NaturalVolume.Compare(b, a) > 0,
                "equal number and title use a stable path tie-break");
            Equal(0, LibraryBookComparer.NaturalVolume.Compare(a, a), "same record comparison");
        });
    }

    private static SavedBook NamedVolume(string sourceName, string? displayTitle = null) => new()
    {
        Path = @"D:\generated-catalog\" + sourceName + ".epub",
        Title = displayTitle ?? sourceName,
        Series = "原创系列"
    };
}

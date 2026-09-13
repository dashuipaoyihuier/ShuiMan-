using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace ShuiMan.Core;

/// <summary>macOS catalog order: explicit volume number, natural display title, then source path.</summary>
public sealed partial class LibraryBookComparer : IComparer<SavedBook>
{
    public static LibraryBookComparer NaturalVolume { get; } = new();

    public int Compare(SavedBook? x, SavedBook? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x == null) return -1;
        if (y == null) return 1;
        var order = VolumeOrder(SourceName(x)).CompareTo(VolumeOrder(SourceName(y)));
        if (order != 0) return order;
        var title = NaturalPathComparer.Instance.Compare(x.Title, y.Title);
        return title != 0 ? title : StringComparer.Ordinal.Compare(x.Path, y.Path);
    }

    // Catalog.swift stores order when discovering the original source. Inferring it
    // from that same name preserves the position when a user edits the display title.
    private static string SourceName(SavedBook book)
    {
        if (string.IsNullOrWhiteSpace(book.Path)) return book.Title;
        var name = Path.GetFileName(book.Path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrEmpty(name)) return book.Title;
        return DocumentEngine.SupportedExtensions.Contains(Path.GetExtension(name))
            ? Path.GetFileNameWithoutExtension(name) : name;
    }

    public static double VolumeOrder(string? name)
    {
        var match = VolumeNumber().Match(name ?? "");
        return match.Success && double.TryParse(match.Groups[1].Value,
            NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var order) ? order : 0;
    }

    // Keep this expression equivalent to Sources/ComicCore/Catalog.swift.
    [GeneratedRegex(@"(?:卷|vol\.?\s*|volume\s*)([0-9]+(?:\.[0-9]+)?)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VolumeNumber();
}

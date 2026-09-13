using System.Windows.Media.Imaging;

namespace ShuiMan.Core;

public record SourceLocator(string Resource, int Occurrence = 0, int ImageIndex = 0)
{
    public string Key => $"{Occurrence}:{ImageIndex}:{Resource}";
}
public sealed class ReadingUnit
{
    public required SourceLocator Locator { get; init; }
    public string Id => Locator.Key;
    public string Title { get; set; } = "";
    public string? ImagePath { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public bool IsCover { get; set; }
    public int? RotationHint { get; set; }
    public bool Complex { get; set; }
    public string? Error { get; set; }
}
public record NavigationItem(string Title, int Index);
public sealed class Publication
{
    public string SourcePath { get; set; } = "";
    public string Identity { get; set; } = "";
    public string Revision { get; set; } = "";
    public string Title { get; set; } = "";
    public string Kind { get; set; } = "images";
    public List<ReadingUnit> Units { get; set; } = [];
    public string? Direction { get; set; }
    public List<NavigationItem> Navigation { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}
public interface IOpenBook : IDisposable
{
    Publication Publication { get; }
    Task<BitmapSource> RenderAsync(int index, int maxEdge = 2400, CancellationToken cancellationToken = default);
    byte[]? Resource(string path);
}
public sealed class PasswordRequiredException : Exception
{
    public PasswordRequiredException() : base("这份 PDF 需要密码，或密码不正确。") { }
}
public sealed class ReaderPreferences
{
    public string Direction { get; set; } = "ltr";
    public string Layout { get; set; } = "single";
    public bool SmartSpreads { get; set; } = true;
    public bool CoverAlone { get; set; } = true;
    public bool AutomaticPairs { get; set; } = true;
    public bool AggressivePairs { get; set; } = true;
    public bool AutomaticOrientation { get; set; } = true;
    public string Fit { get; set; } = "page";
    public bool DarkBackground { get; set; } = true;
}
public sealed class PageOverride
{
    public int? Rotation { get; set; }
    public bool? Standalone { get; set; }
    public bool? JoinNext { get; set; }
    public bool EarlierOnRight { get; set; }
    public bool PairingBreak { get; set; }
    public double PairOffset { get; set; }
    public double PairScale { get; set; } = 1;
}
public sealed class SavedBook
{
    public string Id { get; set; } = "";
    public string Path { get; set; } = "";
    public string Title { get; set; } = "";
    public string Series { get; set; } = "";
    public List<string> Tags { get; set; } = [];
    public DateTime AddedAt { get; set; } = DateTime.MinValue;
    public long MetadataVersion { get; set; }
    public string Revision { get; set; } = "";
    public string? LocatorKey { get; set; }
    public int Position { get; set; }
    public int Total { get; set; }
    public bool Favorite { get; set; }
    public string ReadState { get; set; } = "未读";
    public DateTime OpenedAt { get; set; } = DateTime.UtcNow;
    public ReaderPreferences Preferences { get; set; } = new();
    public Dictionary<string, PageOverride> Overrides { get; set; } = [];
    public HashSet<string> Bookmarks { get; set; } = [];
}
public record SpreadDecision(int Rotation = 0, bool Standalone = false, bool Uncertain = false, string Reason = "");
public record PairDecision(int FirstIndex, bool Automatic = false, bool Swapped = false, double Score = 0, double VerticalOffset = 0, double RightScale = 1,
    bool Suggested = false, double Correlation = 0, double DetailCorrelation = 0, double MeanError = 1, int MatchingBands = 0, double PlacementMargin = 0);
public record DisplayGroup(int[] Indices, bool Spread = false, double VerticalOffset = 0, double RightScale = 1)
{
    public int FirstSourceIndex => Indices.Min();
}

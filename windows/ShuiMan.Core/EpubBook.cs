using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace ShuiMan.Core;

internal sealed class EpubBook : RasterBook
{
    private readonly BookArchive archive;
    private readonly Dictionary<string, (RasterInfo? Info, string? Error)> imageInfo = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> stylesheets = new(StringComparer.Ordinal);
    private long stylesheetBytes;
    private record ManifestItem(string Id, string Href, string MediaType, string[] Properties);
    private record CssRule(string Selector, string Declarations, string Source, int Order);
    private record StyleBlock(string Declarations, string Source, int Specificity, int Order);
    // The locator's image ordinal identifies an image occurrence within XHTML,
    // not a TIFF frame inside that separate image resource.
    protected override int FrameIndex(ReadingUnit unit) => 0;

    public EpubBook(string path, CancellationToken token)
    {
        Publication = DocumentEngine.Describe(path, "epub");
        archive = new BookArchive(path);
        try { Parse(token); }
        catch { archive.Dispose(); throw; }
    }

    private void Parse(CancellationToken token)
    {
        var container = Xml(archive.Bytes("META-INF/container.xml", 1024 * 1024));
        var packagePath = BookArchive.Normalize(Elements(container, "rootfile").FirstOrDefault()?.Attribute("full-path")?.Value
            ?? throw new InvalidDataException("EPUB 缺少包文档入口。"));
        var package = Xml(archive.Bytes(packagePath, 16 * 1024 * 1024));
        var manifestNode = Elements(package, "manifest").FirstOrDefault() ?? throw new InvalidDataException("EPUB 缺少 manifest。");
        var manifest = new Dictionary<string, ManifestItem>(StringComparer.Ordinal);
        foreach (var element in manifestNode.Elements().Where(x => x.Name.LocalName == "item"))
        {
            string id = Attr(element, "id"), href = Attr(element, "href");
            if (id.Length == 0 || href.Length == 0) continue;
            if (!manifest.TryAdd(id, new ManifestItem(id, href, Attr(element, "media-type").ToLowerInvariant(), Tokens(Attr(element, "properties")))))
                throw new InvalidDataException("EPUB manifest 包含重复 id。");
        }
        var title = Elements(package, "title").FirstOrDefault()?.Value.Trim();
        if (!string.IsNullOrWhiteSpace(title)) Publication.Title = title;
        var spine = Elements(package, "spine").FirstOrDefault() ?? throw new InvalidDataException("EPUB 缺少 spine 阅读顺序。");
        string direction = Attr(spine, "page-progression-direction");
        Publication.Direction = direction is "rtl" or "ltr" ? direction : null;
        var coverId = Elements(package, "meta").FirstOrDefault(x => Attr(x, "name") == "cover")?.Attribute("content")?.Value;
        var coverItem = coverId != null ? manifest.GetValueOrDefault(coverId) : null;
        coverItem ??= manifest.Values.FirstOrDefault(x => x.Properties.Contains("cover-image"));
        string? coverPath = coverItem != null ? TryResolve(coverItem.Href, packagePath) : null;
        int occurrence = -1;
        foreach (var itemRef in spine.Elements().Where(x => x.Name.LocalName == "itemref"))
        {
            token.ThrowIfCancellationRequested();
            occurrence++;
            if (occurrence >= 100_000) throw new InvalidDataException("EPUB 阅读顺序包含过多页面。");
            if (Attr(itemRef, "linear").Equals("no", StringComparison.OrdinalIgnoreCase)) continue;
            string idref = Attr(itemRef, "idref");
            if (!manifest.TryGetValue(idref, out var item))
            {
                AddMissing($"missing-spine-{occurrence}", occurrence, $"spine 引用不存在：{idref}");
                continue;
            }
            string? contentPath = TryResolve(item.Href, packagePath);
            if (contentPath == null) { AddMissing($"invalid-spine-{occurrence}", occurrence, "正文资源路径无效。"); continue; }
            if (!archive.Contains(contentPath)) { AddMissing(contentPath, occurrence, "内容文档无法读取。"); continue; }
            if (item.MediaType.StartsWith("image/") && item.MediaType != "image/svg+xml")
            {
                var (info, error) = ImageInfo(contentPath);
                Publication.Units.Add(new ReadingUnit
                {
                    Locator = new SourceLocator(contentPath, occurrence), ImagePath = contentPath,
                    Title = contentPath == coverPath ? "封面" : $"第 {Publication.Units.Count + 1} 页",
                    Width = info?.Width ?? 0, Height = info?.Height ?? 0, IsCover = contentPath == coverPath, Error = error
                });
                continue;
            }
            byte[] bytes;
            try { bytes = archive.Bytes(contentPath, 16 * 1024 * 1024); }
            catch (Exception ex) when (ex is IOException or InvalidDataException) { AddMissing(contentPath, occurrence, ex.Message); continue; }
            var document = new HtmlParser().ParseDocument(DecodeMarkup(bytes));
            var rules = new List<CssRule>();
            foreach (var node in document.QuerySelectorAll("style, link[rel~='stylesheet']"))
            {
                string sheetPath = node.LocalName == "style" ? contentPath : TryResolve(node.GetAttribute("href") ?? "", contentPath) ?? "";
                string? sheet = node.LocalName == "style" ? node.TextContent : sheetPath.Length > 0 ? Stylesheet(sheetPath) : null;
                if (sheet != null) ReadRules(sheet, sheetPath, rules);
            }
            string pageTitle = document.Title?.Trim() ?? "";
            var matchedStyles = MatchStyles(document, rules);
            int imageIndex = 0;
            var body = document.Body;
            foreach (var element in body == null ? document.All : new[] { body }.Concat(body.QuerySelectorAll("*")))
            {
                token.ThrowIfCancellationRequested();
                if (element.LocalName is "script" or "style" or "link" or "meta") continue;
                var styles = StylesFor(element, matchedStyles, contentPath);
                var (background, backgroundSource) = DeclarationValue(styles, @"background(?:-image)?");
                if (background != null)
                    foreach (Match reference in Regex.Matches(background, "url\\(\\s*(?:\"(?<url>[^\"]*)\"|'(?<url>[^']*)'|(?<url>[^)\\s]*))\\s*\\)", RegexOptions.IgnoreCase))
                        AddImage(reference.Groups["url"].Value, backgroundSource ?? contentPath, element);
                if (element.LocalName == "img")
                    AddImage(element.GetAttribute("src") ?? "", contentPath, element);
                else if (element.LocalName == "image" && element.NamespaceUri == "http://www.w3.org/2000/svg")
                    AddImage(element.GetAttribute("href") ?? element.GetAttribute("xlink:href") ?? element.Attributes.FirstOrDefault(attribute => attribute.LocalName == "href")?.Value ?? "", contentPath, element);
            }
            if (imageIndex == 0)
                AddMissing(contentPath, occurrence, "此阅读位置没有可显示的漫画图片。Windows 版仅阅读 EPUB 图片，不提供文字网页排版。");
            else if (HasReadableText(body))
                Publication.Warnings.Add("这本 EPUB 含图片之外的文字；Windows 版只显示漫画图片，不提供文字网页排版。");

            void AddImage(string reference, string relativeTo, IElement element)
            {
                if (Publication.Units.Count >= 100_000) throw new InvalidDataException("EPUB 包含过多漫画图片。");
                string? imagePath = string.IsNullOrWhiteSpace(reference) ? null : TryResolve(reference, relativeTo);
                var (dimensions, imageError) = imagePath != null ? ImageInfo(imagePath) : (null, "页面图片路径无效。");
                bool cover = imagePath != null && imagePath == coverPath;
                Publication.Units.Add(new ReadingUnit
                {
                    Locator = new SourceLocator(contentPath, occurrence, imageIndex++), ImagePath = imagePath,
                    Title = cover ? "封面" : pageTitle.Length > 0 ? pageTitle : $"第 {Publication.Units.Count + 1} 页",
                    Width = dimensions?.Width ?? 0, Height = dimensions?.Height ?? 0,
                    IsCover = cover, Complex = false, RotationHint = RotationFor(element, matchedStyles, contentPath), Error = imageError
                });
            }
        }
        if (Publication.Units.Count == 0) throw new InvalidDataException("这本 EPUB 没有可阅读的正文内容。");
        foreach (var item in manifest.Values.Where(x => x.MediaType == "application/x-dtbncx+xml" || x.Properties.Contains("nav")))
        {
            token.ThrowIfCancellationRequested();
            var tocPath = TryResolve(item.Href, packagePath);
            if (tocPath == null || !archive.Contains(tocPath)) continue;
            try
            {
                var tocBytes = archive.Bytes(tocPath, 4 * 1024 * 1024);
                if (item.MediaType == "application/x-dtbncx+xml")
                {
                    foreach (var point in Elements(Xml(tocBytes), "navPoint"))
                    {
                        var target = point.Descendants().FirstOrDefault(x => x.Name.LocalName == "content")?.Attribute("src")?.Value;
                        var label = point.Descendants().FirstOrDefault(x => x.Name.LocalName == "text")?.Value;
                        AddNavigation(target, label, tocPath);
                    }
                }
                else
                {
                    var doc = new HtmlParser().ParseDocument(DecodeMarkup(tocBytes));
                    var navs = doc.QuerySelectorAll("nav");
                    var nav = navs.FirstOrDefault(x => Tokens(x.GetAttribute("epub:type") ?? "").Contains("toc")) ?? navs.FirstOrDefault();
                    if (nav != null) foreach (var anchor in nav.QuerySelectorAll("a[href]"))
                        AddNavigation(anchor.GetAttribute("href"), anchor.TextContent, tocPath);
                }
            }
            catch (Exception ex) when (ex is IOException or XmlException or InvalidDataException)
            { Publication.Warnings.Add($"部分目录无法读取：{ex.Message}"); }
        }
        if (archive.Contains("META-INF/encryption.xml")) Publication.Warnings.Add("出版物含加密或字体混淆声明；受保护的资源可能无法显示。");
        if (Publication.Units.Any(x => x.Error != null)) Publication.Warnings.Add("部分 EPUB 阅读位置暂不可显示，已保留原 spine 与图片顺序。");
    }

    private string? Stylesheet(string path)
    {
        if (stylesheets.TryGetValue(path, out var text)) return text;
        if (!archive.Contains(path)) return null;
        byte[] bytes;
        try { bytes = archive.Bytes(path, 2 * 1024 * 1024); }
        catch (IOException) { return null; }
        stylesheetBytes += bytes.Length;
        if (stylesheetBytes > 64L * 1024 * 1024) throw new InvalidDataException("EPUB 样式资源超过 64 MiB 解析预算。");
        return stylesheets[path] = DecodeMarkup(bytes);
    }
    private (RasterInfo? Info, string? Error) ImageInfo(string path)
    {
        if (imageInfo.TryGetValue(path, out var result)) return result;
        try { result = (RasterDecoder.Describe(archive.Bytes(path)).First(), null); }
        catch (Exception ex) when (ex is not OperationCanceledException) { result = (null, $"页面图片无法读取：{ex.Message}"); }
        return imageInfo[path] = result;
    }
    private void AddMissing(string resource, int occurrence, string error) => Publication.Units.Add(new ReadingUnit
    { Locator = new SourceLocator(resource, occurrence), Title = "缺失页面", Error = error });
    private void AddNavigation(string? target, string? title, string relativeTo)
    {
        if (target == null) return;
        var path = TryResolve(target, relativeTo);
        int index = Publication.Units.FindIndex(x => x.Locator.Resource == path);
        if (index >= 0) Publication.Navigation.Add(new NavigationItem(string.IsNullOrWhiteSpace(title) ? $"第 {index + 1} 页" : title.Trim(), index));
    }
    private static void ReadRules(string css, string source, List<CssRule> rules)
    {
        // CSS is used only to locate local bitmap references and right-angle hints.
        // It is never executed or treated as a request to render a webpage.
        css = Regex.Replace(css, @"/\*.*?\*/", "", RegexOptions.Singleline);
        foreach (Match block in Regex.Matches(css, @"(?<selector>[^{}]+)\{(?<declarations>[^{}]*)\}"))
        {
            var selectorText = block.Groups["selector"].Value.Trim();
            if (selectorText.StartsWith('@')) continue;
            foreach (var selector in selectorText.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                if (rules.Count >= 20_000) throw new InvalidDataException("EPUB 样式规则过多。");
                rules.Add(new CssRule(selector, block.Groups["declarations"].Value, source, rules.Count));
            }
        }
    }
    private static Dictionary<IElement, List<StyleBlock>> MatchStyles(IDocument document, IEnumerable<CssRule> rules)
    {
        var matches = new Dictionary<IElement, List<StyleBlock>>();
        foreach (var rule in rules)
        {
            if (!Regex.IsMatch(rule.Declarations, @"background|transform", RegexOptions.IgnoreCase)) continue;
            // CSS specificity is only needed to choose between simple publisher
            // image rules. Unsupported selectors contribute no invented images.
            int specificity = Regex.Matches(rule.Selector, @"#[\w-]+").Count * 10_000 +
                Regex.Matches(rule.Selector, @"\.[\w-]+|\[[^]]+\]|:(?!:)[\w-]+").Count * 100 +
                Regex.Matches(rule.Selector, @"(?:^|[\s>+~])\s*[a-zA-Z][\w-]*").Count;
            try
            {
                foreach (var element in document.QuerySelectorAll(rule.Selector))
                {
                    if (!matches.TryGetValue(element, out var values)) matches[element] = values = [];
                    values.Add(new StyleBlock(rule.Declarations, rule.Source, specificity, rule.Order));
                }
            }
            catch (DomException) { }
        }
        return matches;
    }
    private static List<StyleBlock> StylesFor(IElement element, Dictionary<IElement, List<StyleBlock>> styles, string contentPath)
    {
        var result = styles.TryGetValue(element, out var matched)
            ? matched.OrderBy(block => block.Specificity).ThenBy(block => block.Order).ToList() : [];
        if (element.GetAttribute("style") is { Length: > 0 } inline)
            result.Add(new StyleBlock(inline, contentPath, int.MaxValue, int.MaxValue));
        return result;
    }
    private static int? RotationFor(IElement image, Dictionary<IElement, List<StyleBlock>> styles, string contentPath)
    {
        int total = 0;
        bool found = false;
        for (IElement? element = image; element != null; element = element.ParentElement)
        {
            var (transform, _) = DeclarationValue(StylesFor(element, styles, contentPath), @"(?:-[a-z]+-)?transform");
            int? rotation = transform == null ? null : transform.Equals("none", StringComparison.OrdinalIgnoreCase) ? 0 : Rotation("transform:" + transform);
            if (transform != null && rotation == null) return null;
            if (transform == null && element.NamespaceUri == "http://www.w3.org/2000/svg" && element.GetAttribute("transform") is { } svg)
            {
                var match = Regex.Match(svg, @"^\s*rotate\(\s*([-+]?\d+(?:\.\d+)?)(?:[\s,]+[-+]?\d+(?:\.\d+)?[\s,]+[-+]?\d+(?:\.\d+)?)?\s*\)\s*$", RegexOptions.IgnoreCase);
                if (match.Success) rotation = Rotation("transform:rotate(" + match.Groups[1].Value + "deg)");
                if (rotation == null) return null;
            }
            if (rotation != null) { total += rotation.Value; found = true; }
        }
        return found ? (total % 360 + 360) % 360 : null;
    }
    private static (string? Value, string? Source) DeclarationValue(IEnumerable<StyleBlock> blocks, string propertyPattern)
    {
        string? value = null, source = null;
        bool important = false;
        foreach (var block in blocks)
            foreach (Match declaration in Regex.Matches(block.Declarations, @"(?:^|;)\s*(?:" + propertyPattern + @")\s*:\s*(?<value>[^;]*)", RegexOptions.IgnoreCase))
            {
                string candidate = declaration.Groups["value"].Value.Trim();
                bool candidateImportant = Regex.IsMatch(candidate, @"!\s*important\s*$", RegexOptions.IgnoreCase);
                if (important && !candidateImportant) continue;
                value = Regex.Replace(candidate, @"!\s*important\s*$", "", RegexOptions.IgnoreCase).Trim();
                source = block.Source; important = candidateImportant;
            }
        return (value, source);
    }
    private static bool HasReadableText(INode? node)
    {
        if (node == null || node is IElement { LocalName: "script" or "style" or "noscript" or "title" or "desc" }) return false;
        if (node is IText) return !string.IsNullOrWhiteSpace(node.TextContent);
        return node.ChildNodes.Any(HasReadableText);
    }
    private static int? Rotation(string? style)
    {
        if (string.IsNullOrWhiteSpace(style)) return null;
        if (Regex.Matches(style, @"(?:^|;)\s*(?:-\w+-)?transform\s*:", RegexOptions.IgnoreCase).Count != 1) return null;
        var match = Regex.Match(style, @"(?:^|;)\s*(?:-webkit-)?transform\s*:\s*rotate\(\s*([-+]?\d+(?:\.\d+)?)\s*(deg|turn|rad)\s*\)\s*(?:!important\s*)?(?:;|$)", RegexOptions.IgnoreCase);
        if (!match.Success || !double.TryParse(match.Groups[1].Value, CultureInfo.InvariantCulture, out double angle)) return null;
        angle *= match.Groups[2].Value.ToLowerInvariant() switch { "turn" => 360, "rad" => 180 / Math.PI, _ => 1 };
        double nearest = Math.Round(angle / 90) * 90;
        if (Math.Abs(angle - nearest) > .01) return null;
        return ((int)nearest % 360 + 360) % 360;
    }
    private static string DecodeMarkup(byte[] bytes)
    {
        using var input = new MemoryStream(bytes);
        using var reader = new StreamReader(input, Encoding.UTF8, true);
        return reader.ReadToEnd();
    }
    private static XDocument Xml(byte[] bytes)
    {
        using var input = new MemoryStream(bytes);
        using var reader = XmlReader.Create(input, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null, MaxCharactersInDocument = 16 * 1024 * 1024 });
        return XDocument.Load(reader);
    }
    private static IEnumerable<XElement> Elements(XDocument document, string name) => document.Descendants().Where(x => x.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase));
    private static string Attr(XElement element, string name) => element.Attribute(name)?.Value ?? "";
    private static string[] Tokens(string text) => text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
    private static string? TryResolve(string reference, string relativeTo) { try { return BookArchive.Resolve(reference, relativeTo); } catch (Exception ex) when (ex is InvalidDataException or UriFormatException) { return null; } }
    public override byte[]? Resource(string path)
    {
        ObjectDisposedException.ThrowIf(Disposed, this);
        var normalized = BookArchive.Normalize(path);
        return archive.Contains(normalized) ? archive.Bytes(normalized) : null;
    }
    public override void Dispose() { base.Dispose(); archive.Dispose(); }
}

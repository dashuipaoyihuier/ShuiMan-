import Foundation
import SwiftSoup

private final class PackageXML: NSObject, XMLParserDelegate {
    var rootfiles: [String] = []
    var manifest: [String: [String: String]] = [:]
    var spine: [[String: String]] = []
    var title = ""
    var direction: ReadingDirection?
    var coverID: String?
    var element = ""
    func parser(_ parser: XMLParser, didStartElement elementName: String, namespaceURI: String?, qualifiedName qName: String?, attributes: [String: String]) {
        element = elementName.components(separatedBy: ":").last ?? elementName
        switch element {
        case "rootfile": if let path = attributes["full-path"] { rootfiles.append(path) }
        case "item": if let id = attributes["id"] { manifest[id] = attributes }
        case "itemref": spine.append(attributes)
        case "spine": direction = attributes["page-progression-direction"].flatMap(ReadingDirection.init(rawValue:))
        case "meta": if attributes["name"] == "cover" { coverID = attributes["content"] }
        default: break
        }
    }
    func parser(_ parser: XMLParser, foundCharacters string: String) { if element == "title" { title += string } }
    func parser(_ parser: XMLParser, didEndElement elementName: String, namespaceURI: String?, qualifiedName qName: String?) { element = "" }
    static func read(_ data: Data) throws -> PackageXML {
        let delegate = PackageXML(); let parser = XMLParser(data: data)
        parser.shouldResolveExternalEntities = false; parser.delegate = delegate
        guard parser.parse() else { throw ReaderError.message("EPUB 结构 XML 无法解析：\(parser.parserError?.localizedDescription ?? "未知错误")") }
        return delegate
    }
}

public enum EPUBParser {
    public static func parse(archive: BookArchive) throws -> (String, [ReadingUnit], ReadingDirection?, [NavigationItem], [String]) {
        let container = try PackageXML.read(archive.data("META-INF/container.xml", limit: 1024 * 1024))
        guard let packagePath = container.rootfiles.first else { throw ReaderError.message("EPUB 缺少包文档入口。") }
        let package = try PackageXML.read(archive.data(try BookArchive.normalize(packagePath), limit: 16 * 1024 * 1024))
        let coverItem = package.coverID.flatMap { package.manifest[$0] } ?? package.manifest.values.first { $0["properties"]?.contains("cover-image") == true }
        let coverImage = try coverItem?["href"].map { try BookArchive.resolve($0, relativeTo: packagePath) }
        var stylesheets: [String: String] = [:]
        for item in package.manifest.values where item["media-type"] == "text/css" {
            if let href = item["href"], let path = try? BookArchive.resolve(href, relativeTo: packagePath), let data = try? archive.data(path, limit: 2 * 1024 * 1024) {
                stylesheets[path] = String(decoding: data, as: UTF8.self)
            }
        }
        var units: [ReadingUnit] = []; var warnings: [String] = []
        for (occurrence, item) in package.spine.enumerated() {
            try Task.checkCancellation()
            if item["linear"] == "no" { continue }
            guard let ref = item["idref"], let manifest = package.manifest[ref], let href = manifest["href"] else {
                units.append(ReadingUnit(locator: SourceLocator(resource: "missing-\(occurrence)", occurrence: occurrence), title: "缺失页面", error: "spine 引用不存在。")); continue
            }
            let path = try BookArchive.resolve(href, relativeTo: packagePath)
            let locator = SourceLocator(resource: path, occurrence: occurrence)
            guard let data = try? archive.data(path, limit: 16 * 1024 * 1024) else {
                units.append(ReadingUnit(locator: locator, title: "第 \(units.count+1) 页", error: "内容文档无法读取。")); continue
            }
            let document = try SwiftSoup.parse(String(decoding: data, as: UTF8.self))
            var css = ""
            for node in try document.select("style, link[rel=stylesheet]").array() {
                let media = try node.attr("media").trimmingCharacters(in:.whitespacesAndNewlines)
                let sheet: String?
                if node.tagName() == "style" { sheet = try node.html() }
                else if let linkedPath = try? BookArchive.resolve(node.attr("href"),relativeTo:path) { sheet=stylesheets[linkedPath] }
                else { sheet=nil }
                if let sheet { css += "\n" + (media.isEmpty ? sheet : "@media \(media) {\(sheet)}") }
            }
            let images = try document.select("body img").array()
            let bodyText = try document.body()?.text() ?? ""
            let hasComplexNodes = try !document.select("svg, canvas, video, audio, iframe, script, object, table").isEmpty()
            var imagePath: String?; var hint: Int?
            let image = images.count == 1 ? images.first : nil
            if let image {
                imagePath = try? BookArchive.resolve(image.attr("src"), relativeTo: path)
                hint = RotationStyles.hint(css:css,image:image)
            }
            let isCover = imagePath != nil && imagePath == coverImage
            let layoutStyles = css + (try document.select("[style]").array().map { try $0.attr("style") }.joined(separator: "\n"))
            let riskyLayout = layoutStyles.range(of: "position\\s*:\\s*(absolute|fixed)|clip(?:-path)?\\s*:|background(?:-image)?\\s*:.*url\\(|@import", options: .regularExpression) != nil
            let complex = imagePath == nil || !bodyText.isEmpty || hasComplexNodes || riskyLayout
            let title = isCover ? "封面" : (try document.title()).trimmingCharacters(in: .whitespacesAndNewlines)
            units.append(ReadingUnit(locator: locator, title: title.isEmpty ? "第 \(units.count+1) 页" : title, imagePath: imagePath, isCover: isCover, rotationHint: hint, complex: complex))
        }
        guard !units.isEmpty else { throw ReaderError.message("这本 EPUB 没有可阅读的正文内容。") }
        var navigation: [NavigationItem] = []
        for manifest in package.manifest.values where manifest["media-type"] == "application/x-dtbncx+xml" || manifest["properties"]?.contains("nav") == true {
            guard let href = manifest["href"], let path = try? BookArchive.resolve(href, relativeTo: packagePath), let data = try? archive.data(path, limit: 4 * 1024 * 1024), let document = try? SwiftSoup.parse(String(decoding: data, as: UTF8.self)) else { continue }
            if manifest["media-type"] == "application/x-dtbncx+xml" {
                for node in try document.select("navpoint").array() {
                    guard let target = try node.select("content").first()?.attr("src"), let resolved = try? BookArchive.resolve(target, relativeTo: path), let index = units.firstIndex(where: { $0.locator.resource == resolved }) else { continue }
                    navigation.append(NavigationItem(title: try node.select("navlabel").text(), index: index))
                }
            } else {
                for node in try document.select("nav a").array() {
                    guard let resolved = try? BookArchive.resolve(node.attr("href"), relativeTo: path), let index = units.firstIndex(where: { $0.locator.resource == resolved }) else { continue }
                    navigation.append(NavigationItem(title: try node.text(), index: index))
                }
            }
        }
        if archive.contains("META-INF/encryption.xml") { warnings.append("出版物含加密或字体混淆声明；受保护的资源可能无法显示。") }
        return (package.title.trimmingCharacters(in: .whitespacesAndNewlines), units, package.direction, navigation, warnings)
    }
}

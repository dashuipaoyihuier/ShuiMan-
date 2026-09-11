import Foundation
import CryptoKit
import ImageIO
import PDFKit

public actor DocumentEngine {
    private var archive: BookArchive?
    private var pdf: PDFDocument?
    private var mobi: MOBIBook?
    private var current: Publication?
    private let cache = NSCache<NSString, CGImage>()
    private var analysisCache: [String: SpreadDecision] = [:]
    private var pairCache: [Int: PairDecision] = [:]
    public init() { cache.totalCostLimit = 192 * 1024 * 1024 }
    public func open(_ url: URL, password: String? = nil) throws -> Publication {
        let values = try url.resourceValues(forKeys: [.isDirectoryKey, .fileSizeKey, .contentModificationDateKey])
        let path = url.standardizedFileURL.path
        let identity = Self.hash(path)
        var revision = "\(values.fileSize ?? 0)-\(values.contentModificationDate?.timeIntervalSince1970 ?? 0)"
        var units: [ReadingUnit] = []; var title = url.deletingPathExtension().lastPathComponent
        var kind = PublicationKind.images; var direction: ReadingDirection?; var navigation: [NavigationItem] = []; var warnings: [String] = []
        var nextArchive: BookArchive?; var nextPDF: PDFDocument?; var nextMOBI: MOBIBook?
        if url.pathExtension.lowercased() == "epub" {
            kind = .epub
            let bookArchive = try BookArchive(url: url); nextArchive = bookArchive
            (title, units, direction, navigation, warnings) = try EPUBParser.parse(archive: bookArchive)
            if title.isEmpty { title = url.deletingPathExtension().lastPathComponent }
        } else if url.pathExtension.lowercased() == "mobi" {
            kind = .mobi
            let book = try MOBIBook(url:url); nextMOBI = book
            units = book.units; warnings = book.warnings; direction = book.direction
            if !book.title.isEmpty { title = book.title }
        } else if url.pathExtension.lowercased() == "pdf" {
            kind = .pdf
            guard let document = PDFDocument(url: url) else { throw ReaderError.message("无法打开 PDF。") }
            if document.isLocked {
                guard let password, document.unlock(withPassword: password) else { throw ReaderError.passwordRequired }
            }
            nextPDF = document
            for i in 0..<document.pageCount {
                try Task.checkCancellation()
                let page = document.page(at: i); let bounds = page?.bounds(for: .cropBox) ?? .zero
                let sideways = abs(page?.rotation ?? 0) % 180 == 90
                units.append(ReadingUnit(locator: SourceLocator(resource: path, occurrence: i), title: "第 \(i+1) 页", width: sideways ? bounds.height : bounds.width, height: sideways ? bounds.width : bounds.height))
            }
        } else {
            let extensions = Set(["jpg", "jpeg", "png", "gif", "tiff", "tif", "bmp", "heic", "heif", "webp", "avif"])
            let files: [URL]
            if values.isDirectory == true {
                files = try FileManager.default.contentsOfDirectory(at: url, includingPropertiesForKeys: [.isRegularFileKey, .fileSizeKey, .contentModificationDateKey], options: [.skipsHiddenFiles]).filter { extensions.contains($0.pathExtension.lowercased()) }.sorted {
                    let order = $0.lastPathComponent.compare($1.lastPathComponent, options: [.numeric, .caseInsensitive], locale: Locale(identifier: "en_US_POSIX"))
                    return order == .orderedSame ? $0.path < $1.path : order == .orderedAscending
                }
                title = url.lastPathComponent
                revision += files.map { item in
                    let v = try? item.resourceValues(forKeys: [.fileSizeKey, .contentModificationDateKey])
                    return "\(item.lastPathComponent):\(v?.fileSize ?? 0):\(v?.contentModificationDate?.timeIntervalSince1970 ?? 0)"
                }.joined(separator: "|")
            } else { files = [url] }
            for file in files {
                try Task.checkCancellation()
                guard let source = CGImageSourceCreateWithURL(file as CFURL, [kCGImageSourceShouldCache: false] as CFDictionary) else {
                    units.append(ReadingUnit(locator: SourceLocator(resource: file.path), title: file.lastPathComponent, error: "无法识别图片。")); continue
                }
                let count = ["tif", "tiff"].contains(file.pathExtension.lowercased()) ? CGImageSourceGetCount(source) : 1
                for frame in 0..<count {
                    let (width, height) = ImageTools.dimensions(source, index: frame)
                    units.append(ReadingUnit(locator: SourceLocator(resource: file.path, imageIndex: frame), title: file.lastPathComponent + (count > 1 ? " · \(frame+1)" : ""), width: width, height: height))
                }
            }
        }
        guard !units.isEmpty else { throw ReaderError.message("没有找到可阅读的页面。请选择图片文件夹、图片、PDF、EPUB 或 MOBI。") }
        let publication = Publication(sourceURL: url, identity: identity, revision: Self.hash(revision), title: title, kind: kind, units: units, direction: direction, navigation: navigation, warnings: warnings)
        archive = nextArchive; pdf = nextPDF; mobi = nextMOBI; current = publication; cache.removeAllObjects(); analysisCache = [:]; pairCache = [:]
        return publication
    }
    public func image(at index: Int, maxPixel: Int = 2400, rotation: Int = 0) throws -> CGImage {
        guard let publication = current, publication.units.indices.contains(index) else { throw ReaderError.message("页面不存在。") }
        let unit = publication.units[index]
        if let error = unit.error { throw ReaderError.message(error) }
        let key = "\(unit.id):\(maxPixel):\(rotation)" as NSString
        if let image = cache.object(forKey: key) { return image }
        try Task.checkCancellation()
        let raw: CGImage
        switch publication.kind {
        case .images:
            guard let source = CGImageSourceCreateWithURL(URL(fileURLWithPath: unit.locator.resource) as CFURL, [kCGImageSourceShouldCache: false] as CFDictionary) else { throw ReaderError.message("无法读取图片。") }
            raw = try ImageTools.decode(source, index: unit.locator.imageIndex, maxPixel: maxPixel)
        case .epub:
            guard let path = unit.imagePath, let archive else { throw ReaderError.message("此页使用原版式阅读。") }
            raw = try ImageTools.decode(archive.data(path), maxPixel: maxPixel)
        case .mobi:
            guard let mobi else { throw ReaderError.message("MOBI 未打开。") }
            raw = try ImageTools.decode(mobi.imageData(for:unit),maxPixel:maxPixel)
        case .pdf:
            guard let page = pdf?.page(at: index) else { throw ReaderError.message("PDF 页面不可用。") }
            let bounds = page.bounds(for: .cropBox)
            guard bounds.width > 0, bounds.height > 0 else { throw ReaderError.message("PDF 页面尺寸无效。") }
            let scale = CGFloat(maxPixel) / max(bounds.width, bounds.height)
            let sideways = abs(page.rotation) % 180 == 90
            let w = max(1, Int((sideways ? bounds.height : bounds.width) * scale)), h = max(1, Int((sideways ? bounds.width : bounds.height) * scale))
            guard let context = CGContext(data: nil, width: w, height: h, bitsPerComponent: 8, bytesPerRow: 0, space: CGColorSpaceCreateDeviceRGB(), bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue) else { throw ReaderError.message("无法分配页面画布。") }
            context.setFillColor(CGColor(gray: 1, alpha: 1)); context.fill(CGRect(x: 0, y: 0, width: w, height: h))
            guard let reference = page.pageRef else { throw ReaderError.message("PDF 页面内容不可用。") }
            context.concatenate(reference.getDrawingTransform(.cropBox, rect: CGRect(x: 0, y: 0, width: w, height: h), rotate: 0, preserveAspectRatio: true))
            context.drawPDFPage(reference)
            guard let rendered = context.makeImage() else { throw ReaderError.message("PDF 页面绘制失败。") }
            raw = rendered
        }
        let result = ImageTools.rotate(raw, clockwise: rotation)
        cache.setObject(result, forKey: key, cost: result.bytesPerRow * result.height)
        return result
    }
    public func resource(_ path: String) throws -> Data {
        guard let archive else { throw ReaderError.message("出版物资源不可用。") }
        return try archive.data(path)
    }
    public func analyze(at index: Int) throws -> SpreadDecision {
        guard let publication = current, publication.units.indices.contains(index) else { return SpreadDecision() }
        let unit = publication.units[index]
        if unit.complex || unit.error != nil || unit.isCover { return SpreadDecision() }
        if let cached = analysisCache[unit.id] { return cached }
        let sample = try image(at: index, maxPixel: 1300)
        let decision = try SpreadAnalyzer.analyze(image: sample, unit: unit)
        analysisCache[unit.id] = decision
        return decision
    }
    public static func hash(_ value: String) -> String { SHA256.hash(data: Data(value.utf8)).map { String(format: "%02x", $0) }.joined() }
    public func analyzePair(at index: Int) throws -> PairDecision {
        if let cached=pairCache[index] { return cached }
        guard let p=current,index>=0,index+1<p.units.count else { return PairDecision(firstIndex:index) }
        let eligible=[p.units[index],p.units[index+1]].allSatisfy { !$0.isCover && !$0.complex && $0.error == nil && ($0.rotationHint ?? 0) == 0 }
        let decision: PairDecision
        if eligible {
            decision=PairAnalyzer.analyze(first:try image(at:index,maxPixel:768),second:try image(at:index+1,maxPixel:768),firstIndex:index)
        } else { decision=PairDecision(firstIndex:index) }
        pairCache[index]=decision
        return decision
    }
}

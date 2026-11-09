import Foundation
import ImageIO

enum ZIPComicParser {
    static func units(in archive: BookArchive) throws -> [ReadingUnit] {
        let paths = archive.paths.filter { path in
            LibraryScanner.imageExtensions.contains((path as NSString).pathExtension.lowercased()) &&
                !path.split(separator: "/").contains { $0.hasPrefix(".") || $0 == "__MACOSX" }
        }.sorted { left, right in
            let order = left.compare(right, options: [.numeric, .caseInsensitive], locale: Locale(identifier: "en_US_POSIX"))
            return order == .orderedSame ? left < right : order == .orderedAscending
        }
        guard !paths.isEmpty else { throw ReaderError.message("ZIP/CBZ 中没有可阅读的图片。压缩包内的电子书请先解压再打开。") }
        var units: [ReadingUnit] = []
        for path in paths {
            try Task.checkCancellation()
            // The archive path and TIFF frame remain stable even when earlier files are inserted.
            guard let data = try? archive.data(path),
                  let source = CGImageSourceCreateWithData(data as CFData, [kCGImageSourceShouldCache: false] as CFDictionary) else {
                units.append(ReadingUnit(locator: SourceLocator(resource: path), title: path, imagePath: path,
                                         error: "ZIP 图片损坏或不受支持。"))
                continue
            }
            let frames = ["tif", "tiff"].contains((path as NSString).pathExtension.lowercased()) ? max(1, CGImageSourceGetCount(source)) : 1
            for frame in 0..<frames {
                let (width, height) = ImageTools.dimensions(source, index: frame)
                units.append(ReadingUnit(locator: SourceLocator(resource: path, imageIndex: frame),
                                         title: (path as NSString).lastPathComponent + (frames > 1 ? " · \(frame+1)" : ""),
                                         imagePath: path, width: width, height: height, isCover: units.isEmpty,
                                         error: width > 0 && height > 0 ? nil : "ZIP 图片尺寸无效。"))
            }
        }
        return units
    }
}

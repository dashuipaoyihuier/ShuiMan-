import Foundation
import ComicCore
import ZIPFoundation
import ImageIO
import UniformTypeIdentifiers

extension ReaderChecks {
    static func testZIPComics(at root: URL) async throws {
        let directory = root.appendingPathComponent("ZIP-Comics", isDirectory: true)
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        let page = Fixtures.png(Fixtures.image(width: 200, height: 300))
        let tiff = NSMutableData()
        let encoder = CGImageDestinationCreateWithData(tiff, UTType.tiff.identifier as CFString, 2, nil)!
        for _ in 0..<2 { CGImageDestinationAddImage(encoder, Fixtures.image(width: 200,height: 300), nil) }
        precondition(CGImageDestinationFinalize(encoder))
        func write(_ url: URL, _ entries: [(String,Data)]) throws {
            if FileManager.default.fileExists(atPath: url.path) { try FileManager.default.removeItem(at: url) }
            let archive = try Archive(url: url, accessMode: .create)
            for (name,data) in entries {
                try archive.addEntry(with: name, type: .file, uncompressedSize: Int64(data.count), compressionMethod: .deflate) { offset,size in
                    data.subdata(in: Int(offset)..<(Int(offset)+size))
                }
            }
        }
        let entries: [(String,Data)] = [("第1章/10.png",page),("第1章/2.png",page),("第1章/1.png",page),
                                      ("第2章/1.tiff",tiff as Data),("第3章/broken.png",Data([1,2,3])),
                                      ("__MACOSX/hidden.png",page),(".hidden.png",page),("notes.txt",Data("Note".utf8))]
        let engine = DocumentEngine()
        for ext in ["zip","cbz"] {
            let url = directory.appendingPathComponent("Original."+ext)
            try write(url,entries)
            let original = try Data(contentsOf: url)
            let publication = try await engine.open(url)
            try expect(publication.kind == .cbz && publication.units.count == 6,"\(ext): nested images and TIFF frames open directly without extraction")
            try expect(Array(publication.units.prefix(3)).map(\.locator.resource) == ["第1章/1.png","第1章/2.png","第1章/10.png"],"\(ext): natural image order is independent of ZIP entry order")
            try expect(publication.units[3].locator.imageIndex == 0 && publication.units[4].locator.imageIndex == 1,"\(ext): multi-frame TIFF has stable frame locators")
            try expect(publication.units[5].error != nil,"\(ext): corrupt image retains its original reading position")
            for index in 0..<5 {
                let image = try await engine.image(at:index,maxPixel:300)
                try expect(image.width == 200 && image.height == 300,"\(ext): image/frame \(index) renders in original aspect")
            }
            let reopened = try await engine.open(url)
            try expect(publication.units.map(\.id) == reopened.units.map(\.id),"\(ext): page identities survive reopening")
            let afterReading = try Data(contentsOf:url)
            try expect(afterReading == original,"\(ext): reading never rewrites original archive")
            try write(url,[("0.png",page)]+entries)
            let inserted = try await engine.open(url)
            try expect(Array(inserted.units.dropFirst()).map(\.id) == publication.units.map(\.id),"\(ext): inserting an earlier image preserves existing page IDs")
        }
        let scanned = try LibraryScanner.scan(directory)
        try expect(scanned.count == 2,"Library scan discovers ZIP and CBZ volumes")
        let invalid = directory.appendingPathComponent("Invalid.zip")
        for entries in [[("notes.txt",Data([1]))],[("../outside.png",page)]] {
            try write(invalid,entries)
            do { _ = try await engine.open(invalid); throw ReaderError.message("FAIL: invalid ZIP accepted") }
            catch ReaderError.message(let message) where !message.hasPrefix("FAIL:") { print("PASS: ZIP rejects empty or unsafe archive clearly") }
        }
        try FileManager.default.removeItem(at:invalid)
    }
}

import Foundation
import ComicCore

extension ReaderChecks {
    static func testMOBI(at root: URL) async throws {
        let folder = root.appendingPathComponent("MOBI")
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        let portrait = Fixtures.png(Fixtures.image(width: 800, height: 1200))
        let wide = Fixtures.png(Fixtures.image(width: 1600, height: 1000, wide: true))
        func put(_ value: Int, _ offset: Int, _ width: Int, into data: inout Data) {
            for i in 0..<width { data[offset+i] = UInt8((value >> ((width-i-1)*8)) & 255) }
        }
        func book(_ html: String, compressed: Bool = false, trailer: Bool = false, cover: Int = 1) -> Data {
            let original = Array(html.utf8)
            var text = Data(), i = 0
            // Original fixture compressor exercises overlapping PalmDOC backreferences.
            while i < original.count {
                if compressed {
                    var bestLength = 0, bestDistance = 0
                    if i > 0 {
                        for distance in 1...min(i,2047) {
                            var length = 0
                            while length < 10 && i+length < original.count && original[i+length] == original[i+length-distance] { length += 1 }
                            if length >= 3 && length > bestLength { bestLength = length; bestDistance = distance }
                        }
                    }
                    if bestLength >= 3 {
                        let value = 0x8000 | (bestDistance << 3) | (bestLength-3)
                        text.append(UInt8(value >> 8)); text.append(UInt8(value & 255)); i += bestLength; continue
                    }
                    if original[i] >= 128 || (1...8).contains(original[i]) { text.append(1) }
                }
                text.append(original[i]); i += 1
            }
            if trailer { text.append(contentsOf: [0,0x81]) }
            var header = Data(count: 300)
            put(compressed ? 2 : 1,0,2,into:&header); put(original.count,4,4,into:&header)
            put(1,8,2,into:&header); put(4096,10,2,into:&header)
            header.replaceSubrange(16..<20,with:Data("MOBI".utf8))
            put(264,20,4,into:&header); put(2,24,4,into:&header); put(65001,28,4,into:&header)
            put(6,36,4,into:&header); put(2,108,4,into:&header); put(0x40,128,4,into:&header)
            put(trailer ? 3 : 0,240,4,into:&header)
            header.replaceSubrange(280..<284,with:Data("EXTH".utf8))
            put(24,284,4,into:&header); put(1,288,4,into:&header); put(201,292,4,into:&header); put(12,296,4,into:&header)
            header.append(contentsOf:[0,0,0,UInt8(cover)])
            let records = [header,text,portrait,portrait,wide]
            var result = Data(count:78+records.count*8+2)
            result.replaceSubrange(60..<68,with:Data("BOOKMOBI".utf8)); put(records.count,76,2,into:&result)
            for (index,record) in records.enumerated() { put(result.count,78+index*8,4,into:&result); result.append(record) }
            return result
        }
        let html = "<html><body><img recindex='3'><mbp:pagebreak/><img recindex='1'><img recindex='3'><img recindex='99'></body></html>"
        let engine = DocumentEngine()
        for compressed in [false,true] {
            let url = folder.appendingPathComponent(compressed ? "Compressed.mobi" : "Plain.mobi")
            try book(html,compressed:compressed,trailer:compressed).write(to:url)
            let publication = try await engine.open(url)
            try expect(publication.kind == .mobi && publication.units.count == 5,"MOBI compression \(compressed): cover plus every body occurrence")
            try expect(publication.units.map(\.locator.resource) == ["mobi:record:3","mobi:record:4","mobi:record:2","mobi:record:4","mobi:missing"],"MOBI preserves reference order, duplicates and missing position")
            try expect(Set(publication.units.map(\.id)).count == 5 && publication.units.last?.error != nil,"MOBI stable occurrence IDs distinguish duplicate images")
            let image = try await engine.image(at:1)
            try expect(image.width > image.height,"MOBI referenced image decodes in its original geometry")
            let decision = try await engine.analyze(at:1)
            try expect(decision.standalone,"MOBI images enter the existing smart-spread pipeline")
            let restored = try await engine.open(url)
            try expect(restored.units.map(\.id) == publication.units.map(\.id),"MOBI locators survive reopening")
        }
        let coverURL = folder.appendingPathComponent("BodyCover.mobi")
        try book("<img recindex='2'><img recindex='1'>").write(to:coverURL)
        let coverBook = try MOBIBook(url:coverURL)
        try expect(coverBook.units.count == 2 && coverBook.units[0].isCover,"MOBI does not insert an already referenced cover twice")
        let valid = book(html)
        // PDB header is 120 bytes for the five-record fixture.
        var failures: [(String,Data)] = []
        for (name,offset,width,value) in [("DRM",132,2,1),("KF8",156,4,8),("HUFF",120,2,17480),("record bounds",78,4,1),("encoding",148,4,42)] {
            var data = valid; put(value,offset,width,into:&data); failures.append((name,data))
        }
        failures.append(("truncation",Data(valid.prefix(90))))
        failures.append(("mixed text",book("<p>Required story text</p><img recindex='1'>")))
        var corrupt = book(html,compressed:true)
        let textOffset = (0..<4).reduce(0) { ($0 << 8) | Int(corrupt[86+$1]) }
        corrupt[textOffset] = 0x80; corrupt[textOffset+1] = 0
        failures.append(("bad backreference",corrupt))
        for (name,data) in failures {
            let url = folder.appendingPathComponent("Invalid.mobi"); try data.write(to:url)
            do { _ = try MOBIBook(url:url); throw ReaderError.message("FAIL: MOBI accepted \(name)") }
            catch ReaderError.message(let message) where !message.hasPrefix("FAIL:") { print("PASS: MOBI rejects \(name) clearly") }
        }
        try FileManager.default.removeItem(at:folder.appendingPathComponent("Invalid.mobi"))
        let catalog = try LibraryScanner.scan(folder)
        try expect(catalog.count == 3,"Library scanner discovers MOBI volumes")
    }
}

import Foundation
import CoreGraphics
import PDFKit
import ComicCore

extension ReaderChecks {
    static func testPDFGeometry(at root: URL) async throws {
        let url = root.appendingPathComponent("PDF-Geometry.pdf")
        // Unequal page sizes, offset crops and every intrinsic rotation, with edge-to-edge blue artwork.
        let document = PDFDocument()
        for (index, angle) in [0, 90, 180, 270].enumerated() {
            var media = CGRect(x: 20, y: 40, width: 613, height: 887)
            let data = NSMutableData()
            let context = CGContext(consumer: CGDataConsumer(data: data)!, mediaBox: &media, nil)!
            context.beginPDFPage(nil)
            context.setFillColor(CGColor(red: 0.1, green: 0.2, blue: 0.8, alpha: 1)); context.fill(media)
            context.endPDFPage(); context.closePDF()
            let page = PDFDocument(data: data as Data)!.page(at: 0)!
            page.setBounds(CGRect(x: 71, y: 113, width: 401, height: 607), for: .cropBox)
            page.rotation = angle
            document.insert(page, at: index)
        }
        guard document.write(to: url) else { throw ReaderError.message("Cannot write PDF fixture") }
        let engine = DocumentEngine()
        let publication = try await engine.open(url)
        for index in 0..<4 {
            let image = try await engine.image(at: index, maxPixel: 997)
            let unit = publication.units[index]
            try expect(abs(Double(image.width)/Double(image.height) - unit.width/unit.height) < 0.002,
                       "PDF crop and rotation \(index*90): metadata matches rendered aspect")
            let colors = pixelColors(image, points: [(1,1),(image.width-2,1),(1,image.height-2),(image.width-2,image.height-2)])
            try expect(colors.allSatisfy { $0.2 > 150 && $0.0 < 80 }, "PDF offset crop \(index*90): all four edges have artwork, no added white border")
        }
        let cropURL = root.appendingPathComponent("PDF-White-Margins.pdf")
        var media = CGRect(x: 0, y: 0, width: 600, height: 900)
        let context = CGContext(cropURL as CFURL, mediaBox: &media, nil)!
        for index in 0..<4 {
            context.beginPDFPage(nil)
            context.setFillColor(CGColor(gray: 1, alpha: 1)); context.fill(media)
            if index != 1 {
                context.setFillColor(CGColor(red: 0.1, green: 0.2, blue: 0.8, alpha: 1))
                context.fill(index == 2 ? CGRect(x: 15,y: 15,width: 2,height: 2) : CGRect(x: 50,y: 310,width: 300,height: 400))
                if index == 3 {
                    // A faint caption outside the main panel must survive.
                    context.setFillColor(CGColor(gray: 0.95, alpha: 1))
                    context.fill(CGRect(x: 500,y: 30,width: 40,height: 8))
                }
            }
            context.endPDFPage()
        }
        context.closePDF()
        _ = try await engine.open(cropURL)
        let full = try await engine.image(at: 0, maxPixel: 900)
        let cropped = try await engine.image(at: 0, maxPixel: 900, cropPDFWhiteMargins: true)
        try expect(full.width == 600 && full.height == 900 && cropped.width == 310 && cropped.height == 410,
                   "PDF white margins trim to content plus a safety border; original canvas remains available")
        let colors = pixelColors(cropped, points: [(10,10),(cropped.width-11,cropped.height-11)])
        try expect(colors.allSatisfy { $0.2 > 150 && $0.0 < 80 }, "PDF asymmetric white margins preserve both ends of the artwork")
        let restored = try await engine.image(at: 0, maxPixel: 900)
        try expect(restored.width == 600 && restored.height == 900, "Disabling PDF crop restores full page despite cached cropped render")
        for index in [1,2] {
            let image = try await engine.image(at: index, maxPixel: 900, cropPDFWhiteMargins: true)
            try expect(image.width == 600 && image.height == 900, "PDF blank/sparse page \(index) keeps original geometry")
        }
        let caption = try await engine.image(at: 3, maxPixel: 900, cropPDFWhiteMargins: true)
        try expect(caption.width >= 500 && caption.height >= 690, "PDF crop retains faint marginal captions")
        var preferences = try JSONDecoder().decode(ReaderPreferences.self, from: Data("{}".utf8))
        try expect(preferences.cropPDFWhiteMargins, "Existing reader settings adopt PDF white-margin option")
        preferences.cropPDFWhiteMargins = false
        let saved = try JSONDecoder().decode(ReaderPreferences.self, from: JSONEncoder().encode(preferences))
        try expect(!saved.cropPDFWhiteMargins, "PDF full-page preference survives save and reopen")
    }
    private static func pixelColors(_ image: CGImage, points: [(Int,Int)]) -> [(Int,Int,Int)] {
        var bytes = [UInt8](repeating: 0, count: image.width * image.height * 4)
        return bytes.withUnsafeMutableBytes { buffer in
            let context = CGContext(data: buffer.baseAddress, width: image.width, height: image.height, bitsPerComponent: 8,
                                    bytesPerRow: image.width*4, space: CGColorSpaceCreateDeviceRGB(),
                                    bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue | CGBitmapInfo.byteOrder32Big.rawValue)!
            context.draw(image, in: CGRect(x:0,y:0,width:image.width,height:image.height))
            let data = buffer.bindMemory(to: UInt8.self)
            return points.map { x,y in let i=(y*image.width+x)*4; return (Int(data[i]),Int(data[i+1]),Int(data[i+2])) }
        }
    }
}

import Foundation
import CoreGraphics
import CoreText
import ImageIO
import UniformTypeIdentifiers
import ZIPFoundation
import ComicCore
import PDFKit

enum Fixtures {
    static func image(width: Int, height: Int, wide: Bool = false, blank: Bool = false) -> CGImage {
        let context = CGContext(data: nil, width: width, height: height, bitsPerComponent: 8, bytesPerRow: 0, space: CGColorSpaceCreateDeviceRGB(), bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)!
        context.setFillColor(CGColor(red: 0.98, green: 0.96, blue: 0.90, alpha: 1)); context.fill(CGRect(x: 0, y: 0, width: width, height: height))
        if !blank {
            let w = CGFloat(width), h = CGFloat(height)
            context.setFillColor(CGColor(red: 0.22, green: 0.30, blue: 0.34, alpha: 1))
            context.move(to: CGPoint(x: 0, y: h*0.25)); context.addLine(to: CGPoint(x: w*0.23, y: h*0.64))
            context.addLine(to: CGPoint(x: w*0.47, y: h*0.28)); context.addLine(to: CGPoint(x: w*0.72, y: h*0.54))
            context.addLine(to: CGPoint(x: w, y: h*0.17)); context.addLine(to: CGPoint(x: w, y: 0)); context.addLine(to: .zero); context.closePath(); context.fillPath()
            context.setFillColor(CGColor(red: 0.85, green: 0.43, blue: 0.20, alpha: 1)); context.fillEllipse(in: CGRect(x: w*0.73, y: h*0.65, width: h*0.12, height: h*0.12))
            let lines = wide ? ["THE JOURNEY CONTINUES", "A WORLD BEYOND THE HORIZON", "Every new page is an adventure."] : ["MAN DU", "A READER'S JOURNEY", "Turn the page."]
            for (i, text) in lines.enumerated() {
                let font = CTFontCreateWithName("Helvetica-Bold" as CFString, h * (i == 0 ? 0.060 : 0.035), nil)
                let attributed = NSAttributedString(string: text, attributes: [.init(kCTFontAttributeName as String): font, .init(kCTForegroundColorAttributeName as String): CGColor(gray: 0.1, alpha: 1)])
                context.textPosition = CGPoint(x: w*0.065, y: h*(0.9-Double(i)*0.075)); CTLineDraw(CTLineCreateWithAttributedString(attributed), context)
            }
            context.setStrokeColor(CGColor(gray: 1, alpha: 0.5)); context.setLineWidth(3)
            for i in 0..<5 {
                let y = h*(0.06+CGFloat(i)*0.032)
                context.move(to: CGPoint(x: 0, y: y)); context.addLine(to: CGPoint(x: w, y: y+h*0.025)); context.strokePath()
            }
        }
        return context.makeImage()!
    }
    static func verticalDialogue() -> CGImage {
        let context=CGContext(data:nil,width:900,height:1200,bitsPerComponent:8,bytesPerRow:0,space:CGColorSpaceCreateDeviceRGB(),bitmapInfo:CGImageAlphaInfo.premultipliedLast.rawValue)!
        context.setFillColor(CGColor(gray:0.45,alpha:1));context.fill(CGRect(x:0,y:0,width:900,height:1200))
        for (i,text) in ["我們明天一起出發","請你看看這個世界"].enumerated() {
            let x=CGFloat(100+i*370),y=CGFloat(650-i*380)
            context.setFillColor(CGColor(gray:1,alpha:1));context.fillEllipse(in:CGRect(x:x,y:y,width:240,height:350))
            let font=CTFontCreateWithName("PingFangTC-Semibold" as CFString,38,nil)
            for (j,character) in text.enumerated() {
                let string=NSAttributedString(string:String(character),attributes:[.init(kCTFontAttributeName as String):font,.init(kCTForegroundColorAttributeName as String):CGColor(gray:0,alpha:1)])
                context.textPosition=CGPoint(x:x+145-CGFloat(j/4)*65,y:y+250-CGFloat(j%4)*52)
                CTLineDraw(CTLineCreateWithAttributedString(string),context)
            }
        }
        return context.makeImage()!
    }
    static func png(_ image: CGImage) -> Data {
        let buffer = NSMutableData(); let destination = CGImageDestinationCreateWithData(buffer, UTType.png.identifier as CFString, 1, nil)!
        CGImageDestinationAddImage(destination, image, nil); precondition(CGImageDestinationFinalize(destination)); return buffer as Data
    }
    static func generate(at folder: URL) throws {
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        let images = folder.appendingPathComponent("Images")
        try FileManager.default.createDirectory(at: images, withIntermediateDirectories: true)
        let portrait = image(width: 800, height: 1200), wide = image(width: 1600, height: 1000, wide: true)
        let rotated = ImageTools.rotate(wide, clockwise: 270)
        let left = wide.cropping(to: CGRect(x: 0, y: 0, width: 800, height: 1000))!
        let right = wide.cropping(to: CGRect(x: 800, y: 0, width: 800, height: 1000))!
        for (name, image) in [("1.png", portrait), ("2.png", rotated), ("3.png", wide), ("4-left.png", left), ("5-right.png", right), ("10.png", portrait)] {
            try png(image).write(to: images.appendingPathComponent(name))
        }
        let pdfURL = folder.appendingPathComponent("Sample.pdf")
        var bounds = CGRect(x: 0, y: 0, width: 600, height: 900)
        let pdf = CGContext(pdfURL as CFURL, mediaBox: &bounds, nil)!
        for image in [portrait, wide, rotated] {
            var rect = CGRect(x: 0, y: 0, width: image.width, height: image.height)
            let data = NSData(bytes: &rect, length: MemoryLayout<CGRect>.size)
            pdf.beginPDFPage([kCGPDFContextMediaBox as String: data] as CFDictionary)
            pdf.draw(image, in: rect); pdf.endPDFPage()
        }
        pdf.closePDF()
        if let protected = PDFDocument(url: pdfURL) {
            guard protected.write(to: folder.appendingPathComponent("Protected.pdf"), withOptions: [.ownerPasswordOption: "owner-fixture", .userPasswordOption: "comic-test"]) else { throw ReaderError.message("Could not generate protected PDF") }
        }
        if let metadataRotated = PDFDocument(url: pdfURL) {
            metadataRotated.page(at: 0)?.rotation = 90
            guard metadataRotated.write(to: folder.appendingPathComponent("Rotated.pdf")) else { throw ReaderError.message("Could not generate rotated PDF") }
        }
        let epubURL = folder.appendingPathComponent("Sample.epub")
        if FileManager.default.fileExists(atPath: epubURL.path) { try FileManager.default.removeItem(at: epubURL) }
        let archive = try Archive(url: epubURL, accessMode: .create)
        func add(_ name: String, _ data: Data) throws {
            try archive.addEntry(with: name, type: .file, uncompressedSize: Int64(data.count), compressionMethod: name == "mimetype" ? .none : .deflate) { position, size in data.subdata(in: Int(position)..<(Int(position)+size)) }
        }
        func text(_ name: String, _ value: String) throws { try add(name, Data(value.utf8)) }
        try text("mimetype", "application/epub+zip")
        try text("META-INF/container.xml", "<container xmlns=\"urn:oasis:names:tc:opendocument:xmlns:container\" version=\"1.0\"><rootfiles><rootfile full-path=\"OPS/book.opf\" media-type=\"application/oebps-package+xml\"/></rootfiles></container>")
        try text("OPS/book.opf", """
        <package xmlns="http://www.idpf.org/2007/opf" version="2.0" unique-identifier="uid"><metadata xmlns:dc="http://purl.org/dc/elements/1.1/"><dc:title>漫读 · 原型测试册</dc:title><dc:identifier id="uid">comic-reader-fixture-v1</dc:identifier><dc:language>en</dc:language><meta name="cover" content="coverimage"/></metadata><manifest>
        <item id="cover" href="pages/z.html" media-type="application/xhtml+xml"/><item id="rotated" href="pages/a.html" media-type="application/xhtml+xml"/><item id="normal" href="pages/m.html" media-type="application/xhtml+xml"/><item id="complex" href="pages/c.html" media-type="application/xhtml+xml"/>
        <item id="coverimage" href="images/cover.png" media-type="image/png"/><item id="rotimage" href="images/rotated.png" media-type="image/png"/><item id="normalimage" href="images/normal.png" media-type="image/png"/><item id="css" href="style.css" media-type="text/css"/><item id="ncx" href="toc.ncx" media-type="application/x-dtbncx+xml"/>
        </manifest><spine toc="ncx" page-progression-direction="rtl"><itemref idref="cover"/><itemref idref="rotated"/><itemref idref="normal"/><itemref idref="normal"/><itemref idref="complex"/></spine></package>
        """)
        try text("OPS/style.css", "body{margin:0;text-align:center;background:#eee;}img{max-width:100%;max-height:95vh;}img.spread{transform:rotate(90deg);}")
        for (file, image, cls) in [("z.html", "cover.png", "spread"), ("a.html", "rotated.png", "spread"), ("m.html", "normal.png", "normal")] {
            try text("OPS/pages/"+file, "<html xmlns=\"http://www.w3.org/1999/xhtml\"><head><title>\(file)</title><link rel=\"stylesheet\" href=\"../style.css\"/></head><body><img class=\"\(cls)\" src=\"../images/\(image)\"/></body></html>")
        }
        try text("OPS/pages/c.html", "<html><head><title>图文混排</title></head><body style=\"font:20px -apple-system;padding:40px;max-width:800px;margin:auto\"><h1>原版式阅读</h1><p>这一页含有真实文字和两张图片，应该通过 WebKit 显示完整内容。</p><img width=\"260\" src=\"../images/cover.png\"><img width=\"260\" src=\"../images/normal.png\"></body></html>")
        try text("OPS/toc.ncx", "<ncx xmlns=\"http://www.daisy.org/z3986/2005/ncx/\"><navMap><navPoint id=\"n1\"><navLabel><text>测试跨页</text></navLabel><content src=\"pages/a.html\"/></navPoint></navMap></ncx>")
        try add("OPS/images/cover.png", png(portrait)); try add("OPS/images/normal.png", png(portrait)); try add("OPS/images/rotated.png", png(rotated)); try add("OPS/images/unreferenced.png", png(wide))
        let labels = "{\"synthetic\":true,\"sourceGroup\":\"generated-v1\",\"epubCount\":5,\"epubRotatedIndex\":1,\"clockwiseCorrection\":90,\"pair\":[\"4-left.png\",\"5-right.png\"],\"use\":\"smoke tests, not generalization evidence\"}"
        try Data(labels.utf8).write(to: folder.appendingPathComponent("labels.json"))
        try generateStyles(at:folder,portrait:portrait)
    }

    static func generateStyles(at folder:URL,portrait:CGImage) throws {
        let url=folder.appendingPathComponent("Styles.epub")
        if FileManager.default.fileExists(atPath:url.path) { try FileManager.default.removeItem(at:url) }
        let archive=try Archive(url:url,accessMode:.create)
        func add(_ path:String,_ data:Data) throws {
            try archive.addEntry(with:path,type:.file,uncompressedSize:Int64(data.count),compressionMethod:.none) { position,size in data.subdata(in:Int(position)..<(Int(position)+size)) }
        }
        func text(_ path:String,_ value:String) throws { try add(path,Data(value.utf8)) }
        let cases:[(String,String)] = [
            ("<style>.other img.spread{transform:rotate(90deg)}</style>",""),
            ("<style>.spread{transform:rotate(90deg)} #page{transform:none}</style>",""),
            ("<style>#page{transform:none} .spread{transform:rotate(90deg)}</style>",""),
            ("<style>.spread{transform:rotate(90deg)}</style>","transform:none"),
            ("<style>.spread{transform:rotate(90deg)!important}</style>","transform:none"),
            ("<style>@media print{.spread{transform:rotate(90deg)}}</style>",""),
            ("<style>@media screen and (min-width:1000px){.spread{transform:rotate(90deg)}}</style>",""),
            ("<style>.spread{transform:rotate(90deg);transform:none}</style>",""),
            ("<link rel='stylesheet' href='base.css'/><style>.spread{transform:none}</style>",""),
            ("<style>.spread{transform:none}</style><link rel='stylesheet' href='base.css'/>",""),
            ("<style media='print'>.spread{transform:rotate(90deg)}</style>",""),
            ("<style>@media (orientation:portrait){.spread{transform:rotate(90deg)}} @media (orientation:landscape){.spread{transform:rotate(270deg)}}</style>",""),
            ("<style>.spread{transform:rotate(90deg)} img:not(.other){transform:none}</style>",""),
            ("<style>.spread{transform:rotate(90deg)} @supports (display:grid){.spread{transform:none}}</style>","")
        ]
        try text("mimetype","application/epub+zip")
        try text("META-INF/container.xml","<container><rootfiles><rootfile full-path='book.opf'/></rootfiles></container>")
        let manifest=cases.indices.map { "<item id='p\($0)' href='p\($0).xhtml' media-type='application/xhtml+xml'/>" }.joined()
        let spine=cases.indices.map { "<itemref idref='p\($0)'/>" }.joined()
        try text("book.opf","<package version='3.0'><metadata><title>CSS and EPUB 3 fixture</title><meta property='rendition:layout'>pre-paginated</meta></metadata><manifest>\(manifest)<item id='im' href='portrait.png' media-type='image/png'/><item id='css' href='base.css' media-type='text/css'/><item id='nav' href='nav.xhtml' media-type='application/xhtml+xml' properties='nav'/></manifest><spine>\(spine)</spine></package>")
        try text("base.css",".spread{transform:rotate(90deg)}")
        try text("nav.xhtml","<html><body><nav epub:type='toc'><ol><li><a href='p6.xhtml'>Conditional spread</a></li></ol></nav></body></html>")
        for (i,pair) in cases.enumerated() { try text("p\(i).xhtml","<html><head><meta name='viewport' content='width=800,height=1200'/>\(pair.0)</head><body><div class='actual'><img id='page' class='spread' style='\(pair.1)' src='portrait.png'/></div></body></html>") }
        try add("portrait.png",png(portrait))
    }
}

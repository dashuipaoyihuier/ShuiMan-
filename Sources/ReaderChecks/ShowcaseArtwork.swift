import Foundation
import CoreGraphics
import CoreText
import ComicCore
import ZIPFoundation

/// Original vector-drawn landscape; no downloaded or generative artwork.
enum ShowcaseArtwork {
    static func landscape() -> CGImage {
        let c = CGContext(data:nil,width:1600,height:1000,bitsPerComponent:8,bytesPerRow:0,space:CGColorSpaceCreateDeviceRGB(),bitmapInfo:CGImageAlphaInfo.premultipliedLast.rawValue)!
        c.translateBy(x:0,y:1000); c.scaleBy(x:1,y:-1)
        func color(_ hex:Int) -> CGColor { CGColor(red:CGFloat((hex>>16)&255)/255,green:CGFloat((hex>>8)&255)/255,blue:CGFloat(hex&255)/255,alpha:1) }
        func box(_ x:Double,_ y:Double,_ w:Double,_ h:Double,_ col:Int) { c.setFillColor(color(col));c.fill(CGRect(x:x,y:y,width:w,height:h)) }
        func ellipse(_ x:Double,_ y:Double,_ w:Double,_ h:Double,_ col:Int) { c.setFillColor(color(col));c.fillEllipse(in:CGRect(x:x,y:y,width:w,height:h)) }
        func path(_ points:[CGPoint],_ col:Int) { c.setFillColor(color(col));c.addLines(between:points);c.closePath();c.fillPath() }
        func line(_ points:[CGPoint],_ col:Int,_ width:Double) { c.setStrokeColor(color(col));c.setLineWidth(width);c.addLines(between:points);c.strokePath() }
        func label(_ s:String,_ x:Double,_ y:Double,_ size:Double,_ col:Int) {
            c.saveGState(); c.translateBy(x:x,y:y);c.scaleBy(x:1,y:-1)
            let a=NSAttributedString(string:s,attributes:[NSAttributedString.Key(kCTFontAttributeName as String):CTFontCreateWithName("AvenirNext-DemiBold" as CFString,size,nil),NSAttributedString.Key(kCTForegroundColorAttributeName as String):color(col)])
            c.textPosition = .zero;CTLineDraw(CTLineCreateWithAttributedString(a),c);c.restoreGState()
        }
        box(0,0,1600,1000,0xE9E5CE)
        ellipse(1120,80,180,180,0xEAAF73)
        // Distant cliffs and their terraced contours.
        for layer in 0..<3 {
            let base=Double(220+layer*100)
            var p=[CGPoint(x:0,y:800)]
            for x in stride(from:0,through:1600,by:20) {
                let y=base+70*sin(Double(x)*0.006+Double(layer))+35*cos(Double(x)*0.017)
                p.append(CGPoint(x:Double(x),y:y))
            }
            p.append(CGPoint(x:1600,y:800));path(p,[0xB4C7B6,0x7FA99E,0x427F7D][layer])
        }
        // River opens beneath the falls.
        path([CGPoint(x:640,y:420),CGPoint(x:965,y:420),CGPoint(x:1300,y:1000),CGPoint(x:330,y:1000)],0x8FC6BD)
        for i in 0..<80 {
            let y=440+Double(i)*7.1
            let x=620+sin(Double(i)*1.7)*110
            line([CGPoint(x:x,y:y),CGPoint(x:1040+cos(Double(i))*95,y:y+3)],i%3==0 ? 0xEBEFCC:0x397B7A,Double(i%3+1))
        }
        // Village on the left bank.
        for i in 0..<9 {
            let x=100+Double(i%3)*140, y=410+Double(i/3)*120
            box(x,y,106,92,0xF3D9AE)
            path([CGPoint(x:x-12,y:y),CGPoint(x:x+52,y:y-43),CGPoint(x:x+118,y:y)],0xBA604D)
            box(x+17,y+24,22,30,0x214F56);box(x+67,y+24,22,30,0x214F56)
            box(x+47,y+60,23,32,0x846F58)
            line([CGPoint(x:x-10,y:y+94),CGPoint(x:x+123,y:y+94)],0x264F53,5)
        }
        // Broad waterfall: small irregular foam contours continue across its seam.
        path([CGPoint(x:697,y:90),CGPoint(x:916,y:90),CGPoint(x:949,y:750),CGPoint(x:660,y:750)],0xCBE3D5)
        for i in 0..<115 {
            let y=98+Double(i)*5.6
            let x=690+14*sin(Double(i)*0.57)
            line([CGPoint(x:x,y:y),CGPoint(x:797,y:y+sin(Double(i))*2),CGPoint(x:932+10*cos(Double(i)),y:y+4)],i%3==0 ? 0xF5F0D8:0x418985,i%3==0 ? 3:2)
        }
        // Rock bridge crosses the water.
        c.setStrokeColor(color(0xDFB68A));c.setLineWidth(35)
        c.move(to:CGPoint(x:480,y:780));c.addCurve(to:CGPoint(x:1120,y:780),control1:CGPoint(x:640,y:620),control2:CGPoint(x:950,y:620));c.strokePath()
        for i in 0..<25 {
            let x=490+Double(i)*26, t=Double(i)/24
            let y=778-112*sin(t*Double.pi)
            line([CGPoint(x:x,y:y-5),CGPoint(x:x,y:y-45)],0x294F50,5)
        }
        c.setStrokeColor(color(0x294F50));c.setLineWidth(5);c.move(to:CGPoint(x:480,y:744));c.addCurve(to:CGPoint(x:1120,y:744),control1:CGPoint(x:640,y:580),control2:CGPoint(x:950,y:580));c.strokePath()
        // Observatory and pines on the opposite bank.
        box(1195,360,150,245,0xE8D3A6);ellipse(1170,291,200,125,0xCC7859);box(1180,362,180,15,0x203F49)
        for row in 0..<4 { for col in 0..<3 { box(1213+Double(col)*42,405+Double(row)*44,19,28,0x386368) } }
        for i in 0..<15 {
            let x=1050+Double(i)*40, y=540+45*sin(Double(i)), h=90+Double(i%4)*24
            box(x-3,y,6,100,0x315153)
            for tier in 0..<3 {let yy=y-Double(tier)*h/4;path([CGPoint(x:x-32,y:yy+35),CGPoint(x:x,y:yy-h/2),CGPoint(x:x+32,y:yy+35)],i%2==0 ? 0x234F55:0x376F69)}
        }
        // Foreground shrubs frame the spread, away from the center seam.
        for i in 0..<65 {
            let side=i%2==0 ? Double(i%15)*29 : 1200+Double(i%14)*30
            ellipse(side,860+Double(i%7)*17,90,55,i%3==0 ? 0x749887:0x214F55)
        }
        // Small traveller provides scale on the bridge.
        ellipse(994,676,17,18,0x263E45);box(993,693,20,27,0xCC654B)
        line([CGPoint(x:998,y:719),CGPoint(x:996,y:736)],0x263E45,4);line([CGPoint(x:1007,y:719),CGPoint(x:1011,y:734)],0x263E45,4)
        box(52,45,480,120,0xF4EED8);label("THE QUIET VALLEY",78,99,36,0x264E53);label("An original journey  /  ShuiMan",80,139,20,0x5A7C74)
        label("Beyond the bridge, a new chapter awaits.",78,940,21,0xF6EACD)
        return c.makeImage()!
    }
    static func generate(at root:URL) throws {
        let wide=landscape(),left=wide.cropping(to:CGRect(x:0,y:0,width:800,height:1000))!,right=wide.cropping(to:CGRect(x:800,y:0,width:800,height:1000))!
        let c=CGContext(data:nil,width:800,height:1200,bitsPerComponent:8,bytesPerRow:0,space:CGColorSpaceCreateDeviceRGB(),bitmapInfo:CGImageAlphaInfo.premultipliedLast.rawValue)!
        c.setFillColor(CGColor(red:0.96,green:0.94,blue:0.86,alpha:1));c.fill(CGRect(x:0,y:0,width:800,height:1200))
        c.draw(wide,in:CGRect(x:35,y:520,width:730,height:456))
        c.draw(left.cropping(to:CGRect(x:60,y:350,width:600,height:450))!,in:CGRect(x:35,y:160,width:350,height:300))
        c.draw(right.cropping(to:CGRect(x:200,y:280,width:530,height:450))!,in:CGRect(x:415,y:160,width:350,height:300))
        for (s,y,size) in [("THE QUIET VALLEY",1100.0,48.0),("01 / A BRIDGE TO TOMORROW",1040,23),("Original illustrated demo • ShuiMan",75,23)] {
            let a=NSAttributedString(string:s,attributes:[NSAttributedString.Key(kCTFontAttributeName as String):CTFontCreateWithName("AvenirNext-DemiBold" as CFString,size,nil),NSAttributedString.Key(kCTForegroundColorAttributeName as String):CGColor(red:0.15,green:0.31,blue:0.33,alpha:1)])
            c.textPosition=CGPoint(x:35,y:y);CTLineDraw(CTLineCreateWithAttributedString(a),c)
        }
        let cover=c.makeImage()!
        let pair=PairAnalyzer.analyze(first:left,second:right,firstIndex:1)
        print("SHOWCASE actual pair automatic=\(pair.automatic) score=\(pair.score) bands=\(pair.matchingBands) margin=\(pair.placementMargin)")
        let folder=root.appendingPathComponent("Valley/Quiet Valley/Volume 01")
        try FileManager.default.createDirectory(at:folder,withIntermediateDirectories:true)
        try Fixtures.png(cover).write(to:folder.appendingPathComponent("01-cover.png"))
        try Fixtures.png(left).write(to:folder.appendingPathComponent("02-left.png"))
        try Fixtures.png(right).write(to:folder.appendingPathComponent("03-right.png"))
        try Fixtures.png(wide).write(to:root.appendingPathComponent("valley-reference.png"))
        let epub=root.appendingPathComponent("Valley/Quiet Valley/Volume 02.epub")
        if FileManager.default.fileExists(atPath:epub.path) { try FileManager.default.removeItem(at:epub) }
        let archive=try Archive(url:epub,accessMode:.create)
        func add(_ name:String,_ data:Data) throws {
            try archive.addEntry(with:name,type:.file,uncompressedSize:Int64(data.count),compressionMethod:.none){offset,size in data.subdata(in:Int(offset)..<(Int(offset)+size))}
        }
        func text(_ name:String,_ value:String) throws {try add(name,Data(value.utf8))}
        try text("mimetype","application/epub+zip")
        try text("META-INF/container.xml","<container><rootfiles><rootfile full-path='book.opf'/></rootfiles></container>")
        try text("book.opf","<package version='2.0'><metadata><title>Quiet Valley · Volume 02</title><meta name='cover' content='coverimage'/></metadata><manifest><item id='p0' href='cover.xhtml' media-type='application/xhtml+xml'/><item id='p1' href='spread.xhtml' media-type='application/xhtml+xml'/><item id='coverimage' href='cover.png' media-type='image/png'/><item id='im1' href='sideways.png' media-type='image/png'/></manifest><spine><itemref idref='p0'/><itemref idref='p1'/></spine></package>")
        try text("cover.xhtml","<html><body><img src='cover.png'/></body></html>")
        try text("spread.xhtml","<html><body><img style='transform:rotate(90deg)' src='sideways.png'/></body></html>")
        try add("cover.png",Fixtures.png(cover))
        try add("sideways.png",Fixtures.png(ImageTools.rotate(wide,clockwise:270)))
    }
}

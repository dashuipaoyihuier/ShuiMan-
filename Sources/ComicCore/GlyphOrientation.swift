import Foundation
import CoreGraphics
import Vision
import CoreText

public struct GlyphOrientationEvidence: Codable, Sendable {
    public var scores: [String: Double] = [:]
    public var characters: [String: Int] = [:]
    public var rotation: Int?
    public var regions = 0
}

/// Reflow CJK character grids inside enclosed white regions into horizontal OCR lines.
/// The glyph pixels retain their candidate orientation; vertical layout no longer masks it.
public enum GlyphOrientation {
    private struct Mask { var width:Int; var height:Int; var pixels:[UInt8]; var originX=0; var originY=0 }
    private struct Component {
        var ink:Bool;var x:Int;var y:Int;var right:Int;var bottom:Int;var points:[Int]
        var area:Int {(right-x)*(bottom-y)}
    }
    public static func reviewSheets(_ image:CGImage) throws -> [Int:CGImage] {
        let masks=try regions(image)
        return Dictionary(uniqueKeysWithValues:[0,90,180,270].compactMap { angle in
            sheet(masks.compactMap{line(rotate($0,angle:angle))}).map{(angle,$0)}
        })
    }
    public static func analyze(_ image:CGImage) throws -> GlyphOrientationEvidence {
        let masks=try regions(image)
        var evidence=GlyphOrientationEvidence();evidence.regions=masks.count
        guard !masks.isEmpty else {return evidence}
        // OCR may read sideways CJK glyphs while reporting a horizontal text box.
        // Validate the physical strokes against rendered candidates, not OCR confidence.
        struct Vote {var region:Int;var bounds:CGRect;var rotation:Int;var score:Double;var margin:Double}
        var votes:[Vote]=[]
        for angle in [0,90,180,270] {
            try Task.checkCancellation()
            let lines=masks.enumerated().compactMap { index,mask in line(rotate(mask,angle:angle)).map{(index,$0)} }
            guard let sheet=sheet(lines.map{$0.1}) else {continue}
            let request=VNRecognizeTextRequest();request.revision=VNRecognizeTextRequestRevision3
            request.recognitionLevel = .accurate;request.usesLanguageCorrection=false;request.minimumTextHeight=0
            request.recognitionLanguages=["zh-Hant","en-US"]
            try VNImageRequestHandler(cgImage:sheet).perform([request])
            for observation in request.results ?? [] {
                guard let candidate=observation.topCandidates(1).first,candidate.confidence>=0.3 else {continue}
                let text=candidate.string
                for index in text.indices {
                    let character=String(text[index])
                    guard character.unicodeScalars.contains(where:{(0x3400...0x9fff).contains($0.value)}),
                          let rectangle=try? candidate.boundingBox(for:index..<text.index(after:index)) else {continue}
                    let center=rectangle.boundingBox
                    let column=Int((Double(center.midX)*Double(sheet.width)-12.0)/48.0)
                    let row=Int(((1.0-Double(center.midY))*Double(sheet.height)-12.0)/64.0)
                    guard lines.indices.contains(row),lines[row].1.indices.contains(column) else {continue}
                    let actual=normalized(lines[row].1[column])
                    let templates=templates(character)
                    var matches:[(Int,Double)]=[]
                    for turn in 0..<4 {
                        let score=templates.map{similarity(actual,rotateSquare($0,turn:turn))}.max() ?? 0
                        matches.append((turn*90,score))
                    }
                    matches.sort{$0.1>$1.1}
                    let best=matches[0],margin=best.1-matches[1].1
                    guard best.1>=0.55,margin>=0.035 else {continue}
                    let region=lines[row].0,tile=lines[row].1[column],original=masks[region]
                    let bounds: CGRect
                    switch angle {
                    case 90: bounds=CGRect(x:tile.originY,y:original.height-tile.originX-tile.width,width:tile.height,height:tile.width)
                    case 180: bounds=CGRect(x:original.width-tile.originX-tile.width,y:original.height-tile.originY-tile.height,width:tile.width,height:tile.height)
                    case 270: bounds=CGRect(x:original.width-tile.originY-tile.height,y:tile.originX,width:tile.height,height:tile.width)
                    default: bounds=CGRect(x:tile.originX,y:tile.originY,width:tile.width,height:tile.height)
                    }
                    let vote=Vote(region:region,bounds:bounds,rotation:(angle-best.0+360)%360,score:best.1,margin:margin)
                    if let existing=votes.firstIndex(where:{ value in
                        let overlap=value.bounds.intersection(bounds)
                        return value.region==region && !overlap.isNull && overlap.width*overlap.height>min(value.bounds.width*value.bounds.height,bounds.width*bounds.height)*0.6
                    }) {
                        if margin*best.1>votes[existing].margin*votes[existing].score {votes[existing]=vote}
                    } else {votes.append(vote)}
                }
            }
        }
        for vote in votes {
            evidence.scores[String(vote.rotation),default:0]+=vote.score
            evidence.characters[String(vote.rotation),default:0]+=1
        }
        let ranked=evidence.scores.sorted{$0.value>$1.value}
        if let best=ranked.first {
            let runner=ranked.dropFirst().first?.value ?? 0
            if evidence.characters[best.key,default:0]>=3,best.value>=2.4,
               best.value-runner>=1.8,best.value>=max(0.1,runner)*1.6 {
                evidence.rotation=Int(best.key)
            }
        }
        return evidence
    }
    private final class Templates: NSObject {let values:[[UInt8]];init(_ values:[[UInt8]]) {self.values=values}}
    private static let templateCache:NSCache<NSString,Templates> = {
        let cache=NSCache<NSString,Templates>();cache.countLimit=1024;return cache
    }()
    private static func templates(_ character:String)->[[UInt8]] {
        if let cached=templateCache.object(forKey:character as NSString) {return cached.values}
        let values=["PingFangTC-Semibold","STHeitiTC-Light","STKaiti","SongtiTC-Bold"].compactMap { name -> [UInt8]? in
            var pixels=[UInt8](repeating:255,count:80*80)
            let result=pixels.withUnsafeMutableBytes { bytes -> Bool in
                guard let context=CGContext(data:bytes.baseAddress,width:80,height:80,bitsPerComponent:8,bytesPerRow:80,space:CGColorSpaceCreateDeviceGray(),bitmapInfo:0) else {return false}
                context.setFillColor(CGColor(gray:1,alpha:1));context.fill(CGRect(x:0,y:0,width:80,height:80))
                let font=CTFontCreateWithName(name as CFString,60,nil)
                let string=NSAttributedString(string:character,attributes:[.init(kCTFontAttributeName as String):font,.init(kCTForegroundColorAttributeName as String):CGColor(gray:0,alpha:1)])
                context.textPosition=CGPoint(x:8,y:15);CTLineDraw(CTLineCreateWithAttributedString(string),context);return true
            }
            guard result else {return nil}
            return normalized(Mask(width:80,height:80,pixels:pixels.map{$0<205 ? 0 : 255}))
        }
        templateCache.setObject(Templates(values),forKey:character as NSString);return values
    }
    private static func normalized(_ mask:Mask)->[UInt8] {
        var minX=mask.width,minY=mask.height,maxX=0,maxY=0
        for y in 0..<mask.height {for x in 0..<mask.width where mask.pixels[y*mask.width+x]<128 {
            minX=min(minX,x);minY=min(minY,y);maxX=max(maxX,x);maxY=max(maxY,y)
        }}
        guard minX<=maxX,minY<=maxY else {return [UInt8](repeating:0,count:1024)}
        var result=[UInt8](repeating:0,count:1024)
        for y in 0..<32 {for x in 0..<32 {
            let sx=minX+min(maxX-minX,Int((Double(x)+0.5)*Double(maxX-minX+1)/32))
            let sy=minY+min(maxY-minY,Int((Double(y)+0.5)*Double(maxY-minY+1)/32))
            result[y*32+x]=mask.pixels[sy*mask.width+sx]<128 ? 1 : 0
        }}
        return result
    }
    private static func rotateSquare(_ pixels:[UInt8],turn:Int)->[UInt8] {
        if turn==0 {return pixels}
        var result=[UInt8](repeating:0,count:1024)
        for y in 0..<32 {for x in 0..<32 {
            let nx=turn==1 ? 31-y : (turn==2 ? 31-x : y)
            let ny=turn==1 ? x : (turn==2 ? 31-y : 31-x)
            result[ny*32+nx]=pixels[y*32+x]
        }}
        return result
    }
    private static func similarity(_ a:[UInt8],_ b:[UInt8])->Double {
        var intersection=0,total=0
        for i in a.indices {intersection+=Int(a[i]&b[i]);total+=Int(a[i])+Int(b[i])}
        return Double(2*intersection)/Double(max(1,total))
    }
    private static func regions(_ image:CGImage) throws -> [Mask] {
        let factor=min(1,1000.0/Double(max(image.width,image.height)))
        let w=Int(Double(image.width)*factor),h=Int(Double(image.height)*factor)
        var pixels=[UInt8](repeating:255,count:w*h)
        let ok=pixels.withUnsafeMutableBytes { bytes -> Bool in
            guard let context=CGContext(data:bytes.baseAddress,width:w,height:h,bitsPerComponent:8,bytesPerRow:w,space:CGColorSpaceCreateDeviceGray(),bitmapInfo:0) else {return false}
            context.setFillColor(CGColor(gray:1,alpha:1));context.fill(CGRect(x:0,y:0,width:w,height:h))
            context.draw(image,in:CGRect(x:0,y:0,width:w,height:h));return true
        }
        guard ok else {return []}
        var labels=[Int](repeating:-1,count:w*h),components:[Component]=[]
        func neighbors(_ p:Int)->[Int] {
            let x=p%w,y=p/w
            return [x>0 ? p-1 : -1,x+1<w ? p+1 : -1,y>0 ? p-w : -1,y+1<h ? p+w : -1].filter{$0>=0}
        }
        for p in pixels.indices where labels[p]<0 {
            if p%w==0 {try Task.checkCancellation()}
            let ink=pixels[p]<205,id=components.count
            var component=Component(ink:ink,x:p%w,y:p/w,right:p%w+1,bottom:p/w+1,points:[p]);labels[p]=id
            var head=0
            while head<component.points.count {
                let q=component.points[head];head+=1
                let x=q%w,y=q/w
                component.x=min(component.x,x);component.y=min(component.y,y);component.right=max(component.right,x+1);component.bottom=max(component.bottom,y+1)
                for next in neighbors(q) where labels[next]<0 && (pixels[next]<205)==ink {labels[next]=id;component.points.append(next)}
            }
            components.append(component)
        }
        var groups:[Int:[Int]]=[:]
        for (id,c) in components.enumerated() where c.ink && c.points.count>=2 && c.right-c.x<w*15/100 && c.bottom-c.y<h*15/100 {
            var adjacent:[Int:Int]=[:]
            for p in c.points {for n in neighbors(p) where pixels[n]>=205 {adjacent[labels[n],default:0]+=1}}
            guard let parent=adjacent.max(by:{$0.value<$1.value})?.key else {continue}
            let area=components[parent]
            guard area.points.count>=300,Double(area.points.count)/Double(area.area)>=0.5,area.area<w*h/4,
                  c.x>area.x,c.y>area.y,c.right<area.right,c.bottom<area.bottom else {continue}
            groups[parent,default:[]].append(id)
        }
        var masks:[Mask]=[]
        for key in groups.keys.sorted() {
            let ids=groups[key]!
            guard ids.count>=4 else {continue}
            let parts=ids.map{components[$0]}
            let x=parts.map(\.x).min()!,y=parts.map(\.y).min()!,right=parts.map(\.right).max()!,bottom=parts.map(\.bottom).max()!
            let width=right-x,height=bottom-y
            guard width>=10,height>=10 else {continue}
            var mask=Mask(width:width,height:height,pixels:[UInt8](repeating:255,count:width*height))
            for part in parts {for p in part.points {mask.pixels[(p/w-y)*width+p%w-x]=0}}
            if line(mask) != nil {masks.append(mask)}
        }
        // Bound OCR cost and give larger text grids priority over tiny decoration.
        return Array(masks.sorted{$0.width*$0.height>$1.width*$1.height}.prefix(12))
    }
    private static func rotate(_ mask:Mask,angle:Int)->Mask {
        if angle==0 {return mask}
        let w=angle==180 ? mask.width : mask.height,h=angle==180 ? mask.height : mask.width
        var result=Mask(width:w,height:h,pixels:[UInt8](repeating:255,count:w*h))
        for y in 0..<mask.height {for x in 0..<mask.width {
            let nx=angle==90 ? mask.height-1-y : (angle==180 ? mask.width-1-x : y)
            let ny=angle==90 ? x : (angle==180 ? mask.height-1-y : mask.width-1-x)
            result.pixels[ny*w+nx]=mask.pixels[y*mask.width+x]
        }}
        return result
    }
    private static func bands(_ projection:[Bool])->[Range<Int>] {
        var result:[Range<Int>]=[];var start:Int?
        for i in projection.indices {
            if projection[i],start==nil {start=i}
            if !projection[i],let first=start {result.append(first..<i);start=nil}
        }
        if let first=start {result.append(first..<projection.count)}
        return result
    }
    private static func line(_ mask:Mask)->[Mask]? {
        var xs=[Bool](repeating:false,count:mask.width),ys=[Bool](repeating:false,count:mask.height)
        for y in 0..<mask.height {for x in 0..<mask.width where mask.pixels[y*mask.width+x]==0 {xs[x]=true;ys[y]=true}}
        let xb=bands(xs),yb=bands(ys)
        let sizes=(xb+yb).map(\.count).filter{$0>=5}.sorted()
        guard !sizes.isEmpty else {return nil}
        let pitch=Double(sizes[sizes.count/2])
        func merged(_ bands:[Range<Int>])->[Range<Int>] {
            var result:[Range<Int>]=[]
            for band in bands {
                if let previous=result.last,Double(previous.count)<pitch*0.65,Double(band.upperBound-previous.lowerBound)<=pitch*1.4 {
                    result[result.count-1]=previous.lowerBound..<band.upperBound
                } else {result.append(band)}
            }
            return result
        }
        var glyphs:[Mask]=[]
        for x in merged(xb).reversed() {for y in merged(yb) {
            guard Double(x.count)/Double(y.count)>=0.6,Double(x.count)/Double(y.count)<=1.65,
                  Double(min(x.count,y.count))>=pitch*0.6 else {continue}
            var pixels:[UInt8]=[]
            for row in y {pixels.append(contentsOf:mask.pixels[(row*mask.width+x.lowerBound)..<(row*mask.width+x.upperBound)])}
            let coverage=Double(pixels.filter{$0==0}.count)/Double(pixels.count)
            guard coverage>=0.08,coverage<=0.80 else {continue}
            glyphs.append(Mask(width:x.count,height:y.count,pixels:pixels,originX:x.lowerBound,originY:y.lowerBound))
        }}
        return glyphs.count>=3 && glyphs.count<=24 ? glyphs : nil
    }
    private static func sheet(_ lines:[[Mask]])->CGImage? {
        guard !lines.isEmpty else {return nil}
        let cell=48,w=24+cell*(lines.map(\.count).max() ?? 1),h=24+lines.count*64
        guard let context=CGContext(data:nil,width:w,height:h,bitsPerComponent:8,bytesPerRow:0,space:CGColorSpaceCreateDeviceGray(),bitmapInfo:0) else {return nil}
        context.setFillColor(CGColor(gray:1,alpha:1));context.fill(CGRect(x:0,y:0,width:w,height:h))
        for (row,glyphs) in lines.enumerated() {for (column,glyph) in glyphs.enumerated() {
            guard let provider=CGDataProvider(data:Data(glyph.pixels) as CFData),let image=CGImage(width:glyph.width,height:glyph.height,bitsPerComponent:8,bitsPerPixel:8,bytesPerRow:glyph.width,space:CGColorSpaceCreateDeviceGray(),bitmapInfo:CGBitmapInfo(rawValue:0),provider:provider,decode:nil,shouldInterpolate:true,intent:.defaultIntent) else {continue}
            context.draw(image,in:CGRect(x:12+column*cell,y:h-12-(row+1)*64,width:40,height:40))
        }}
        return context.makeImage()
    }
}

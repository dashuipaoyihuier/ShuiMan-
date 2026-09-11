import Foundation
import CoreGraphics

public struct PairDecision: Codable, Sendable {
    public var firstIndex: Int
    public var swapped: Bool
    public var verticalOffset: Double
    public var rightScale: Double?
    public var score: Double
    public var correlation: Double
    public var detailCorrelation: Double
    public var meanError: Double
    public var matchingBands: Int
    public var placementMargin: Double
    public var automatic: Bool
    public var suggested: Bool
    public init(firstIndex: Int, swapped: Bool = false, verticalOffset: Double = 0, score: Double = 0,
                correlation: Double = 0, detailCorrelation: Double = 0, meanError: Double = 1,
                matchingBands: Int = 0, placementMargin: Double = 0, automatic: Bool = false, suggested: Bool = false, rightScale: Double = 1) {
        self.firstIndex=firstIndex; self.swapped=swapped; self.verticalOffset=verticalOffset; self.score=score
        self.correlation=correlation; self.detailCorrelation=detailCorrelation; self.meanError=meanError
        self.matchingBands=matchingBands; self.placementMargin=placementMargin; self.automatic=automatic; self.suggested=suggested
        self.rightScale=rightScale
    }
}

public enum PairAnalyzer {
    public static let algorithmVersion = "seam-scale-offset-v2"
    private static let height = 384
    private struct Features { let left: [Double]; let right: [Double]; let thumbnail: [Double] }
    public static func diagnostics(first:CGImage,second:CGImage)->[String:Double] {
        guard let a=features(first),let b=features(second) else { return [:] }
        func ink(_ f:Features)->Double { Double(f.thumbnail.filter{$0<0.75}.count)/Double(f.thumbnail.count) }
        func changes(_ p:[Double])->Double { Double(zip(p.dropFirst(),p).filter{abs($0.0-$0.1)>0.035}.count) }
        return ["firstOwnEdges":correlate(a.left,a.right),"secondOwnEdges":correlate(b.left,b.right),"firstInk":ink(a),"secondInk":ink(b),"firstLeftChanges":changes(a.left),"firstRightChanges":changes(a.right),"secondLeftChanges":changes(b.left),"secondRightChanges":changes(b.right)]
    }

    public static func analyze(first: CGImage, second: CGImage, firstIndex: Int) -> PairDecision {
        let ratios = [first,second].map { Double($0.width)/Double($0.height) }
        guard ratios.allSatisfy({ (0.45...0.90).contains($0) }), abs(ratios[0]-ratios[1]) < 0.18,
              let a = features(first), let b = features(second), meanError(a.thumbnail,b.thumbnail) >= 0.025,
              [a,b].allSatisfy({ Double($0.thumbnail.filter{$0<0.75}.count)/Double($0.thumbnail.count)>=0.07 }) else {
            return PairDecision(firstIndex: firstIndex)
        }
        let forward = placement(left: a.right, right: b.left, index: firstIndex, swapped: false)
        let reverse = placement(left: b.right, right: a.left, index: firstIndex, swapped: true)
        var best = forward.score >= reverse.score ? forward : reverse
        best.placementMargin = abs(forward.score-reverse.score)
        best.automatic = best.score >= 0.68 && best.correlation >= 0.84 && best.detailCorrelation >= 0.40 && best.meanError <= 0.12 && best.matchingBands >= 5 && best.placementMargin >= 0.15
        best.suggested = best.automatic || (best.score >= 0.49 && best.correlation >= 0.64 && best.detailCorrelation >= 0.19 && best.meanError <= 0.16 && best.matchingBands >= 3 && best.placementMargin >= 0.10)
        return best
    }
    private static func grayscale(_ image: CGImage, width: Int, height: Int) -> [Double]? {
        var bytes=[UInt8](repeating:255,count:width*height)
        let success=bytes.withUnsafeMutableBytes { pointer -> Bool in
            guard let context=CGContext(data:pointer.baseAddress,width:width,height:height,bitsPerComponent:8,bytesPerRow:width,space:CGColorSpaceCreateDeviceGray(),bitmapInfo:CGImageAlphaInfo.none.rawValue) else { return false }
            context.setFillColor(CGColor(gray:1,alpha:1)); context.fill(CGRect(x:0,y:0,width:width,height:height))
            context.interpolationQuality = .high
            context.draw(image,in:CGRect(x:0,y:0,width:width,height:height)); return true
        }
        return success ? bytes.map { Double($0)/255 } : nil
    }
    private static func features(_ image: CGImage) -> Features? {
        let w=max(64,Int((Double(image.width)/Double(image.height)*Double(height)).rounded()))
        guard let pixels=grayscale(image,width:w,height:height), let thumb=grayscale(image,width:24,height:32) else { return nil }
        func ink(_ x: Int) -> Double { Double((0..<height).filter { pixels[$0*w+x] < 0.85 }.count)/Double(height) }
        let margin=Int(Double(w)*0.04)
        let left=(0...margin).first(where:{ ink($0)>0.12 }) ?? 0
        let right=(0...margin).map { w-1-$0 }.first(where:{ ink($0)>0.12 }) ?? (w-1)
        func profile(_ range: Range<Int>) -> [Double] {
            (0..<height).map { y in range.reduce(0.0) { $0+pixels[y*w+$1] }/Double(range.count) }
        }
        return Features(left:profile(left..<min(w,left+3)),right:profile(max(0,right-2)..<(right+1)),thumbnail:thumb)
    }
    private static func placement(left: [Double], right: [Double], index: Int, swapped: Bool) -> PairDecision {
        var best=PairDecision(firstIndex:index,swapped:swapped)
        guard min(deviation(left),deviation(right)) >= 0.08 else { return best }
        // Shared paper shading and scan-frame corners can correlate almost perfectly.
        // Require actual texture transitions along both proposed inner edges.
        let changes=[left,right].map { profile in zip(profile.dropFirst(),profile).filter{abs($0.0-$0.1)>0.035}.count }
        guard changes.allSatisfy({Double($0)>=Double(height)*0.12}) else { return best }
        func measure(scale:Double,offset:Double) -> PairDecision {
            var a:[Double]=[],b:[Double]=[]
            a.reserveCapacity(height);b.reserveCapacity(height)
            for row in 0..<height {
                let source=(Double(row)/Double(height)-offset)/scale*Double(height)
                guard source>=0,source<Double(height-1) else { continue }
                let lower=Int(source), fraction=source-Double(lower)
                a.append(left[row]);b.append(right[lower]*(1-fraction)+right[lower+1]*fraction)
            }
            guard a.count>=Int(Double(height)*0.80) else { return PairDecision(firstIndex:index,swapped:swapped) }
            let correlation=correlate(a,b), error=meanError(a,b)
            let da=zip(a.dropFirst(),a).map(-), db=zip(b.dropFirst(),b).map(-)
            let detail=correlate(da,db)
            var bands=0
            for band in 0..<12 {
                let range=(band*a.count/12)..<((band+1)*a.count/12)
                let x=Array(a[range]),y=Array(b[range])
                if min(deviation(x),deviation(y)) >= 0.055 && correlate(x,y)>0.7 && meanError(x,y)<0.15 { bands+=1 }
            }
            let score=0.55*max(0,correlation)+0.20*max(0,detail)+0.25*Double(bands)/12
            return PairDecision(firstIndex:index,swapped:swapped,verticalOffset:offset,score:score,correlation:correlation,detailCorrelation:detail,meanError:error,matchingBands:bands,rightScale:scale)
        }
        for shift in -5...5 {
            let candidate=measure(scale:1,offset:Double(shift)/Double(height))
            if candidate.score>best.score { best=candidate }
        }
        let baseline=best
        // Search a bounded similarity transform; both axes scale together, so artwork
        // is never stretched to force a rectangular result. Keep the complete union.
        var coarse=best
        for s in -5...5 { for t in -8...8 {
            let candidate=measure(scale:1+Double(s)*0.02,offset:Double(t)*0.01)
            if candidate.score>coarse.score { coarse=candidate }
        } }
        for s in -3...3 { for t in -4...4 {
            let candidate=measure(scale:(coarse.rightScale ?? 1)+Double(s)*0.002,offset:coarse.verticalOffset+Double(t)*0.001)
            if candidate.score>best.score { best=candidate }
        } }
        if best.score < baseline.score+0.025 { return baseline }
        if abs((best.rightScale ?? 1)-1)>0.015 || abs(best.verticalOffset)>0.015 {
            guard best.correlation>=0.78,best.detailCorrelation>=0.28,best.matchingBands>=4 else { return baseline }
        }
        // Fine registration uses a continuous objective. The discrete band count is
        // useful for deciding whether to pair, but can otherwise quantize alignment.
        let seed=best
        func fidelity(_ value:PairDecision)->Double { value.correlation+0.25*value.detailCorrelation-0.5*value.meanError }
        for s in -10...10 { for t in -6...6 {
            let candidate=measure(scale:(seed.rightScale ?? 1)+Double(s)*0.0005,offset:seed.verticalOffset+Double(t)*0.0005)
            if candidate.matchingBands>=seed.matchingBands-1,candidate.score>=seed.score-0.025,fidelity(candidate)>fidelity(best) { best=candidate }
        } }
        return best
    }
    private static func meanError(_ a:[Double],_ b:[Double])->Double { zip(a,b).reduce(0) { $0+abs($1.0-$1.1) }/Double(max(1,min(a.count,b.count))) }
    private static func deviation(_ a:[Double])->Double {
        let mean=a.reduce(0,+)/Double(a.count)
        return sqrt(a.reduce(0) { $0+pow($1-mean,2) }/Double(a.count))
    }
    private static func correlate(_ a:[Double],_ b:[Double])->Double {
        let ma=a.reduce(0,+)/Double(a.count),mb=b.reduce(0,+)/Double(b.count)
        var xy=0.0,xx=0.0,yy=0.0
        for (x,y) in zip(a,b) { xy+=(x-ma)*(y-mb); xx+=pow(x-ma,2); yy+=pow(y-mb,2) }
        return xy/max(1e-8,sqrt(xx*yy))
    }
}

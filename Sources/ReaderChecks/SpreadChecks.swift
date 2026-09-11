import Foundation
import CoreGraphics
import ComicCore

extension ReaderChecks {
    static func testAutomaticSpreads() throws {
        let edge=PageCanvasLayout(viewport:CGSize(width:1600,height:1000),ratios:[1.6],spread:true,verticalOffset:0,zoom:1,fitWidth:false,edgeInset:0)
        try expect(edge.frames==[CGRect(x:0,y:0,width:1600,height:1000)],"Immersive fit removes artificial canvas margins")
        let portrait=PageCanvasLayout(viewport:CGSize(width:1600,height:1000),ratios:[0.7],spread:false,verticalOffset:0,zoom:1,fitWidth:false,edgeInset:0)
        try expect(portrait.frames[0].height==1000 && portrait.frames[0].width==700,"Immersive portrait preserves complete image and aspect ratio")
        let units = (0..<7).map { ReadingUnit(locator:SourceLocator(resource:"unit\($0)"),title:"\($0)",isCover:$0 == 0) }
        let decisions = Dictionary(uniqueKeysWithValues:units.map { ($0.id,SpreadDecision()) })
        var preferences = ReaderPreferences()
        preferences.aggressivePairs = false
        let candidate = PairDecision(firstIndex:2,swapped:true,verticalOffset:-0.01,score:0.85,automatic:true,suggested:true)
        func plan(_ pairs: [Int:PairDecision], _ overrides: [String:PageOverride] = [:], _ analysis: [String:SpreadDecision]? = nil) -> [DisplayGroup] {
            LayoutEngine.groups(units:units,preferences:preferences,overrides:overrides,decisions:analysis ?? decisions,pairs:pairs)
        }
        let combined = plan([2:candidate])
        try expect(combined.first { $0.indices.contains(2) }?.indices == [3,2], "Automatic pair restores physical placement in single-page mode")
        preferences.direction = .rtl
        try expect(plan([2:candidate]) == combined, "Automatic pair placement is independent of navigation direction")
        try expect(plan([2:candidate]).flatMap(\.indices).sorted() == Array(0..<7), "Automatic grouping consumes every source exactly once")
        try expect(plan([2:candidate],[units[2].id:PageOverride(joinNext:false)]).allSatisfy { $0.indices.count == 1 }, "User rejection prevents automatic re-pairing")
        try expect(plan([2:candidate],[units[3].id:PageOverride(rotation:0)]).allSatisfy { $0.indices.count == 1 }, "Manual orientation vetoes automatic pairing")
        var uncertain = decisions; uncertain[units[2].id] = SpreadDecision(uncertain:true)
        try expect(plan([2:candidate],[:],uncertain).allSatisfy { $0.indices.count == 1 }, "Uncertain orientation vetoes automatic pairing")
        try expect(plan([2:candidate],[:],[:]).allSatisfy { $0.indices.count == 1 }, "Pair waits for orientation checks on both halves")
        let rival = PairDecision(firstIndex:3,score:0.82,automatic:true,suggested:true)
        try expect(plan([2:candidate,3:rival]).allSatisfy { $0.indices.count == 1 }, "Conflicting neighboring candidates remain separate")
        let manual = [units[1].id:PageOverride(joinNext:true,swapPair:false)]
        try expect(plan([2:candidate],manual).flatMap(\.indices).sorted() == Array(0..<7), "Manual overlap cannot duplicate an automatically paired page")
        preferences.automaticPairs = false
        try expect(plan([2:candidate]).allSatisfy { $0.indices.count == 1 }, "Automatic pair preference disables automatic merging")
        preferences.automaticPairs=true; preferences.aggressivePairs=true
        let suggested=PairDecision(firstIndex:2,swapped:true,score:0.59,automatic:false,suggested:true,rightScale:1.06)
        try expect(plan([2:suggested]).first(where:{$0.indices.count==2})?.rightScale == 1.06,"Aggressive mode automatically accepts a validated suggestion with scale")
        preferences.aggressivePairs=false
        try expect(plan([2:suggested]).allSatisfy{$0.indices.count==1},"Conservative mode retains the confirmation boundary")
        preferences.smartSpreads=false
        var turned=decisions;turned[units[2].id]=SpreadDecision(rotation:90,standalone:true)
        try expect(plan([:],[:],turned).first(where:{$0.indices.contains(2)})?.spread == true,"Automatic orientation remains independent of smart spreads")
        let oldPreferences = try JSONDecoder().decode(ReaderPreferences.self,from:Data("{\"direction\":\"rtl\",\"layout\":\"double\",\"smartSpreads\":false,\"coverAlone\":false}".utf8))
        try expect(oldPreferences.direction == .rtl && oldPreferences.layout == .double && !oldPreferences.smartSpreads && oldPreferences.automaticPairs, "Version 0.1 preferences load with new defaults")
        let oldOverride = try JSONDecoder().decode(PageOverride.self,from:Data("{\"joinNext\":true,\"swapPair\":true}".utf8))
        try expect(oldOverride.joinNext == true && oldOverride.pairOffset == nil, "Version 0.1 manual pairs remain readable")

        let left = seamFixture(right:false), right = seamFixture(right:true)
        let pair = PairAnalyzer.analyze(first:left,second:right,firstIndex:0)
        print("MEASURE: synthetic pair score=\(pair.score) offset=\(pair.verticalOffset) swapped=\(pair.swapped)")
        try expect(pair.automatic && !pair.swapped, "Rich continuous synthetic seam automatically pairs")
        let reversed = PairAnalyzer.analyze(first:right,second:left,firstIndex:0)
        try expect(reversed.automatic && reversed.swapped, "Seam analysis infers reversed source order")
        let shifted = PairAnalyzer.analyze(first:left,second:seamFixture(right:true,shift:8),firstIndex:0)
        print("MEASURE: shifted pair score=\(shifted.score) offset=\(shifted.verticalOffset)")
        try expect(shifted.suggested && abs(shifted.verticalOffset + 8.0/1024) < 0.003, "Detected offset moves a lowered right image upward")
        let rescaled=PairAnalyzer.analyze(first:left,second:seamFixture(right:true,shift:35,scale:0.94),firstIndex:0)
        print("MEASURE: affine scale=\(rescaled.rightScale ?? 1) offset=\(rescaled.verticalOffset)")
        try expect(rescaled.suggested && abs((rescaled.rightScale ?? 1)-1/0.94)<0.009 && abs(rescaled.verticalOffset+35.0/1024/0.94)<0.009,"Scale and translation recover a mismatched scan without stretching artwork")
        let irregular=PageCanvasLayout(viewport:CGSize(width:1200,height:800),ratios:[0.75,0.75],spread:true,verticalOffset:-0.06,zoom:1,fitWidth:false,rightScale:1.08)
        try expect(irregular.frames[0].maxX==irregular.frames[1].minX && irregular.frames[0].height != irregular.frames[1].height && irregular.frames.allSatisfy{CGRect(origin:.zero,size:irregular.size).contains($0)},"Unequal aligned pages preserve their complete union and black margins")
        let duplicate = PairAnalyzer.analyze(first:left,second:left,firstIndex:0)
        let blank = Fixtures.image(width:768,height:1024,blank:true)
        try expect(!duplicate.suggested && !PairAnalyzer.analyze(first:blank,second:blank,firstIndex:0).suggested, "Duplicate pictures and blank pages never justify pairing")
        let smoothA=seamFixture(right:false,frequency:0.05),smoothB=seamFixture(right:true,frequency:0.05)
        try expect(!PairAnalyzer.analyze(first:smoothA,second:smoothB,firstIndex:0).suggested,"Smooth shared paper gradients cannot justify automatic pairing")
        for offset in [-0.02,0.02] {
            let layout = PageCanvasLayout(viewport:CGSize(width:1200,height:800),ratios:[0.75,0.75],spread:true,verticalOffset:offset,zoom:1,fitWidth:false)
            let frames = layout.frames
            try expect(frames.allSatisfy { CGRect(origin:.zero,size:layout.size).contains($0) } && frames[0].maxX == frames[1].minX && abs(frames[1].minY-frames[0].minY-offset*frames[0].height)<0.001, "Aligned pair keeps complete image bounds, offset \(offset)")
        }
    }
    /// Bytes are generated top-to-bottom. A positive shift lowers the right-hand content.
    static func seamFixture(right: Bool, shift: Int = 0, scale:Double=1, frequency:Double=1) -> CGImage {
        let w=768,h=1024
        var bytes=[UInt8](repeating:0,count:w*h)
        for y in 0..<h { for x in 0..<w {
            let globalX=Double(x+(right ? w : 0)), row=Double(y-(right ? shift : 0))/(right ? scale : 1)*frequency
            let phase=globalX*0.008
            let v=0.50+0.17*sin(row*0.049+phase)+0.15*sin(row*0.117+phase*0.8)+0.13*cos(row*0.189-phase*1.2)
            bytes[y*w+x]=UInt8(max(0,min(255,Int(v*255))))
        } }
        let provider=CGDataProvider(data:Data(bytes) as CFData)!
        return CGImage(width:w,height:h,bitsPerComponent:8,bitsPerPixel:8,bytesPerRow:w,space:CGColorSpaceCreateDeviceGray(),bitmapInfo:CGBitmapInfo(rawValue:CGImageAlphaInfo.none.rawValue),provider:provider,decode:nil,shouldInterpolate:true,intent:.defaultIntent)!
    }
}

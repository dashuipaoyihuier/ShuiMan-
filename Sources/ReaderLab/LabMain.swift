import Foundation
import Vision
import CoreGraphics
import ImageIO
import UniformTypeIdentifiers
import ComicCore

@main
struct ReaderLab {
    static func main() async {
        do { try await run() }
        catch { fputs(error.localizedDescription+"\n", stderr); exit(1) }
    }
    static func run() async throws {
        let args = Array(CommandLine.arguments.dropFirst())
        func value(_ key: String) -> String? { guard let i = args.firstIndex(of: key), args.indices.contains(i+1) else { return nil }; return args[i+1] }
        let mode = value("--mode") ?? "probe"
        if mode == "mobi-check" {
            guard let file=value("--book"),let output=value("--output") else {throw ReaderError.message("--book and --output required")}
            let engine=DocumentEngine();let book=try await engine.open(URL(fileURLWithPath:file))
            var rows:[[String:Any]]=[]
            for i in book.units.indices {
                let image=try await engine.image(at:i,maxPixel:1300)
                rows.append(["position":i+1,"resource":book.units[i].locator.resource,"occurrence":book.units[i].locator.occurrence,"cover":book.units[i].isCover,"width":image.width,"height":image.height])
            }
            try JSONSerialization.data(withJSONObject:["title":book.title,"kind":book.kind.rawValue,"warnings":book.warnings,"pages":rows],options:[.prettyPrinted,.sortedKeys]).write(to:URL(fileURLWithPath:output))
            print("Decoded all \(rows.count) reading units; \(book.title)");return
        }
        if mode == "glyphs" {
            guard let file=value("--book") else {throw ReaderError.message("--book required")}
            let engine=DocumentEngine();let book=try await engine.open(URL(fileURLWithPath:file))
            let positions=(value("--positions") ?? "1").split(separator:",").compactMap{Int($0)}
            for position in positions {
                let image=try await engine.image(at:position-1,maxPixel:1300)
                if let prefix=value("--review-prefix") {
                    for (angle,sheet) in try GlyphOrientation.reviewSheets(image) {
                        let path="\(prefix)-\(position)-\(angle).png"
                        let dest=CGImageDestinationCreateWithURL(URL(fileURLWithPath:path) as CFURL,UTType.png.identifier as CFString,1,nil)!
                        CGImageDestinationAddImage(dest,sheet,nil);CGImageDestinationFinalize(dest)
                    }
                }
                let start=Date();let result=try GlyphOrientation.analyze(image)
                print("\(position): \(String(decoding:try JSONEncoder().encode(result),as:UTF8.self)) seconds=\(Date().timeIntervalSince(start))")
            };return
        }
        if mode == "catalog" {
            guard let path=value("--root"),let output=value("--output") else { throw ReaderError.message("--root and --output required") }
            let start=Date();let volumes=try LibraryScanner.scan(URL(fileURLWithPath:path))
            try JSONEncoder().encode(volumes).write(to:URL(fileURLWithPath:output))
            print("Indexed \(volumes.count) volumes, \(Set(volumes.map(\.seriesID)).count) series in \(Date().timeIntervalSince(start)) seconds");return
        }
        if mode == "plan" {
            guard let file=value("--book"),let output=value("--output") else { throw ReaderError.message("--book and --output required") }
            let engine=DocumentEngine();let book=try await engine.open(URL(fileURLWithPath:file))
            var decisions:[String:SpreadDecision]=[:];var pairs:[Int:PairDecision]=[:]
            for i in book.units.indices { decisions[book.units[i].id]=try await engine.analyze(at:i) }
            for i in 0..<max(0,book.units.count-1) { pairs[i]=try await engine.analyzePair(at:i) }
            var report:[String:Any]=["file":file,"units":book.units.count]
            for aggressive in [false,true] {
                var preferences=ReaderPreferences();preferences.layout = .single;preferences.aggressivePairs=aggressive
                let groups=LayoutEngine.groups(units:book.units,preferences:preferences,overrides:[:],decisions:decisions,pairs:pairs)
                let merged=groups.filter{$0.indices.count==2}.map { group -> [String:Any] in
                    ["positions":group.indices.map{$0+1},"scale":group.rightScale,"offset":group.verticalOffset]
                }
                report[aggressive ? "aggressive" : "conservative"]=merged
                print("aggressive=\(aggressive): \(merged.count) automatic pairs")
            }
            try JSONSerialization.data(withJSONObject:report,options:[.prettyPrinted,.sortedKeys]).write(to:URL(fileURLWithPath:output));return
        }
        if mode == "render-pair" {
            guard let file=value("--book"),let output=value("--output"),let position=Int(value("--position") ?? "") else { throw ReaderError.message("--book, --position, --output required") }
            let engine=DocumentEngine();let book=try await engine.open(URL(fileURLWithPath:file))
            guard position>0,position<book.units.count else { throw ReaderError.message("Invalid pair position") }
            let pair=try await engine.analyzePair(at:position-1)
            let diagnostics=PairAnalyzer.diagnostics(first:try await engine.image(at:position-1,maxPixel:768),second:try await engine.image(at:position,maxPixel:768))
            print(diagnostics)
            let indices=pair.swapped ? [position,position-1] : [position-1,position]
            var images:[CGImage]=[]
            for i in indices { images.append(try await engine.image(at:i,maxPixel:2400)) }
            let corrected = !args.contains("--unaligned")
            let layout=PageCanvasLayout(viewport:CGSize(width:1800,height:1300),ratios:images.map{Double($0.width)/Double($0.height)},spread:true,verticalOffset:corrected ? pair.verticalOffset : 0,zoom:1,fitWidth:false,rightScale:corrected ? pair.rightScale ?? 1 : 1)
            let canvas=CGContext(data:nil,width:1800,height:1300,bitsPerComponent:8,bytesPerRow:0,space:CGColorSpaceCreateDeviceRGB(),bitmapInfo:CGImageAlphaInfo.premultipliedLast.rawValue)!
            canvas.setFillColor(CGColor(gray:0.075,alpha:1));canvas.fill(CGRect(x:0,y:0,width:1800,height:1300))
            for (i,image) in images.enumerated() {
                let rect=layout.frames[i]
                canvas.draw(image,in:CGRect(x:rect.minX,y:1300-rect.maxY,width:rect.width,height:rect.height))
            }
            let destination=CGImageDestinationCreateWithURL(URL(fileURLWithPath:output) as CFURL,UTType.png.identifier as CFString,1,nil)!
            CGImageDestinationAddImage(destination,canvas.makeImage()!,nil)
            guard CGImageDestinationFinalize(destination) else { throw ReaderError.message("Preview write failed") }
            print(String(decoding:try JSONEncoder().encode(pair),as:UTF8.self));return
        }
        if mode == "pairs" {
            guard let file=value("--book"),let output=value("--output") else { throw ReaderError.message("--book and --output required") }
            let engine=DocumentEngine();let book=try await engine.open(URL(fileURLWithPath:file))
            var pairs:[PairDecision]=[]
            for i in 0..<max(0,book.units.count-1) {
                let pair=try await engine.analyzePair(at:i);pairs.append(pair)
                if args.contains("--diagnostics"),pair.suggested {
                    print("DIAGNOSTIC \(i+1):",PairAnalyzer.diagnostics(first:try await engine.image(at:i,maxPixel:768),second:try await engine.image(at:i+1,maxPixel:768)))
                }
            }
            try JSONEncoder().encode(pairs).write(to:URL(fileURLWithPath:output))
            for pair in pairs where pair.suggested { print("\(pair.firstIndex+1)–\(pair.firstIndex+2): auto=\(pair.automatic) swapped=\(pair.swapped) score=\(pair.score) bands=\(pair.matchingBands) offset=\(pair.verticalOffset)") }
            print("Measured \(pairs.count) neighboring pairs of \(book.title)")
            return
        }
        if mode == "corpus" {
            let path=value("--inventory") ?? ".build/corpus/inventory.json"
            let expected=try JSONSerialization.jsonObject(with:Data(contentsOf:URL(fileURLWithPath:path))) as! [String:Any]
            var reports:[[String:Any]]=[];var failures=0
            for reference in expected["books"] as! [[String:Any]] {
                let file=reference["file"] as! String
                let engine=DocumentEngine();let start=Date()
                do {
                    let book=try await engine.open(URL(fileURLWithPath:file))
                    let pages=reference["pages"] as! [[String:Any]]
                    var errors:[String]=[]
                    if book.units.count != pages.count { errors.append("spine count differs") }
                    for (i,unit) in book.units.enumerated() {
                        if i<pages.count && unit.locator.resource != pages[i]["resource"] as? String { errors.append("source order differs at \(i+1)") }
                        if !unit.complex {
                            do { _ = try await engine.image(at:i,maxPixel:256) }
                            catch { errors.append("\(i+1): \(error.localizedDescription)") }
                        }
                    }
                    failures+=errors.count
                    reports.append(["file":file,"sha256":reference["sha256"]!,"units":book.units.count,"native":book.units.filter{!$0.complex}.count,"errors":errors,"seconds":Date().timeIntervalSince(start)])
                    print("\(file): \(book.units.count) units, \(errors.count) errors")
                } catch { failures+=1; reports.append(["file":file,"errors":[error.localizedDescription]]) }
            }
            if let output=value("--output") { try JSONSerialization.data(withJSONObject:["books":reports,"errors":failures],options:[.prettyPrinted,.sortedKeys]).write(to:URL(fileURLWithPath:output)) }
            if failures>0 { throw ReaderError.message("Corpus failures: \(failures)") }
            return
        }
        if mode == "orientation" {
            guard let file = value("--book") else { throw ReaderError.message("--book required") }
            let engine = DocumentEngine(); let book = try await engine.open(URL(fileURLWithPath: file))
            let positions = value("--positions").map { $0.split(separator: ",").compactMap { Int($0) } } ?? Array(1...book.units.count)
            var results: [[String: Any]] = []
            for position in positions {
                let start = Date()
                var unit = book.units[position-1]
                if args.contains("--without-style") { unit.rotationHint = nil }
                let image = try await engine.image(at: position-1, maxPixel: 1300)
                let decision = unit.isCover ? SpreadDecision() : try SpreadAnalyzer.analyze(image: image, unit: unit)
                let item: [String: Any] = ["position":position,"resource":unit.locator.resource,"rotation":decision.rotation,"standalone":decision.standalone,"uncertain":decision.uncertain,"scores":decision.orientationScores,"reason":decision.reason,"seconds":Date().timeIntervalSince(start)]
                results.append(item)
                if decision.rotation != 0 || decision.uncertain { print("\(position): rotation=\(decision.rotation) uncertain=\(decision.uncertain) \(decision.reason)") }
            }
            if let output = value("--output") {
                let report: [String: Any] = ["file":book.sourceURL.lastPathComponent,"algorithm":SpreadAnalyzer.algorithmVersion,"results":results]
                try JSONSerialization.data(withJSONObject: report, options: [.prettyPrinted,.sortedKeys]).write(to: URL(fileURLWithPath: output))
            }
            print("Analyzed \(results.count) pages of \(book.title)")
            return
        }
        if mode == "probe" {
            guard let file = value("--book") else { throw ReaderError.message("--book required") }
            let engine = DocumentEngine(); let book = try await engine.open(URL(fileURLWithPath: file))
            let positions = (value("--positions") ?? "1").split(separator: ",").compactMap { Int($0) }
            for position in positions {
                let original = try await engine.image(at: position-1, maxPixel: Int(value("--size") ?? "1600") ?? 1600)
                for angle in [0,90,180,270] {
                    let image = ImageTools.rotate(original, clockwise: angle)
                    let request = VNRecognizeTextRequest(); request.revision = Int(value("--revision") ?? "3") ?? 3
                    request.recognitionLevel = .accurate; request.usesLanguageCorrection = false
                    request.recognitionLanguages = ["zh-Hant", "en-US"]; request.minimumTextHeight = 0
                    try VNImageRequestHandler(cgImage: image).perform([request])
                    let observations: [[String: Any]] = (request.results ?? []).compactMap { observation in
                        guard let text = observation.topCandidates(1).first else { return nil }
                        let dx = observation.topRight.x-observation.topLeft.x
                        let dy = observation.topRight.y-observation.topLeft.y
                        return ["text":String(text.string.prefix(12)), "count":text.string.count, "confidence":text.confidence,
                                "angle":atan2(dy,dx)*180 / .pi, "width":observation.boundingBox.width, "height":observation.boundingBox.height]
                    }
                    let report: [String: Any] = ["file":book.sourceURL.lastPathComponent,"position":position,"inputRotation":angle,"observations":observations]
                    print(String(decoding: try JSONSerialization.data(withJSONObject: report, options: [.sortedKeys]), as: UTF8.self))
                }
            }
        }
    }
}

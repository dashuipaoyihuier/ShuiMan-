import Foundation
import CoreGraphics
import ComicCore

@main
struct ReaderChecks {
    static func expect(_ value: @autoclosure () -> Bool, _ message: String) throws {
        guard value() else { throw ReaderError.message("FAIL: "+message) }
        print("PASS: "+message)
    }
    static func main() async {
        do { try await run() }
        catch { fputs("\(error.localizedDescription)\n", stderr); exit(1) }
    }
    static func run() async throws {
        let args = Array(CommandLine.arguments.dropFirst())
        let root = URL(fileURLWithPath: FileManager.default.currentDirectoryPath)
        let fixtures = root.appendingPathComponent("Tests/Fixtures")
        try Fixtures.generate(at: fixtures)
        print("Generated original fixtures at \(fixtures.path)")
        if args.first == "--generate-only" { return }
        try await testMOBI(at: fixtures)
        if args.contains("--analyze") {
            let dialogue=Fixtures.verticalDialogue()
            for angle in [0,90,180,270] {
                let detected=try GlyphOrientation.analyze(ImageTools.rotate(dialogue,clockwise:angle))
                print("GLYPH: input=\(angle) \(String(decoding:try JSONEncoder().encode(detected),as:UTF8.self))")
                try expect(detected.rotation==(360-angle)%360,"Vertical CJK physical strokes recover orientation from \(angle) degrees")
            }
            let landscape=Fixtures.image(width:1600,height:1000,wide:true)
            for rotation in [90,270] {
                let stored=ImageTools.rotate(landscape,clockwise:rotation)
                let evidence=try TextOrientation.analyze(stored)
                try expect(evidence.rotation == (360-rotation), "Unstyled synthetic horizontal text restores from \(rotation) degrees")
            }
            let portrait=Fixtures.image(width:800,height:1200)
            let upright=try TextOrientation.analyze(portrait)
            try expect(upright.rotation == 0, "Upright synthetic lettering remains upright")
            for angle in [90,270] {
                let sideways=ImageTools.rotate(portrait,clockwise:angle)
                let result=try SpreadAnalyzer.analyze(image:sideways,unit:ReadingUnit(locator:SourceLocator(resource:"sideways-single"),title:"Single page"))
                try expect(result.rotation == 360-angle && result.standalone,"Landscape-stored ordinary portrait page turns upright at \(angle) degrees")
            }
        }
        let testEngine = DocumentEngine()
        let images = try await testEngine.open(fixtures.appendingPathComponent("Images"))
        try expect(images.units.count == 6, "Image folder imports six pages")
        try expect(images.units.last?.title == "10.png", "Natural sorting puts 10 after 5")
        let wideImage = try await testEngine.image(at: 2)
        try expect(wideImage.width > wideImage.height, "Wide image dimensions remain horizontal")
        let wideDecision = try await testEngine.analyze(at: 2)
        try expect(wideDecision.standalone && wideDecision.rotation == 0, "Wide page gets full-spread presentation")
        let pdf = try await testEngine.open(fixtures.appendingPathComponent("Sample.pdf"))
        try expect(pdf.units.count == 3, "PDF has three pages")
        let pdfWide = try await testEngine.image(at: 1)
        try expect(pdfWide.width > pdfWide.height, "PDF landscape page renders with correct bounds")
        _ = try await testEngine.open(fixtures.appendingPathComponent("Rotated.pdf"))
        let metadataRotated = try await testEngine.image(at: 0)
        try expect(metadataRotated.width > metadataRotated.height, "PDF page rotation metadata is applied to display bounds")
        do { _ = try await testEngine.open(fixtures.appendingPathComponent("Protected.pdf")); throw ReaderError.message("FAIL: protected PDF opened without password") }
        catch ReaderError.passwordRequired { print("PASS: PDF requests its password") }
        do { _ = try await testEngine.open(fixtures.appendingPathComponent("Protected.pdf"), password: "wrong"); throw ReaderError.message("FAIL: protected PDF accepted wrong password") }
        catch ReaderError.passwordRequired { print("PASS: PDF rejects wrong password") }
        let unlocked = try await testEngine.open(fixtures.appendingPathComponent("Protected.pdf"), password: "comic-test")
        try expect(unlocked.units.count == 3, "PDF opens with the correct password")
        _ = try await testEngine.image(at: 0)
        let epub = try await testEngine.open(fixtures.appendingPathComponent("Sample.epub"))
        try expect(epub.units.count == 5, "EPUB includes only five spine occurrences")
        try expect(epub.units[0].locator.resource == "OPS/pages/z.html" && epub.units[1].locator.resource == "OPS/pages/a.html", "EPUB spine beats filename order")
        try expect(epub.units[2].id != epub.units[3].id, "Repeated resource occurrences retain distinct stable IDs")
        try expect(epub.units[4].complex, "Multi-image text page uses original layout")
        try expect(epub.units[0].isCover && epub.units[0].rotationHint == 90, "Cover metadata remains separate from CSS rotation hint")
        let coverDecision = try await testEngine.analyze(at: 0)
        try expect(coverDecision.rotation == 0 && !coverDecision.standalone, "Cover is not auto-rotated by its class")
        let styledSpread = try await testEngine.analyze(at: 1)
        try expect(styledSpread.rotation == 90 && styledSpread.standalone, "Publication rotation style automatically restores the synthetic spread")
        try expect(epub.direction == .rtl && epub.navigation.first?.index == 1, "EPUB direction and NCX navigation parsed")
        let rotated = try await testEngine.image(at: 1, rotation: 90)
        try expect(rotated.width > rotated.height, "Manual 90 degree correction restores full landscape dimensions")
        let bookArchive = try BookArchive(url: fixtures.appendingPathComponent("Sample.epub"))
        try expect(!bookArchive.contains("OPS/images/absent.png"), "Missing resources remain distinguishable")
        let resolved = try BookArchive.resolve("../images/normal.png#fragment", relativeTo: "OPS/pages/a.html")
        try expect(resolved == "OPS/images/normal.png", "Relative resource resolution preserves publication boundaries")
        do { _ = try BookArchive.resolve("../../../outside", relativeTo: "OPS/pages/a.html"); throw ReaderError.message("FAIL: traversal accepted") }
        catch ReaderError.message(let text) where !text.hasPrefix("FAIL") { print("PASS: path traversal rejected") }
        try testLayout()
        try testAutomaticSpreads()
        try testPersistence(directory: root.appendingPathComponent(".build/test-library"), publication: epub)
        let styles = try await testEngine.open(fixtures.appendingPathComponent("Styles.epub"))
        let styleHints: [Int?] = [nil,0,0,0,90,nil,90,0,0,90,nil,nil,nil,nil]
        try expect(styles.units.count == styleHints.count && styles.navigation.first?.index == 6, "EPUB 3 synthetic spine and nav order are preserved")
        for (i,hint) in styleHints.enumerated() {
            try expect(styles.units[i].rotationHint == hint, "CSS selector, cascade and media fixture \(i+1)")
        }
        let sourceWide = Fixtures.image(width: 1600, height: 1000, wide: true)
        let a = sourceWide.cropping(to: CGRect(x: 0, y: 0, width: 800, height: 1000))!
        let b = sourceWide.cropping(to: CGRect(x: 800, y: 0, width: 800, height: 1000))!
        let seam = SpreadAnalyzer.seamScore(left: a, right: b)
        try expect((seam ?? 0) > 0.90, "Synthetic split image has continuous seam")
        let blank = Fixtures.image(width: 800, height: 1000, blank: true)
        try expect(SpreadAnalyzer.seamScore(left: blank, right: blank) == nil, "Blank edges cannot justify a pair")
        if let sampleIndex = args.firstIndex(of: "--sample"), args.indices.contains(sampleIndex+1) {
            let sampleURL = URL(fileURLWithPath: args[sampleIndex+1]); let engine = DocumentEngine()
            let start = Date(); let sample = try await engine.open(sampleURL)
            try expect(sample.units.count == 191, "User sample has 191 reading units")
            try expect(sample.units[1].imagePath == "image/moe-001540.jpg" && sample.units[2].imagePath == "image/moe-000772.jpg", "User sample begins in verified spine order")
            try expect(sample.units.last?.imagePath == "image/theendinfo.png", "User sample retains ending page")
            _ = try await engine.image(at: 0)
            print(String(format: "MEASURE: sample parse and first image %.3f seconds (single run, not P95)", Date().timeIntervalSince(start)))
            if args.contains("--analyze") {
                for i in [0, 34, 41, 44, 45, 46] {
                    let start = Date(); let decision = try await engine.analyze(at: i)
                    let report: [String: Any] = ["position":i+1, "rotation":decision.rotation, "standalone":decision.standalone, "uncertain":decision.uncertain, "reason":decision.reason, "scores":decision.orientationScores, "seconds":Date().timeIntervalSince(start)]
                    print("ANALYSIS: "+String(decoding: try JSONSerialization.data(withJSONObject: report, options: [.sortedKeys]), as: UTF8.self))
                    try expect(decision.rotation == (i == 0 ? 0 : 90), "Sample labeled orientation at position \(i+1)")
                }
            }
        }
        try catalogChecks()
        print("ALL CHECKS PASSED. See the separately labeled corpus report for real-book evidence.")
    }
    static func testLayout() throws {
        let units = (0..<7).map { ReadingUnit(locator: SourceLocator(resource: "page\($0)", occurrence: $0), title: "\($0)", isCover: $0 == 0) }
        var preferences = ReaderPreferences(); preferences.layout = .double; preferences.direction = .rtl
        let decisions = [units[3].id: SpreadDecision(standalone: true)]
        let basic = LayoutEngine.groups(units: units, preferences: preferences, overrides: [:], decisions: decisions)
        try expect(basic.map(\.indices) == [[0],[2,1],[3],[5,4],[6]], "Cover, RTL, standalone spread, and odd final page layout")
        let correction = [units[2].id: PageOverride(joinNext: true, swapPair: true)]
        let pairedRTL = LayoutEngine.groups(units: units, preferences: preferences, overrides: correction, decisions: [:])
        preferences.direction = .ltr
        let pairedLTR = LayoutEngine.groups(units: units, preferences: preferences, overrides: correction, decisions: [:])
        try expect(pairedRTL.first(where: { $0.indices.contains(2) })?.indices == [3,2] && pairedLTR.first(where: { $0.indices.contains(2) })?.indices == [3,2], "Confirmed physical pair is stable when direction changes")
        let flattened = pairedLTR.flatMap(\.indices).sorted()
        try expect(flattened == Array(0..<7), "Every source unit appears exactly once")
        let overlapping = [units[1].id: PageOverride(joinNext: true, swapPair: false), units[2].id: PageOverride(joinNext: true, swapPair: false)]
        let planned = LayoutEngine.groups(units: units, preferences: preferences, overrides: overlapping, decisions: [:])
        try expect(planned.flatMap(\.indices).sorted() == Array(0..<7), "Overlapping pair candidates cannot consume a page twice")
    }
    static func testPersistence(directory: URL, publication: Publication) throws {
        let store = try LibraryStore(directory: directory)
        let record = SavedBook(id: "test-book", path: publication.sourceURL.path, title: "Test", revision: "v1", locator: publication.units[1].locator, preferences: ReaderPreferences(), overrides: [publication.units[1].id: PageOverride(rotation: 90)], bookmarks: [publication.units[2].locator], openedAt: Date(), fileBookmark: nil)
        try store.save(record)
        let reopened = try LibraryStore(directory: directory)
        let saved = try reopened.books().first { $0.id == record.id }
        try expect(saved?.locator == record.locator && saved?.overrides[publication.units[1].id]?.rotation == 90 && saved?.bookmarks == record.bookmarks, "SQLite reopening preserves locator, rotation and bookmark")
    }
}

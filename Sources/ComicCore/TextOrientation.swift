import Foundation
import CoreGraphics
import Vision

public struct OrientationEvidence: Codable, Sendable {
    public var scores: [String: Double] = [:]
    public var regions: [String: Int] = [:]
    public var rotation: Int?
    public var suggestedRotation: Int?
    public var agreement: Double = 0
}

public enum TextOrientation {
    /// Vision revision 3 reports oriented text quadrilaterals, even when its recognizer
    /// internally reads rotated text. Compare their baselines, not four OCR confidence sums.
    public static func analyze(_ image: CGImage) throws -> OrientationEvidence {
        let request = VNRecognizeTextRequest()
        request.revision = VNRecognizeTextRequestRevision3
        request.recognitionLevel = .accurate
        request.usesLanguageCorrection = false
        let available = try request.supportedRecognitionLanguages()
        request.recognitionLanguages = ["zh-Hant", "en-US", "ja-JP"].filter { available.contains($0) }
        request.minimumTextHeight = 0
        try Task.checkCancellation()
        try VNImageRequestHandler(cgImage: image).perform([request])
        try Task.checkCancellation()
        var evidence = OrientationEvidence()
        var seenText = Set<String>()
        for observation in request.results ?? [] {
            guard let candidate = observation.topCandidates(1).first, candidate.confidence >= 0.3 else { continue }
            let letters = candidate.string.unicodeScalars.filter { CharacterSet.alphanumerics.contains($0) }.count
            guard letters >= 3 else { continue }
            let normalized = candidate.string.unicodeScalars.filter { CharacterSet.alphanumerics.contains($0) }.map(String.init).joined().uppercased()
            let cjk = candidate.string.unicodeScalars.filter { (0x3400...0x9fff).contains($0.value) || (0x3040...0x30ff).contains($0.value) }.count
            let digits = candidate.string.unicodeScalars.filter { CharacterSet.decimalDigits.contains($0) }.count
            // Short decorative Latin words, repeated across panels, must not orient the whole page.
            if cjk == 0 && digits == 0 && letters < 5 { continue }
            let rect = observation.boundingBox
            // Tiny peripheral marks provide poor evidence for the orientation of a whole page.
            let shortSide = min(rect.width * CGFloat(image.width), rect.height * CGFloat(image.height))
            if shortSide < CGFloat(max(image.width, image.height)) * 0.012,
               rect.midY < 0.07 || rect.midY > 0.93 || rect.midX < 0.04 || rect.midX > 0.96 { continue }
            let dx = (observation.topRight.x-observation.topLeft.x) * CGFloat(image.width)
            let dy = (observation.topRight.y-observation.topLeft.y) * CGFloat(image.height)
            let degrees = atan2(dy, dx) * 180 / .pi
            let quarter = Int((degrees / 90).rounded())
            guard abs(degrees - Double(quarter)*90) <= 12 else { continue }
            let rotation = ((quarter * 90) % 360 + 360) % 360
            // Top-to-bottom CJK can be ordinary vertical writing. Baseline geometry alone
            // cannot distinguish its upright glyphs from a clockwise-stored horizontal line.
            if rotation == 270 && Double(cjk)/Double(letters) >= 0.5 { continue }
            let key = String(rotation)
            guard seenText.insert(key+":"+normalized).inserted else { continue }
            evidence.scores[key, default: 0] += Double(min(letters, 20)) * Double(candidate.confidence)
            evidence.regions[key, default: 0] += 1
        }
        let ranked = evidence.scores.sorted { $0.value == $1.value ? $0.key < $1.key : $0.value > $1.value }
        if let best = ranked.first, let rotation = Int(best.key) {
            let total = evidence.scores.values.reduce(0,+)
            evidence.agreement = best.value / max(total, 0.001)
            if best.value >= 4.5, evidence.regions[best.key, default: 0] >= 2, evidence.agreement >= 0.88 {
                evidence.rotation = rotation
            } else if rotation != 0, best.value >= 3, evidence.agreement >= 0.75 {
                evidence.suggestedRotation = rotation
            }
        }
        return evidence
    }
}

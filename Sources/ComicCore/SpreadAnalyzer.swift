import Foundation
import Vision
import CoreGraphics

public enum SpreadAnalyzer {
    public static let algorithmVersion = "glyph-reflow-orientation-v4"
    public static func analyze(image: CGImage, unit: ReadingUnit) throws -> SpreadDecision {
        if unit.isCover { return SpreadDecision() }
        let ratio = Double(image.width) / Double(image.height)
        guard ratio >= 0.30, ratio <= 3.2 else { return SpreadDecision(standalone:ratio>=1.2) }
        let evidence: OrientationEvidence?
        do { evidence = try TextOrientation.analyze(image) }
        catch { try Task.checkCancellation(); evidence = nil }
        let scores = evidence?.scores ?? [:]
        // Baselines describe text flow, not CJK glyph orientation. Reflow enclosed
        // character grids when whole-page OCR has no reliable sideways answer.
        var glyph:GlyphOrientationEvidence?
        if evidence?.rotation == nil || evidence?.rotation == 0 {
            glyph=try? GlyphOrientation.analyze(image)
            try Task.checkCancellation()
        }
        let glyphRotation=glyph?.rotation
        if let rotation=glyphRotation,rotation != 0 {
            if let hint=unit.rotationHint,hint != 0,hint != rotation {
                return SpreadDecision(standalone:ratio>=1.2,reason:"字形与出版物朝向冲突 · 可手动旋转",uncertain:true,orientationScores:scores)
            }
            return SpreadDecision(rotation:rotation,standalone:true,reason:"局部字形方向 · 自动转正",orientationScores:scores)
        }
        if let rotation = evidence?.rotation {
            if let hint = unit.rotationHint, hint != 0, hint != rotation, rotation != 0 {
                // Conflicting publisher/visual evidence is reviewable, never forced.
                return SpreadDecision(standalone:ratio>=1.2,reason: "朝向线索有冲突 · 可手动旋转", uncertain: true, orientationScores: scores)
            }
            if rotation == 0, let hint = unit.rotationHint, hint == 90 || hint == 270 {
                // Horizontal CJK baselines may themselves be sideways vertical columns.
                // The document's explicit rotation remains the stronger source in this case.
                return SpreadDecision(rotation: hint, standalone: true, reason: "出版物旋转样式 · 完整展示", orientationScores: scores)
            }
            if rotation != 0 {
                return SpreadDecision(rotation: rotation, standalone: rotation % 180 == 90, reason: "文字朝向 · 自动转正", orientationScores: scores)
            }
            return SpreadDecision(standalone:ratio>=1.2,reason:ratio>=1.2 ? "横向页面 · 完整展示" : "",orientationScores: scores)
        }
        if let hint = unit.rotationHint, hint == 90 || hint == 270, 1 / ratio >= 1.2 {
            return SpreadDecision(rotation: hint, standalone: true, reason: "出版物旋转样式 · 完整展示", orientationScores: scores)
        }
        if evidence?.suggestedRotation == 90,glyphRotation != 0 {
            // Verify sparse sideways text in a second view. Ordinary vertical CJK that
            // was excluded in the original view becomes contradictory 180-degree
            // evidence here, preventing a short decorative line from turning a page.
            if let verified = try? TextOrientation.analyze(ImageTools.rotate(image,clockwise:90)) {
                let upright=verified.scores["0"] ?? 0
                let total=verified.scores.values.reduce(0,+)
                if upright>=3,upright/max(total,0.001)>=0.88 {
                    return SpreadDecision(rotation:90,standalone:true,reason:"文字朝向复核 · 自动转正",orientationScores:scores)
                }
            }
            try Task.checkCancellation()
        }
        if evidence?.suggestedRotation != nil {
            return SpreadDecision(standalone:ratio>=1.2,reason: "可能需要转正 · 可手动旋转", uncertain: true, orientationScores: scores)
        }
        return SpreadDecision(standalone:ratio>=1.2,reason:ratio>=1.2 ? "横向页面 · 完整展示" : "",orientationScores: scores)
    }

    // Prototype seam experiment, not enabled for unattended merging. Low-information borders are rejected.
    public static func seamScore(left: CGImage, right: CGImage) -> Double? {
        let rows = 128, strip = 8
        func pixels(_ image: CGImage, rightEdge: Bool) -> [UInt8]? {
            let x = rightEdge ? max(0, image.width - max(2, image.width / 32)) : 0
            guard let crop = image.cropping(to: CGRect(x: x, y: 0, width: max(2, image.width / 32), height: image.height)) else { return nil }
            var bytes = [UInt8](repeating: 0, count: rows * strip)
            let success = bytes.withUnsafeMutableBytes { pointer -> Bool in
                guard let context = CGContext(data: pointer.baseAddress, width: strip, height: rows, bitsPerComponent: 8, bytesPerRow: strip, space: CGColorSpaceCreateDeviceGray(), bitmapInfo: CGImageAlphaInfo.none.rawValue) else { return false }
                context.draw(crop, in: CGRect(x: 0, y: 0, width: strip, height: rows)); return true
            }
            return success ? bytes : nil
        }
        guard let a = pixels(left, rightEdge: true), let b = pixels(right, rightEdge: false) else { return nil }
        let edgeA = (0..<rows).map { Double(a[$0 * strip + strip - 1]) / 255 }
        let edgeB = (0..<rows).map { Double(b[$0 * strip]) / 255 }
        func variance(_ values: [Double]) -> Double {
            let mean = values.reduce(0, +) / Double(values.count)
            return values.reduce(0) { $0 + pow($1 - mean, 2) } / Double(values.count)
        }
        guard variance(edgeA) > 0.015, variance(edgeB) > 0.015 else { return nil }
        let error = zip(edgeA, edgeB).reduce(0.0) { $0 + abs($1.0 - $1.1) } / Double(rows)
        return max(0, 1 - error)
    }
}

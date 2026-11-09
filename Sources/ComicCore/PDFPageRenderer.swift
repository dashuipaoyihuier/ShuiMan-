import CoreGraphics
import PDFKit

/// Use the same effective box for indexing and rendering, including nonzero origins.
/// PDF artwork keeps its own whitespace; only the page's declared bounds are applied.
enum PDFPageRenderer {
    static func geometry(_ page: PDFPage) throws -> (reference: CGPDFPage, bounds: CGRect, size: CGSize) {
        guard let reference = page.pageRef else { throw ReaderError.message("PDF 页面内容不可用。") }
        let media = reference.getBoxRect(.mediaBox).standardized
        let crop = reference.getBoxRect(.cropBox).standardized
        let bounds = crop.intersection(media)
        guard !bounds.isNull, !bounds.isInfinite, bounds.width > 0, bounds.height > 0 else {
            throw ReaderError.message("PDF 页面尺寸无效。")
        }
        let sideways = abs(Int(reference.rotationAngle)) % 180 == 90
        return (reference, bounds, sideways ? CGSize(width: bounds.height, height: bounds.width) : bounds.size)
    }

    static func render(_ page: PDFPage, maxPixel: Int) throws -> CGImage {
        let geometry = try geometry(page)
        let edge = CGFloat(max(1, min(8192, maxPixel)))
        let scale = edge / max(geometry.size.width, geometry.size.height)
        let w = max(1, Int((geometry.size.width * scale).rounded()))
        let h = max(1, Int((geometry.size.height * scale).rounded()))
        guard let context = CGContext(data: nil, width: w, height: h, bitsPerComponent: 8, bytesPerRow: 0,
                                      space: CGColorSpaceCreateDeviceRGB(), bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue) else {
            throw ReaderError.message("无法分配页面画布。")
        }
        let target = CGRect(x: 0, y: 0, width: w, height: h)
        context.setFillColor(CGColor(gray: 1, alpha: 1)); context.fill(target)
        // First transform the actual effective crop rectangle and its intrinsic rotation.
        // The final scale covers the rounded pixel canvas exactly: no aspect-fit white strips.
        let rotation = CGAffineTransform(rotationAngle: -CGFloat(geometry.reference.rotationAngle) * .pi / 180)
        let rotated = geometry.bounds.applying(rotation)
        context.scaleBy(x: CGFloat(w) / rotated.width, y: CGFloat(h) / rotated.height)
        context.translateBy(x: -rotated.minX, y: -rotated.minY)
        context.concatenate(rotation)
        context.clip(to: geometry.bounds)
        context.drawPDFPage(geometry.reference)
        guard let image = context.makeImage() else { throw ReaderError.message("PDF 页面绘制失败。") }
        return image
    }
}

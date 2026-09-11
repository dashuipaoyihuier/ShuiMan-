import Foundation
import ImageIO
import CoreGraphics

public enum ImageTools {
    public static func dimensions(_ source: CGImageSource, index: Int = 0) -> (Double, Double) {
        let props = CGImageSourceCopyPropertiesAtIndex(source, index, nil) as? [CFString: Any]
        let w = (props?[kCGImagePropertyPixelWidth] as? NSNumber)?.doubleValue ?? 0
        let h = (props?[kCGImagePropertyPixelHeight] as? NSNumber)?.doubleValue ?? 0
        let orientation = (props?[kCGImagePropertyOrientation] as? NSNumber)?.intValue ?? 1
        return (5...8).contains(orientation) ? (h, w) : (w, h)
    }
    public static func decode(_ data: Data, index: Int = 0, maxPixel: Int = 2400) throws -> CGImage {
        guard let source = CGImageSourceCreateWithData(data as CFData, [kCGImageSourceShouldCache: false] as CFDictionary) else { throw ReaderError.message("无法解码图片。") }
        return try decode(source, index: index, maxPixel: maxPixel)
    }
    public static func decode(_ source: CGImageSource, index: Int = 0, maxPixel: Int = 2400) throws -> CGImage {
        let options: [CFString: Any] = [kCGImageSourceCreateThumbnailFromImageAlways: true, kCGImageSourceCreateThumbnailWithTransform: true, kCGImageSourceThumbnailMaxPixelSize: maxPixel, kCGImageSourceShouldCacheImmediately: true]
        guard let image = CGImageSourceCreateThumbnailAtIndex(source, index, options as CFDictionary) else { throw ReaderError.message("图片已损坏，或当前系统不支持此格式。") }
        return image
    }
    public static func rotate(_ image: CGImage, clockwise: Int) -> CGImage {
        let angle = ((clockwise % 360) + 360) % 360
        if angle == 0 { return image }
        let swap = angle == 90 || angle == 270
        let width = swap ? image.height : image.width; let height = swap ? image.width : image.height
        guard let context = CGContext(data: nil, width: width, height: height, bitsPerComponent: 8, bytesPerRow: 0, space: CGColorSpaceCreateDeviceRGB(), bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue) else { return image }
        context.translateBy(x: CGFloat(width)/2, y: CGFloat(height)/2)
        context.rotate(by: -CGFloat(angle) * .pi / 180)
        context.draw(image, in: CGRect(x: -CGFloat(image.width)/2, y: -CGFloat(image.height)/2, width: CGFloat(image.width), height: CGFloat(image.height)))
        return context.makeImage() ?? image
    }
}

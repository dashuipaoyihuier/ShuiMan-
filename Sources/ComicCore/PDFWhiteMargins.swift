import CoreGraphics
import Foundation

enum PDFWhiteMargins {
    static func cropped(_ image: CGImage) -> CGImage {
        let width = image.width, height = image.height, stride = width * 4
        // Examine every pixel, so a small caption or thin border still protects its margin.
        var pixels = [UInt8](repeating: 255, count: stride * height)
        let bounds: CGRect? = pixels.withUnsafeMutableBytes { bytes in
            guard let context = CGContext(data: bytes.baseAddress, width: width, height: height,
                                          bitsPerComponent: 8, bytesPerRow: stride,
                                          space: CGColorSpaceCreateDeviceRGB(),
                                          bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue | CGBitmapInfo.byteOrder32Big.rawValue) else { return nil }
            context.draw(image, in: CGRect(x: 0, y: 0, width: width, height: height))
            let data = bytes.bindMemory(to: UInt8.self)
            var left = width, right = -1, top = height, bottom = -1
            for y in 0..<height {
                for x in 0..<width {
                    let i = y * stride + x * 4
                    if data[i] < 250 || data[i+1] < 250 || data[i+2] < 250 {
                        left = min(left, x); right = max(right, x)
                        top = min(top, y); bottom = max(bottom, y)
                    }
                }
            }
            // Blank pages and tiny isolated marks must not become giant zoomed dots.
            guard right >= left, bottom >= top,
                  Double((right-left+1) * (bottom-top+1)) >= Double(width * height) * 0.03 else { return nil }
            let margin = max(2, Int(ceil(Double(max(width, height)) * 0.005)))
            left = max(0, left-margin); right = min(width-1, right+margin)
            top = max(0, top-margin); bottom = min(height-1, bottom+margin)
            return CGRect(x: left, y: top, width: right-left+1, height: bottom-top+1)
        }
        guard let bounds, bounds.width < CGFloat(width) || bounds.height < CGFloat(height) else { return image }
        return image.cropping(to: bounds) ?? image
    }
}

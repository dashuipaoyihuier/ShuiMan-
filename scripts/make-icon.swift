import AppKit

// Original geometric icon, drawn from paths; no external artwork.
let folder = URL(fileURLWithPath: FileManager.default.currentDirectoryPath).appendingPathComponent(".build/AppIcon.iconset")
try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
for size in [16, 32, 128, 256, 512] {
    for scale in [1, 2] {
        let pixels = size * scale
        let bitmap = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: pixels, pixelsHigh: pixels, bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0)!
        let graphics = NSGraphicsContext(bitmapImageRep: bitmap)!
        NSGraphicsContext.saveGraphicsState(); NSGraphicsContext.current = graphics
        graphics.cgContext.scaleBy(x: CGFloat(pixels)/1024, y: CGFloat(pixels)/1024)
        NSColor(calibratedRed: 0.06, green: 0.43, blue: 0.51, alpha: 1).setFill()
        NSBezierPath(roundedRect: NSRect(x: 62, y: 62, width: 900, height: 900), xRadius: 200, yRadius: 200).fill()
        NSColor(calibratedRed: 0.95, green: 0.98, blue: 0.97, alpha: 1).setFill()
        let left = NSBezierPath()
        left.move(to: NSPoint(x: 220, y: 310)); left.line(to: NSPoint(x: 220, y: 712))
        left.curve(to: NSPoint(x: 490, y: 674), controlPoint1: NSPoint(x: 310, y: 748), controlPoint2: NSPoint(x: 408, y: 714))
        left.line(to: NSPoint(x: 490, y: 260)); left.curve(to: NSPoint(x: 220, y: 310), controlPoint1: NSPoint(x: 400, y: 308), controlPoint2: NSPoint(x: 305, y: 340)); left.close(); left.fill()
        let right = NSBezierPath()
        right.move(to: NSPoint(x: 534, y: 260)); right.line(to: NSPoint(x: 534, y: 674))
        right.curve(to: NSPoint(x: 804, y: 712), controlPoint1: NSPoint(x: 616, y: 714), controlPoint2: NSPoint(x: 714, y: 748))
        right.line(to: NSPoint(x: 804, y: 310)); right.curve(to: NSPoint(x: 534, y: 260), controlPoint1: NSPoint(x: 710, y: 340), controlPoint2: NSPoint(x: 628, y: 308)); right.close(); right.fill()
        NSColor(calibratedRed: 0.06, green: 0.43, blue: 0.51, alpha: 1).setStroke()
        let panels = NSBezierPath(); panels.lineWidth = 19; panels.lineCapStyle = .round
        for y in [430, 530, 630] {
            panels.move(to: NSPoint(x: 279, y: y)); panels.line(to: NSPoint(x: 431, y: y-20))
        }
        panels.stroke()
        NSColor(calibratedRed: 0.52, green: 0.87, blue: 0.85, alpha: 1).setStroke()
        let wave = NSBezierPath(); wave.lineWidth = 23; wave.lineCapStyle = .round
        wave.move(to: NSPoint(x: 232, y: 212))
        wave.curve(to: NSPoint(x: 512, y: 207), controlPoint1: NSPoint(x: 332, y: 262), controlPoint2: NSPoint(x: 404, y: 153))
        wave.curve(to: NSPoint(x: 792, y: 212), controlPoint1: NSPoint(x: 620, y: 262), controlPoint2: NSPoint(x: 684, y: 153))
        wave.stroke()
        NSGraphicsContext.restoreGraphicsState()
        let name = "icon_\(size)x\(size)\(scale == 2 ? "@2x" : "").png"
        try bitmap.representation(using: .png, properties: [:])!.write(to: folder.appendingPathComponent(name))
    }
}
print(folder.path)

import SwiftUI
import AppKit
import ComicCore

struct ReaderCanvas: NSViewRepresentable {
    let pages: [CanvasPage]
    let spread: Bool
    let verticalOffset: Double
    let rightScale: Double
    let zoom: Double
    let fitWidth: Bool
    let reverseSwipe: Bool
    let edgeToEdge: Bool
    let onStep: (Int) -> Void
    let onZoom: (Double) -> Void
    func makeNSView(context: Context) -> ComicScrollView {
        let scroll = ComicScrollView()
        scroll.drawsBackground = true; scroll.backgroundColor = NSColor(calibratedWhite: 0.075, alpha: 1)
        scroll.hasVerticalScroller = true; scroll.hasHorizontalScroller = true
        scroll.autohidesScrollers = true; scroll.scrollerStyle = .overlay
        scroll.documentView = ComicDrawingView()
        return scroll
    }
    func updateNSView(_ scroll: ComicScrollView, context: Context) {
        scroll.onStep = onStep; scroll.onZoom = onZoom; scroll.reverseSwipe = reverseSwipe
        scroll.edgeToEdge=edgeToEdge
        scroll.configure(pages: pages, spread: spread, verticalOffset: verticalOffset, rightScale:rightScale, zoom: zoom, fitWidth: fitWidth)
    }
}

final class ComicScrollView: NSScrollView {
    var onStep: ((Int) -> Void)?
    var onZoom: ((Double) -> Void)?
    var reverseSwipe = false
    var edgeToEdge = false
    private var pages: [CanvasPage] = []
    private var spread = false
    private var verticalOffset = 0.0
    private var rightScale = 1.0
    private var zoom = 1.0
    private var fitWidth = false
    private var identity = ""
    private var lastGestureTime = Date.distantPast
    override var acceptsFirstResponder: Bool { true }
    func configure(pages: [CanvasPage], spread: Bool, verticalOffset: Double, rightScale:Double, zoom: Double, fitWidth: Bool) {
        let nextID = pages.map { "\($0.id):\($0.image.map { ObjectIdentifier($0).hashValue } ?? 0)" }.joined(separator: ",")
        let changed = nextID != identity
        self.pages = pages; self.spread = spread; self.zoom = zoom; self.fitWidth = fitWidth; identity = nextID
        self.verticalOffset = verticalOffset; self.rightScale = rightScale
        arrange()
        if changed { contentView.scroll(to: .zero); reflectScrolledClipView(contentView) }
    }
    override func layout() { super.layout(); arrange() }
    private func arrange() {
        guard let drawing = documentView as? ComicDrawingView else { return }
        let viewport = contentView.bounds.size
        guard viewport.width > 0, viewport.height > 0 else { return }
        let ratios = pages.map { Double($0.image?.width ?? 900) / Double($0.image?.height ?? 1300) }
        let layout = PageCanvasLayout(viewport:viewport,ratios:ratios,spread:spread,verticalOffset:verticalOffset,zoom:zoom,fitWidth:fitWidth,rightScale:rightScale,edgeInset:edgeToEdge ? 0 : 24)
        if drawing.frame.size != layout.size { drawing.setFrameSize(layout.size) }
        drawing.pages = pages; drawing.pageFrames = layout.frames; drawing.needsDisplay = true
    }
    override func magnify(with event: NSEvent) { onZoom?(min(6, max(0.5, zoom*(1+event.magnification)))) }
    override func mouseDown(with event: NSEvent) {
        window?.makeFirstResponder(self)
        if event.clickCount == 2 { onZoom?(zoom > 1.1 ? 1 : 2) }
        else { super.mouseDown(with: event) }
    }
    override func scrollWheel(with event: NSEvent) {
        if zoom <= 1.01, !fitWidth, abs(event.scrollingDeltaX) > abs(event.scrollingDeltaY)*2,
           abs(event.scrollingDeltaX) > 12, event.momentumPhase.isEmpty, Date().timeIntervalSince(lastGestureTime) > 0.6 {
            lastGestureTime = Date(); onStep?((event.scrollingDeltaX < 0 ? 1 : -1) * (reverseSwipe ? -1 : 1))
        } else { super.scrollWheel(with: event) }
    }
    override func keyDown(with event: NSEvent) {
        switch event.keyCode {
        case 49: onStep?(event.modifierFlags.contains(.shift) ? -1 : 1)
        case 121: onStep?(1)
        case 116: onStep?(-1)
        default: super.keyDown(with: event)
        }
    }
}

final class ComicDrawingView: NSView {
    var pages: [CanvasPage] = []
    var pageFrames: [CGRect] = []
    private var dragOrigin: NSPoint?
    private var scrollOrigin: NSPoint?
    override var isFlipped: Bool { true }
    override func draw(_ dirtyRect: NSRect) {
        guard let context = NSGraphicsContext.current?.cgContext else { return }
        for (i, page) in pages.enumerated() where pageFrames.indices.contains(i) {
            let rect = pageFrames[i]
            NSColor.white.setFill(); rect.fill()
            if let image = page.image {
                context.saveGState(); context.translateBy(x: rect.minX, y: rect.maxY); context.scaleBy(x: 1, y: -1)
                context.interpolationQuality = .high
                context.draw(image, in: CGRect(origin: .zero, size: rect.size)); context.restoreGState()
            } else {
                let text = "第 \(page.id+1) 页\n\n\(page.error ?? "无法显示")"
                (text as NSString).draw(in: rect.insetBy(dx: 24, dy: 40), withAttributes: [.font: NSFont.systemFont(ofSize: 15), .foregroundColor: NSColor.darkGray])
            }
        }
    }
    override func mouseDown(with event: NSEvent) {
        guard let scroll = enclosingScrollView as? ComicScrollView else { return }
        window?.makeFirstResponder(scroll)
        if event.clickCount == 2 { scroll.mouseDown(with: event); return }
        dragOrigin = event.locationInWindow; scrollOrigin = scroll.contentView.bounds.origin
    }
    override func mouseDragged(with event: NSEvent) {
        guard let initial = dragOrigin, let start = scrollOrigin, let scroll = enclosingScrollView else { return }
        let dx = event.locationInWindow.x - initial.x, dy = event.locationInWindow.y - initial.y
        let maxX = max(0, bounds.width-scroll.contentView.bounds.width), maxY = max(0, bounds.height-scroll.contentView.bounds.height)
        scroll.contentView.scroll(to: NSPoint(x: min(maxX, max(0, start.x-dx)), y: min(maxY, max(0, start.y+dy))))
        scroll.reflectScrolledClipView(scroll.contentView)
    }
}

import Foundation
import CoreGraphics

/// Rectangles in the canvas's top-left coordinate system. Preserve both complete images,
/// including the small protruding strip introduced by seam alignment.
public struct PageCanvasLayout {
    public let size: CGSize
    public let frames: [CGRect]
    public init(viewport: CGSize, ratios: [Double], spread: Bool, verticalOffset: Double, zoom: Double, fitWidth: Bool, rightScale:Double = 1, edgeInset:Double = 24) {
        let gap = spread ? 0.0 : 14.0
        let paired=spread && ratios.count == 2
        let offset = paired ? max(-0.15, min(0.15, verticalOffset)) : 0
        let scale = paired ? max(0.85,min(1.18,rightScale)) : 1
        let fullHeight=max(1,offset+scale)-min(0,offset)
        let margin = max(0,edgeInset)
        let gaps = gap * Double(max(0, ratios.count-1))
        let totalRatio = ratios.enumerated().reduce(0.0) { $0+$1.element*($1.offset == 1 ? scale : 1) }
        let heightByWidth = max(1, viewport.width-margin*2-gaps)/max(totalRatio,0.1)
        let height = (fitWidth ? heightByWidth : min(heightByWidth,max(1,viewport.height-margin*2)/fullHeight))*zoom
        let width = height*totalRatio+gaps, totalHeight = height*fullHeight
        size = CGSize(width:max(viewport.width,width+margin*2),height:max(viewport.height,totalHeight+margin*2))
        var x = (size.width-width)/2
        let y = (size.height-totalHeight)/2
        frames = ratios.enumerated().map { i,ratio in
            let pageHeight=height*(i == 1 ? scale : 1)
            defer { x += pageHeight*ratio+gap }
            return CGRect(x:x,y:y+(i == 0 ? max(0,-offset) : max(0,offset))*height,width:pageHeight*ratio,height:pageHeight)
        }
    }
}

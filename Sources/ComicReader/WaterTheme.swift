import SwiftUI
import AppKit

/// Water-inspired chrome. Comic artwork keeps its original colors.
enum WaterTheme {
    static let accent = Color(nsColor: NSColor(name: nil) { appearance in
        appearance.bestMatch(from: [.darkAqua, .aqua]) == .darkAqua
            ? NSColor(srgbRed: 0.38, green: 0.80, blue: 0.83, alpha: 1)
            : NSColor(srgbRed: 0.06, green: 0.43, blue: 0.51, alpha: 1)
    })
    static let surface = Color(nsColor: NSColor(name: nil) { appearance in
        appearance.bestMatch(from: [.darkAqua, .aqua]) == .darkAqua
            ? NSColor(srgbRed: 0.07, green: 0.12, blue: 0.15, alpha: 1)
            : NSColor(srgbRed: 0.94, green: 0.97, blue: 0.96, alpha: 1)
    })
}

struct WaterCover: View {
    private static let artwork: NSImage? = Bundle.main.url(forResource: "WaterCover", withExtension: "png").flatMap { NSImage(contentsOf: $0) }
    var body: some View {
        ZStack(alignment: .leading) {
            Color(red: 0.063, green: 0.176, blue: 0.227)
            if let artwork = Self.artwork {
                GeometryReader { geometry in
                    Image(nsImage: artwork).resizable().scaledToFill()
                        .frame(width: min(geometry.size.width * 0.65, 560), height: geometry.size.height, alignment: .bottom).clipped()
                        .frame(maxWidth: .infinity, alignment: .trailing)
                }.accessibilityHidden(true)
            }
            LinearGradient(colors: [Color(red:0.04,green:0.13,blue:0.18).opacity(0.95), .clear], startPoint: .leading, endPoint: .trailing)
            VStack(alignment: .leading, spacing: 10) {
                Text("水漫").font(.system(size: 34, weight: .semibold, design: .rounded))
                Text("翻开一页，漫入故事。") .font(.system(size: 15))
            }.foregroundStyle(.white).padding(28)
        }.frame(height: 170).clipShape(RoundedRectangle(cornerRadius: 18))
    }
}

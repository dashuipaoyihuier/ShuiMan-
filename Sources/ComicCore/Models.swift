import Foundation
import CoreGraphics

public enum ReaderError: LocalizedError {
    case message(String)
    case passwordRequired
    public var errorDescription: String? {
        switch self { case .message(let text): return text; case .passwordRequired: return "这份 PDF 需要密码。" }
    }
}

public enum PublicationKind: String, Codable, Sendable { case images, pdf, epub, mobi }
public enum ReadingDirection: String, Codable, CaseIterable, Sendable { case ltr, rtl }
public enum ReadingLayout: String, Codable, CaseIterable, Sendable { case single, double }

public struct SourceLocator: Codable, Hashable, Sendable {
    public var resource: String
    public var occurrence: Int
    public var imageIndex: Int
    public init(resource: String, occurrence: Int = 0, imageIndex: Int = 0) {
        self.resource = resource; self.occurrence = occurrence; self.imageIndex = imageIndex
    }
    public var key: String { "\(occurrence):\(imageIndex):\(resource)" }
}

public struct ReadingUnit: Identifiable, Sendable {
    public var id: String { locator.key }
    public var locator: SourceLocator
    public var title: String
    public var imagePath: String?
    public var width: Double
    public var height: Double
    public var isCover: Bool
    public var rotationHint: Int?
    public var complex: Bool
    public var error: String?
    public init(locator: SourceLocator, title: String, imagePath: String? = nil, width: Double = 0, height: Double = 0, isCover: Bool = false, rotationHint: Int? = nil, complex: Bool = false, error: String? = nil) {
        self.locator = locator; self.title = title; self.imagePath = imagePath
        self.width = width; self.height = height; self.isCover = isCover
        self.rotationHint = rotationHint; self.complex = complex; self.error = error
    }
}

public struct NavigationItem: Identifiable, Sendable {
    public var id: String { "\(index):\(title)" }
    public var title: String
    public var index: Int
}

public struct Publication: Sendable {
    public var sourceURL: URL
    public var identity: String
    public var revision: String
    public var title: String
    public var kind: PublicationKind
    public var units: [ReadingUnit]
    public var direction: ReadingDirection?
    public var navigation: [NavigationItem]
    public var warnings: [String]
}

public struct PageOverride: Codable, Sendable {
    public var rotation: Int?
    public var standalone: Bool?
    public var joinNext: Bool?
    public var swapPair: Bool?
    public var pairOffset: Double?
    public var pairScale: Double?
    public init(rotation: Int? = nil, standalone: Bool? = nil, joinNext: Bool? = nil, swapPair: Bool? = nil, pairOffset: Double? = nil, pairScale:Double? = nil) {
        self.rotation = rotation; self.standalone = standalone; self.joinNext = joinNext; self.swapPair = swapPair
        self.pairOffset = pairOffset
        self.pairScale = pairScale
    }
}

public struct SpreadDecision: Codable, Sendable {
    public var rotation: Int
    public var standalone: Bool
    public var reason: String
    public var uncertain: Bool
    public var orientationScores: [String: Double]
    public init(rotation: Int = 0, standalone: Bool = false, reason: String = "", uncertain: Bool = false, orientationScores: [String: Double] = [:]) {
        self.rotation = rotation; self.standalone = standalone; self.reason = reason; self.uncertain = uncertain
        self.orientationScores = orientationScores
    }
}

public struct DisplayGroup: Equatable, Sendable {
    public var indices: [Int] // Physical left-to-right order; never mutate source order.
    public var spread: Bool
    public var verticalOffset: Double = 0 // Right image down relative to left, as a fraction of fitted height.
    public var rightScale: Double = 1
    public var firstSourceIndex: Int { indices.min() ?? 0 }
}

public struct ReaderPreferences: Codable, Sendable {
    public var direction: ReadingDirection = .ltr
    public var layout: ReadingLayout = .single
    public var smartSpreads: Bool = true
    public var coverAlone: Bool = true
    public var automaticPairs: Bool = true
    public var aggressivePairs: Bool = true
    public var automaticOrientation: Bool = true
    public init() {}
    private enum CodingKeys: String, CodingKey { case direction, layout, smartSpreads, coverAlone, automaticPairs, aggressivePairs, automaticOrientation }
    public init(from decoder: Decoder) throws {
        let values=try decoder.container(keyedBy:CodingKeys.self)
        direction=try values.decodeIfPresent(ReadingDirection.self,forKey:.direction) ?? .ltr
        layout=try values.decodeIfPresent(ReadingLayout.self,forKey:.layout) ?? .single
        smartSpreads=try values.decodeIfPresent(Bool.self,forKey:.smartSpreads) ?? true
        coverAlone=try values.decodeIfPresent(Bool.self,forKey:.coverAlone) ?? true
        automaticPairs=try values.decodeIfPresent(Bool.self,forKey:.automaticPairs) ?? true
        aggressivePairs=try values.decodeIfPresent(Bool.self,forKey:.aggressivePairs) ?? true
        automaticOrientation=try values.decodeIfPresent(Bool.self,forKey:.automaticOrientation) ?? true
    }
}

public struct SavedBook: Codable, Identifiable, Sendable {
    public var id: String
    public var path: String
    public var title: String
    public var revision: String
    public var locator: SourceLocator?
    public var preferences: ReaderPreferences
    public var overrides: [String: PageOverride]
    public var bookmarks: [SourceLocator]
    public var openedAt: Date
    public var fileBookmark: Data?
    public init(id: String, path: String, title: String, revision: String, locator: SourceLocator?, preferences: ReaderPreferences, overrides: [String: PageOverride], bookmarks: [SourceLocator], openedAt: Date, fileBookmark: Data?) {
        self.id = id; self.path = path; self.title = title; self.revision = revision; self.locator = locator
        self.preferences = preferences; self.overrides = overrides; self.bookmarks = bookmarks
        self.openedAt = openedAt; self.fileBookmark = fileBookmark
    }
}

public enum LayoutEngine {
    public static func groups(units: [ReadingUnit], preferences: ReaderPreferences, overrides: [String: PageOverride], decisions: [String: SpreadDecision], pairs: [Int: PairDecision] = [:]) -> [DisplayGroup] {
        func alone(_ i: Int) -> Bool {
            let unit = units[i]
            if unit.complex || unit.error != nil { return true }
            if let forced = overrides[unit.id]?.standalone { return forced }
            if preferences.coverAlone && unit.isCover { return true }
            return (preferences.automaticOrientation && (decisions[unit.id]?.rotation ?? 0) != 0) || (preferences.smartSpreads && decisions[unit.id]?.standalone == true)
        }
        var automatic: [Int:PairDecision] = [:]
        if preferences.smartSpreads && preferences.automaticPairs {
            for (i,pair) in pairs where (pair.automatic || (preferences.aggressivePairs && pair.suggested)) && i >= 0 && i+1 < units.count {
                guard !alone(i),!alone(i+1),!units[i].isCover,!units[i+1].isCover,
                      overrides[units[i].id]?.joinNext == nil,
                      overrides[units[i+1].id]?.joinNext != true,
                      decisions[units[i].id]?.rotation == 0, decisions[units[i+1].id]?.rotation == 0,
                      decisions[units[i].id]?.uncertain == false, decisions[units[i+1].id]?.uncertain == false,
                      overrides[units[i].id]?.rotation == nil,overrides[units[i+1].id]?.rotation == nil else { continue }
                let rival=max(pairs[i-1]?.score ?? 0,pairs[i+1]?.score ?? 0)
                if pair.score-rival >= (preferences.aggressivePairs ? 0.04 : 0.08) { automatic[i]=pair }
            }
        }
        var result: [DisplayGroup] = []; var i = 0
        while i < units.count {
            let correction = overrides[units[i].id]
            if correction?.joinNext == true, i + 1 < units.count, !units[i].complex, !units[i+1].complex {
                let earlierOnRight = correction?.swapPair ?? (preferences.direction == .rtl)
                result.append(DisplayGroup(indices: earlierOnRight ? [i+1, i] : [i, i+1], spread: true, verticalOffset: correction?.pairOffset ?? 0, rightScale:correction?.pairScale ?? 1)); i += 2
            } else if let pair=automatic[i] {
                result.append(DisplayGroup(indices:pair.swapped ? [i+1,i]:[i,i+1],spread:true,verticalOffset:pair.verticalOffset,rightScale:pair.rightScale ?? 1)); i+=2
            } else if alone(i) || preferences.layout == .single || i + 1 == units.count || alone(i+1) || overrides[units[i+1].id]?.joinNext == true || automatic[i+1] != nil {
                result.append(DisplayGroup(indices: [i], spread: alone(i))); i += 1
            } else {
                result.append(DisplayGroup(indices: preferences.direction == .rtl ? [i+1, i] : [i, i+1], spread: false)); i += 2
            }
        }
        return result
    }
}

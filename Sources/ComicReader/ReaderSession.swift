import SwiftUI
import AppKit
import ComicCore

struct CanvasPage: Identifiable {
    let id: Int
    let image: CGImage?
    let error: String?
}

@MainActor
final class ReaderSession: ObservableObject {
    static let shared = ReaderSession()
    let libraryModel = LibraryModel()
    @Published var tab: LibraryTab = .browse
    weak var readingWindow: NSWindow?
    func toggleFullScreen() {
        guard let window=readingWindow ?? NSApp.keyWindow else {return}
        window.makeKeyAndOrderFront(nil)
        window.toggleFullScreen(nil)
    }
    @Published var readingControlsVisible = true
    var isReading: Bool { tab == .reading && publication != nil }
    func toggleReadingControls() {
        guard isReading else {return}
        readingControlsVisible.toggle()
    }
    @Published var publication: Publication?
    @Published var engine: DocumentEngine?
    @Published var recent: [SavedBook] = []
    @Published var preferences = ReaderPreferences()
    @Published var overrides: [String: PageOverride] = [:]
    @Published var decisions: [String: SpreadDecision] = [:]
    @Published var pairDecisions: [Int: PairDecision] = [:]
    @Published var bookmarks: [SourceLocator] = []
    @Published var index = 0
    @Published var pages: [CanvasPage] = []
    @Published var busy = false
    @Published var rendering = false
    @Published var errorMessage: String?
    @Published var status = ""
    @Published var showSidebar = true
    @Published var originalLayout = false
    @Published var zoom = 1.0
    @Published var fitWidth = false
    @Published var passwordURL: URL?
    @Published var jumpText = ""
    private var store: LibraryStore?
    private var opening: Task<Void, Never>?
    private var loading: Task<Void, Never>?
    private var analyzing: Task<Void, Never>?
    private var requestID = UUID()
    private var renderID = UUID()
    private var scopeURL: URL?
    private var fileBookmark: Data?
    private var inheritedPreferences: ReaderPreferences?
    private var groups: [DisplayGroup] = []
    private var deferredDecisions: [String: SpreadDecision] = [:]
    private var deferredPairs: [Int: PairDecision] = [:]

    var group: DisplayGroup? { groups.first { $0.indices.contains(index) } }
    var unit: ReadingUnit? { publication?.units.indices.contains(index) == true ? publication?.units[index] : nil }
    var showWeb: Bool { publication?.kind == .epub && (originalLayout || unit?.complex == true) }
    var currentBookmarked: Bool { unit.map { bookmarks.contains($0.locator) } ?? false }
    var pageLabel: String {
        guard let p = publication, let group else { return "" }
        let ordered = group.indices.sorted().map { String($0+1) }.joined(separator: "–")
        return "\(ordered) / \(p.units.count)"
    }
    var spreadMessage: String {
        if let group, group.indices.count == 2 && group.spread, let p = publication {
            return overrides[p.units[group.firstSourceIndex].id]?.joinNext == true ? "完整双图 · 已确认配对" : "完整双图 · 自动识别"
        }
        guard let unit else { return "" }
        if overrides[unit.id]?.rotation != nil { return "已应用页面修正" }
        return preferences.smartSpreads || (preferences.automaticOrientation && (decisions[unit.id]?.rotation ?? 0) != 0) ? (decisions[unit.id]?.reason ?? "") : ""
    }
    init() {
        do { store = try LibraryStore(); recent = try store?.books() ?? [] }
        catch { errorMessage = "阅读记录不可用：\(error.localizedDescription)" }
    }
    func chooseFile() {
        let panel = NSOpenPanel()
        panel.canChooseDirectories = true; panel.canChooseFiles = true; panel.allowsMultipleSelection = false
        panel.message = "选择图片、图片文件夹、PDF、EPUB 或 MOBI"
        if panel.runModal() == .OK, let url = panel.url { open(url) }
    }
    func open(_ url: URL, password: String? = nil) {
        tab = .reading
        readingControlsVisible=true
        inheritedPreferences=nil
        save(); opening?.cancel(); loading?.cancel(); analyzing?.cancel()
        let token = UUID(); requestID = token; busy = true; status = "正在打开 \(url.lastPathComponent)…"
        let scoped = url.startAccessingSecurityScopedResource()
        let newEngine = DocumentEngine()
        opening = Task {
            do {
                let result = try await newEngine.open(url, password: password)
                guard !Task.isCancelled, token == requestID else { if scoped { url.stopAccessingSecurityScopedResource() }; return }
                scopeURL?.stopAccessingSecurityScopedResource(); scopeURL = scoped ? url : nil
                fileBookmark = try? url.bookmarkData(options: [.withSecurityScope, .securityScopeAllowOnlyReadAccess], includingResourceValuesForKeys: nil, relativeTo: nil)
                publication = result; engine = newEngine; pages = []; decisions = [:]; deferredDecisions = [:]
                pairDecisions = [:]; deferredPairs = [:]
                preferences = inheritedPreferences ?? ReaderPreferences(); if inheritedPreferences == nil { preferences.direction = result.direction ?? .ltr }; inheritedPreferences=nil
                overrides = [:]; bookmarks = []; index = 0; originalLayout = false
                if let saved = recent.first(where: { $0.id == result.identity }) {
                    preferences = saved.preferences
                    bookmarks = saved.bookmarks.filter { locator in result.units.contains { $0.locator == locator } }
                    if saved.revision == result.revision { overrides = saved.overrides }
                    if let locator = saved.locator, let restored = result.units.firstIndex(where: { $0.locator == locator }) { index = restored }
                    if saved.revision != result.revision { status = "文件内容已变化；已保留可匹配的位置。" }
                }
                busy = false; status = result.warnings.first ?? ""
                rebuild(); render(); save()
            } catch {
                if scoped { url.stopAccessingSecurityScopedResource() }
                guard token == requestID, !Task.isCancelled else { return }
                busy = false; status = ""
                if case ReaderError.passwordRequired = error { passwordURL = url }
                else { errorMessage = error.localizedDescription }
            }
        }
    }
    func openRecent(_ book: SavedBook) {
        if let data = book.fileBookmark {
            var stale = false
            if let url = try? URL(resolvingBookmarkData: data, options: [.withSecurityScope], relativeTo: nil, bookmarkDataIsStale: &stale) { open(url); return }
        }
        guard FileManager.default.fileExists(atPath: book.path) else { errorMessage = "找不到原文件。请使用“打开”重新选择它。"; return }
        open(URL(fileURLWithPath: book.path))
    }
    func home() {
        tab = .browse
        save(); opening?.cancel(); loading?.cancel(); analyzing?.cancel(); requestID = UUID(); renderID = UUID()
        scopeURL?.stopAccessingSecurityScopedResource(); scopeURL = nil
        publication = nil; engine = nil; pages = []; busy = false; rendering = false; status = ""
        recent = (try? store?.books()) ?? []
    }
    private func rebuild() {
        guard let p = publication else { return }
        groups = LayoutEngine.groups(units: p.units, preferences: preferences, overrides: overrides, decisions: decisions, pairs: pairDecisions)
    }
    func changedPreferences() { rebuild(); render(); save() }
    var nextVolume: LibraryVolume? {
        guard let p=publication,(group?.indices.max() ?? index)>=p.units.count-1 else { return nil }
        return libraryModel.catalog.next(after:p.sourceURL.standardizedFileURL.path)
    }
    func openNextVolume(_ volume: LibraryVolume) {
        let inherited=preferences
        openVolume(volume)
        inheritedPreferences=inherited
    }
    func openVolume(_ volume: LibraryVolume) {
        if let saved=recent.first(where:{$0.path==volume.path}) { openRecent(saved) }
        else { open(URL(fileURLWithPath:volume.path)) }
    }
    func step(_ delta: Int) {
        guard let position = groups.firstIndex(where: { $0.indices.contains(index) }), groups.indices.contains(position + delta) else { return }
        go(to: groups[position + delta].firstSourceIndex)
    }
    func physicalArrow(left: Bool) {
        guard tab == .reading else { return }
        if NSApp.keyWindow?.firstResponder is NSTextView { return }
        step((left == (preferences.direction == .rtl)) ? 1 : -1)
    }
    func go(to target: Int) {
        guard let p = publication, p.units.indices.contains(target) else { return }
        for (key, value) in deferredDecisions { decisions[key] = value }; deferredDecisions = [:]
        for (key, value) in deferredPairs { pairDecisions[key] = value }; deferredPairs = [:]
        index = target; status = ""; rebuild(); render(); save()
    }
    func jump() {
        guard let page = Int(jumpText), let p = publication, (1...p.units.count).contains(page) else { return }
        NSApp.keyWindow?.makeFirstResponder(nil); go(to: page - 1); jumpText = ""
    }
    func rotate(_ angle: Int) {
        guard let unit else { return }
        let current = rotation(for: index)
        cancelCurrentPair()
        var value = overrides[unit.id] ?? PageOverride()
        value.rotation = ((current + angle) % 360 + 360) % 360
        value.standalone = true; overrides[unit.id] = value
        rebuild(); render(); save()
    }
    func restorePage() {
        guard let unit else { return }
        if group?.spread == true, group?.indices.count == 2, let first = group?.firstSourceIndex, let p = publication {
            overrides.removeValue(forKey: p.units[first].id)
        }
        overrides.removeValue(forKey: unit.id); rebuild(); render(); save()
    }
    private func cancelCurrentPair() {
        guard let group, group.indices.count == 2, group.spread, let p = publication else { return }
        let key = p.units[group.firstSourceIndex].id
        var correction = overrides[key] ?? PageOverride()
        correction.joinNext = false; correction.swapPair = nil; correction.pairOffset = nil; correction.pairScale = nil
        overrides[key] = correction
    }
    func cancelPair() { cancelCurrentPair(); rebuild(); render(); save() }
    func disableSpread() {
        guard let unit else { return }
        cancelCurrentPair()
        var correction = overrides[unit.id] ?? PageOverride(); correction.rotation = 0; correction.standalone = false; correction.joinNext = false
        overrides[unit.id] = correction; rebuild(); render(); save()
    }
    func joinNext() {
        guard let p = publication, index + 1 < p.units.count, !p.units[index].complex, !p.units[index+1].complex else { return }
        if index > 0 { var prior = overrides[p.units[index-1].id] ?? PageOverride(); prior.joinNext = false; overrides[p.units[index-1].id] = prior }
        var next = overrides[p.units[index+1].id] ?? PageOverride(); next.joinNext = false; overrides[p.units[index+1].id] = next
        var correction = overrides[p.units[index].id] ?? PageOverride(); correction.joinNext = true
        let pair = pairDecisions[index]
        correction.swapPair = pair.flatMap { $0.placementMargin >= 0.10 ? $0.swapped : nil } ?? (preferences.direction == .rtl)
        correction.pairOffset = pair?.suggested == true ? pair?.verticalOffset : 0
        correction.pairScale = pair?.suggested == true ? pair?.rightScale : 1
        overrides[p.units[index].id] = correction; rebuild(); render(); save()
    }
    func swapPair() {
        guard let first = group?.firstSourceIndex, let p = publication, group?.indices.count == 2 else { return }
        var correction = overrides[p.units[first].id] ?? PageOverride()
        correction.joinNext = true; correction.swapPair = group?.indices.first == first
        let scale = group?.rightScale ?? 1
        correction.pairOffset = -(group?.verticalOffset ?? 0)/scale; correction.pairScale = 1/scale
        overrides[p.units[first].id] = correction; rebuild(); render(); save()
    }
    func realignPair() {
        guard let group,group.indices.count == 2,let p=publication else { return }
        let first=group.firstSourceIndex
        guard group.indices.allSatisfy({ rotation(for:$0) == 0 }),let pair=pairDecisions[first],pair.suggested else {
            status="接缝证据不足，保留当前排列";return
        }
        var correction=overrides[p.units[first].id] ?? PageOverride()
        correction.joinNext=true;correction.swapPair=pair.swapped
        correction.pairOffset=pair.verticalOffset;correction.pairScale=pair.rightScale
        overrides[p.units[first].id]=correction;status="";rebuild();render();save()
    }
    var pairSuggestion: PairDecision? {
        guard preferences.smartSpreads, !showWeb, let p = publication,
              !(group?.spread == true && group?.indices.count == 2) else { return nil }
        return pairDecisions.values.filter { pair in
            let i = pair.firstIndex
            guard pair.suggested, i >= 0, i+1 < p.units.count, (i...i+1).contains(index),
                  overrides[p.units[i].id]?.joinNext == nil else { return false }
            return [i,i+1].allSatisfy {
                let key = p.units[$0].id
                return !p.units[$0].complex && !p.units[$0].isCover && p.units[$0].error == nil &&
                    overrides[key]?.rotation == nil && overrides[key]?.standalone != true &&
                    decisions[key]?.rotation == 0 && decisions[key]?.standalone == false && decisions[key]?.uncertain == false
            }
        }.max { $0.score < $1.score }
    }
    func acceptPairSuggestion() {
        guard let pair = pairSuggestion, let p = publication else { return }
        index = pair.firstIndex
        if index > 0 {
            var prior = overrides[p.units[index-1].id] ?? PageOverride(); prior.joinNext = false
            overrides[p.units[index-1].id] = prior
        }
        var next = overrides[p.units[index+1].id] ?? PageOverride(); next.joinNext = false
        overrides[p.units[index+1].id] = next
        var correction = overrides[p.units[index].id] ?? PageOverride()
        correction.joinNext = true; correction.swapPair = pair.swapped; correction.pairOffset = pair.verticalOffset; correction.pairScale = pair.rightScale
        overrides[p.units[index].id] = correction; rebuild(); render(); save()
    }
    func toggleBookmark() {
        guard let unit else { return }
        if bookmarks.contains(unit.locator) { bookmarks.removeAll { $0 == unit.locator } } else { bookmarks.append(unit.locator) }; save()
    }
    func rotation(for i: Int) -> Int {
        guard let p = publication else { return 0 }
        let key = p.units[i].id
        return overrides[key]?.rotation ?? (preferences.automaticOrientation ? decisions[key]?.rotation ?? 0 : 0)
    }
    func render(resetZoom: Bool = true) {
        loading?.cancel(); analyzing?.cancel()
        guard let engine, let p = publication, let group else { return }
        if resetZoom { zoom = 1; fitWidth = false }
        let token = UUID(); renderID = token; rendering = !showWeb; pages = []
        if showWeb { startAnalysis(); return }
        loading = Task {
            // Resolve the visible candidate before first paint. Once shown, a page never turns itself.
            if preferences.smartSpreads || preferences.automaticOrientation {
                // Include rivals on both sides so jumping to the second half gives the same grouping.
                var orientationIndices = Set(group.indices)
                for i in max(0,index-2)..<min(p.units.count-1,index+3) where preferences.smartSpreads {
                    if pairDecisions[i] == nil {
                        let pair = try? await engine.analyzePair(at: i)
                        guard !Task.isCancelled, renderID == token else { return }
                        if let pair { pairDecisions[i] = pair }
                    }
                    if pairDecisions[i]?.suggested == true { orientationIndices.formUnion([i,i+1]) }
                }
                for i in orientationIndices.sorted() where decisions[p.units[i].id] == nil && overrides[p.units[i].id]?.rotation == nil {
                    let decision = try? await engine.analyze(at: i)
                    guard !Task.isCancelled, renderID == token else { return }
                    if let decision { decisions[p.units[i].id] = decision }
                }
                rebuild()
                // An ordinary double-page group can change when one member becomes
                // standalone. Resolve any newly visible member before publishing it too.
                var attempted = orientationIndices
                while let i = self.group?.indices.first(where: { decisions[p.units[$0].id] == nil && overrides[p.units[$0].id]?.rotation == nil && !attempted.contains($0) }) {
                    attempted.insert(i)
                    let decision = try? await engine.analyze(at:i)
                    guard !Task.isCancelled, renderID == token else { return }
                    if let decision { decisions[p.units[i].id] = decision }
                    rebuild()
                }
            }
            guard !Task.isCancelled, renderID == token, let resolvedGroup = self.group else { return }
            let indices = resolvedGroup.indices
            let rotations = indices.map { rotation(for: $0) }
            var loaded: [CanvasPage] = []
            for (offset, i) in indices.enumerated() {
                do { let image = try await engine.image(at: i, maxPixel: 3200, rotation: rotations[offset]); loaded.append(CanvasPage(id: i, image: image, error: nil)) }
                catch { loaded.append(CanvasPage(id: i, image: nil, error: error.localizedDescription)) }
            }
            guard !Task.isCancelled, renderID == token, publication?.identity == p.identity else { return }
            pages = loaded; rendering = false
            startAnalysis()
            if let next = groups.first(where: { $0.firstSourceIndex > resolvedGroup.firstSourceIndex })?.firstSourceIndex {
                _ = try? await engine.image(at: next, maxPixel: 3200)
            }
        }
    }
    func startAnalysis() {
        analyzing?.cancel()
        guard preferences.smartSpreads || preferences.automaticOrientation, let p = publication, let engine else { return }
        let bookID = p.identity
        let visible = Set(group?.indices ?? [index])
        let candidates = Array(index..<min(p.units.count, index+5)).filter { decisions[p.units[$0].id] == nil && deferredDecisions[p.units[$0].id] == nil }
        analyzing = Task {
            for i in candidates {
                guard !Task.isCancelled else { return }
                let result: SpreadDecision
                do { result = try await engine.analyze(at: i) }
                catch { continue }
                guard !Task.isCancelled, publication?.identity == bookID else { return }
                let key = p.units[i].id
                if visible.contains(i), result.rotation != 0 || result.standalone {
                    deferredDecisions[key] = result
                    if i == index { status = "已识别跨页 · 点击“应用识别”完整展示" }
                } else { decisions[key] = result }
            }
            // Pairing never changes the visible group after first paint. Reconsider it on navigation.
            for i in max(0,index)..<min(p.units.count-1,index+5) where preferences.smartSpreads && pairDecisions[i] == nil && deferredPairs[i] == nil {
                let result = try? await engine.analyzePair(at: i)
                guard !Task.isCancelled, publication?.identity == bookID else { return }
                if let result { deferredPairs[i] = result }
            }
        }
    }
    var canApplyAnalysis: Bool { unit.flatMap { deferredDecisions[$0.id] } != nil }
    func applyAnalysis() {
        guard let unit, let result = deferredDecisions.removeValue(forKey: unit.id) else { return }
        decisions[unit.id] = result; status = ""; rebuild(); render(); save()
    }
    func save() {
        guard let p = publication else { return }
        let record = SavedBook(id: p.identity, path: p.sourceURL.path, title: p.title, revision: p.revision, locator: unit?.locator, preferences: preferences, overrides: overrides, bookmarks: bookmarks, openedAt: Date(), fileBookmark: fileBookmark)
        do { try store?.save(record); recent = try store?.books() ?? []; libraryModel.record(p,index:group?.indices.max() ?? index) }
        catch { status = "保存阅读记录失败：\(error.localizedDescription)" }
    }
}

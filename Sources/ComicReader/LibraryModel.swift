import SwiftUI
import AppKit
import ComicCore
import ImageIO
import UniformTypeIdentifiers

enum LibraryTab: String, CaseIterable { case reading = "阅读", browse = "浏览", tags = "标签", importing = "导入"
    var icon: String { switch self { case .reading: "book.fill"; case .browse: "books.vertical.fill"; case .tags: "tag.fill"; case .importing: "square.and.arrow.down" } }
}
@MainActor
final class LibraryModel: ObservableObject {
    @Published var catalog=Catalog()
    @Published var scanning=false
    @Published var message="添加漫画目录，开始建立你的书库。"
    @Published var error: String?
    private var store: LibraryStore?
    private var task: Task<Void,Never>?
    private var watcher: Task<Void,Never>?
    init() {
        do { store=try LibraryStore();catalog=try store?.catalog() ?? Catalog() }
        catch { store=nil;self.error=error.localizedDescription }
        watcher=Task { [weak self] in
            self?.refresh()
            while !Task.isCancelled {
                try? await Task.sleep(for:.seconds(60))
                guard !Task.isCancelled else { return }
                self?.refresh()
            }
        }
    }
    func persist() {
        do { try store?.saveCatalog(catalog) } catch { self.error="书库保存失败：\(error.localizedDescription)" }
    }
    func chooseDirectory() {
        let panel=NSOpenPanel();panel.canChooseDirectories=true;panel.canChooseFiles=false
        panel.message="添加漫画库目录；仅读取原文件，不复制漫画。"
        if panel.runModal() == .OK,let url=panel.url { add(url) }
    }
    func add(_ url:URL) {
        let path=url.standardizedFileURL.path
        if !catalog.roots.contains(where:{$0.path==path}) {
            let bookmark=try? url.bookmarkData(options:[.withSecurityScope,.securityScopeAllowOnlyReadAccess],includingResourceValuesForKeys:nil,relativeTo:nil)
            catalog.roots.append(LibraryRoot(path:path,bookmark:bookmark));persist()
        }
        refresh()
    }
    func removeRoot(_ root:LibraryRoot) {
        guard !scanning else { return }
        catalog.roots.removeAll{$0.path==root.path};persist()
        message="已停止扫描此目录；已有书卷和阅读记录仍保留。"
    }
    func refresh() {
        guard !scanning,!catalog.roots.isEmpty else { return }
        scanning=true
        let roots=catalog.roots
        task=Task {
            var failures:[String]=[]
            for root in roots {
                if Task.isCancelled { break }
                message="正在扫描 \(URL(fileURLWithPath:root.path).lastPathComponent)…"
                let result=await Task.detached(priority:.utility) { () -> Result<[LibraryVolume],Error> in
                    var stale=false
                    let url=root.bookmark.flatMap{try? URL(resolvingBookmarkData:$0,options:[.withSecurityScope,.withoutUI],relativeTo:nil,bookmarkDataIsStale:&stale)} ?? URL(fileURLWithPath:root.path)
                    let scoped=url.startAccessingSecurityScopedResource();defer { if scoped {url.stopAccessingSecurityScopedResource()} }
                    return Result { try LibraryScanner.scan(url) }
                }.value
                switch result {
                case .success(let volumes): catalog.merge(volumes,root:root.path)
                case .failure(let error):
                    for i in catalog.volumes.indices where catalog.volumes[i].root==root.path { catalog.volumes[i].available=false }
                    failures.append("\(URL(fileURLWithPath:root.path).lastPathComponent)：\(error.localizedDescription)")
                }
                persist()
            }
            scanning=false
            message=failures.isEmpty ? "已更新 · \(catalog.volumes.count) 卷 · \(Set(catalog.volumes.map(\.seriesID)).count) 个系列" : "部分目录无法扫描；保留已有索引。\n"+failures.joined(separator:"\n")
        }
    }
    func update(_ volume: LibraryVolume) {
        guard let i=catalog.volumes.firstIndex(where:{$0.path==volume.path}) else { return }
        catalog.volumes[i]=volume;persist()
    }
    func record(_ publication: Publication, index:Int) {
        let path=publication.sourceURL.standardizedFileURL.path
        if !catalog.volumes.contains(where:{$0.path==path}),let volume=try? LibraryScanner.volume(publication.sourceURL,root:publication.sourceURL.deletingLastPathComponent(),directory:publication.kind == .images && publication.units.count>1) {
            catalog.volumes.append(volume)
        }
        if let i=catalog.volumes.firstIndex(where:{$0.path==path}) {
            catalog.volumes[i].available=true
            catalog.volumes[i].updateProgress(position:index+1,total:publication.units.count)
            persist()
        }
    }
}

actor CoverCache {
    static let shared=CoverCache()
    private let directory=FileManager.default.urls(for:.cachesDirectory,in:.userDomainMask)[0].appendingPathComponent("ComicReader/Covers")
    func image(path:String,revision:String) async -> CGImage? {
        let file=directory.appendingPathComponent(LibraryScanner.cacheKey(path+revision)+".jpg")
        if let source=CGImageSourceCreateWithURL(file as CFURL,nil),let image=CGImageSourceCreateImageAtIndex(source,0,nil) { return image }
        guard !Task.isCancelled else { return nil }
        let engine=DocumentEngine()
        guard let book=try? await engine.open(URL(fileURLWithPath:path)) else { return nil }
        // Missing covers can fall back to the first readable page.
        for i in 0..<min(3,book.units.count) {
            guard !Task.isCancelled else { return nil }
            if let image=try? await engine.image(at:i,maxPixel:420) {
                try? FileManager.default.createDirectory(at:directory,withIntermediateDirectories:true)
                if let destination=CGImageDestinationCreateWithURL(file as CFURL,UTType.jpeg.identifier as CFString,1,nil) {
                    CGImageDestinationAddImage(destination,image,[kCGImageDestinationLossyCompressionQuality:0.8] as CFDictionary);CGImageDestinationFinalize(destination)
                }
                return image
            }
        }
        return nil
    }
}

struct CatalogCover: View {
    let path:String
    let revision:String
    @State private var image:NSImage?
    var body: some View {
        ZStack {
            RoundedRectangle(cornerRadius:10).fill(Color.primary.opacity(0.045))
            if let image { Image(nsImage:image).resizable().scaledToFit().padding(5) }
            else { Image(systemName:"book.closed").font(.system(size:38,weight:.ultraLight)).foregroundStyle(.secondary) }
        }.clipShape(RoundedRectangle(cornerRadius:10))
        .task(id:path+revision) {
            image=nil
            if let cg=await CoverCache.shared.image(path:path,revision:revision),!Task.isCancelled { image=NSImage(cgImage:cg,size:.zero) }
        }
    }
}

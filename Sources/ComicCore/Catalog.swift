import Foundation
import CryptoKit

public enum ReadState: String, Codable, CaseIterable, Sendable { case unread = "未读", reading = "在读", finished = "已读" }
public struct LibraryRoot: Codable, Identifiable, Sendable {
    public var id: String { path }
    public var path: String
    public var bookmark: Data?
    public init(path: String, bookmark: Data? = nil) { self.path=path; self.bookmark=bookmark }
}
public struct LibraryVolume: Codable, Identifiable, Sendable {
    public var id: String { path }
    public var path: String
    public var root: String
    public var series: String
    public var seriesID: String
    public var title: String
    public var order: Double
    public var revision: String
    public var available = true
    public var customized = false
    public var tags: [String] = []
    public var favorite = false
    public var state: ReadState = .unread
    public var position = 0
    public var total = 0
    public mutating func updateProgress(position: Int, total: Int) {
        let changed=self.position != position || self.total != total
        self.position=position;self.total=total
        if changed { state=position>=total ? .finished : .reading }
    }
    public init(path: String, root: String, series: String, seriesID: String, title: String, order: Double, revision: String) {
        self.path=path; self.root=root; self.series=series; self.seriesID=seriesID; self.title=title; self.order=order; self.revision=revision
    }
}
public struct Catalog: Codable, Sendable {
    public var roots: [LibraryRoot] = []
    public var volumes: [LibraryVolume] = []
    public init() {}
    public static func sorted(_ volumes: [LibraryVolume]) -> [LibraryVolume] {
        volumes.sorted { a,b in
            if a.order != b.order { return a.order < b.order }
            let comparison=a.title.localizedStandardCompare(b.title)
            return comparison == .orderedSame ? a.path < b.path : comparison == .orderedAscending
        }
    }
    public func next(after path: String) -> LibraryVolume? {
        guard let volume=volumes.first(where:{$0.path==path}) else { return nil }
        let siblings=Self.sorted(volumes.filter{$0.seriesID==volume.seriesID})
        guard let i=siblings.firstIndex(where:{$0.path==path}),i+1<siblings.count else { return nil }
        return siblings[i+1]
    }
    public mutating func merge(_ found: [LibraryVolume], root: String) {
        let discovered=Dictionary(uniqueKeysWithValues:found.map{($0.path,$0)})
        for i in volumes.indices where volumes[i].root==root {
            volumes[i].available=discovered[volumes[i].path] != nil
        }
        for value in found {
            if let i=volumes.firstIndex(where:{$0.path==value.path}) {
                volumes[i].available=true; volumes[i].revision=value.revision;volumes[i].root=root
                if !volumes[i].customized {
                    volumes[i].series=value.series;volumes[i].seriesID=value.seriesID;volumes[i].order=value.order;volumes[i].title=value.title
                }
            } else { volumes.append(value) }
        }
    }
}
public enum LibraryScanner {
    public static let imageExtensions=Set(["jpg","jpeg","png","gif","tif","tiff","bmp","heic","heif","webp","avif"])
    public static func volume(_ url: URL, root: URL, directory: Bool = false) throws -> LibraryVolume {
        let info=try url.resourceValues(forKeys:[.fileSizeKey,.contentModificationDateKey])
        let parent=url.deletingLastPathComponent()
        let name=directory ? url.lastPathComponent : url.deletingPathExtension().lastPathComponent
        let expression=try NSRegularExpression(pattern:"(?:卷|vol\\.?\\s*|volume\\s*)([0-9]+(?:\\.[0-9]+)?)",options:[.caseInsensitive])
        let range=NSRange(name.startIndex...,in:name)
        let match=expression.firstMatch(in:name,range:range)
        let order=match.flatMap{Range($0.range(at:1),in:name)}.flatMap{Double(name[$0])} ?? 0
        var revision="\(info.fileSize ?? 0)-\(info.contentModificationDate?.timeIntervalSince1970 ?? 0)"
        if directory {
            let children=try FileManager.default.contentsOfDirectory(at:url,includingPropertiesForKeys:[.fileSizeKey,.contentModificationDateKey],options:[.skipsHiddenFiles])
            let values=try children.filter{imageExtensions.contains($0.pathExtension.lowercased())}.sorted{$0.path<$1.path}.map { child in
                let info=try child.resourceValues(forKeys:[.fileSizeKey,.contentModificationDateKey])
                return "\(child.lastPathComponent):\(info.fileSize ?? 0):\(info.contentModificationDate?.timeIntervalSince1970 ?? 0)"
            }
            revision=cacheKey(values.joined(separator:"|"))
        }
        return LibraryVolume(path:url.standardizedFileURL.path,root:root.standardizedFileURL.path,series:parent.lastPathComponent,seriesID:parent.standardizedFileURL.path,title:name,order:order,revision:revision)
    }
    // Enumerate names/stat only; never decode an entire library during import.
    public static func scan(_ root: URL) throws -> [LibraryVolume] {
        var result:[LibraryVolume]=[]
        func visit(_ folder: URL) throws {
            try Task.checkCancellation()
            let children=try FileManager.default.contentsOfDirectory(at:folder,includingPropertiesForKeys:[.isDirectoryKey,.isSymbolicLinkKey],options:[.skipsHiddenFiles])
            var pictures=false,books=false
            for child in children {
                let values=try child.resourceValues(forKeys:[.isDirectoryKey,.isSymbolicLinkKey])
                if values.isSymbolicLink==true { continue }
                if values.isDirectory==true { try visit(child) }
                else if ["epub","pdf","mobi"].contains(child.pathExtension.lowercased()) {
                    result.append(try volume(child,root:root));books=true
                } else if imageExtensions.contains(child.pathExtension.lowercased()) { pictures=true }
            }
            if pictures && !books { result.append(try volume(folder,root:root,directory:true)) }
        }
        try visit(root)
        return result
    }
    public static func cacheKey(_ value: String) -> String { SHA256.hash(data:Data(value.utf8)).map{String(format:"%02x",$0)}.joined() }
}

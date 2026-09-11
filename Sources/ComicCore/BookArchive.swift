import Foundation
import ZIPFoundation

public final class BookArchive {
    private let archive: Archive
    private var entries: [String: Entry] = [:]
    private let lock = NSLock()
    public init(url: URL) throws {
        archive = try Archive(url: url, accessMode: .read)
        var total: UInt64 = 0
        for entry in archive {
            if entries.count > 100_000 { throw ReaderError.message("压缩包包含过多资源。") }
            if entry.type == .directory { continue }
            guard entry.type == .file else { throw ReaderError.message("不支持压缩包内的链接资源。") }
            let path = try Self.normalize(entry.path)
            guard entries[path] == nil else { throw ReaderError.message("压缩包存在重名资源：\(path)") }
            total += entry.uncompressedSize
            guard total <= 8 * 1024 * 1024 * 1024 else { throw ReaderError.message("出版物展开体积超出原型的 8 GiB 限制。") }
            entries[path] = entry
        }
    }
    public func contains(_ path: String) -> Bool { entries[path] != nil }
    public func data(_ path: String, limit: Int = 256 * 1024 * 1024) throws -> Data {
        guard let entry = entries[path] else { throw ReaderError.message("缺少资源：\(path)") }
        guard entry.uncompressedSize <= limit else { throw ReaderError.message("单个资源过大：\(path)") }
        lock.lock(); defer { lock.unlock() }
        var result = Data()
        let checksum = try archive.extract(entry) { chunk in
            guard result.count + chunk.count <= limit else { throw ReaderError.message("资源展开超出限制。") }
            result.append(chunk)
        }
        guard checksum == entry.checksum else { throw ReaderError.message("资源校验失败：\(path)") }
        return result
    }
    public static func resolve(_ reference: String, relativeTo document: String) throws -> String {
        let raw = reference.split(separator: "#", maxSplits: 1, omittingEmptySubsequences: false)[0]
        let clean = String(raw).split(separator: "?", maxSplits: 1, omittingEmptySubsequences: false)[0]
        guard let decoded = String(clean).removingPercentEncoding, !decoded.contains(":"), !decoded.hasPrefix("/") else {
            throw ReaderError.message("不允许外部资源路径。")
        }
        if decoded.isEmpty { return document }
        let parent = (document as NSString).deletingLastPathComponent
        return try normalize(parent.isEmpty ? decoded : parent + "/" + decoded)
    }
    public static func normalize(_ path: String) throws -> String {
        guard !path.hasPrefix("/"), !path.contains("\\"), !path.contains("\0") else { throw ReaderError.message("无效归档路径。") }
        var stack: [String] = []
        for component in path.split(separator: "/") {
            if component == "." { continue }
            if component == ".." {
                guard !stack.isEmpty else { throw ReaderError.message("归档路径超出出版物范围。") }
                stack.removeLast()
            } else { stack.append(String(component)) }
        }
        guard !stack.isEmpty else { throw ReaderError.message("空资源路径。") }
        return stack.joined(separator: "/")
    }
}

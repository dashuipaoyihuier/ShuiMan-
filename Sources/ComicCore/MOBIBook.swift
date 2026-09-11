import Foundation
import ImageIO
import SwiftSoup

/// Unencrypted MOBI 6 image comics, including the MOBI 6 rendition of hybrid books.
/// Record ordinals are never treated as reading order: HTML recindex references are authoritative.
public final class MOBIBook {
    private let data: Data
    private let offsets: [Int]
    public private(set) var units: [ReadingUnit] = []
    public private(set) var title = ""
    public private(set) var warnings: [String] = []
    public private(set) var direction: ReadingDirection?
    private static let textLimit = 32 * 1024 * 1024
    private static func invalid(_ detail: String) -> ReaderError { .message("MOBI 文件无效：\(detail)") }
    private static func number(_ bytes: Data, _ offset: Int, _ width: Int) throws -> Int {
        guard offset >= 0, width > 0, offset <= bytes.count-width else { throw invalid("记录被截断。") }
        return bytes[offset..<offset+width].reduce(0) { ($0 << 8) | Int($1) }
    }
    public init(url: URL) throws {
        data = try Data(contentsOf: url, options: .mappedIfSafe)
        guard data.count >= 78, data[60..<68] == Data("BOOKMOBI".utf8) else { throw Self.invalid("不是 BOOKMOBI 容器。") }
        let count = try Self.number(data,76,2)
        guard count > 1, 78+count*8 <= data.count else { throw Self.invalid("记录表不完整。") }
        var positions: [Int] = []
        for i in 0..<count {
            let offset = try Self.number(data,78+i*8,4)
            guard offset >= 78+count*8, (offset < data.count), offset > (positions.last ?? 0) else { throw Self.invalid("记录偏移越界或无序。") }
            positions.append(offset)
        }
        offsets = positions + [data.count]
        let header = try record(0)
        let compression = try Self.number(header,0,2), textLength = try Self.number(header,4,4)
        let textRecords = try Self.number(header,8,2)
        guard try Self.number(header,12,2) == 0 else { throw ReaderError.message("此 MOBI 受 DRM 保护，无法读取。请使用无 DRM 的漫画文件。") }
        guard header.count >= 116, header[16..<20] == Data("MOBI".utf8) else { throw Self.invalid("缺少 MOBI 头。") }
        let length = try Self.number(header,20,4), version = try Self.number(header,36,4)
        guard length >= 100, length <= header.count-16 else { throw Self.invalid("MOBI 头长度越界。") }
        guard version <= 6 else { throw ReaderError.message("当前支持 MOBI 6 及含 MOBI 6 的双格式漫画；纯 KF8/AZW3 请先转换为 EPUB。") }
        guard compression == 1 || compression == 2 else { throw ReaderError.message("此 MOBI 使用尚未支持的 HUFF/CDIC 或其他压缩方式，请先转换为 EPUB。") }
        guard textLength > 0, textLength <= Self.textLimit, textRecords > 0, textRecords < count else { throw Self.invalid("正文长度或记录数量异常。") }
        let imageStart = try Self.number(header,108,4)
        guard imageStart > textRecords, imageStart < count else { throw Self.invalid("图片记录起点无效。") }
        let encodingID = try Self.number(header,28,4)
        let encoding: String.Encoding
        switch encodingID { case 65001: encoding = .utf8; case 1252: encoding = .windowsCP1252
        default: throw ReaderError.message("暂不支持此 MOBI 的文字编码（\(encodingID)）。") }
        var metadata: [Int: Data] = [:]
        if length >= 116, try Self.number(header,128,4) & 0x40 != 0 {
            let start = 16+length
            guard start+12 <= header.count, header[start..<start+4] == Data("EXTH".utf8) else { throw Self.invalid("EXTH 元数据缺失。") }
            let size = try Self.number(header,start+4,4), entries = try Self.number(header,start+8,4)
            guard size >= 12, size <= header.count-start, entries <= (size-12)/8 else { throw Self.invalid("EXTH 长度无效。") }
            var cursor = start+12
            for _ in 0..<entries {
                guard cursor+8 <= start+size else { throw Self.invalid("EXTH 记录越界。") }
                let type = try Self.number(header,cursor,4), bytes = try Self.number(header,cursor+4,4)
                guard bytes >= 8, bytes <= start+size-cursor else { throw Self.invalid("EXTH 记录长度无效。") }
                metadata[type] = header.subdata(in: cursor+8..<cursor+bytes); cursor += bytes
            }
        }
        let nameOffset = try Self.number(header,84,4), nameLength = try Self.number(header,88,4)
        if nameOffset <= header.count, nameLength <= header.count-nameOffset {
            title = String(data: header.subdata(in: nameOffset..<nameOffset+nameLength), encoding: encoding) ?? ""
        }
        if let value = metadata[503], let name = String(data:value,encoding:encoding), !name.isEmpty { title = name }
        // EXTH 525 is text writing mode, not authoritative physical page progression.
        // Leave navigation direction to the reader preference rather than infer it from writing mode.
        if let boundary = metadata[121], boundary.count == 4, try Self.number(boundary,0,4) != 0xffffffff {
            warnings.append("双格式 MOBI：当前读取兼容正文，KF8 专属布局和局部放大信息未应用。")
        }
        let extra = length >= 228 ? try Self.number(header,240,4) : 0
        var text = Data()
        for i in 1...textRecords {
            try Task.checkCancellation()
            let bytes = try Self.stripTrailer(record(i), flags:extra)
            let decoded = compression == 2 ? try Self.decompress(bytes) : bytes
            guard decoded.count <= Self.textLimit-text.count else { throw Self.invalid("正文解压超过安全上限。") }
            text.append(decoded)
        }
        guard text.count >= textLength else { throw Self.invalid("正文解压长度不足。") }
        guard let html = String(data:text.prefix(textLength),encoding:encoding) else { throw Self.invalid("正文编码损坏。") }
        let document = try SwiftSoup.parse(html)
        guard let body = document.body(), try body.text().trimmingCharacters(in:.whitespacesAndNewlines).isEmpty else {
            throw ReaderError.message("此 MOBI 含文字或混合排版，当前漫画模式不能完整呈现，请转换为 EPUB 阅读。")
        }
        guard try body.select("svg, script, iframe, object").isEmpty() else { throw ReaderError.message("此 MOBI 含复杂页面，当前漫画模式不支持。") }
        let images = try body.select("img").array()
        guard !images.isEmpty, images.count <= 100_000 else { throw Self.invalid("没有可阅读的图片正文。") }
        var refs: [Int?] = []
        for element in images {
            let value = try element.attr("recindex")
            if let index = Int(value), index > 0, index <= count { refs.append(imageStart+index-1) }
            else { refs.append(nil) }
        }
        var cover: Int?
        if let value = metadata[201], value.count == 4 {
            let relative = try Self.number(value,0,4)
            if relative < count-imageStart { cover = imageStart+relative }
        }
        // MOBI readers expose EXTH cover separately; only insert when absent from body.
        if let cover, !refs.contains(where:{$0 == cover}) { units.append(makeUnit(record:cover,occurrence:-1,isCover:true)) }
        for (index, ref) in refs.enumerated() {
            try Task.checkCancellation()
            units.append(makeUnit(record:ref,occurrence:index,isCover:ref != nil && ref == cover))
        }
        if units.contains(where:{$0.error != nil}) { warnings.append("部分图片缺失或无法解码，已保留原阅读位置。") }
        warnings.append("MOBI 图片模式按正文图片顺序阅读，不还原 HTML/CSS 拼版。")
    }
    private func makeUnit(record index: Int?, occurrence: Int, isCover: Bool) -> ReadingUnit {
        let resource = index.map { "mobi:record:\($0)" } ?? "mobi:missing"
        var width = 0.0, height = 0.0, error: String?
        if let index, let bytes = try? record(index), let source = CGImageSourceCreateWithData(bytes as CFData,nil) {
            (width,height) = ImageTools.dimensions(source,index:0)
            if width <= 0 || height <= 0 { error = "MOBI 图片尺寸无效。" }
        } else { error = "MOBI 图片记录缺失或不支持。" }
        return ReadingUnit(locator:SourceLocator(resource:resource,occurrence:occurrence),title:isCover ? "封面" : "第 \(occurrence+1) 页",width:width,height:height,isCover:isCover,error:error)
    }
    public func imageData(for unit: ReadingUnit) throws -> Data {
        guard unit.locator.resource.hasPrefix("mobi:record:"), let index = Int(unit.locator.resource.dropFirst(12)) else { throw Self.invalid("图片定位无效。") }
        return try record(index)
    }
    private func record(_ index: Int) throws -> Data {
        guard index >= 0, index+1 < offsets.count else { throw Self.invalid("图片引用越界。") }
        return data.subdata(in: offsets[index]..<offsets[index+1])
    }
    private static func stripTrailer(_ input: Data, flags: Int) throws -> Data {
        var end = input.count, bits = flags >> 1
        while bits != 0 {
            if bits & 1 != 0 {
                var size = 0, shift = 0, terminated = false
                for i in (max(0,end-4)..<end).reversed() {
                    let byte = Int(input[i]); size |= (byte & 127) << shift; shift += 7
                    if byte & 128 != 0 { terminated = true; break }
                }
                guard terminated, size > 0, size <= end else { throw invalid("正文尾部索引损坏。") }
                end -= size
            }
            bits >>= 1
        }
        if flags & 1 != 0 {
            guard end > 0 else { throw invalid("多字节尾部被截断。") }
            let size = Int(input[end-1] & 3)+1
            guard size <= end else { throw invalid("多字节尾部越界。") }; end -= size
        }
        return input.subdata(in:0..<end)
    }
    private static func decompress(_ input: Data) throws -> Data {
        var output = [UInt8](), i = 0
        while i < input.count {
            let byte = Int(input[i]); i += 1
            if (1...8).contains(byte) {
                guard byte <= input.count-i else { throw invalid("PalmDOC 字面量被截断。") }
                output.append(contentsOf:input[i..<i+byte]); i += byte
            } else if byte < 128 { output.append(UInt8(byte)) }
            else if byte >= 192 { output.append(32); output.append(UInt8(byte ^ 128)) }
            else {
                guard i < input.count else { throw invalid("PalmDOC 回引用被截断。") }
                let value = (byte << 8) | Int(input[i]); i += 1
                let distance = (value >> 3) & 2047, length = (value & 7)+3
                guard distance > 0, distance <= output.count else { throw invalid("PalmDOC 回引用越界。") }
                for _ in 0..<length { output.append(output[output.count-distance]) }
            }
            guard output.count <= 65536 else { throw invalid("PalmDOC 记录解压超过上限。") }
        }
        return Data(output)
    }
}

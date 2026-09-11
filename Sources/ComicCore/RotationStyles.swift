import Foundation
import SwiftSoup

/// Extracts a possible screen rotation, not a browser layout. Supports ordinary selectors,
/// declaration order, specificity, inline styles and !important. Conditional screen rules
/// are hints only; contradictory possible orientations produce no automatic style hint.
enum RotationStyles {
    private struct Rule { let selector: String; let body: String; let condition: String }
    private struct Value {
        let rotation: Int?
        let important: Int
        let inline: Int
        let specificity: Int
        let order: Int
        var priority: [Int] { [important,inline,specificity,order] }
    }
    static func hint(css: String, image: Element) -> Int? {
        let clean = css.replacingOccurrences(of:"/\\*[\\s\\S]*?\\*/",with:"",options:.regularExpression)
        if clean.range(of:"@(supports|layer|container|import|scope)\\b",options:[.regularExpression,.caseInsensitive]) != nil { return nil }
        let rules = parse(clean)
        let conditions = Set(rules.map(\.condition)).union([""])
        var winners: [Int] = []
        for condition in conditions {
            var values: [Value] = []
            for (order,rule) in rules.enumerated() where rule.condition.isEmpty || rule.condition == condition || condition.hasPrefix(rule.condition+"&&") {
                for selector in rule.selector.split(separator:",").map({ $0.trimmingCharacters(in:.whitespacesAndNewlines) }) {
                    guard declaration(rule.body,inline:0,specificity:0,order:0) != nil else { continue }
                    guard let matched = try? image.iS(selector) else { return nil }
                    guard matched else { continue }
                    guard let specificity = specificity(selector) else { return nil }
                    if let value = declaration(rule.body,inline:0,specificity:specificity,order:order) { values.append(value) }
                }
            }
            if let inline = try? image.attr("style"), let value = declaration(inline,inline:1,specificity:0,order:rules.count) { values.append(value) }
            if let best = values.max(by:{ $0.priority.lexicographicallyPrecedes($1.priority) }) {
                // An unsupported transform in an applicable winning rule must not expose
                // a lower-precedence rotation declaration.
                guard let rotation = best.rotation else { return nil }
                winners.append(rotation)
            }
        }
        let nonzero = Set(winners.filter { $0 != 0 })
        return nonzero.count == 1 ? nonzero.first : (nonzero.isEmpty && !winners.isEmpty ? 0 : nil)
    }
    private static func declaration(_ body: String, inline: Int, specificity: Int, order: Int) -> Value? {
        var result: Value?
        for entry in body.split(separator:";") {
            let parts=entry.split(separator:":",maxSplits:1)
            guard parts.count == 2, parts[0].trimmingCharacters(in:.whitespacesAndNewlines).lowercased() == "transform" else { continue }
            let raw=parts[1].trimmingCharacters(in:.whitespacesAndNewlines).lowercased()
            let important=raw.contains("!important") ? 1 : 0
            let value=raw.replacingOccurrences(of:"!important",with:"").trimmingCharacters(in:.whitespacesAndNewlines)
            var angle: Int?
            if value == "none" { angle=0 }
            else if let regex=try? NSRegularExpression(pattern:"^rotate\\(\\s*(-?[0-9]+)(?:\\.0+)?deg\\s*\\)$"),
                    let match=regex.firstMatch(in:value,range:NSRange(value.startIndex...,in:value)),
                    let range=Range(match.range(at:1),in:value),let number=Int(value[range]),number%90 == 0 { angle=((number%360)+360)%360 }
            if result == nil || important >= result!.important { result=Value(rotation:angle,important:important,inline:inline,specificity:specificity,order:order) }
        }
        return result
    }
    private static func specificity(_ selector: String) -> Int? {
        // Functional/pseudo selectors and namespaces need a complete CSS parser; leave
        // those to WebKit rather than approximating their specificity.
        guard !selector.contains(":"), !selector.contains("|"), !selector.contains("\\") else { return nil }
        func count(_ pattern: String) -> Int {
            (try? NSRegularExpression(pattern:pattern).numberOfMatches(in:selector,range:NSRange(selector.startIndex...,in:selector))) ?? 0
        }
        return count("#[\\w-]+")*1_000_000+count("\\.[\\w-]+|\\[[^]]+\\]")*1_000+count("(?:^|[\\s>+~])(?:[a-zA-Z][\\w-]*)")
    }
    private static func parse(_ css: String, condition: String = "", depth: Int = 0) -> [Rule] {
        guard depth < 12 else { return [] }
        let chars=Array(css);var cursor=0;var result:[Rule]=[]
        while cursor<chars.count {
            let start=cursor
            while cursor<chars.count && chars[cursor] != "{" { cursor+=1 }
            guard cursor<chars.count else { break }
            let selector=String(chars[start..<cursor]).trimmingCharacters(in:.whitespacesAndNewlines)
            cursor+=1;let bodyStart=cursor;var braces=1;var quote:Character?
            while cursor<chars.count && braces>0 {
                let c=chars[cursor]
                if let q=quote { if c == q && (cursor == 0 || chars[cursor-1] != "\\") { quote=nil } }
                else if c == "\"" || c == "'" { quote=c }
                else if c == "{" { braces+=1 }
                else if c == "}" { braces-=1 }
                cursor+=1
            }
            guard braces == 0 else { break }
            let body=String(chars[bodyStart..<(cursor-1)])
            let lower=selector.lowercased()
            if lower.hasPrefix("@media") {
                // print and not-screen media cannot provide a screen orientation hint.
                if !lower.contains("print"),!lower.contains("not screen") {
                    result += parse(body,condition:condition+"&&"+lower,depth:depth+1)
                }
            } else if !lower.hasPrefix("@") { result.append(Rule(selector:selector,body:body,condition:condition)) }
        }
        return result
    }
}

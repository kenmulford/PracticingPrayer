import Foundation

public struct ParseResult: Equatable, Sendable {
    public let prayers: [SharedPrayer]
    public let suggestedCardTitle: String
}

/// Port of the MAUI `TextSelectionParser`; the regexes stay `NSRegularExpression` so UTF-16 matching
/// behaves as it does under .NET.
public enum TextSelectionParser {
    private static let titleMaxLength = 100
    private static let sentenceShapeWordThreshold = 6
    private static let minHeaderLetters = 3
    private static let maxHeaderLength = 60
    private static let uppercaseRatioThreshold = 0.8
    private static let maxAcronymLength = 3
    private static let clauseDelimiters: Set<Character> = [",", ";", ":", "\u{2014}", "-"]

    private static let lineSplitter = regex(#"\r\n|\r|\n"#)
    private static let blockSplitter = regex(#"(?:\r\n|\r|\n)[ \t]*(?:\r\n|\r|\n)+"#)
    private static let markerStripper = regex(#"^\s*(?:\d+[\.\)]|[-*\x{2022}])\s*"#)

    private struct Line {
        let content: String
        let hadMarker: Bool
    }

    public static func parse(_ rawText: String, now: Date, locale: Locale = .current) -> ParseResult {
        let defaultTitle = ImportResolver.defaultCardTitle(now: now, locale: locale)
        let normalized = TextNormalization.normalizeQuotes(rawText)
        let blocks = split(normalized, by: blockSplitter)

        var headerTitle: String?
        var entries: [SharedPrayer] = []

        for (b, block) in blocks.enumerated() {
            let blockLines = split(block, by: lineSplitter)
            var firstContentLine = 0
            if b == 0 {
                let (extracted, linesToSkip) = extractHeaderTitle(blockLines, contentFollowsExternally: blocks.count > 1)
                headerTitle = extracted
                firstContentLine = linesToSkip
            }

            var contentLines: [Line] = []
            for raw in blockLines.dropFirst(firstContentLine) {
                let ns = raw as NSString
                let match = markerStripper.firstMatch(in: raw, range: NSRange(location: 0, length: ns.length))
                let stripped = match.map { ns.substring(from: $0.range.length) } ?? raw
                let content = TextNormalization.collapseWhitespace(stripped)
                if content.isEmpty { continue }
                contentLines.append(Line(content: content, hadMarker: match != nil))
            }
            if contentLines.isEmpty { continue }

            if contentLines.count >= 2, isSentenceShaped(contentLines[1].content), !contentLines[1].hadMarker {
                let details = contentLines.dropFirst().map(\.content).joined(separator: "\n")
                entries.append(SharedPrayer(title: capTitle(contentLines[0].content), notes: details))
            } else {
                entries.append(contentsOf: contentLines.map { buildPrayer($0.content) })
            }
        }

        if entries.isEmpty, !normalized.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
            entries.append(buildPrayer(TextNormalization.collapseWhitespace(normalized)))
        }
        return ParseResult(prayers: entries, suggestedCardTitle: headerTitle ?? defaultTitle)
    }

    private static func regex(_ pattern: String) -> NSRegularExpression {
        // Patterns are literals checked by the test suite.
        try! NSRegularExpression(pattern: pattern)
    }

    /// Mirrors `Regex.Split`: a match at the start yields a leading empty piece.
    private static func split(_ text: String, by regex: NSRegularExpression) -> [String] {
        let ns = text as NSString
        var pieces: [String] = []
        var cursor = 0
        for match in regex.matches(in: text, range: NSRange(location: 0, length: ns.length)) {
            pieces.append(ns.substring(with: NSRange(location: cursor, length: match.range.location - cursor)))
            cursor = match.range.location + match.range.length
        }
        pieces.append(ns.substring(from: cursor))
        return pieces
    }

    private static func words(_ text: String) -> [Substring] {
        text.split(separator: " ", omittingEmptySubsequences: true)
    }

    private static func isSentenceShaped(_ line: String) -> Bool {
        line.contains(where: clauseDelimiters.contains) || words(line).count > sentenceShapeWordThreshold
    }

    /// Consecutive ALL-CAPS header lines collapse to the last; a content line (or a later block) must follow.
    private static func extractHeaderTitle(_ lines: [String], contentFollowsExternally: Bool) -> (String?, Int) {
        var headerEnd = -1
        var headerTrimmed: String?
        var sawContent = false
        for (i, line) in lines.enumerated() {
            let trimmed = line.trimmingCharacters(in: .whitespacesAndNewlines)
            if trimmed.isEmpty { continue }
            if isHeaderLine(trimmed) {
                headerEnd = i
                headerTrimmed = trimmed
            } else {
                sawContent = true
                break
            }
        }
        guard headerEnd >= 0, let headerTrimmed, sawContent || contentFollowsExternally else { return (nil, 0) }
        return (titleCase(headerTrimmed), headerEnd + 1)
    }

    private static func isHeaderLine(_ trimmed: String) -> Bool {
        if trimmed.count < minHeaderLetters || trimmed.count > maxHeaderLength { return false }
        let range = NSRange(location: 0, length: (trimmed as NSString).length)
        if markerStripper.firstMatch(in: trimmed, range: range) != nil { return false }
        var letters = 0
        var upper = 0
        for c in trimmed where c.isLetter {
            letters += 1
            if c.isUppercase { upper += 1 }
        }
        return letters >= minHeaderLetters && Double(upper) / Double(letters) >= uppercaseRatioThreshold
    }

    /// Title-cases each word; tokens of 3 letters or fewer that were all caps stay as written (NPM, FBC).
    private static func titleCase(_ s: String) -> String {
        var titled = ""
        var previousStartsWord = true
        for c in s.lowercased() {
            titled.append(previousStartsWord ? String(c).uppercased() : String(c))
            previousStartsWord = !(c.isLetter || c == "'")
        }
        let sourceTokens = words(s)
        var titledTokens = words(titled).map(String.init)
        guard sourceTokens.count == titledTokens.count else { return titled }
        for (i, token) in sourceTokens.enumerated()
        where token.count <= maxAcronymLength && !token.isEmpty && token.allSatisfy({ $0.isLetter && $0.isUppercase }) {
            titledTokens[i] = String(token)
        }
        return titledTokens.joined(separator: " ")
    }

    /// Rule 6: split on the first clause delimiter, or take the first 5 words past 6; the 100-character cap applies on top.
    private static func buildPrayer(_ text: String) -> SharedPrayer {
        if let idx = text.firstIndex(where: clauseDelimiters.contains), idx > text.startIndex {
            let head = String(text[..<idx]).replacing(/\s+$/, with: "")
            return SharedPrayer(title: capTitle(head), notes: text)
        }
        let parts = words(text)
        if parts.count > 6 {
            return SharedPrayer(title: capTitle(parts.prefix(5).joined(separator: " ")), notes: text)
        }
        return text.count <= titleMaxLength
            ? SharedPrayer(title: text, notes: nil)
            : SharedPrayer(title: capTitle(text), notes: String(text.dropFirst(titleMaxLength)))
    }

    private static func capTitle(_ title: String) -> String {
        title.count <= titleMaxLength ? title : String(title.prefix(titleMaxLength))
    }
}

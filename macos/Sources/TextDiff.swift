import Foundation
import SwiftUI
import AppKit

enum DiffItem: Equatable {
    case unchanged(String)
    case added(String)
    case removed(String)
}

enum TextDiff {
    /// Tokenizes text into words, punctuation, and whitespace chunks.
    static func tokenize(_ text: String) -> [String] {
        var tokens: [String] = []
        let pattern = "[\\p{L}\\p{N}_]+|[^\\p{L}\\p{N}_\\s]+|\\s+"
        guard let regex = try? NSRegularExpression(pattern: pattern) else {
            return text.map { String($0) }
        }
        let nsString = text as NSString
        let matches = regex.matches(in: text, range: NSRange(location: 0, length: nsString.length))
        for match in matches {
            tokens.append(nsString.substring(with: match.range))
        }
        return tokens
    }

    /// Computes token-level Longest Common Subsequence (LCS) diff.
    static func computeWordDiff(original: String, modified: String) -> [DiffItem] {
        let a = tokenize(original)
        let b = tokenize(modified)
        let n = a.count
        let m = b.count

        if n == 0 && m == 0 { return [] }
        if n == 0 { return b.map { .added($0) } }
        if m == 0 { return a.map { .removed($0) } }

        // DP matrix
        var dp = Array(repeating: Array(repeating: 0, count: m + 1), count: n + 1)
        for i in 0..<n {
            for j in 0..<m {
                if a[i] == b[j] {
                    dp[i + 1][j + 1] = dp[i][j] + 1
                } else {
                    dp[i + 1][j + 1] = max(dp[i + 1][j], dp[i][j + 1])
                }
            }
        }

        var i = n
        var j = m
        var result: [DiffItem] = []

        while i > 0 || j > 0 {
            if i > 0 && j > 0 && a[i - 1] == b[j - 1] {
                result.append(.unchanged(a[i - 1]))
                i -= 1
                j -= 1
            } else if j > 0 && (i == 0 || dp[i][j - 1] >= dp[i - 1][j]) {
                result.append(.added(b[j - 1]))
                j -= 1
            } else if i > 0 && (j == 0 || dp[i][j - 1] < dp[i - 1][j]) {
                result.append(.removed(a[i - 1]))
                i -= 1
            }
        }

        return result.reversed()
    }

    /// Builds a formatted AttributedString with green additions and red strikethrough deletions.
    static func makeAttributedString(original: String, modified: String) -> AttributedString {
        let items = computeWordDiff(original: original, modified: modified)
        var attr = AttributedString()

        for item in items {
            switch item {
            case .unchanged(let text):
                var piece = AttributedString(text)
                piece.foregroundColor = .primary
                attr.append(piece)
            case .added(let text):
                var piece = AttributedString(text)
                piece.foregroundColor = Color(nsColor: .systemGreen)
                piece.backgroundColor = Color(nsColor: .systemGreen).opacity(0.18)
                attr.append(piece)
            case .removed(let text):
                var piece = AttributedString(text)
                piece.foregroundColor = Color(nsColor: .systemRed)
                piece.backgroundColor = Color(nsColor: .systemRed).opacity(0.18)
                piece.inlinePresentationIntent = .strikethrough
                attr.append(piece)
            }
        }
        return attr
    }
}

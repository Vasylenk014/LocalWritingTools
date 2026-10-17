import Foundation

enum TextTransformation: String, CaseIterable, Identifiable {
    case uppercase
    case lowercase
    case capitalize
    case sentenceCase
    case cleanSpacing
    case straightenQuotes
    case removeLineBreaks

    var id: String { rawValue }

    var title: String {
        switch self {
        case .uppercase: return "Make Upper Case"
        case .lowercase: return "Make Lower Case"
        case .capitalize: return "Capitalize"
        case .sentenceCase: return "Sentence case"
        case .cleanSpacing: return "Clean Extra Spaces"
        case .straightenQuotes: return "Straighten Quotes"
        case .removeLineBreaks: return "Remove Line Breaks"
        }
    }

    func apply(to text: String) -> String {
        switch self {
        case .uppercase:
            return text.uppercased()
        case .lowercase:
            return text.lowercased()
        case .capitalize:
            return text.capitalized
        case .sentenceCase:
            return Self.toSentenceCase(text)
        case .cleanSpacing:
            return Self.cleanSpacing(text)
        case .straightenQuotes:
            return Self.straightenQuotes(text)
        case .removeLineBreaks:
            return Self.removeLineBreaks(text)
        }
    }

    private static func toSentenceCase(_ text: String) -> String {
        guard !text.isEmpty else { return text }
        let working = (text == text.uppercased() && text.contains { $0.isLetter }) ? text.lowercased() : text
        var result = ""
        var capitalizeNext = true

        for char in working {
            if capitalizeNext && char.isLetter {
                result.append(Character(char.uppercased()))
                capitalizeNext = false
            } else {
                result.append(char)
            }

            if char == "." || char == "!" || char == "?" || char == "\n" {
                capitalizeNext = true
            }
        }
        return result
    }

    private static func cleanSpacing(_ text: String) -> String {
        let lines = text.components(separatedBy: "\n").map { line in
            var trimmed = line.trimmingCharacters(in: .whitespaces)
            while trimmed.contains("  ") {
                trimmed = trimmed.replacingOccurrences(of: "  ", with: " ")
            }
            return trimmed
        }
        var cleaned: [String] = []
        var consecutiveEmpty = 0
        for line in lines {
            if line.isEmpty {
                consecutiveEmpty += 1
                if consecutiveEmpty <= 1 {
                    cleaned.append(line)
                }
            } else {
                consecutiveEmpty = 0
                cleaned.append(line)
            }
        }
        return cleaned.joined(separator: "\n").trimmingCharacters(in: .whitespacesAndNewlines)
    }

    private static func straightenQuotes(_ text: String) -> String {
        text.replacingOccurrences(of: "“", with: "\"")
            .replacingOccurrences(of: "”", with: "\"")
            .replacingOccurrences(of: "«", with: "\"")
            .replacingOccurrences(of: "»", with: "\"")
            .replacingOccurrences(of: "‘", with: "'")
            .replacingOccurrences(of: "’", with: "'")
            .replacingOccurrences(of: "`", with: "'")
    }

    private static func removeLineBreaks(_ text: String) -> String {
        let paragraphs = text.components(separatedBy: "\n\n")
        let unwrapped = paragraphs.map { p in
            p.components(separatedBy: "\n")
                .map { $0.trimmingCharacters(in: .whitespaces) }
                .joined(separator: " ")
        }
        return unwrapped.joined(separator: "\n\n")
    }
}

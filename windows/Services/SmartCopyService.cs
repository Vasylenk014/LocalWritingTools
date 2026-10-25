using System;
using System.Text.RegularExpressions;
using WritingTools.Models;

namespace WritingTools.Services;

public static class SmartCopyService
{
    private static readonly Regex CorrectedSectionRegex = new(
        @"(?:^|\n)\s*CORRECTED:\s*(.*?)(?=\n\s*(?:CONCISE:|CHANGES:|NOTES:)|$)",
        RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    private static readonly Regex ThinkBlockRegex = new(
        @"<think>.*?</think>",
        RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    private static readonly Regex ThoughtBlockRegex = new(
        @"<thought>.*?</thought>",
        RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    private static readonly Regex ReasoningBlockRegex = new(
        @"<reasoning>.*?</reasoning>",
        RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    private static readonly Regex UnclosedReasoningRegex = new(
        @"<(think|thought|reasoning)>.*?$",
        RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    private static readonly Regex DraftTagRegex = new(
        @"</?draft>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    public static string GetSmartCopyText(string rawResult, AppMode mode)
    {
        if (string.IsNullOrWhiteSpace(rawResult)) return string.Empty;

        var cleaned = CleanArtifacts(rawResult);

        if (mode == AppMode.DetailedReview)
        {
            var extracted = ExtractCorrectedSection(cleaned);
            if (!string.IsNullOrWhiteSpace(extracted))
            {
                return extracted;
            }
        }

        return cleaned;
    }

    public static string ExtractCorrectedSection(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var match = CorrectedSectionRegex.Match(text);
        if (match.Success)
        {
            var extracted = match.Groups[1].Value.Trim();
            return CleanArtifacts(extracted);
        }

        return text.Trim();
    }

    public static string CleanArtifacts(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var result = text;

        // Remove <think>...</think>, <thought>...</thought>, and <reasoning>...</reasoning> blocks
        result = ThinkBlockRegex.Replace(result, "");
        result = ThoughtBlockRegex.Replace(result, "");
        result = ReasoningBlockRegex.Replace(result, "");
        result = UnclosedReasoningRegex.Replace(result, "");

        // Remove <draft> and </draft> tags
        result = DraftTagRegex.Replace(result, "");

        // Remove markdown code fences if wrapping the entire output
        var trimmed = result.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal) && trimmed.EndsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = trimmed.IndexOf('\n');
            var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (firstNewline >= 0 && lastFence > firstNewline)
            {
                result = trimmed.Substring(firstNewline + 1, lastFence - firstNewline - 1);
            }
        }

        // Trim leading/trailing quotes if the model wrapped the entire output in quotation marks
        trimmed = result.Trim();
        if ((trimmed.StartsWith("\"", StringComparison.Ordinal) && trimmed.EndsWith("\"", StringComparison.Ordinal) && trimmed.Length > 2) ||
            (trimmed.StartsWith("“", StringComparison.Ordinal) && trimmed.EndsWith("”", StringComparison.Ordinal) && trimmed.Length > 2))
        {
            result = trimmed.Substring(1, trimmed.Length - 2);
        }

        return result.Trim();
    }
}

using System;
using System.Text.RegularExpressions;
using WritingTools.Models;

namespace WritingTools.Services;

public static class SmartCopyService
{
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

        // Match CORRECTED: followed by content up to CONCISE:, CHANGES:, NOTES: or end of string
        var match = Regex.Match(
            text,
            @"(?:^|\n)\s*CORRECTED:\s*(.*?)(?=\n\s*(?:CONCISE:|CHANGES:|NOTES:)|$)",
            RegexOptions.Singleline | RegexOptions.IgnoreCase
        );

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
        result = Regex.Replace(result, @"<think>.*?</think>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        result = Regex.Replace(result, @"<thought>.*?</thought>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        result = Regex.Replace(result, @"<reasoning>.*?</reasoning>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        result = Regex.Replace(result, @"<(think|thought|reasoning)>.*?$", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);

        // Remove <draft> and </draft> tags
        result = Regex.Replace(result, @"</?draft>", "", RegexOptions.IgnoreCase);

        // Remove markdown code fences if wrapping the entire output
        var trimmed = result.Trim();
        if (trimmed.StartsWith("```") && trimmed.EndsWith("```"))
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
        if ((trimmed.StartsWith("\"") && trimmed.EndsWith("\"") && trimmed.Length > 2) ||
            (trimmed.StartsWith("“") && trimmed.EndsWith("”") && trimmed.Length > 2))
        {
            result = trimmed.Substring(1, trimmed.Length - 2);
        }

        return result.Trim();
    }
}

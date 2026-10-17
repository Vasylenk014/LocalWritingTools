using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using WritingTools.Models;

namespace WritingTools.Services;

public record DiffToken(string Text, DiffTokenType Type);

public class DiffService
{
    private static readonly Regex TokenRegex = new(@"[\p{L}\p{N}_]+|[^\p{L}\p{N}_\s]+|\s+", RegexOptions.Compiled);

    public static List<string> Tokenize(string text)
    {
        if (string.IsNullOrEmpty(text)) return new List<string>();
        var matches = TokenRegex.Matches(text);
        var tokens = new List<string>(matches.Count);
        foreach (Match m in matches)
        {
            tokens.Add(m.Value);
        }
        return tokens;
    }

    public static List<DiffToken> ComputeDiff(string originalText, string modifiedText)
    {
        var origTokens = Tokenize(originalText);
        var modTokens = Tokenize(modifiedText);

        int m = origTokens.Count;
        int n = modTokens.Count;

        // Longest Common Subsequence (LCS) matrix
        int[,] lcs = new int[m + 1, n + 1];

        for (int i = 0; i < m; i++)
        {
            for (int j = 0; j < n; j++)
            {
                if (origTokens[i] == modTokens[j])
                {
                    lcs[i + 1, j + 1] = lcs[i, j] + 1;
                }
                else
                {
                    lcs[i + 1, j + 1] = Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
                }
            }
        }

        // Backtrack to assemble diff tokens
        var result = new List<DiffToken>();
        int curOrig = m;
        int curMod = n;

        var reversed = new List<DiffToken>();

        while (curOrig > 0 || curMod > 0)
        {
            if (curOrig > 0 && curMod > 0 && origTokens[curOrig - 1] == modTokens[curMod - 1])
            {
                reversed.Add(new DiffToken(origTokens[curOrig - 1], DiffTokenType.Unchanged));
                curOrig--;
                curMod--;
            }
            else if (curMod > 0 && (curOrig == 0 || lcs[curOrig, curMod - 1] >= lcs[curOrig - 1, curMod]))
            {
                reversed.Add(new DiffToken(modTokens[curMod - 1], DiffTokenType.Added));
                curMod--;
            }
            else if (curOrig > 0 && (curMod == 0 || lcs[curOrig, curMod - 1] < lcs[curOrig - 1, curMod]))
            {
                reversed.Add(new DiffToken(origTokens[curOrig - 1], DiffTokenType.Removed));
                curOrig--;
            }
        }

        reversed.Reverse();

        // Merge adjacent tokens of the same type for optimal WPF text rendering performance
        for (int i = 0; i < reversed.Count; i++)
        {
            var token = reversed[i];
            if (result.Count > 0 && result[^1].Type == token.Type)
            {
                result[^1] = new DiffToken(result[^1].Text + token.Text, token.Type);
            }
            else
            {
                result.Add(token);
            }
        }

        return result;
    }
}

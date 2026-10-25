using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace WritingTools.Services;

public static class TextTransformService
{
    private static readonly Regex HorizontalSpaceRegex = new(@"[ \t]+", RegexOptions.Compiled);
    private static readonly Regex LineBreakRegex = new(@"[\r\n]+", RegexOptions.Compiled);

    public static string ToUpperCase(string input) => input.ToUpperInvariant();

    public static string ToLowerCase(string input) => input.ToLowerInvariant();

    public static string CapitalizeWords(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        var textInfo = CultureInfo.CurrentCulture.TextInfo;
        return textInfo.ToTitleCase(input.ToLower(CultureInfo.CurrentCulture));
    }

    public static string ToSentenceCase(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;

        var sb = new StringBuilder(input.Length);
        bool capitalizeNext = true;

        foreach (char c in input)
        {
            if (capitalizeNext && char.IsLetter(c))
            {
                sb.Append(char.ToUpper(c, CultureInfo.CurrentCulture));
                capitalizeNext = false;
            }
            else
            {
                sb.Append(char.ToLower(c, CultureInfo.CurrentCulture));
            }

            if (c == '.' || c == '?' || c == '!' || c == '\n')
            {
                capitalizeNext = true;
            }
        }

        return sb.ToString();
    }

    public static string CleanExtraSpaces(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;

        // Replace 2+ spaces or tabs with a single space, but preserve newlines
        var lines = input.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        var sb = new StringBuilder(input.Length);

        bool prevWasEmpty = false;

        foreach (var rawLine in lines)
        {
            var line = HorizontalSpaceRegex.Replace(rawLine.Trim(), " ");
            if (string.IsNullOrWhiteSpace(line))
            {
                if (!prevWasEmpty && sb.Length > 0)
                {
                    sb.AppendLine();
                    prevWasEmpty = true;
                }
            }
            else
            {
                if (sb.Length > 0 && !prevWasEmpty)
                {
                    sb.AppendLine();
                }
                sb.Append(line);
                prevWasEmpty = false;
            }
        }

        return sb.ToString().Trim();
    }

    public static string StraightenQuotes(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;

        return input
            .Replace('«', '"')
            .Replace('»', '"')
            .Replace('“', '"')
            .Replace('”', '"')
            .Replace('„', '"')
            .Replace('‟', '"')
            .Replace('‘', '\'')
            .Replace('’', '\'')
            .Replace('‚', '\'')
            .Replace('‛', '\'');
    }

    public static string RemoveLineBreaks(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;

        // Replace line breaks with single space, collapsing double spaces
        var unwrapped = LineBreakRegex.Replace(input, " ");
        return HorizontalSpaceRegex.Replace(unwrapped, " ").Trim();
    }
}

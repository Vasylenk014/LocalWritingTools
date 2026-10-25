using System;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

// Disambiguate WPF types from Windows Forms types
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;
using FontFamily = System.Windows.Media.FontFamily;

namespace WritingTools.Services;

public static class MarkdownDocumentRenderer
{
    // Dark theme palette
    private static readonly SolidColorBrush DarkTextPrimaryBrush = new(Color.FromRgb(0xF4, 0xF4, 0xF6));
    private static readonly SolidColorBrush DarkTextSecondaryBrush = new(Color.FromRgb(0xA1, 0xA1, 0xAA));
    private static readonly SolidColorBrush DarkHeaderAccentBrush = new(Color.FromRgb(0x81, 0x8C, 0xF8)); // Indigo 400
    private static readonly SolidColorBrush DarkGreenBrush = new(Color.FromRgb(0x22, 0xC5, 0x5E));
    private static readonly SolidColorBrush DarkRedBrush = new(Color.FromRgb(0xEF, 0x44, 0x44));

    // Light theme palette
    private static readonly SolidColorBrush LightTextPrimaryBrush = new(Color.FromRgb(0x0F, 0x17, 0x2A)); // Slate 900
    private static readonly SolidColorBrush LightTextSecondaryBrush = new(Color.FromRgb(0x47, 0x55, 0x69)); // Slate 600
    private static readonly SolidColorBrush LightHeaderAccentBrush = new(Color.FromRgb(0x4F, 0x46, 0xE5)); // Indigo 600
    private static readonly SolidColorBrush LightGreenBrush = new(Color.FromRgb(0x15, 0x80, 0x3D)); // Green 700
    private static readonly SolidColorBrush LightRedBrush = new(Color.FromRgb(0xB9, 0x1C, 0x1C)); // Red 700

    static MarkdownDocumentRenderer()
    {
        DarkTextPrimaryBrush.Freeze();
        DarkTextSecondaryBrush.Freeze();
        DarkHeaderAccentBrush.Freeze();
        DarkGreenBrush.Freeze();
        DarkRedBrush.Freeze();

        LightTextPrimaryBrush.Freeze();
        LightTextSecondaryBrush.Freeze();
        LightHeaderAccentBrush.Freeze();
        LightGreenBrush.Freeze();
        LightRedBrush.Freeze();
    }

    public static FlowDocument Render(string markdownText, bool isLightTheme = false)
    {
        var primaryBrush = isLightTheme ? LightTextPrimaryBrush : DarkTextPrimaryBrush;
        var secondaryBrush = isLightTheme ? LightTextSecondaryBrush : DarkTextSecondaryBrush;
        var accentBrush = isLightTheme ? LightHeaderAccentBrush : DarkHeaderAccentBrush;
        var greenBrush = isLightTheme ? LightGreenBrush : DarkGreenBrush;
        var redBrush = isLightTheme ? LightRedBrush : DarkRedBrush;

        var doc = new FlowDocument
        {
            PagePadding = new Thickness(0),
            Background = Brushes.Transparent,
            Foreground = primaryBrush,
            FontFamily = new FontFamily("Segoe UI Variable, Segoe UI"),
            FontSize = 13.0,
            LineHeight = 21.0
        };

        if (string.IsNullOrWhiteSpace(markdownText))
        {
            return doc;
        }

        var lines = markdownText.Replace("\r\n", "\n").Split('\n');
        bool isFirstHeader = true;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd();
            var trimmed = line.Trim();

            if (string.IsNullOrEmpty(trimmed))
            {
                continue;
            }

            // 1. Section Headers (e.g. "CORRECTED:", "CONCISE:", "CHANGES:", "NOTES:" or Markdown # / ##)
            if (IsSectionHeader(trimmed, out var headerTitle))
            {
                var headerPara = new Paragraph
                {
                    Margin = new Thickness(0, isFirstHeader ? 2 : 12, 0, 4)
                };
                isFirstHeader = false;

                var headerRun = new Run(headerTitle)
                {
                    FontWeight = FontWeights.Bold,
                    FontSize = 13.5,
                    Foreground = accentBrush
                };
                headerPara.Inlines.Add(headerRun);
                doc.Blocks.Add(headerPara);
                continue;
            }

            // 2. Bullet Points (e.g. "- item" or "* item")
            if (trimmed.StartsWith("- ") || trimmed.StartsWith("* "))
            {
                var bulletContent = trimmed.Substring(2).Trim();
                var bulletPara = new Paragraph
                {
                    Margin = new Thickness(14, 2, 0, 3)
                };

                var bulletSymbol = new Run("• ")
                {
                    FontWeight = FontWeights.Bold,
                    Foreground = accentBrush
                };
                bulletPara.Inlines.Add(bulletSymbol);

                AppendFormattedInlines(bulletPara, bulletContent, primaryBrush, secondaryBrush, accentBrush, greenBrush, redBrush);
                doc.Blocks.Add(bulletPara);
                continue;
            }

            // 3. Numbered List Items (e.g. "1. item", "2) item")
            var numMatch = Regex.Match(trimmed, @"^(\d+[\.\)])\s+(.*)$");
            if (numMatch.Success)
            {
                var numPrefix = numMatch.Groups[1].Value + " ";
                var numContent = numMatch.Groups[2].Value.Trim();
                var numPara = new Paragraph
                {
                    Margin = new Thickness(12, 2, 0, 4)
                };

                var numRun = new Run(numPrefix)
                {
                    FontWeight = FontWeights.Bold,
                    Foreground = accentBrush
                };
                numPara.Inlines.Add(numRun);

                AppendFormattedInlines(numPara, numContent, primaryBrush, secondaryBrush, accentBrush, greenBrush, redBrush);
                doc.Blocks.Add(numPara);
                continue;
            }

            // 4. Regular Paragraphs
            var para = new Paragraph
            {
                Margin = new Thickness(0, 0, 0, 6)
            };
            AppendFormattedInlines(para, trimmed, primaryBrush, secondaryBrush, accentBrush, greenBrush, redBrush);
            doc.Blocks.Add(para);
        }

        return doc;
    }

    private static bool IsSectionHeader(string text, out string title)
    {
        title = text;
        var upper = text.ToUpperInvariant();

        if (upper == "CORRECTED:" || upper.StartsWith("CORRECTED:"))
        {
            title = "✨ " + text;
            return true;
        }
        if (upper == "CONCISE:" || upper.StartsWith("CONCISE:"))
        {
            title = "⚡ " + text;
            return true;
        }
        if (upper == "CHANGES:" || upper.StartsWith("CHANGES:"))
        {
            title = "🔍 " + text;
            return true;
        }
        if (upper == "NOTES:" || upper.StartsWith("NOTES:"))
        {
            title = "📝 " + text;
            return true;
        }

        // Markdown headings: #, ##, ###
        if (text.StartsWith("### "))
        {
            title = text.Substring(4).Trim();
            return true;
        }
        if (text.StartsWith("## "))
        {
            title = text.Substring(3).Trim();
            return true;
        }
        if (text.StartsWith("# "))
        {
            title = text.Substring(2).Trim();
            return true;
        }

        return false;
    }

    private static void AppendFormattedInlines(
        Paragraph paragraph,
        string text,
        SolidColorBrush primaryBrush,
        SolidColorBrush secondaryBrush,
        SolidColorBrush accentBrush,
        SolidColorBrush greenBrush,
        SolidColorBrush redBrush)
    {
        var arrowMatch = Regex.Match(text, @"^([""“«][^""”»]+[""”»])\s*(?:→|->)\s*([""“«][^""”»]+[""”»])(.*)$");
        if (arrowMatch.Success)
        {
            var orig = arrowMatch.Groups[1].Value;
            var mod = arrowMatch.Groups[2].Value;
            var rest = arrowMatch.Groups[3].Value;

            var origRun = new Run(orig)
            {
                Foreground = redBrush,
                TextDecorations = TextDecorations.Strikethrough
            };
            paragraph.Inlines.Add(origRun);

            var arrowRun = new Run(" → ")
            {
                Foreground = secondaryBrush,
                FontWeight = FontWeights.Bold
            };
            paragraph.Inlines.Add(arrowRun);

            var modRun = new Run(mod)
            {
                Foreground = greenBrush,
                FontWeight = FontWeights.SemiBold
            };
            paragraph.Inlines.Add(modRun);

            if (!string.IsNullOrEmpty(rest))
            {
                AppendMarkdownSpans(paragraph, rest, primaryBrush, accentBrush);
            }
            return;
        }

        AppendMarkdownSpans(paragraph, text, primaryBrush, accentBrush);
    }

    private static void AppendMarkdownSpans(
        Paragraph paragraph,
        string text,
        SolidColorBrush primaryBrush,
        SolidColorBrush accentBrush)
    {
        var parts = Regex.Split(text, @"(\*\*.*?\*\*)");
        foreach (var part in parts)
        {
            if (string.IsNullOrEmpty(part)) continue;

            if (part.StartsWith("**") && part.EndsWith("**") && part.Length >= 4)
            {
                var boldText = part.Substring(2, part.Length - 4);
                var boldRun = new Run(boldText)
                {
                    FontWeight = FontWeights.Bold,
                    Foreground = accentBrush
                };
                paragraph.Inlines.Add(boldRun);
            }
            else
            {
                var normalRun = new Run(part)
                {
                    Foreground = primaryBrush
                };
                paragraph.Inlines.Add(normalRun);
            }
        }
    }
}

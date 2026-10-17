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
    private static readonly SolidColorBrush TextPrimaryBrush = new(Color.FromRgb(0xF4, 0xF4, 0xF6));
    private static readonly SolidColorBrush TextSecondaryBrush = new(Color.FromRgb(0xA1, 0xA1, 0xAA));
    private static readonly SolidColorBrush HeaderAccentBrush = new(Color.FromRgb(0x81, 0x8C, 0xF8)); // Indigo
    private static readonly SolidColorBrush GreenBrush = new(Color.FromRgb(0x22, 0xC5, 0x5E));
    private static readonly SolidColorBrush RedBrush = new(Color.FromRgb(0xEF, 0x44, 0x44));

    static MarkdownDocumentRenderer()
    {
        TextPrimaryBrush.Freeze();
        TextSecondaryBrush.Freeze();
        HeaderAccentBrush.Freeze();
        GreenBrush.Freeze();
        RedBrush.Freeze();
    }

    public static FlowDocument Render(string markdownText)
    {
        var doc = new FlowDocument
        {
            PagePadding = new Thickness(0),
            Background = Brushes.Transparent,
            FontFamily = new FontFamily("Segoe UI Variable, Segoe UI"),
            FontSize = 13.0,
            LineHeight = 22.0
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
                    Foreground = HeaderAccentBrush
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
                    Foreground = HeaderAccentBrush
                };
                bulletPara.Inlines.Add(bulletSymbol);

                AppendFormattedInlines(bulletPara, bulletContent);
                doc.Blocks.Add(bulletPara);
                continue;
            }

            // 3. Regular Paragraphs
            var para = new Paragraph
            {
                Margin = new Thickness(0, 0, 0, 6)
            };
            AppendFormattedInlines(para, trimmed);
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

    private static void AppendFormattedInlines(Paragraph paragraph, string text)
    {
        // Handle changes like "old text" → "new text" — explanation
        // Match: "<original>" → "<new>" or "<original>" -> "<new>"
        var arrowMatch = Regex.Match(text, @"^([""“«][^""”»]+[""”»])\s*(?:→|->)\s*([""“«][^""”»]+[""”»])(.*)$");
        if (arrowMatch.Success)
        {
            var orig = arrowMatch.Groups[1].Value;
            var mod = arrowMatch.Groups[2].Value;
            var rest = arrowMatch.Groups[3].Value;

            var origRun = new Run(orig)
            {
                Foreground = RedBrush,
                TextDecorations = TextDecorations.Strikethrough
            };
            paragraph.Inlines.Add(origRun);

            var arrowRun = new Run(" → ")
            {
                Foreground = TextSecondaryBrush,
                FontWeight = FontWeights.Bold
            };
            paragraph.Inlines.Add(arrowRun);

            var modRun = new Run(mod)
            {
                Foreground = GreenBrush,
                FontWeight = FontWeights.SemiBold
            };
            paragraph.Inlines.Add(modRun);

            if (!string.IsNullOrEmpty(rest))
            {
                AppendMarkdownSpans(paragraph, rest);
            }
            return;
        }

        AppendMarkdownSpans(paragraph, text);
    }

    private static void AppendMarkdownSpans(Paragraph paragraph, string text)
    {
        // Parse **bold** markers
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
                    Foreground = HeaderAccentBrush
                };
                paragraph.Inlines.Add(boldRun);
            }
            else
            {
                var normalRun = new Run(part)
                {
                    Foreground = TextPrimaryBrush
                };
                paragraph.Inlines.Add(normalRun);
            }
        }
    }
}

using System;

namespace WritingTools.Models;

public class HistoryItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public AppMode Mode { get; set; }
    public string OriginalText { get; set; } = string.Empty;
    public string? ContextText { get; set; }
    public string ResultText { get; set; } = string.Empty;
    public string StatsText { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;

    public string PreviewOriginal => Truncate(OriginalText, 40);
    public string PreviewResult => Truncate(ResultText, 40);
    public string FormattedTime => Timestamp.ToString("HH:mm:ss");

    private static string Truncate(string text, int max)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var clean = text.Replace("\r", " ").Replace("\n", " ").Trim();
        return clean.Length <= max ? clean : clean.Substring(0, max) + "...";
    }
}

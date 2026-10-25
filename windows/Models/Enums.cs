namespace WritingTools.Models;

public enum AppMode
{
    QuickFix,
    Brainstorm,
    DetailedReview,
    Summarize
}

public enum AppTheme
{
    System,
    Dark,
    Light
}

public enum TonePreset
{
    Standard,
    Friendly,
    Concise,
    Deescalate,
    Formal,
    Rephrase,
    Internal
}

public enum ThinkingOption
{
    Auto,
    Off,
    Low,
    Medium,
    High,
    On
}

public enum DiffTokenType
{
    Unchanged,
    Added,
    Removed
}

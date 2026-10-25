namespace WritingTools.Models;

public enum AppMode
{
    QuickFix,
    DetailedReview,
    Summarize
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

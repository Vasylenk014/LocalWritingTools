using System.Text.Json.Serialization;

namespace WritingTools.Models;

public class FlmModelInfo
{
    [JsonPropertyName("model")]
    public string ModelId { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("installed")]
    public bool Installed { get; set; }

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("footprint")]
    public double Footprint { get; set; }

    [JsonPropertyName("details")]
    public FlmModelDetails? Details { get; set; }

    public bool IsLoaded { get; set; }
    public string FriendlyName { get; set; } = string.Empty;
    public List<string> ReasoningAllowedOptions { get; set; } = new();
    public string? ReasoningDefault { get; set; }

    // Computed display properties
    public string DisplayName
    {
        get
        {
            var title = !string.IsNullOrWhiteSpace(FriendlyName) ? FriendlyName : (string.IsNullOrEmpty(ModelId) ? Name : ModelId);
            if (IsLoaded) return $"● {title}";
            if (!string.IsNullOrWhiteSpace(FriendlyName)) return title;
            return (Installed ? "● " : "⏬ ") + title;
        }
    }

    public bool SupportsThinking =>
        (ReasoningAllowedOptions.Count > 0) ||
        (Details?.Think == true) ||
        (Details?.ThinkToggleable == true) ||
        ModelId.Contains("qwen3", StringComparison.OrdinalIgnoreCase) ||
        ModelId.Contains("qwen-3", StringComparison.OrdinalIgnoreCase) ||
        ModelId.Contains("qwq", StringComparison.OrdinalIgnoreCase) ||
        ModelId.Contains("gemma4", StringComparison.OrdinalIgnoreCase) ||
        ModelId.Contains("gemma-4", StringComparison.OrdinalIgnoreCase) ||
        ModelId.Contains("minicpm", StringComparison.OrdinalIgnoreCase) ||
        ModelId.Contains("gpt-oss", StringComparison.OrdinalIgnoreCase) ||
        ModelId.Contains("deepseek-r1", StringComparison.OrdinalIgnoreCase) ||
        ModelId.Contains("deepseek-reasoner", StringComparison.OrdinalIgnoreCase) ||
        ModelId.Contains("r1", StringComparison.OrdinalIgnoreCase) ||
        ModelId.Contains("reason", StringComparison.OrdinalIgnoreCase) ||
        ModelId.Contains("think", StringComparison.OrdinalIgnoreCase) ||
        ModelId.Contains("thought", StringComparison.OrdinalIgnoreCase);
}

public class FlmModelDetails
{
    [JsonPropertyName("family")]
    public string? Family { get; set; }

    [JsonPropertyName("format")]
    public string? Format { get; set; }

    [JsonPropertyName("parameter_size")]
    public string? ParameterSize { get; set; }

    [JsonPropertyName("quantization_level")]
    public string? QuantizationLevel { get; set; }

    [JsonPropertyName("think")]
    public bool? Think { get; set; }

    [JsonPropertyName("think_toggleable")]
    public bool? ThinkToggleable { get; set; }
}

public class FlmListResponse
{
    [JsonPropertyName("models")]
    public List<FlmModelInfo> Models { get; set; } = new();
}

public class OpenAiModelsResponse
{
    [JsonPropertyName("data")]
    public List<OpenAiModelItem> Data { get; set; } = new();
}

public class OpenAiModelItem
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("object")]
    public string Object { get; set; } = string.Empty;

    [JsonPropertyName("owned_by")]
    public string OwnedBy { get; set; } = string.Empty;
}

public class LmStudioModelsResponse
{
    [JsonPropertyName("models")]
    public List<LmStudioModelItem>? Models { get; set; }
}

public class LmStudioModelItem
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;

    [JsonPropertyName("display_name")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("loaded_instances")]
    public List<LmStudioLoadedInstance>? LoadedInstances { get; set; }

    [JsonPropertyName("capabilities")]
    public LmStudioCapabilities? Capabilities { get; set; }
}

public class LmStudioLoadedInstance
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
}

public class LmStudioCapabilities
{
    [JsonPropertyName("reasoning")]
    public LmStudioReasoning? Reasoning { get; set; }
}

public class LmStudioReasoning
{
    [JsonPropertyName("allowed_options")]
    public List<string>? AllowedOptions { get; set; }

    [JsonPropertyName("default")]
    public string? Default { get; set; }
}

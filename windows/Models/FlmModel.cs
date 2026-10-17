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

    // Computed display properties
    public string DisplayName => (Installed ? "● " : "⏬ ") + (string.IsNullOrEmpty(ModelId) ? Name : ModelId);

    public bool SupportsThinking =>
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

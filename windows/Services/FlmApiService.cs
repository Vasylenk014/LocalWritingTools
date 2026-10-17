using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WritingTools.Models;

namespace WritingTools.Services;

public class GenerationStats
{
    public double SpeedTps { get; set; }
    public double TtftSeconds { get; set; }
    public int CompletionTokens { get; set; }
    public int TotalTokens { get; set; }
    public TimeSpan TotalDuration { get; set; }

    public string FormatSummary()
    {
        var sb = new StringBuilder();
        if (SpeedTps > 0)
        {
            sb.Append($"⚡ {SpeedTps:F1} tok/s");
        }
        if (TtftSeconds > 0)
        {
            if (sb.Length > 0) sb.Append(" | ");
            sb.Append($"TTFT: {TtftSeconds:F2}s");
        }
        else if (TotalDuration.TotalSeconds > 0)
        {
            if (sb.Length > 0) sb.Append(" | ");
            sb.Append($"{TotalDuration.TotalSeconds:F1}s");
        }
        if (CompletionTokens > 0)
        {
            if (sb.Length > 0) sb.Append(" | ");
            sb.Append($"{CompletionTokens} tokens");
        }
        return sb.ToString();
    }
}

public class StreamChunkEventArgs : EventArgs
{
    public string Content { get; set; } = string.Empty;
    public string ReasoningContent { get; set; } = string.Empty;
    public bool IsThinking { get; set; }
    public int ThinkingCharCount { get; set; }
    public GenerationStats? Stats { get; set; }
    public bool IsDone { get; set; }
}

public enum ServerState
{
    Offline,
    Starting,
    Online
}

public class FlmApiService
{
    public const string DefaultBaseUrl = "http://127.0.0.1:52625";
    private readonly HttpClient _httpClient;
    private Process? _serverProcess;

    private string _currentBaseUrl = DefaultBaseUrl;
    private LlmProvider _currentProvider = LlmProvider.FastFlowLmNpu;
    private string _apiKey = "flm";

    public ServerState CurrentState { get; private set; } = ServerState.Offline;
    public string? ActiveModel { get; private set; }
    public event EventHandler<ServerState>? StateChanged;

    public LlmProvider CurrentProvider => _currentProvider;
    public string CurrentBaseUrl => _currentBaseUrl;
    public bool IsServerRunningLocally => _serverProcess != null && !_serverProcess.HasExited;

    public FlmApiService()
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(5)
        };
    }

    public void ConfigureProvider(LlmProvider provider, string baseUrl, string? apiKey = null)
    {
        _currentProvider = provider;
        _currentBaseUrl = string.IsNullOrWhiteSpace(baseUrl) ? GetDefaultBaseUrl(provider) : baseUrl.TrimEnd('/');
        _apiKey = string.IsNullOrWhiteSpace(apiKey) ? (provider == LlmProvider.FastFlowLmNpu ? "flm" : "") : apiKey;
    }

    public static string GetDefaultBaseUrl(LlmProvider provider) => provider switch
    {
        LlmProvider.LmStudio => "http://localhost:1234",
        LlmProvider.Custom => "http://localhost:11434",
        _ => "http://127.0.0.1:52625"
    };

    public static async Task<(bool Success, int ModelCount, string Message)> TestConnectionAsync(string baseUrl, string? apiKey = null)
    {
        try
        {
            var url = string.IsNullOrWhiteSpace(baseUrl) ? "http://localhost:1234" : baseUrl.TrimEnd('/');
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{url}/v1/models");
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            }

            var res = await client.SendAsync(req);
            if (!res.IsSuccessStatusCode)
            {
                return (false, 0, $"HTTP {(int)res.StatusCode} {res.ReasonPhrase}");
            }

            var json = await res.Content.ReadAsStringAsync();
            var openAiList = JsonSerializer.Deserialize<OpenAiModelsResponse>(json);
            int count = openAiList?.Data?.Count ?? 0;
            return (true, count, $"✓ Connected! Found {count} model(s).");
        }
        catch (Exception ex)
        {
            return (false, 0, $"Connection failed: {ex.Message}");
        }
    }

    public static void KillAllFlmProcesses()
    {
        try
        {
            var processes = Process.GetProcessesByName("flm");
            foreach (var proc in processes)
            {
                try
                {
                    proc.Kill(true);
                    proc.WaitForExit(1000);
                }
                catch { }
                finally
                {
                    proc.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error killing flm processes: {ex.Message}");
        }
    }

    private DateTime _lastModelDetect = DateTime.MinValue;

    public string? DetectRunningModel(bool force = false)
    {
        if (!force && !string.IsNullOrEmpty(ActiveModel) && (DateTime.UtcNow - _lastModelDetect).TotalSeconds < 15)
        {
            return ActiveModel;
        }

        try
        {
            _lastModelDetect = DateTime.UtcNow;
            using var searcher = new System.Management.ManagementObjectSearcher(
                "SELECT CommandLine FROM Win32_Process WHERE Name = 'flm.exe'");
            foreach (var obj in searcher.Get())
            {
                var cmd = obj["CommandLine"]?.ToString();
                if (!string.IsNullOrEmpty(cmd))
                {
                    var match = System.Text.RegularExpressions.Regex.Match(cmd, @"serve\s+([^\s]+)");
                    if (match.Success)
                    {
                        var model = match.Groups[1].Value.Trim('"', '\'');
                        ActiveModel = model;
                        return model;
                    }
                }
            }
        }
        catch { }
        return ActiveModel;
    }

    public async Task<bool> CheckServerHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromMilliseconds(1500));

            using var req = new HttpRequestMessage(HttpMethod.Get, $"{_currentBaseUrl}/v1/models");
            if (!string.IsNullOrEmpty(_apiKey))
            {
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            }

            var response = await _httpClient.SendAsync(req, cts.Token);
            bool isHealthy = response.IsSuccessStatusCode;
            if (isHealthy)
            {
                if (_currentProvider == LlmProvider.FastFlowLmNpu)
                {
                    DetectRunningModel();
                }
                else
                {
                    if (string.IsNullOrEmpty(ActiveModel))
                    {
                        ActiveModel = _currentProvider == LlmProvider.LmStudio ? "LM Studio" : "Local Server";
                    }
                }

                if (CurrentState != ServerState.Online)
                {
                    SetState(ServerState.Online);
                }
            }
            else
            {
                ActiveModel = null;
                if (CurrentState == ServerState.Online)
                {
                    SetState(ServerState.Offline);
                }
            }
            return isHealthy;
        }
        catch
        {
            ActiveModel = null;
            if (CurrentState == ServerState.Online)
            {
                SetState(ServerState.Offline);
            }
            return false;
        }
    }

    public async Task<bool> StartServerAsync(string modelTag = "gemma4-it:e4b", IProgress<string>? progress = null)
    {
        if (_currentProvider != LlmProvider.FastFlowLmNpu)
        {
            progress?.Report($"Checking connection to {_currentProvider} at {_currentBaseUrl}...");
            bool isOnline = await CheckServerHealthAsync();
            if (isOnline)
            {
                progress?.Report($"✓ Connected to {_currentProvider} ({_currentBaseUrl})");
                return true;
            }
            else
            {
                progress?.Report($"⚠ Cannot connect to {_currentBaseUrl}. Make sure Local Server is started.");
                return false;
            }
        }

        if (await CheckServerHealthAsync())
        {
            var running = DetectRunningModel();
            if (string.Equals(running, modelTag, StringComparison.OrdinalIgnoreCase))
            {
                ActiveModel = modelTag;
                SetState(ServerState.Online);
                progress?.Report($"Server is already running with {modelTag}.");
                return true;
            }

            // A different model is running on NPU. Seamlessly switch on-the-fly!
            progress?.Report($"Switching model: stopping '{running ?? "current"}' to load '{modelTag}'...");
            StopServer();
            await Task.Delay(800);
        }

        SetState(ServerState.Starting);
        progress?.Report("Cleaning up previous server processes...");

        // Ensure no stale processes occupy port 52625
        StopServer();
        SetState(ServerState.Starting);

        await Task.Delay(500);

        try
        {
            progress?.Report($"Starting FLM with {modelTag}...");
            var startInfo = new ProcessStartInfo
            {
                FileName = "flm",
                Arguments = $"serve {modelTag} --port 52625 --pmode performance",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            _serverProcess = Process.Start(startInfo);
            if (_serverProcess == null)
            {
                SetState(ServerState.Offline);
                progress?.Report("Failed to launch flm process");
                return false;
            }

            _serverProcess.OutputDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) Debug.WriteLine($"[FLM OUT] {e.Data}"); };
            _serverProcess.ErrorDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) Debug.WriteLine($"[FLM ERR] {e.Data}"); };
            _serverProcess.BeginOutputReadLine();
            _serverProcess.BeginErrorReadLine();

            progress?.Report($"Loading '{modelTag}' weights to NPU (~15-20s)...");

            // Wait up to 35 seconds (70 * 500ms) for server to respond
            for (int i = 0; i < 70; i++)
            {
                await Task.Delay(500);
                if (_serverProcess.HasExited)
                {
                    Debug.WriteLine($"FLM process exited prematurely with code {_serverProcess.ExitCode}");
                    SetState(ServerState.Offline);
                    progress?.Report($"Server process stopped unexpectedly (exit code {_serverProcess.ExitCode})");
                    return false;
                }

                if (await CheckServerHealthAsync())
                {
                    ActiveModel = modelTag;
                    SetState(ServerState.Online);
                    progress?.Report($"FastFlowLM server is online with {modelTag}!");
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to start FLM server: {ex.Message}");
            progress?.Report($"Error starting server: {ex.Message}");
        }

        SetState(ServerState.Offline);
        progress?.Report("Timed out waiting for server to start.");
        return false;
    }

    public void StopServer()
    {
        try
        {
            if (_serverProcess != null && !_serverProcess.HasExited)
            {
                _serverProcess.Kill(true);
                _serverProcess.Dispose();
                _serverProcess = null;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to stop FLM server: {ex.Message}");
        }

        KillAllFlmProcesses();
        ActiveModel = null;
        SetState(ServerState.Offline);
    }

    public async Task<bool> StopServerAsync(IProgress<string>? progress = null)
    {
        if (_currentProvider != LlmProvider.FastFlowLmNpu)
        {
            progress?.Report($"{_currentProvider} server is managed externally. Use the Stop button in LM Studio.");
            return true;
        }

        progress?.Report("Stopping server and releasing NPU memory...");
        StopServer();

        // Verify all flm processes and port 52625 are shut down
        for (int i = 0; i < 15; i++)
        {
            await Task.Delay(200);
            var procs = Process.GetProcessesByName("flm");
            if (procs.Length == 0 && !await CheckServerHealthAsync())
            {
                ActiveModel = null;
                SetState(ServerState.Offline);
                progress?.Report("FastFlowLM server stopped successfully. NPU memory released.");
                return true;
            }
        }

        bool stillHealthy = await CheckServerHealthAsync();
        if (!stillHealthy)
        {
            ActiveModel = null;
            SetState(ServerState.Offline);
            progress?.Report("FastFlowLM server stopped successfully. NPU memory released.");
            return true;
        }

        progress?.Report("Warning: Server may not have stopped completely.");
        return false;
    }

    private void SetState(ServerState state)
    {
        if (CurrentState != state)
        {
            CurrentState = state;
            StateChanged?.Invoke(this, state);
        }
    }

    public async Task<List<FlmModelInfo>> GetModelsAsync(CancellationToken cancellationToken = default)
    {
        if (_currentProvider == LlmProvider.FastFlowLmNpu)
        {
            // First, attempt to get models via `flm list --json`
            var modelsFromCli = await GetModelsFromCliAsync();
            if (modelsFromCli != null && modelsFromCli.Count > 0)
            {
                return FilterAndSortModels(modelsFromCli);
            }
        }

        // Query GET /v1/models from server (standard for LM Studio, FastFlowLM, Ollama, etc.)
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{_currentBaseUrl}/v1/models");
            if (!string.IsNullOrEmpty(_apiKey))
            {
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            }
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(3));

            var response = await _httpClient.SendAsync(req, cts.Token);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                var openAiList = JsonSerializer.Deserialize<OpenAiModelsResponse>(json);
                if (openAiList?.Data != null)
                {
                    var list = openAiList.Data.Select(d => new FlmModelInfo
                    {
                        ModelId = d.Id,
                        Name = d.Id,
                        Installed = true // if listed on running server, consider ready
                    }).ToList();

                    return FilterAndSortModels(list);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to query /v1/models: {ex.Message}");
        }

        return new List<FlmModelInfo>();
    }

    private static List<FlmModelInfo> FilterAndSortModels(List<FlmModelInfo> models)
    {
        // Filter service / non-chat models
        var filtered = models.Where(m =>
        {
            var id = m.ModelId.ToLowerInvariant();
            return !id.StartsWith("embed-gemma") &&
                   !id.StartsWith("whisper-") &&
                   !id.StartsWith("smolvla");
        }).ToList();

        // Installed models on top, then alphabetical
        return filtered
            .OrderByDescending(m => m.Installed)
            .ThenBy(m => m.ModelId)
            .ToList();
    }

    private static async Task<List<FlmModelInfo>?> GetModelsFromCliAsync()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "flm",
                Arguments = "list --json",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return null;

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var exited = await Task.Run(() => process.WaitForExit(4000));
            if (!exited)
            {
                try { process.Kill(); } catch { }
                return null;
            }

            var output = await outputTask;
            if (string.IsNullOrWhiteSpace(output)) return null;

            // Find JSON start
            var jsonStart = output.IndexOf('{');
            if (jsonStart >= 0)
            {
                var json = output.Substring(jsonStart);
                var response = JsonSerializer.Deserialize<FlmListResponse>(json);
                return response?.Models;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to run flm list --json: {ex.Message}");
        }

        return null;
    }

    public async Task StreamCompletionAsync(
        string model,
        string systemPrompt,
        string userPrompt,
        ThinkingOption thinkingOption,
        IProgress<StreamChunkEventArgs> progress,
        CancellationToken cancellationToken)
    {
        var messages = new List<object>
        {
            new { role = "system", content = systemPrompt },
            new { role = "user", content = userPrompt }
        };

        var requestObj = new Dictionary<string, object>
        {
            ["model"] = model,
            ["messages"] = messages,
            ["temperature"] = 0.2,
            ["stream"] = true,
            ["stream_options"] = new { include_usage = true }
        };

        // Thinking parameter mapping according to PRD
        switch (thinkingOption)
        {
            case ThinkingOption.Off:
                requestObj["think"] = false;
                break;
            case ThinkingOption.On:
                requestObj["think"] = true;
                break;
            case ThinkingOption.Low:
                requestObj["think"] = true;
                requestObj["reasoning_effort"] = "low";
                break;
            case ThinkingOption.Medium:
                requestObj["think"] = true;
                requestObj["reasoning_effort"] = "medium";
                break;
            case ThinkingOption.High:
                requestObj["think"] = true;
                requestObj["reasoning_effort"] = "high";
                break;
            case ThinkingOption.Auto:
            default:
                // Omit think parameter
                break;
        }

        var jsonBody = JsonSerializer.Serialize(requestObj);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_currentBaseUrl}/v1/chat/completions")
        {
            Content = new StringContent(jsonBody, Encoding.UTF8, "application/json")
        };
        if (!string.IsNullOrEmpty(_apiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        }

        var stopwatch = Stopwatch.StartNew();
        double ttft = 0;
        int tokenCount = 0;
        int thinkingChars = 0;
        bool inThinkTag = false;

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        GenerationStats? finalStats = null;

        while (!reader.EndOfStream)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(line)) continue;

            // Detect and throw FastFlowLM error payloads (e.g. {"error":"..."})
            if (line.TrimStart().StartsWith("{\"error\":", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    using var errDoc = JsonDocument.Parse(line);
                    if (errDoc.RootElement.TryGetProperty("error", out var errMsgEl))
                    {
                        var errMsg = errMsgEl.GetString() ?? "Unknown FastFlowLM server error";
                        throw new InvalidOperationException($"FastFlowLM Error: {errMsg}");
                    }
                }
                catch (JsonException) { }
                throw new InvalidOperationException($"FastFlowLM Error: {line}");
            }

            if (line.StartsWith("data: "))
            {
                var payload = line.Substring(6).Trim();
                if (payload == "[DONE]")
                {
                    break;
                }

                try
                {
                    using var doc = JsonDocument.Parse(payload);
                    var root = doc.RootElement;

                    // Parse usage if present
                    if (root.TryGetProperty("usage", out var usageEl) && usageEl.ValueKind == JsonValueKind.Object)
                    {
                        finalStats ??= new GenerationStats();
                        if (usageEl.TryGetProperty("decoding_speed_tps", out var speedEl) && speedEl.TryGetDouble(out var speed))
                        {
                            finalStats.SpeedTps = speed;
                        }
                        if (usageEl.TryGetProperty("prefill_duration_ttft", out var ttftEl) && ttftEl.TryGetDouble(out var ttftVal))
                        {
                            finalStats.TtftSeconds = ttftVal;
                        }
                        if (usageEl.TryGetProperty("completion_tokens", out var compTokensEl) && compTokensEl.TryGetInt32(out var compTokens))
                        {
                            finalStats.CompletionTokens = compTokens;
                        }
                        if (usageEl.TryGetProperty("total_tokens", out var totTokensEl) && totTokensEl.TryGetInt32(out var totTokens))
                        {
                            finalStats.TotalTokens = totTokens;
                        }
                    }

                    // Parse delta
                    if (root.TryGetProperty("choices", out var choicesEl) && choicesEl.GetArrayLength() > 0)
                    {
                        var choice = choicesEl[0];
                        if (choice.TryGetProperty("delta", out var deltaEl))
                        {
                            string? content = null;
                            string? reasoning = null;

                            if (deltaEl.TryGetProperty("reasoning_content", out var reasoningEl) &&
                                reasoningEl.ValueKind == JsonValueKind.String)
                            {
                                reasoning = reasoningEl.GetString();
                            }

                            if (deltaEl.TryGetProperty("content", out var contentEl) &&
                                contentEl.ValueKind == JsonValueKind.String)
                            {
                                content = contentEl.GetString();
                            }

                            // Check TTFT on first token
                            if (ttft == 0 && (!string.IsNullOrEmpty(content) || !string.IsNullOrEmpty(reasoning)))
                            {
                                ttft = stopwatch.Elapsed.TotalSeconds;
                            }

                            if (!string.IsNullOrEmpty(reasoning))
                            {
                                thinkingChars += reasoning.Length;
                                progress.Report(new StreamChunkEventArgs
                                {
                                    ReasoningContent = reasoning,
                                    IsThinking = true,
                                    ThinkingCharCount = thinkingChars
                                });
                            }
                            else if (!string.IsNullOrEmpty(content))
                            {
                                // Detect <think> tags in standard content
                                if (content.Contains("<think>"))
                                {
                                    inThinkTag = true;
                                }

                                if (inThinkTag)
                                {
                                    thinkingChars += content.Length;
                                    progress.Report(new StreamChunkEventArgs
                                    {
                                        ReasoningContent = content,
                                        IsThinking = true,
                                        ThinkingCharCount = thinkingChars
                                    });

                                    if (content.Contains("</think>"))
                                    {
                                        inThinkTag = false;
                                    }
                                }
                                else
                                {
                                    tokenCount++;
                                    progress.Report(new StreamChunkEventArgs
                                    {
                                        Content = content,
                                        IsThinking = false
                                    });
                                }
                            }
                        }
                    }
                }
                catch (JsonException)
                {
                    // Ignore non-json or fragmented lines
                }
            }
        }

        stopwatch.Stop();

        // Assemble final statistics
        finalStats ??= new GenerationStats();
        finalStats.TotalDuration = stopwatch.Elapsed;
        if (finalStats.TtftSeconds <= 0 && ttft > 0)
        {
            finalStats.TtftSeconds = ttft;
        }
        if (finalStats.CompletionTokens <= 0)
        {
            finalStats.CompletionTokens = tokenCount;
        }
        if (finalStats.SpeedTps <= 0 && tokenCount > 0 && stopwatch.Elapsed.TotalSeconds > 0)
        {
            var genDuration = Math.Max(0.1, stopwatch.Elapsed.TotalSeconds - finalStats.TtftSeconds);
            finalStats.SpeedTps = tokenCount / genDuration;
        }

        progress.Report(new StreamChunkEventArgs
        {
            IsDone = true,
            Stats = finalStats
        });
    }
}

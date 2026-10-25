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
    public string? StatusMessage { get; set; }
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
    private readonly Dictionary<string, LmStudioModelItem> _lmStudioModelCache = new(StringComparer.OrdinalIgnoreCase);

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
        if (_currentProvider == LlmProvider.LmStudio)
        {
            progress?.Report("Unloading models from LM Studio and freeing GPU memory...");
            bool ok = await UnloadAllLmStudioModelsAsync(null, progress);
            if (ok)
            {
                progress?.Report("LM Studio models unloaded. GPU memory freed.");
                return true;
            }
            progress?.Report("Failed to unload all LM Studio models.");
            return false;
        }

        if (_currentProvider != LlmProvider.FastFlowLmNpu)
        {
            progress?.Report($"{_currentProvider} server is managed externally.");
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

    public async Task<List<LmStudioModelItem>?> GetLmStudioModelsRawAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{_currentBaseUrl}/api/v1/models");
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
                var lmStudioList = JsonSerializer.Deserialize<LmStudioModelsResponse>(json);
                if (lmStudioList?.Models != null)
                {
                    lock (_lmStudioModelCache)
                    {
                        _lmStudioModelCache.Clear();
                        foreach (var m in lmStudioList.Models)
                        {
                            if (!string.IsNullOrEmpty(m.Key))
                            {
                                _lmStudioModelCache[m.Key] = m;
                            }
                        }
                    }
                }
                return lmStudioList?.Models;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to query /api/v1/models: {ex.Message}");
        }
        return null;
    }

    public LmStudioModelItem? GetCachedLmStudioModel(string? modelKey)
    {
        if (string.IsNullOrWhiteSpace(modelKey)) return null;
        lock (_lmStudioModelCache)
        {
            if (_lmStudioModelCache.TryGetValue(modelKey, out var item))
            {
                return item;
            }
            return _lmStudioModelCache.Values.FirstOrDefault(m =>
                string.Equals(m.Key, modelKey, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(m.DisplayName, modelKey, StringComparison.OrdinalIgnoreCase) ||
                (m.Key != null && m.Key.EndsWith("/" + modelKey, StringComparison.OrdinalIgnoreCase)));
        }
    }

    public async Task<bool> UnloadLmStudioModelInstanceAsync(string instanceId, CancellationToken cancellationToken = default)
    {
        try
        {
            var body = JsonSerializer.Serialize(new { instance_id = instanceId });
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{_currentBaseUrl}/api/v1/models/unload")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            if (!string.IsNullOrEmpty(_apiKey))
            {
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            }
            using var response = await _httpClient.SendAsync(req, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to unload model instance '{instanceId}': {ex.Message}");
            return false;
        }
    }

    public async Task<bool> UnloadAllLmStudioModelsAsync(string? exceptModelKey = null, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var rawModels = await GetLmStudioModelsRawAsync(cancellationToken);
        if (rawModels == null) return true;

        var instancesToUnload = new List<(string modelName, string instanceId)>();
        foreach (var m in rawModels)
        {
            if (exceptModelKey != null && string.Equals(m.Key, exceptModelKey, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (m.LoadedInstances != null)
            {
                foreach (var inst in m.LoadedInstances)
                {
                    if (!string.IsNullOrWhiteSpace(inst.Id))
                    {
                        instancesToUnload.Add((m.DisplayName ?? m.Key, inst.Id));
                    }
                }
            }
        }

        if (instancesToUnload.Count == 0)
        {
            return true;
        }

        foreach (var (modelName, instId) in instancesToUnload)
        {
            progress?.Report($"Unloading '{modelName}' from GPU memory...");
            await UnloadLmStudioModelInstanceAsync(instId, cancellationToken);
        }

        // Poll /api/v1/models until instances are unloaded (up to 8s)
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 8000)
        {
            await Task.Delay(250, cancellationToken);
            var updated = await GetLmStudioModelsRawAsync(cancellationToken);
            if (updated == null) break;

            bool stillLoaded = false;
            foreach (var m in updated)
            {
                if (exceptModelKey != null && string.Equals(m.Key, exceptModelKey, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (m.LoadedInstances != null && m.LoadedInstances.Any(i => instancesToUnload.Any(u => u.instanceId == i.Id)))
                {
                    stillLoaded = true;
                    break;
                }
            }

            if (!stillLoaded)
            {
                break;
            }
        }

        // Wait a grace period for CUDA / GPU driver memory manager to finish reclaiming VRAM pages
        progress?.Report("Waiting for GPU VRAM to clear completely...");
        await Task.Delay(800, cancellationToken);
        progress?.Report("GPU memory cleared.");
        return true;
    }

    public async Task<bool> LoadLmStudioModelAsync(string modelKey, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        try
        {
            progress?.Report($"Loading '{modelKey}' into GPU memory (~5-10s)...");
            var body = JsonSerializer.Serialize(new { model = modelKey });
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{_currentBaseUrl}/api/v1/models/load")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            if (!string.IsNullOrEmpty(_apiKey))
            {
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            }
            using var response = await _httpClient.SendAsync(req, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                await Task.Delay(500, cancellationToken);
                progress?.Report($"Model '{modelKey}' ready in GPU memory.");
                return true;
            }
            else
            {
                var err = await response.Content.ReadAsStringAsync(cancellationToken);
                Debug.WriteLine($"Failed to load model '{modelKey}': HTTP {(int)response.StatusCode} {err}");
                return false;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to load model '{modelKey}': {ex.Message}");
            return false;
        }
    }

    public async Task<bool> EnsureLmStudioModelReadyAsync(string targetModelKey, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var rawModels = await GetLmStudioModelsRawAsync(cancellationToken);
        if (rawModels == null) return false;

        var targetModel = rawModels.FirstOrDefault(m => string.Equals(m.Key, targetModelKey, StringComparison.OrdinalIgnoreCase));
        bool isTargetLoaded = targetModel?.LoadedInstances != null && targetModel.LoadedInstances.Count > 0;

        // Check if any other models are loaded
        bool otherModelsLoaded = rawModels.Any(m =>
            !string.Equals(m.Key, targetModelKey, StringComparison.OrdinalIgnoreCase) &&
            m.LoadedInstances != null && m.LoadedInstances.Count > 0);

        if (otherModelsLoaded)
        {
            // Unload all other models and wait for VRAM to clear completely
            await UnloadAllLmStudioModelsAsync(exceptModelKey: targetModelKey, progress, cancellationToken);
        }

        // If target is not loaded, load it explicitly into VRAM now
        if (!isTargetLoaded)
        {
            bool loaded = await LoadLmStudioModelAsync(targetModelKey, progress, cancellationToken);
            return loaded;
        }

        progress?.Report($"Model '{targetModelKey}' is ready in GPU VRAM.");
        return true;
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

        // For LM Studio, query native /api/v1/models to detect VRAM loaded status and reasoning capabilities
        if (_currentProvider == LlmProvider.LmStudio)
        {
            var rawModels = await GetLmStudioModelsRawAsync(cancellationToken);
            if (rawModels != null && rawModels.Count > 0)
            {
                var list = rawModels
                    .Where(m => string.Equals(m.Type, "llm", StringComparison.OrdinalIgnoreCase))
                    .Select(m =>
                    {
                        bool isLoaded = m.LoadedInstances != null && m.LoadedInstances.Count > 0;
                        return new FlmModelInfo
                        {
                            ModelId = m.Key,
                            Name = m.DisplayName ?? m.Key,
                            FriendlyName = m.DisplayName ?? m.Key,
                            Installed = isLoaded,
                            IsLoaded = isLoaded,
                            ReasoningAllowedOptions = m.Capabilities?.Reasoning?.AllowedOptions ?? new(),
                            ReasoningDefault = m.Capabilities?.Reasoning?.Default
                        };
                    }).ToList();

                return FilterAndSortModels(list);
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

        // Loaded in VRAM first, then installed on disk, then alphabetical
        return filtered
            .OrderByDescending(m => m.IsLoaded)
            .ThenByDescending(m => m.Installed)
            .ThenBy(m => m.DisplayName)
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
        CancellationToken cancellationToken,
        double temperature = 0.2)
    {
        if (_currentProvider == LlmProvider.LmStudio)
        {
            try
            {
                await StreamLmStudioNativeAsync(model, systemPrompt, userPrompt, thinkingOption, progress, cancellationToken, temperature);
                return;
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                Debug.WriteLine("/api/v1/chat not found, falling back to /v1/chat/completions");
            }
        }

        await StreamOpenAiCompatibleAsync(model, systemPrompt, userPrompt, thinkingOption, progress, cancellationToken, temperature);
    }

    private async Task StreamOpenAiCompatibleAsync(
        string model,
        string systemPrompt,
        string userPrompt,
        ThinkingOption thinkingOption,
        IProgress<StreamChunkEventArgs> progress,
        CancellationToken cancellationToken,
        double temperature = 0.2)
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
            ["temperature"] = temperature,
            ["max_tokens"] = 2048,
            ["stream"] = true,
            ["stream_options"] = new { include_usage = true }
        };

        // Thinking parameter mapping according to PRD & LM Studio / OpenAI standard
        switch (thinkingOption)
        {
            case ThinkingOption.Off:
                requestObj["think"] = false;
                requestObj["chat_template_kwargs"] = new Dictionary<string, object> { ["thinking"] = false };
                requestObj["reasoning_effort"] = "none";
                break;
            case ThinkingOption.On:
                requestObj["think"] = true;
                requestObj["chat_template_kwargs"] = new Dictionary<string, object> { ["thinking"] = true };
                requestObj["reasoning_effort"] = "low";
                break;
            case ThinkingOption.Low:
                requestObj["think"] = true;
                requestObj["chat_template_kwargs"] = new Dictionary<string, object> { ["thinking"] = true };
                requestObj["reasoning_effort"] = "low";
                break;
            case ThinkingOption.Medium:
                requestObj["think"] = true;
                requestObj["chat_template_kwargs"] = new Dictionary<string, object> { ["thinking"] = true };
                requestObj["reasoning_effort"] = "medium";
                break;
            case ThinkingOption.High:
                requestObj["think"] = true;
                requestObj["chat_template_kwargs"] = new Dictionary<string, object> { ["thinking"] = true };
                requestObj["reasoning_effort"] = "high";
                break;
            case ThinkingOption.Auto:
            default:
                // Omit think parameter to let model decide
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

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        GenerationStats? finalStats = null;

        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) != null)
        {
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
                                // Detect <think>, <thought>, <reasoning> tags in standard content
                                if (content.Contains("<think>", StringComparison.OrdinalIgnoreCase) ||
                                    content.Contains("<thought>", StringComparison.OrdinalIgnoreCase) ||
                                    content.Contains("<reasoning>", StringComparison.OrdinalIgnoreCase))
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

                                    if (content.Contains("</think>", StringComparison.OrdinalIgnoreCase) ||
                                        content.Contains("</thought>", StringComparison.OrdinalIgnoreCase) ||
                                        content.Contains("</reasoning>", StringComparison.OrdinalIgnoreCase))
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

    private async Task StreamLmStudioNativeAsync(
        string model,
        string systemPrompt,
        string userPrompt,
        ThinkingOption thinkingOption,
        IProgress<StreamChunkEventArgs> progress,
        CancellationToken cancellationToken,
        double temperature = 0.2)
    {
        var requestObj = new Dictionary<string, object>
        {
            ["model"] = model,
            ["system_prompt"] = systemPrompt,
            ["input"] = userPrompt,
            ["temperature"] = temperature,
            ["stream"] = true,
            ["store"] = false
        };

        var cachedModel = GetCachedLmStudioModel(model);
        if (cachedModel == null)
        {
            try
            {
                await GetLmStudioModelsRawAsync(cancellationToken).ConfigureAwait(false);
                cachedModel = GetCachedLmStudioModel(model);
            }
            catch { }
        }

        var allowed = cachedModel?.Capabilities?.Reasoning?.AllowedOptions;
        bool hasAllowedOptions = allowed != null && allowed.Count > 0;
        bool allowsOff = !hasAllowedOptions || allowed!.Contains("off", StringComparer.OrdinalIgnoreCase);
        bool allowsOn = !hasAllowedOptions || allowed!.Contains("on", StringComparer.OrdinalIgnoreCase);

        if (thinkingOption == ThinkingOption.Off)
        {
            if (allowsOff)
            {
                requestObj["reasoning"] = "off";
            }
            // For models requiring reasoning (e.g. gpt-oss-20b only supports low/medium/high),
            // omitting "reasoning" defaults to the model's native level and avoids HTTP 400.
        }
        else if (thinkingOption == ThinkingOption.On)
        {
            if (allowsOn)
            {
                requestObj["reasoning"] = "on";
            }
            else
            {
                // Fall back to default level (e.g. "low") for models with tiered reasoning
                var def = cachedModel?.Capabilities?.Reasoning?.Default;
                if (!string.IsNullOrWhiteSpace(def) && allowed!.Contains(def, StringComparer.OrdinalIgnoreCase))
                {
                    requestObj["reasoning"] = def;
                }
                else if (allowed != null && allowed.Count > 0)
                {
                    requestObj["reasoning"] = allowed[0];
                }
            }
        }
        else if (thinkingOption == ThinkingOption.Low)
        {
            requestObj["reasoning"] = "low";
        }
        else if (thinkingOption == ThinkingOption.Medium)
        {
            requestObj["reasoning"] = "medium";
        }
        else if (thinkingOption == ThinkingOption.High)
        {
            requestObj["reasoning"] = "high";
        }
        // If ThinkingOption.Auto, omit "reasoning" so model uses its configured default

        var jsonBody = JsonSerializer.Serialize(requestObj);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_currentBaseUrl}/api/v1/chat")
        {
            Content = new StringContent(jsonBody, Encoding.UTF8, "application/json")
        };
        if (!string.IsNullOrEmpty(_apiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        }

        var stopwatch = Stopwatch.StartNew();
        int thinkingChars = 0;
        int tokenCount = 0;
        double ttft = 0;
        GenerationStats? finalStats = null;

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var errBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                using var errDoc = JsonDocument.Parse(errBody);
                if (errDoc.RootElement.TryGetProperty("error", out var errObj) &&
                    errObj.TryGetProperty("message", out var msgEl))
                {
                    throw new InvalidOperationException($"LM Studio Error: {msgEl.GetString()}");
                }
            }
            catch (JsonException) { }
            throw new InvalidOperationException($"LM Studio HTTP {(int)response.StatusCode}: {errBody}");
        }

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            if (line.StartsWith("data: "))
            {
                var payload = line.Substring(6).Trim();
                if (payload == "[DONE]") break;

                try
                {
                    using var doc = JsonDocument.Parse(payload);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("type", out var typeEl))
                    {
                        var eventType = typeEl.GetString();
                        switch (eventType)
                        {
                            case "model_load.start":
                                progress.Report(new StreamChunkEventArgs { StatusMessage = "Loading model into VRAM..." });
                                break;
                            case "model_load.progress":
                                double prog = root.TryGetProperty("progress", out var pEl) ? pEl.GetDouble() : 0;
                                progress.Report(new StreamChunkEventArgs { StatusMessage = $"Loading model into VRAM... {(int)(prog * 100)}%" });
                                break;
                            case "prompt_processing.start":
                                progress.Report(new StreamChunkEventArgs { StatusMessage = "Processing prompt..." });
                                break;
                            case "reasoning.start":
                                progress.Report(new StreamChunkEventArgs { IsThinking = true, StatusMessage = "Thinking..." });
                                break;
                            case "reasoning.delta":
                                var rContent = root.TryGetProperty("content", out var rcEl) ? rcEl.GetString() ?? "" : "";
                                thinkingChars += rContent.Length;
                                progress.Report(new StreamChunkEventArgs
                                {
                                    IsThinking = true,
                                    ThinkingCharCount = thinkingChars,
                                    ReasoningContent = rContent,
                                    StatusMessage = $"Thinking... ({thinkingChars} chars)"
                                });
                                break;
                            case "message.start":
                                progress.Report(new StreamChunkEventArgs { IsThinking = false, StatusMessage = "Writing..." });
                                break;
                            case "message.delta":
                                if (ttft == 0) ttft = stopwatch.Elapsed.TotalSeconds;
                                tokenCount++;
                                var mContent = root.TryGetProperty("content", out var mcEl) ? mcEl.GetString() ?? "" : "";
                                progress.Report(new StreamChunkEventArgs { Content = mContent, IsThinking = false });
                                break;
                            case "chat.end":
                                if (root.TryGetProperty("result", out var resEl) && resEl.TryGetProperty("stats", out var stEl))
                                {
                                    finalStats = new GenerationStats
                                    {
                                        TotalDuration = stopwatch.Elapsed
                                    };
                                    if (stEl.TryGetProperty("tokensPerSecond", out var tpsEl) && tpsEl.TryGetDouble(out var tps)) finalStats.SpeedTps = tps;
                                    if (stEl.TryGetProperty("timeToFirstTokenSeconds", out var ttftEl) && ttftEl.TryGetDouble(out var ttftVal)) finalStats.TtftSeconds = ttftVal;
                                    if (stEl.TryGetProperty("totalTokens", out var totTokEl) && totTokEl.TryGetInt32(out var tot)) finalStats.TotalTokens = tot;
                                    if (stEl.TryGetProperty("completionTokens", out var compTokEl) && compTokEl.TryGetInt32(out var comp)) finalStats.CompletionTokens = comp;
                                    else if (stEl.TryGetProperty("reasoningOutputTokens", out var rtokEl) && rtokEl.TryGetInt32(out var rtok)) finalStats.CompletionTokens = rtok + tokenCount;
                                }
                                break;
                            case "error":
                                var errMsg = root.TryGetProperty("error", out var errEl) && errEl.TryGetProperty("message", out var mEl) ? mEl.GetString() : "LM Studio API error";
                                throw new InvalidOperationException($"LM Studio Error: {errMsg}");
                        }
                    }
                }
                catch (JsonException) { }
            }
        }

        stopwatch.Stop();
        finalStats ??= new GenerationStats { TotalDuration = stopwatch.Elapsed };
        if (finalStats.TtftSeconds <= 0 && ttft > 0) finalStats.TtftSeconds = ttft;
        if (finalStats.CompletionTokens <= 0) finalStats.CompletionTokens = tokenCount;
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

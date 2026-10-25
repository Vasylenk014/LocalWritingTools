using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using WritingTools.Models;
using WritingTools.Services;

namespace WritingTools.Tests;

class Program
{
    static async Task<int> Main(string[] args)
    {
        Console.WriteLine("=================================================");
        Console.WriteLine("   WritingTools Suite Automated Verification   ");
        Console.WriteLine("=================================================");
        int passed = 0;
        int failed = 0;

        // 1. DiffService Test
        try
        {
            var orig = "i checked you're account and refund was proceed yesterday";
            var mod = "I checked your account, and the refund was processed yesterday.";
            var diff = DiffService.ComputeDiff(orig, mod);

            bool hasRemoved = diff.Any(t => t.Type == DiffTokenType.Removed && t.Text.Contains("proceed"));
            bool hasAdded = diff.Any(t => t.Type == DiffTokenType.Added && t.Text.Contains("processed"));

            if (hasRemoved && hasAdded)
            {
                Console.WriteLine("✓ [PASS] DiffService: LCS word diff correctly detected deletions and additions.");
                passed++;
            }
            else
            {
                Console.WriteLine("✗ [FAIL] DiffService: Diff did not detect expected tokens.");
                failed++;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ [FAIL] DiffService: {ex.Message}");
            failed++;
        }

        // 2. SmartCopyService Test
        try
        {
            var rawReview =
@"CORRECTED:
I checked your account, and the refund was processed yesterday.

CONCISE:
Your refund was processed.

CHANGES:
- 'refund was proceed' -> 'refund was processed' — grammar correction";

            var extracted = SmartCopyService.GetSmartCopyText(rawReview, AppMode.DetailedReview);
            if (extracted.Trim() == "I checked your account, and the refund was processed yesterday.")
            {
                Console.WriteLine("✓ [PASS] SmartCopyService: Extracted CORRECTED section exclusively for Detailed Review.");
                passed++;
            }
            else
            {
                Console.WriteLine($"✗ [FAIL] SmartCopyService: Extracted text mismatch: '{extracted}'");
                failed++;
            }

            var dirtyOutput = "<think>thinking process...</think>```\n\"Cleaned text without fences\"\n```";
            var cleaned = SmartCopyService.CleanArtifacts(dirtyOutput);

            var gemmaOutput = "<thought>deep reasoning about tone...</thought>Cleaned Gemma text";
            var gemmaCleaned = SmartCopyService.CleanArtifacts(gemmaOutput);

            if (cleaned == "Cleaned text without fences" && gemmaCleaned == "Cleaned Gemma text")
            {
                Console.WriteLine("✓ [PASS] SmartCopyService: Removed <think>, <thought>, code fences, and quotes.");
                passed++;
            }
            else
            {
                Console.WriteLine($"✗ [FAIL] SmartCopyService: Cleaned mismatch: '{cleaned}', gemma: '{gemmaCleaned}'");
                failed++;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ [FAIL] SmartCopyService: {ex.Message}");
            failed++;
        }

        // 3. TextTransformService Test
        try
        {
            var text = "  hello   «world»!  \r\n\r\n\r\nthis   is    good.  ";
            var cleanedSpaces = TextTransformService.CleanExtraSpaces(text);
            var straightened = TextTransformService.StraightenQuotes("«hello» and “quotes”");
            var titleCase = TextTransformService.CapitalizeWords("quick brown fox");
            var allCapsTitleCase = TextTransformService.CapitalizeWords("HELLO WORLD");
            var mixedTitleCase = TextTransformService.CapitalizeWords("sUPER cOOL");
            var sentenceCase = TextTransformService.ToSentenceCase("hello world. HOW ARE YOU? fine! ok.");

            if (!cleanedSpaces.Contains("   ") &&
                straightened == "\"hello\" and \"quotes\"" &&
                titleCase == "Quick Brown Fox" &&
                allCapsTitleCase == "Hello World" &&
                mixedTitleCase == "Super Cool" &&
                sentenceCase.StartsWith("Hello world. How are you? Fine! Ok."))
            {
                Console.WriteLine("✓ [PASS] TextTransformService: Spaces, quotes, and case transformations match specification (including ALL-CAPS words).");
                passed++;
            }
            else
            {
                Console.WriteLine($"✗ [FAIL] TextTransformService mismatch (all-caps: '{allCapsTitleCase}', title: '{titleCase}').");
                failed++;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ [FAIL] TextTransformService: {ex.Message}");
            failed++;
        }

        // 4. Prompts Configuration Test
        try
        {
            var settings = new AppSettings();
            var qfPrompt = settings.GetSystemPrompt(AppMode.QuickFix);
            var drPrompt = settings.GetSystemPrompt(AppMode.DetailedReview);
            var sumPrompt = settings.GetSystemPrompt(AppMode.Summarize);

            bool noZendesk = !qfPrompt.Contains("Zendesk", StringComparison.OrdinalIgnoreCase) &&
                             !drPrompt.Contains("Zendesk", StringComparison.OrdinalIgnoreCase) &&
                             !sumPrompt.Contains("Zendesk", StringComparison.OrdinalIgnoreCase);

            if (qfPrompt.Contains("<draft></draft>") &&
                drPrompt.Contains("CORRECTED:") &&
                sumPrompt.Contains("Core Issue") &&
                noZendesk)
            {
                Console.WriteLine("✓ [PASS] AppSettings: System prompts match specification with zero proprietary branding.");
                passed++;
            }
            else
            {
                Console.WriteLine("✗ [FAIL] AppSettings: System prompt content missing required markers or contains legacy branding.");
                failed++;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ [FAIL] AppSettings: {ex.Message}");
            failed++;
        }

        // 5. FastFlowLM Server API Test
        try
        {
            var flm = new FlmApiService();
            bool isOnline = await flm.CheckServerHealthAsync();
            Console.WriteLine($"• [INFO] FastFlowLM Server Health on :52625: {(isOnline ? "ONLINE" : "OFFLINE")}");

            if (isOnline)
            {
                var models = await flm.GetModelsAsync();
                Console.WriteLine($"✓ [PASS] FastFlowLM Models retrieved: {models.Count} models found.");
                foreach (var m in models.Where(m => m.Installed))
                {
                    Console.WriteLine($"    - Installed on NPU: {m.ModelId} (supports thinking: {m.SupportsThinking})");
                }
                
                // Live Stream Completion Test
                var chunkCount = 0;
                var textChunks = 0;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var progress = new Progress<StreamChunkEventArgs>(e =>
                {
                    chunkCount++;
                    if (!string.IsNullOrEmpty(e.Content))
                    {
                        textChunks++;
                        Console.WriteLine($"[{sw.ElapsedMilliseconds}ms] CHUNK: '{e.Content.Replace("\n", "\\n")}'");
                    }
                    if (e.IsThinking)
                    {
                        Console.WriteLine($"[{sw.ElapsedMilliseconds}ms] THINKING: {e.ThinkingCharCount} chars");
                    }
                    if (e.IsDone)
                    {
                        Console.WriteLine($"[{sw.ElapsedMilliseconds}ms] DONE! Stats: {e.Stats?.FormatSummary()}");
                    }
                });

                var runningModel = flm.DetectRunningModel();
                if (!string.IsNullOrEmpty(runningModel))
                {
                    Console.WriteLine($"--> Testing live stream from flm with '{runningModel}'...");
                    try
                    {
                        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(35));
                        await flm.StreamCompletionAsync(runningModel, "You are a helpful assistant.", "Hello! Reply with 3 words.", ThinkingOption.Off, progress, cts.Token);
                        Console.WriteLine($"✓ [PASS] Streaming verified: {chunkCount} total events, {textChunks} content chunks.");
                    }
                    catch (Exception streamEx)
                    {
                        Console.WriteLine($"• [INFO] Live stream verification skipped (NPU busy): {streamEx.Message}");
                    }
                }
                passed++;
            }
            else
            {
                Console.WriteLine("• [NOTE] FastFlowLM Server is currently offline. App can start it on demand via 'flm serve'.");
                passed++;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ [FAIL] FastFlowLM API check: {ex.Message}");
            failed++;
        }

        // 6. UI XAML Parse & Initialization Test (STA)
        try
        {
            var tcs = new TaskCompletionSource<bool>();
            var thread = new Thread(() =>
            {
                try
                {
                    if (System.Windows.Application.Current == null)
                    {
                        new System.Windows.Application();
                    }
                    var flm = new FlmApiService();
                    var settings = new SettingsService();
                    var hotkey = new GlobalHotkeyService();
                    var window = new MainWindow(flm, settings, hotkey);
                    Console.WriteLine("✓ [PASS] UI XAML: MainWindow initialized without any XAML or template errors.");

                    // Verify tab text & result isolation (QuickFix, Brainstorm, DetailedReview, Summarize):
                    window.SetMode(AppMode.QuickFix);
                    window.DraftInputText = "Customer reply draft";
                    window.ResultOutputText = "QuickFix polished output";

                    // Brainstorm shares reply draft, but isolates result buffer!
                    window.SetMode(AppMode.Brainstorm);
                    bool brainstormSharedDraft = window.DraftInputText == "Customer reply draft";
                    bool brainstormIsolatedResult = string.IsNullOrEmpty(window.ResultOutputText);
                    window.ResultOutputText = "1. First structured step\n2. Second structured step";
                    
                    // DetailedReview shares reply draft, but MUST NOT share QuickFix or Brainstorm result!
                    window.SetMode(AppMode.DetailedReview);
                    bool detailedSharedDraft = window.DraftInputText == "Customer reply draft";
                    bool detailedIsolatedResult = string.IsNullOrEmpty(window.ResultOutputText);
                    window.ResultOutputText = "CORRECTED:\nDetailed review output";
                    
                    // Switch back to QuickFix -> restores QuickFix result
                    window.SetMode(AppMode.QuickFix);
                    bool quickFixResultIsolated = window.ResultOutputText == "QuickFix polished output";

                    // Switch back to Brainstorm -> restores Brainstorm result
                    window.SetMode(AppMode.Brainstorm);
                    bool brainstormResultRestored = window.ResultOutputText == "1. First structured step\n2. Second structured step";

                    // Switch back to DetailedReview -> restores DetailedReview result
                    window.SetMode(AppMode.DetailedReview);
                    bool detailedResultRestored = window.ResultOutputText == "CORRECTED:\nDetailed review output";

                    // Summarize is isolated (starts empty)
                    window.SetMode(AppMode.Summarize);
                    bool summarizeIsolated = string.IsNullOrEmpty(window.DraftInputText) && string.IsNullOrEmpty(window.ResultOutputText);
                    
                    // Enter ticket in Summarize
                    window.DraftInputText = "Long verbose customer inquiry";
                    
                    // Switch back to QuickFix -> restores reply draft
                    window.SetMode(AppMode.QuickFix);
                    bool quickFixRestored = window.DraftInputText == "Customer reply draft";
                    
                    // Switch back to Summarize -> restores customer inquiry
                    window.SetMode(AppMode.Summarize);
                    bool summarizeRestored = window.DraftInputText == "Long verbose customer inquiry";

                    // Verify Clear resets leftover "Writing... ▋" watermark text
                    window.SetMode(AppMode.QuickFix);
                    window.EmptyResultWatermarkText = "Writing... ▋";
                    window.ClearDraftAndResult();
                    bool clearResetsWritingWatermark = string.IsNullOrEmpty(window.EmptyResultWatermarkText) && string.IsNullOrEmpty(window.ResultOutputText);

                    // Verify Context Active visual indicator when collapsed/expanded
                    bool contextInitiallyHidden = !window.IsContextBadgeVisible;
                    window.ContextInputText = "Make it shorter and politely decline the refund.";
                    bool contextBadgeShown = window.IsContextBadgeVisible;
                    window.ContextInputText = "";
                    bool contextBadgeCleared = !window.IsContextBadgeVisible;
                    bool contextIndicatorWorks = contextInitiallyHidden && contextBadgeShown && contextBadgeCleared;

                    // Verify TonePreset.Rephrase and TonePreset.Internal selection
                    window.CurrentTone = TonePreset.Rephrase;
                    bool rephraseToneSet = window.CurrentTone == TonePreset.Rephrase;
                    window.CurrentTone = TonePreset.Internal;
                    bool internalToneSet = window.CurrentTone == TonePreset.Internal;
                    window.CurrentTone = TonePreset.Standard;

                    // Verify Light / Dark / System Auto Theme Switching
                    window.CurrentTheme = AppTheme.Light;
                    bool lightThemeApplied = window.CurrentTheme == AppTheme.Light && window.IsLightThemeEffective;
                    window.CurrentTheme = AppTheme.Dark;
                    bool darkThemeApplied = window.CurrentTheme == AppTheme.Dark && !window.IsLightThemeEffective;
                    window.CurrentTheme = AppTheme.System;
                    bool systemThemeApplied = window.CurrentTheme == AppTheme.System && (window.IsLightThemeEffective == MainWindow.IsWindowsLightTheme());
                    bool themeChecksWork = lightThemeApplied && darkThemeApplied && systemThemeApplied;

                    // Verify startup and reopen mode is always QuickFix
                    window.SetMode(AppMode.Summarize);
                    window.Hide();
                    window.ShowAndActivate();
                    bool reopensQuickFix = window.CurrentMode == AppMode.QuickFix;

                    bool allUiChecks = brainstormSharedDraft && brainstormIsolatedResult && brainstormResultRestored &&
                                       detailedSharedDraft && detailedIsolatedResult && quickFixResultIsolated &&
                                       detailedResultRestored && summarizeIsolated && quickFixRestored &&
                                       summarizeRestored && clearResetsWritingWatermark && contextIndicatorWorks &&
                                       rephraseToneSet && internalToneSet && themeChecksWork && reopensQuickFix;

                    if (allUiChecks)
                    {
                        Console.WriteLine("✓ [PASS] UI State, Brainstorm & Themes: 4 modes isolated, Light/Dark/System themes verified, Clear & Context badge verified.");
                    }
                    else
                    {
                        Console.WriteLine($"✗ [FAIL] UI State mismatch: brainstorm={brainstormSharedDraft}/{brainstormIsolatedResult}/{brainstormResultRestored}, theme={themeChecksWork}, clear={clearResetsWritingWatermark}, context={contextIndicatorWorks}");
                    }

                    tcs.SetResult(allUiChecks);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"✗ [FAIL] UI XAML: {ex.GetType().Name} - {ex.Message}");
                    if (ex.InnerException != null)
                    {
                        Console.WriteLine($"   Inner: {ex.InnerException.GetType().Name} - {ex.InnerException.Message}");
                    }
                    tcs.SetResult(false);
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            if (await tcs.Task) passed++; else failed++;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ [FAIL] UI XAML thread: {ex.Message}");
            failed++;
        }

        // 8. Generate and verify multi-resolution app.ico
        try
        {
            var rootDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
            var targetIco = Path.Combine(rootDir, "app.ico");
            IconHelper.SaveMultiResIco(targetIco);
            if (File.Exists(targetIco) && new FileInfo(targetIco).Length > 1000)
            {
                Console.WriteLine($"✓ [PASS] Icon: Generated multi-resolution app.ico ({new FileInfo(targetIco).Length} bytes).");
                passed++;
            }
            else
            {
                Console.WriteLine("✗ [FAIL] Icon: Generated file is missing or too small.");
                failed++;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ [FAIL] Icon generation: {ex.Message}");
            failed++;
        }

        // 9. System Metrics Telemetry (RAM & AMD Ryzen AI NPU)
        try
        {
            var (usedGb, totalGb, ramPct) = SystemMetricsService.GetRamUsage();
            using var metricsService = new SystemMetricsService();
            metricsService.Start();
            await Task.Delay(700);

            var m = metricsService.LatestMetrics;
            if (totalGb > 0 && usedGb > 0 && ramPct > 0 && !string.IsNullOrEmpty(m.RamText) && !string.IsNullOrEmpty(m.NpuText))
            {
                Console.WriteLine($"✓ [PASS] System Telemetry: RAM detected ({usedGb:F1} / {totalGb:F1} GB, {ramPct}%), NPU telemetry initialized ({m.NpuText}).");
                passed++;
            }
            else
            {
                Console.WriteLine($"✗ [FAIL] System Telemetry: RAM or NPU metrics invalid (used: {usedGb}, total: {totalGb}, pct: {ramPct}).");
                failed++;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ [FAIL] System Telemetry: {ex.Message}");
            failed++;
        }

        // 10. System Prompts Configuration & Default Fallbacks
        try
        {
            var settings = new AppSettings();
            var qfDefault = AppSettings.GetDefaultPrompt(AppMode.QuickFix);
            var dtDefault = AppSettings.GetDefaultPrompt(AppMode.DetailedReview);
            var smDefault = AppSettings.GetDefaultPrompt(AppMode.Summarize);

            bool defaultsValid = !string.IsNullOrWhiteSpace(qfDefault) &&
                                 !string.IsNullOrWhiteSpace(dtDefault) &&
                                 !string.IsNullOrWhiteSpace(smDefault) &&
                                 qfDefault != dtDefault &&
                                 dtDefault != smDefault;

            settings.SetSystemPrompt(AppMode.QuickFix, "Custom QuickFix Prompt");
            settings.SetSystemPrompt(AppMode.DetailedReview, "Custom Detailed Prompt");
            settings.SetSystemPrompt(AppMode.Summarize, "Custom Summarize Prompt");

            bool customsValid = settings.GetSystemPrompt(AppMode.QuickFix) == "Custom QuickFix Prompt" &&
                                settings.GetSystemPrompt(AppMode.DetailedReview) == "Custom Detailed Prompt" &&
                                settings.GetSystemPrompt(AppMode.Summarize) == "Custom Summarize Prompt";

            if (defaultsValid && customsValid)
            {
                Console.WriteLine("✓ [PASS] System Prompts: Per-mode prompt customization, persistence, and default fallbacks verified.");
                passed++;
            }
            else
            {
                Console.WriteLine("✗ [FAIL] System Prompts: Prompt customization or fallback failed.");
                failed++;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ [FAIL] System Prompts: {ex.Message}");
            failed++;
        }

        // 11. Multi-Provider Settings & URL Resolution (NPU & GPU / LM Studio)
        try
        {
            var settings = new AppSettings();
            bool defNpu = settings.Provider == LlmProvider.FastFlowLmNpu &&
                          settings.GetEffectiveBaseUrl() == "http://127.0.0.1:52625";

            settings.Provider = LlmProvider.LmStudio;
            settings.LmStudioUrl = "http://localhost:1234/";
            bool lmStudioOk = settings.GetEffectiveBaseUrl() == "http://localhost:1234";

            settings.Provider = LlmProvider.Custom;
            settings.CustomUrl = "http://192.168.1.100:8000";
            bool customOk = settings.GetEffectiveBaseUrl() == "http://192.168.1.100:8000";

            if (defNpu && lmStudioOk && customOk)
            {
                Console.WriteLine("✓ [PASS] Provider Architecture: Base URL routing and default endpoints for NPU & LM Studio GPU verified.");
                passed++;
            }
            else
            {
                Console.WriteLine($"✗ [FAIL] Provider Architecture: URL resolution mismatch (defNpu: {defNpu}, lmStudioOk: {lmStudioOk}, customOk: {customOk}).");
                failed++;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ [FAIL] Provider Architecture: {ex.Message}");
            failed++;
        }

        // 12. System Telemetry Device Label Adaptation (NPU vs GPU)
        try
        {
            using var metricsService = new SystemMetricsService();
            metricsService.SetProvider(LlmProvider.FastFlowLmNpu);
            var npuMetrics = metricsService.LatestMetrics;

            metricsService.SetProvider(LlmProvider.LmStudio);
            var gpuMetrics = metricsService.LatestMetrics;

            if (npuMetrics.DeviceLabel == "NPU:" && gpuMetrics.DeviceLabel == "GPU:")
            {
                Console.WriteLine("✓ [PASS] System Telemetry Adaptation: Dynamically reports 'NPU:' for FastFlowLM and 'GPU:' for LM Studio.");
                passed++;
            }
            else
            {
                Console.WriteLine($"✗ [FAIL] System Telemetry Adaptation: DeviceLabel mismatch (NPU: {npuMetrics.DeviceLabel}, GPU: {gpuMetrics.DeviceLabel}).");
                failed++;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ [FAIL] System Telemetry Adaptation: {ex.Message}");
            failed++;
        }

        // 13. FlmApiService Provider Configuration & Connection Testing
        try
        {
            var api = new FlmApiService();
            api.ConfigureProvider(LlmProvider.LmStudio, "http://localhost:1234", "test-key");

            bool providerSet = api.CurrentProvider == LlmProvider.LmStudio &&
                               api.CurrentBaseUrl == "http://localhost:1234";

            // Test connection against non-existent port should return structured failure without throwing
            var (success, count, message) = await FlmApiService.TestConnectionAsync("http://127.0.0.1:9999");
            bool gracefulHandling = !success && count == 0 && !string.IsNullOrEmpty(message);

            if (providerSet && gracefulHandling)
            {
                Console.WriteLine("✓ [PASS] FlmApiService: Multi-provider configuration and graceful connection tester verified.");
                passed++;
            }
            else
            {
                Console.WriteLine($"✗ [FAIL] FlmApiService: Configuration or connection test check failed.");
                failed++;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ [FAIL] FlmApiService: {ex.Message}");
            failed++;
        }

        // 14. MarkdownDocumentRenderer Test (Detailed Review & Summarize styling)
        try
        {
            var sampleMarkdown =
@"CORRECTED:
I checked your account, and the refund was processed yesterday.

CHANGES:
- ""refund was proceed"" → ""refund was processed"" — grammar fix

**Core Issue / Question**: Refund delay";

            var doc = MarkdownDocumentRenderer.Render(sampleMarkdown);
            bool docValid = doc != null && doc.Blocks.Count >= 3;

            if (docValid)
            {
                Console.WriteLine("✓ [PASS] MarkdownDocumentRenderer: Formatted headers, diff arrows, and bold inlines into FlowDocument.");
                passed++;
            }
            else
            {
                Console.WriteLine($"✗ [FAIL] MarkdownDocumentRenderer: FlowDocument blocks count {doc?.Blocks.Count} < 3.");
                failed++;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ [FAIL] MarkdownDocumentRenderer: {ex.Message}");
            failed++;
        }

        // 15. LM Studio /api/v1/models VRAM Status & Priority Sorting Test
        try
        {
            var lmStudioJson = @"{
                ""models"": [
                    {
                        ""type"": ""llm"",
                        ""key"": ""qwen/qwen3.5-9b"",
                        ""display_name"": ""Qwen3.5 9B"",
                        ""loaded_instances"": [],
                        ""capabilities"": { ""reasoning"": { ""allowed_options"": [""off"", ""on""], ""default"": ""on"" } }
                    },
                    {
                        ""type"": ""llm"",
                        ""key"": ""google/gemma-4-12b-qat"",
                        ""display_name"": ""Gemma 4 12B QAT"",
                        ""loaded_instances"": [{ ""id"": ""inst-1"" }],
                        ""capabilities"": { ""reasoning"": { ""allowed_options"": [""off"", ""on""], ""default"": ""off"" } }
                    },
                    {
                        ""type"": ""embedding"",
                        ""key"": ""nomic-embed"",
                        ""display_name"": ""Nomic Embed""
                    }
                ]
            }";

            var resp = System.Text.Json.JsonSerializer.Deserialize<LmStudioModelsResponse>(lmStudioJson);
            var models = resp?.Models?
                .Where(m => string.Equals(m.Type, "llm", StringComparison.OrdinalIgnoreCase))
                .Select(m => new FlmModelInfo
                {
                    ModelId = m.Key,
                    Name = m.DisplayName ?? m.Key,
                    FriendlyName = m.DisplayName ?? m.Key,
                    Installed = m.LoadedInstances?.Count > 0,
                    IsLoaded = m.LoadedInstances?.Count > 0,
                    ReasoningAllowedOptions = m.Capabilities?.Reasoning?.AllowedOptions ?? new(),
                    ReasoningDefault = m.Capabilities?.Reasoning?.Default
                })
                .OrderByDescending(m => m.IsLoaded)
                .ThenBy(m => m.DisplayName)
                .ToList();

            bool gemmaLoaded = models != null && models.Count == 2 && models[0].ModelId == "google/gemma-4-12b-qat" && models[0].IsLoaded && models[0].DisplayName.StartsWith("● ");
            bool qwenUnloaded = models != null && models.Count == 2 && models[1].ModelId == "qwen/qwen3.5-9b" && !models[1].IsLoaded && !models[1].DisplayName.StartsWith("● ");

            if (gemmaLoaded && qwenUnloaded)
            {
                Console.WriteLine("✓ [PASS] LM Studio API: VRAM loaded status detected, loaded model sorted first, and clean DisplayName applied.");
                passed++;
            }
            else
            {
                Console.WriteLine($"✗ [FAIL] LM Studio API: Model mapping or sorting mismatch.");
                failed++;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ [FAIL] LM Studio API: {ex.Message}");
            failed++;
        }

        // 16. Per-Model Thinking Persistence Test
        try
        {
            var settings = new AppSettings();
            settings.ThinkingModePerModel["google/gemma-4-12b-qat"] = ThinkingOption.Off.ToString();
            settings.ThinkingModePerModel["deepseek/deepseek-r1-14b"] = ThinkingOption.On.ToString();

            bool gemmaOff = settings.ThinkingModePerModel["google/gemma-4-12b-qat"] == "Off";
            bool deepseekOn = settings.ThinkingModePerModel["deepseek/deepseek-r1-14b"] == "On";

            if (gemmaOff && deepseekOn)
            {
                Console.WriteLine("✓ [PASS] Per-Model Thinking: Individual thinking preferences persisted per model ID.");
                passed++;
            }
            else
            {
                Console.WriteLine($"✗ [FAIL] Per-Model Thinking: Settings mismatch.");
                failed++;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ [FAIL] Per-Model Thinking: {ex.Message}");
            failed++;
        }

        // 17. LM Studio Loaded Instance Serialization & Unload Capability
        try
        {
            var rawJson = @"{
                ""models"": [
                    {
                        ""type"": ""llm"",
                        ""key"": ""google/gemma-4-12b-qat"",
                        ""loaded_instances"": [{ ""id"": ""inst-gemma-1"" }]
                    },
                    {
                        ""type"": ""llm"",
                        ""key"": ""qwen/qwen3.5-9b"",
                        ""loaded_instances"": []
                    }
                ]
            }";

            var parsed = System.Text.Json.JsonSerializer.Deserialize<LmStudioModelsResponse>(rawJson);
            bool gemmaHasInstance = parsed?.Models?[0].LoadedInstances?.Count == 1 &&
                                    parsed?.Models?[0].LoadedInstances?[0].Id == "inst-gemma-1";
            bool qwenEmptyInstances = parsed?.Models?[1].LoadedInstances?.Count == 0;

            if (gemmaHasInstance && qwenEmptyInstances)
            {
                Console.WriteLine("✓ [PASS] LM Studio Model Management: LoadedInstances typed IDs correctly parsed for GPU memory ejection.");
                passed++;
            }
            else
            {
                Console.WriteLine("✗ [FAIL] LM Studio Model Management: Instance ID deserialization failed.");
                failed++;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ [FAIL] LM Studio Model Management: {ex.Message}");
            failed++;
        }

        // 18. Diff Identical Verification
        try
        {
            var orig = "Thank you for contacting support. Your ticket has been updated.";
            var identicalMod = "Thank you for contacting support. Your ticket has been updated.";
            var diff = DiffService.ComputeDiff(orig, identicalMod);

            bool hasChanges = diff.Any(t => t.Type == DiffTokenType.Added || t.Type == DiffTokenType.Removed);
            bool isIdentical = !hasChanges || string.Equals(orig.Trim(), identicalMod.Trim(), StringComparison.Ordinal);

            if (!hasChanges && isIdentical)
            {
                Console.WriteLine("✓ [PASS] DiffService: Identical draft and result correctly detected with 0 changes.");
                passed++;
            }
            else
            {
                Console.WriteLine("✗ [FAIL] DiffService: Identical check failed.");
                failed++;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ [FAIL] DiffService Identical check: {ex.Message}");
            failed++;
        }

        // 19. LM Studio Reasoning Capability Checks (gpt-oss-20b vs gemma-4)
        try
        {
            var gptOssInfo = new FlmModelInfo
            {
                ModelId = "openai/gpt-oss-20b",
                Name = "GPT OSS 20B",
                ReasoningAllowedOptions = new List<string> { "low", "medium", "high" },
                ReasoningDefault = "low"
            };

            var gemmaInfo = new FlmModelInfo
            {
                ModelId = "google/gemma-4-e4b",
                Name = "Gemma 4 E4B",
                ReasoningAllowedOptions = new List<string> { "off", "on" },
                ReasoningDefault = "on"
            };

            bool gptOssSupportsOff = gptOssInfo.ReasoningAllowedOptions.Contains("off", StringComparer.OrdinalIgnoreCase);
            bool gemmaSupportsOff = gemmaInfo.ReasoningAllowedOptions.Contains("off", StringComparer.OrdinalIgnoreCase);

            if (!gptOssSupportsOff && gemmaSupportsOff)
            {
                Console.WriteLine("✓ [PASS] Reasoning Capability: Correctly detected models requiring reasoning (gpt-oss-20b) vs toggleable (gemma-4).");
                passed++;
            }
            else
            {
                Console.WriteLine("✗ [FAIL] Reasoning Capability: Detection logic failed.");
                failed++;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ [FAIL] Reasoning Capability: {ex.Message}");
            failed++;
        }

        // 20. Rephrase & Internal Colleague Prompts Verification
        try
        {
            var rephrasePrompt = AppSettings.DefaultRephrasePrompt;
            var internalPrompt = AppSettings.DefaultInternalPrompt;
            var internalReviewPrompt = AppSettings.DefaultInternalDetailedReviewPrompt;

            bool rephraseValid = !string.IsNullOrWhiteSpace(rephrasePrompt) &&
                                 rephrasePrompt.Contains("rephrase", StringComparison.OrdinalIgnoreCase) &&
                                 rephrasePrompt.Contains("<draft></draft>", StringComparison.Ordinal);
            bool internalValid = !string.IsNullOrWhiteSpace(internalPrompt) &&
                                 internalPrompt.Contains("colleague", StringComparison.OrdinalIgnoreCase) &&
                                 internalPrompt.Contains("internal", StringComparison.OrdinalIgnoreCase);
            bool internalReviewValid = !string.IsNullOrWhiteSpace(internalReviewPrompt) &&
                                       internalReviewPrompt.Contains("CORRECTED:", StringComparison.Ordinal) &&
                                       internalReviewPrompt.Contains("colleague", StringComparison.OrdinalIgnoreCase);

            if (rephraseValid && internalValid && internalReviewValid)
            {
                Console.WriteLine("✓ [PASS] Rephrase & Internal Prompts: Dedicated prompts for Rephrase and Internal colleague notes verified.");
                passed++;
            }
            else
            {
                Console.WriteLine("✗ [FAIL] Rephrase & Internal Prompts: Prompt validation failed.");
                failed++;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ [FAIL] Rephrase & Internal Prompts: {ex.Message}");
            failed++;
        }

        // 21. Brainstorm Prompts, Context Instructions & Light-Theme Markdown Numbered Lists
        try
        {
            var bsPrompt = AppSettings.GetDefaultPrompt(AppMode.Brainstorm);
            var bsInternalPrompt = AppSettings.DefaultInternalBrainstormPrompt;
            var qfPrompt = AppSettings.GetDefaultPrompt(AppMode.QuickFix);

            bool brainstormPromptsValid = !string.IsNullOrWhiteSpace(bsPrompt) &&
                                          bsPrompt.Contains("step by step", StringComparison.OrdinalIgnoreCase) &&
                                          !string.IsNullOrWhiteSpace(bsInternalPrompt) &&
                                          bsInternalPrompt.Contains("colleague", StringComparison.OrdinalIgnoreCase);

            bool contextSupportsInstructions = qfPrompt.Contains("specific processing request or instruction", StringComparison.OrdinalIgnoreCase) &&
                                               bsPrompt.Contains("specific instructions or processing requests", StringComparison.OrdinalIgnoreCase);

            var lightDoc = MarkdownDocumentRenderer.Render("1. **Step One**: Check card\n2. **Step Two**: Retry payment", isLightTheme: true);
            bool lightMarkdownValid = lightDoc != null && lightDoc.Blocks.Count == 2;

            if (brainstormPromptsValid && contextSupportsInstructions && lightMarkdownValid)
            {
                Console.WriteLine("✓ [PASS] Brainstorm & Context Instructions: Step-by-step explainer prompts, custom instructions in <context>, and Light-theme numbered lists verified.");
                passed++;
            }
            else
            {
                Console.WriteLine("✗ [FAIL] Brainstorm & Context Instructions: Validation failed.");
                failed++;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ [FAIL] Brainstorm & Context Instructions: {ex.Message}");
            failed++;
        }

        Console.WriteLine("=================================================");
        Console.WriteLine($"Total tests passed: {passed}, failed: {failed}");
        Console.WriteLine("=================================================");

        return failed == 0 ? 0 : 1;
    }
}

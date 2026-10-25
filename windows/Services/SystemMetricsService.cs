using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using WritingTools.Models;

namespace WritingTools.Services;

public record SystemMetrics(
    double RamUsedGb,
    double RamTotalGb,
    int RamPercent,
    double ComputePercent,
    bool IsComputeActive,
    string RamText,
    string ComputeText,
    string DeviceLabel = "NPU:"
)
{
    // Backwards-compatible aliases
    public double NpuPercent => ComputePercent;
    public bool IsNpuActive => IsComputeActive;
    public string NpuText => ComputeText;
}

public class SystemMetricsService : IDisposable
{
    private static readonly Regex LuidRegex = new(@"luid_(0x[0-9a-fA-F]+_0x[0-9a-fA-F]+)", RegexOptions.Compiled);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private class MEMORYSTATUSEX
    {
        public uint dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

    private readonly CancellationTokenSource _cts = new();
    private readonly object _lock = new();
    private SystemMetrics _latestMetrics = new(0, 0, 0, 0, false, "-- / -- GB", "0% (Idle)", "NPU:");
    private volatile bool _isGenerating;
    private volatile bool _isServerRunning;
    private volatile bool _isPaused;
    private volatile LlmProvider _provider = LlmProvider.FastFlowLmNpu;
    private string? _npuLuid;
    private readonly Dictionary<string, PerformanceCounter> _activeCounters = new();
    private DateTime _lastCounterRefresh = DateTime.MinValue;

    public SystemMetrics LatestMetrics
    {
        get
        {
            lock (_lock)
            {
                return _latestMetrics;
            }
        }
    }

    public void SetProvider(LlmProvider provider)
    {
        if (_provider != provider)
        {
            _provider = provider;
            lock (_activeCounters)
            {
                foreach (var counter in _activeCounters.Values)
                {
                    try { counter.Dispose(); } catch { }
                }
                _activeCounters.Clear();
                _lastCounterRefresh = DateTime.MinValue;
            }

            lock (_lock)
            {
                _latestMetrics = _latestMetrics with
                {
                    DeviceLabel = provider == LlmProvider.FastFlowLmNpu ? "NPU:" : "GPU:"
                };
            }
        }
    }

    public void SetGenerating(bool isGenerating)
    {
        _isGenerating = isGenerating;
    }

    public void SetServerRunning(bool isRunning)
    {
        _isServerRunning = isRunning;
    }

    public void SetPaused(bool isPaused)
    {
        _isPaused = isPaused;
    }

    public void Start()
    {
        Task.Run(MonitorLoopAsync, _cts.Token);
    }

    public static (double usedGb, double totalGb, int percent) GetRamUsage()
    {
        try
        {
            var mem = new MEMORYSTATUSEX();
            if (GlobalMemoryStatusEx(mem))
            {
                double total = mem.ullTotalPhys / (1024.0 * 1024.0 * 1024.0);
                double used = (mem.ullTotalPhys - mem.ullAvailPhys) / (1024.0 * 1024.0 * 1024.0);
                int pct = (int)mem.dwMemoryLoad;
                return (Math.Round(used, 1), Math.Round(total, 1), pct);
            }
        }
        catch { }
        return (0, 0, 0);
    }

    private async Task MonitorLoopAsync()
    {
        // Initial warm-up
        await Task.Delay(500, _cts.Token);

        while (!_cts.Token.IsCancellationRequested)
        {
            try
            {
                if (_isPaused && !_isGenerating)
                {
                    await Task.Delay(2000, _cts.Token);
                    continue;
                }

                var (usedGb, totalGb, ramPct) = GetRamUsage();
                string ramText = totalGb > 0 ? $"{usedGb:F1} / {totalGb:F1} GB ({ramPct}%)" : "-- / -- GB";

                double computeVal = SampleCompute();
                bool isGen = _isGenerating;
                bool isServer = _isServerRunning;
                string deviceLabel = _provider == LlmProvider.FastFlowLmNpu ? "NPU:" : "GPU:";

                string computeText;
                bool computeActive = false;

                if (isGen)
                {
                    computeActive = true;
                    double displayVal = computeVal > 5 ? computeVal : 95.0;
                    computeText = $"{displayVal:F0}% (Active)";
                }
                else if (computeVal >= 1.5)
                {
                    computeActive = true;
                    computeText = $"{computeVal:F0}%";
                }
                else if (isServer)
                {
                    computeText = "0% (Idle)";
                }
                else
                {
                    computeText = "0% (Standby)";
                }

                lock (_lock)
                {
                    _latestMetrics = new SystemMetrics(
                        usedGb,
                        totalGb,
                        ramPct,
                        computeVal,
                        computeActive,
                        ramText,
                        computeText,
                        deviceLabel
                    );
                }
            }
            catch { }

            try
            {
                await Task.Delay(1500, _cts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private double SampleCompute()
    {
        try
        {
            // Refresh counter instances periodically (every 25 seconds or initially)
            if (_npuCountersNeedRefresh())
            {
                RefreshNpuCounters();
            }

            double totalNpu = 0;
            List<string>? deadCounters = null;

            lock (_activeCounters)
            {
                foreach (var kvp in _activeCounters)
                {
                    try
                    {
                        float val = kvp.Value.NextValue();
                        if (val > 0)
                        {
                            totalNpu += val;
                        }
                    }
                    catch
                    {
                        deadCounters ??= new List<string>();
                        deadCounters.Add(kvp.Key);
                    }
                }

                if (deadCounters != null)
                {
                    foreach (var dead in deadCounters)
                    {
                        if (_activeCounters.Remove(dead, out var counter))
                        {
                            try { counter.Dispose(); } catch { }
                        }
                    }
                }
            }

            return Math.Min(100.0, Math.Round(totalNpu, 1));
        }
        catch
        {
            return 0.0;
        }
    }

    private bool _npuCountersNeedRefresh()
    {
        return _activeCounters.Count == 0 || (DateTime.UtcNow - _lastCounterRefresh).TotalSeconds > 25;
    }

    private void RefreshNpuCounters()
    {
        try
        {
            _lastCounterRefresh = DateTime.UtcNow;
            var cat = new PerformanceCounterCategory("GPU Engine");
            var instances = cat.GetInstanceNames();

            // Detect NPU LUID if not cached
            if (string.IsNullOrEmpty(_npuLuid))
            {
                var luids = instances
                    .Select(i => LuidRegex.Match(i))
                    .Where(m => m.Success)
                    .Select(m => m.Groups[1].Value)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                foreach (var luid in luids)
                {
                    bool has3d = instances.Any(i => i.Contains(luid, StringComparison.OrdinalIgnoreCase) &&
                                                    i.Contains("engtype_3D", StringComparison.OrdinalIgnoreCase));
                    bool hasCompute = instances.Any(i => i.Contains(luid, StringComparison.OrdinalIgnoreCase) &&
                                                         i.Contains("engtype_Compute", StringComparison.OrdinalIgnoreCase));

                    if (!has3d && hasCompute)
                    {
                        _npuLuid = luid;
                        break;
                    }
                }
            }

            var matchingInstances = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (_provider == LlmProvider.FastFlowLmNpu)
            {
                foreach (var inst in instances)
                {
                    bool matchesNpu = !string.IsNullOrEmpty(_npuLuid)
                        ? inst.Contains(_npuLuid, StringComparison.OrdinalIgnoreCase) && inst.Contains("engtype_Compute", StringComparison.OrdinalIgnoreCase)
                        : inst.Contains("engtype_Compute", StringComparison.OrdinalIgnoreCase);

                    if (matchesNpu)
                    {
                        matchingInstances.Add(inst);
                    }
                }
            }
            else
            {
                // GPU mode: track 3D and compute on graphics cards
                foreach (var inst in instances)
                {
                    bool isNpu = !string.IsNullOrEmpty(_npuLuid) && inst.Contains(_npuLuid, StringComparison.OrdinalIgnoreCase);
                    if (!isNpu && (inst.Contains("engtype_3D", StringComparison.OrdinalIgnoreCase) || inst.Contains("engtype_Compute", StringComparison.OrdinalIgnoreCase)))
                    {
                        matchingInstances.Add(inst);
                    }
                }
            }

            lock (_activeCounters)
            {
                foreach (var inst in matchingInstances)
                {
                    if (!_activeCounters.ContainsKey(inst))
                    {
                        try
                        {
                            var counter = new PerformanceCounter("GPU Engine", "Utilization Percentage", inst);
                            counter.NextValue(); // Prime initial sample
                            _activeCounters[inst] = counter;
                        }
                        catch { }
                    }
                }

                // Remove any counters that no longer exist
                var toRemove = _activeCounters.Keys.Where(k => !matchingInstances.Contains(k)).ToList();
                foreach (var k in toRemove)
                {
                    if (_activeCounters.Remove(k, out var counter))
                    {
                        try { counter.Dispose(); } catch { }
                    }
                }
            }
        }
        catch { }
    }

    public void Dispose()
    {
        try { _cts.Cancel(); } catch { }

        lock (_activeCounters)
        {
            foreach (var counter in _activeCounters.Values)
            {
                try { counter.Dispose(); } catch { }
            }
            _activeCounters.Clear();
        }
    }
}

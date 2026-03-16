using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using IntLimiter.Models;

namespace IntLimiter.Services;

public class BandwidthLimiterService : IDisposable
{
    private readonly Dictionary<int, BandwidthLimit> _limits = new();
    private BandwidthLimit? _globalLimit;
    private bool _disposed;
    private bool _wfpAvailable;
    private IntPtr _engineHandle = IntPtr.Zero;

    // Active WFP filter IDs per pid (kept for future WFP block-filter expansion)
    private readonly Dictionary<int, ulong> _activeFilterIds = new();

    // Stable, unique GUID identifying the IntLimiter WFP sublayer across sessions.
    private static readonly Guid SubLayerGuid = new("3F8A1C2E-74D9-4B5E-A8F0-C7D3E9210B6A");

    // RTT assumption for CWND calculation when we have no per-connection RTT measurement.
    // 50 ms is a reasonable default for broadband internet links.
    private const double DefaultRttSeconds = 0.050;
    private const double BitsPerByte = 8.0;
    /// <summary>Minimum TCP MSS in bytes — prevents CWND from being set so low that connections stall.</summary>
    private const uint MinTcpMss = 1460;

    public BandwidthLimiterService()
    {
        TryOpenWfpEngine();
    }

    private void TryOpenWfpEngine()
    {
#if WINDOWS7_0_OR_GREATER
        try
        {
            var result = Native.WfpNative.FwpmEngineOpen0(
                null, Native.WfpNative.RPC_C_AUTHN_WINNT, IntPtr.Zero, IntPtr.Zero, out _engineHandle);
            _wfpAvailable = (result == 0);
            if (_wfpAvailable)
            {
                Native.WfpNative.EnsureSubLayer(_engineHandle, SubLayerGuid);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WFP engine open failed (not admin?): {ex.Message}");
            _wfpAvailable = false;
        }
#else
        _wfpAvailable = false;
#endif
    }

    public void SetLimit(int pid, string appPath, double maxUpBps, double maxDownBps)
    {
        var limit = new BandwidthLimit
        {
            ProcessId      = pid,
            AppPath        = appPath,
            MaxUploadBps   = maxUpBps,
            MaxDownloadBps = maxDownBps,
            IsEnabled      = true
        };
        _limits[pid] = limit;
    }

    public void RemoveLimit(int pid)
    {
        _limits.Remove(pid);
        RemoveWfpFilter(pid);
    }

    public void SetGlobalLimit(double maxUpBps, double maxDownBps)
    {
        _globalLimit = new BandwidthLimit
        {
            ProcessId      = -1,
            MaxUploadBps   = maxUpBps,
            MaxDownloadBps = maxDownBps,
            IsEnabled      = maxUpBps > 0 || maxDownBps > 0
        };
    }

    public IReadOnlyDictionary<int, BandwidthLimit> GetLimits() => _limits;
    public BandwidthLimit? GetGlobalLimit() => _globalLimit;

    // PIDs for which we have actively set a CWND limit — need clearing when limit removed.
    private readonly System.Collections.Generic.HashSet<int> _throttledPids = new();

    /// <summary>
    /// Called on each monitoring tick with the latest per-app network data.
    /// Applies or clears TCP congestion-window limits on each app's active connections
    /// to implement actual bandwidth throttling.
    /// </summary>
    public void ApplyThrottling(IReadOnlyList<AppNetworkInfo> apps, IPerConnectionStats? connStats)
    {
        if (_disposed || connStats == null) return;

        bool hasGlobalLimit = _globalLimit?.IsEnabled == true;

        foreach (var app in apps)
        {
            _limits.TryGetValue(app.ProcessId, out var perAppLimit);
            bool hasPerAppLimit = perAppLimit?.IsEnabled == true;

            if (!hasPerAppLimit && !hasGlobalLimit)
            {
                // No limit active — clear previously-set CWND limits for this process (if any).
                if (_throttledPids.Contains(app.ProcessId))
                {
                    ClearCwndForApp(app, connStats);
                    _throttledPids.Remove(app.ProcessId);
                }
                continue;
            }

            // Determine the effective upload / download limit for this process.
            double effectiveUpBps   = 0;
            double effectiveDownBps = 0;

            if (hasPerAppLimit)
            {
                effectiveUpBps   = perAppLimit!.MaxUploadBps;
                effectiveDownBps = perAppLimit!.MaxDownloadBps;
            }

            if (hasGlobalLimit)
            {
                int appCount = Math.Max(1, apps.Count);
                double globalShare = 1.0 / appCount;

                double globalUpShare   = _globalLimit!.MaxUploadBps   * globalShare;
                double globalDownShare = _globalLimit!.MaxDownloadBps  * globalShare;

                // Use the tighter of per-app and global proportional limits.
                if (hasPerAppLimit)
                {
                    if (globalUpShare > 0)
                        effectiveUpBps = effectiveUpBps > 0
                            ? Math.Min(effectiveUpBps, globalUpShare)
                            : globalUpShare;
                    if (globalDownShare > 0)
                        effectiveDownBps = effectiveDownBps > 0
                            ? Math.Min(effectiveDownBps, globalDownShare)
                            : globalDownShare;
                }
                else
                {
                    effectiveUpBps   = globalUpShare;
                    effectiveDownBps = globalDownShare;
                }
            }

            ApplyCwndForApp(app, connStats, effectiveUpBps, effectiveDownBps);
            _throttledPids.Add(app.ProcessId);
        }
    }

    /// <summary>
    /// Computes the required TCP congestion-window (CWND) limit to achieve a
    /// target bandwidth and applies it to every TCP connection of the process.
    /// </summary>
    private static void ApplyCwndForApp(AppNetworkInfo app, IPerConnectionStats connStats,
                                         double maxUpBps, double maxDownBps)
    {
        if (app.TcpConnections.Count == 0) return;

        int connCount = Math.Max(1, app.TcpConnections.Count);

        // Distribute the limit evenly across active connections.
        double upBpsPerConn   = maxUpBps   > 0 ? maxUpBps   / connCount : 0;
        double downBpsPerConn = maxDownBps > 0 ? maxDownBps / connCount : 0;

        // Use the more restrictive direction's CWND (the upload limit drives send-side CWND,
        // and we approximate the download direction by limiting the receive window indirectly).
        double limitBps = 0;
        if (upBpsPerConn > 0 && downBpsPerConn > 0)
            limitBps = Math.Min(upBpsPerConn, downBpsPerConn);
        else
            limitBps = Math.Max(upBpsPerConn, downBpsPerConn);

        // CWND (bytes) ≈ BitsPerSec / 8 × RTT_seconds
        uint cwnd = 0;
        if (limitBps > 0)
        {
            double cwndBytes = (limitBps / BitsPerByte) * DefaultRttSeconds;
            cwnd = (uint)Math.Max(MinTcpMss, Math.Ceiling(cwndBytes));
        }

        foreach (var conn in app.TcpConnections)
        {
            connStats.SetConnectionCwndLimit(conn, cwnd);
        }
    }

    private static void ClearCwndForApp(AppNetworkInfo app, IPerConnectionStats connStats)
    {
        foreach (var conn in app.TcpConnections)
        {
            connStats.SetConnectionCwndLimit(conn, 0); // 0 = no limit
        }
    }

    private void RemoveWfpFilter(int pid)
    {
        if (!_wfpAvailable || _engineHandle == IntPtr.Zero) return;
        if (_activeFilterIds.TryGetValue(pid, out var filterId))
        {
#if WINDOWS7_0_OR_GREATER
            try { Native.WfpNative.FwpmFilterDeleteById0(_engineHandle, filterId); } catch { }
#endif
            _activeFilterIds.Remove(pid);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_wfpAvailable && _engineHandle != IntPtr.Zero)
        {
#if WINDOWS7_0_OR_GREATER
            foreach (var pid in new List<int>(_activeFilterIds.Keys))
                RemoveWfpFilter(pid);
            try { Native.WfpNative.FwpmEngineClose0(_engineHandle); } catch { }
#endif
            _engineHandle = IntPtr.Zero;
        }
    }
}

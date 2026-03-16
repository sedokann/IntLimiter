using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    private Timer? _tokenBucketTimer;

    // Token buckets: pid -> (uploadTokens, downloadTokens)
    private readonly Dictionary<int, (double upload, double download)> _tokenBuckets = new();
    // Active WFP filter IDs per pid
    private readonly Dictionary<int, ulong> _activeFilterIds = new();

    private static readonly Guid SubLayerGuid = new("A1B2C3D4-E5F6-7890-ABCD-EF1234567890");

    public BandwidthLimiterService()
    {
        TryOpenWfpEngine();
        _tokenBucketTimer = new Timer(TokenBucketTick, null, 100, 100);
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
            ProcessId = pid,
            AppPath = appPath,
            MaxUploadBps = maxUpBps,
            MaxDownloadBps = maxDownBps,
            IsEnabled = true
        };
        _limits[pid] = limit;
        _tokenBuckets[pid] = (maxUpBps * 0.1, maxDownBps * 0.1);
    }

    public void RemoveLimit(int pid)
    {
        _limits.Remove(pid);
        _tokenBuckets.Remove(pid);
        RemoveWfpFilter(pid);
    }

    public void SetGlobalLimit(double maxUpBps, double maxDownBps)
    {
        _globalLimit = new BandwidthLimit
        {
            ProcessId = -1,
            MaxUploadBps = maxUpBps,
            MaxDownloadBps = maxDownBps,
            IsEnabled = maxUpBps > 0 || maxDownBps > 0
        };
    }

    public IReadOnlyDictionary<int, BandwidthLimit> GetLimits() => _limits;
    public BandwidthLimit? GetGlobalLimit() => _globalLimit;

    private void TokenBucketTick(object? state)
    {
        if (_disposed) return;
        const double intervalSeconds = 0.1;

        foreach (var (pid, limit) in _limits)
        {
            if (!limit.IsEnabled) continue;
            var (upload, download) = _tokenBuckets.GetValueOrDefault(pid, (0, 0));
            double maxBucketUp = limit.MaxUploadBps > 0 ? limit.MaxUploadBps * intervalSeconds * 2 : double.MaxValue;
            double maxBucketDown = limit.MaxDownloadBps > 0 ? limit.MaxDownloadBps * intervalSeconds * 2 : double.MaxValue;
            upload = Math.Min(maxBucketUp, upload + (limit.MaxUploadBps * intervalSeconds));
            download = Math.Min(maxBucketDown, download + (limit.MaxDownloadBps * intervalSeconds));
            _tokenBuckets[pid] = (upload, download);
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
        _tokenBucketTimer?.Dispose();
        _tokenBucketTimer = null;

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

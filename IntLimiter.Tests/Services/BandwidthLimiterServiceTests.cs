using System;
using System.Collections.Generic;
using System.Linq;
using IntLimiter.Services;
using Xunit;

namespace IntLimiter.Tests.Services;

public class BandwidthLimiterServiceTests
{
    [Fact]
    public void Constructor_DoesNotThrow()
    {
        using var svc = new BandwidthLimiterService();
        Assert.NotNull(svc);
    }

    [Fact]
    public void SetLimit_StoresLimit()
    {
        using var svc = new BandwidthLimiterService();
        svc.SetLimit(1234, @"C:\app.exe", 1_000_000, 5_000_000);
        var limits = svc.GetLimits();
        Assert.True(limits.ContainsKey(1234));
        Assert.Equal(1_000_000, limits[1234].MaxUploadBps);
        Assert.Equal(5_000_000, limits[1234].MaxDownloadBps);
        Assert.True(limits[1234].IsEnabled);
    }

    [Fact]
    public void RemoveLimit_RemovesLimit()
    {
        using var svc = new BandwidthLimiterService();
        svc.SetLimit(999, @"C:\test.exe", 100, 200);
        Assert.True(svc.GetLimits().ContainsKey(999));
        svc.RemoveLimit(999);
        Assert.False(svc.GetLimits().ContainsKey(999));
    }

    [Fact]
    public void SetGlobalLimit_StoresGlobalLimit()
    {
        using var svc = new BandwidthLimiterService();
        svc.SetGlobalLimit(10_000_000, 50_000_000);
        var global = svc.GetGlobalLimit();
        Assert.NotNull(global);
        Assert.Equal(10_000_000, global!.MaxUploadBps);
        Assert.Equal(50_000_000, global.MaxDownloadBps);
        Assert.True(global.IsEnabled);
    }

    [Fact]
    public void SetGlobalLimit_ZeroZero_DisablesGlobal()
    {
        using var svc = new BandwidthLimiterService();
        svc.SetGlobalLimit(0, 0);
        var global = svc.GetGlobalLimit();
        Assert.NotNull(global);
        Assert.False(global!.IsEnabled);
    }

    [Fact]
    public void SetLimit_MultipleApps()
    {
        using var svc = new BandwidthLimiterService();
        svc.SetLimit(100, @"C:\a.exe", 1000, 2000);
        svc.SetLimit(200, @"C:\b.exe", 3000, 4000);
        svc.SetLimit(300, @"C:\c.exe", 5000, 6000);
        var limits = svc.GetLimits();
        Assert.Equal(3, limits.Count);
    }

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        var svc = new BandwidthLimiterService();
        svc.Dispose();
        svc.Dispose(); // should not throw
    }

    [Fact]
    public void ApplyThrottling_NoLimits_DoesNotCallSetCwnd()
    {
        // When no limits are set, ApplyThrottling should not call SetConnectionCwndLimit.
        using var svc = new BandwidthLimiterService();

        var conn = new TcpConnectionInfo(42, 0x0100007F, 12345, 0x08080808, 443, 5);
        var app = new IntLimiter.Models.AppNetworkInfo
        {
            ProcessId   = 42,
            ProcessName = "test",
            TcpConnections = new List<TcpConnectionInfo> { conn },
        };

        int cwndSetCallCount = 0;
        var fakeStats = new FakePerConnectionStats(
            onSet: (_, _) => cwndSetCallCount++);

        svc.ApplyThrottling(new List<IntLimiter.Models.AppNetworkInfo> { app }, fakeStats);

        Assert.Equal(0, cwndSetCallCount);
    }

    [Fact]
    public void ApplyThrottling_WithPerAppLimit_CallsSetCwnd()
    {
        using var svc = new BandwidthLimiterService();
        svc.SetLimit(42, @"C:\app.exe", 1_000_000, 1_000_000); // 1 Mbps up/down

        var conn = new TcpConnectionInfo(42, 0x0100007F, 12345, 0x08080808, 443, 5);
        var app = new IntLimiter.Models.AppNetworkInfo
        {
            ProcessId   = 42,
            ProcessName = "test",
            TcpConnections = new List<TcpConnectionInfo> { conn },
        };

        uint? lastCwnd = null;
        var fakeStats = new FakePerConnectionStats(
            onSet: (_, cwnd) => lastCwnd = cwnd);

        svc.ApplyThrottling(new List<IntLimiter.Models.AppNetworkInfo> { app }, fakeStats);

        Assert.NotNull(lastCwnd);
        // CWND should be > 0 (limit applied) and ≥ 1 MSS (1460 bytes)
        Assert.True(lastCwnd > 0, "Expected non-zero CWND limit");
        Assert.True(lastCwnd >= 1460, $"CWND {lastCwnd} should be at least 1 MSS (1460 bytes)");
    }

    [Fact]
    public void ApplyThrottling_AfterRemoveLimit_ClearsCwnd()
    {
        using var svc = new BandwidthLimiterService();
        svc.SetLimit(42, @"C:\app.exe", 1_000_000, 1_000_000);

        var conn = new TcpConnectionInfo(42, 0x0100007F, 12345, 0x08080808, 443, 5);
        var app = new IntLimiter.Models.AppNetworkInfo
        {
            ProcessId   = 42,
            ProcessName = "test",
            TcpConnections = new List<TcpConnectionInfo> { conn },
        };

        uint? lastCwnd = null;
        var fakeStats = new FakePerConnectionStats(
            onSet: (_, cwnd) => lastCwnd = cwnd);

        // First call with limit active — this registers the app as throttled.
        svc.ApplyThrottling(new List<IntLimiter.Models.AppNetworkInfo> { app }, fakeStats);
        Assert.True((lastCwnd ?? 0) > 0, "Expected non-zero CWND on first call with limit");

        // Now remove the limit and apply again.
        svc.RemoveLimit(42);
        lastCwnd = null;
        svc.ApplyThrottling(new List<IntLimiter.Models.AppNetworkInfo> { app }, fakeStats);

        // CWND should be cleared (set to 0) after limit was removed.
        Assert.Equal(0u, lastCwnd);
    }

    private class FakePerConnectionStats : IPerConnectionStats
    {
        private readonly Action<TcpConnectionInfo, uint> _onSet;
        public FakePerConnectionStats(Action<TcpConnectionInfo, uint> onSet) => _onSet = onSet;
        public (ulong bytesOut, ulong bytesIn) GetConnectionByteStats(TcpConnectionInfo conn) => (0, 0);
        public void SetConnectionCwndLimit(TcpConnectionInfo conn, uint limCwnd) => _onSet(conn, limCwnd);
    }
}

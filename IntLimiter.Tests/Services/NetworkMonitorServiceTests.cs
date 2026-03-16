using System;
using System.Collections.Generic;
using System.Threading;
using IntLimiter.Models;
using IntLimiter.Services;
using Moq;
using Xunit;

namespace IntLimiter.Tests.Services;

public class NetworkMonitorServiceTests
{
    private static Mock<INetworkDataSource> CreateMock(
        List<TcpConnectionInfo>? tcp = null,
        List<UdpEndpointInfo>? udp = null,
        List<InterfaceStats>? iface = null)
    {
        var mock = new Mock<INetworkDataSource>();
        mock.Setup(m => m.GetTcpConnections()).Returns(tcp ?? new List<TcpConnectionInfo>());
        mock.Setup(m => m.GetUdpConnections()).Returns(udp ?? new List<UdpEndpointInfo>());
        mock.Setup(m => m.GetNetworkInterfaceStats()).Returns(iface ?? new List<InterfaceStats>());
        return mock;
    }

    /// <summary>
    /// A fake data source that implements both INetworkDataSource and IPerConnectionStats.
    /// Lets us inject controlled per-connection byte counts to verify rate computation.
    /// </summary>
    private class FakeDataSource : INetworkDataSource, IPerConnectionStats
    {
        private readonly List<TcpConnectionInfo> _tcp;
        private readonly Dictionary<(uint, ushort, uint, ushort), (ulong bytesOut, ulong bytesIn)> _stats = new();

        public FakeDataSource(List<TcpConnectionInfo> tcp) => _tcp = tcp;

        public IReadOnlyList<TcpConnectionInfo> GetTcpConnections() => _tcp;
        public IReadOnlyList<UdpEndpointInfo> GetUdpConnections() => Array.Empty<UdpEndpointInfo>();
        public IReadOnlyList<InterfaceStats> GetNetworkInterfaceStats() =>
            new List<InterfaceStats> { new InterfaceStats("Eth", 0, 0) };

        public void SetBytes(TcpConnectionInfo conn, ulong bytesOut, ulong bytesIn) =>
            _stats[(conn.LocalAddr, conn.LocalPort, conn.RemoteAddr, conn.RemotePort)] = (bytesOut, bytesIn);

        public (ulong bytesOut, ulong bytesIn) GetConnectionByteStats(TcpConnectionInfo conn) =>
            _stats.TryGetValue((conn.LocalAddr, conn.LocalPort, conn.RemoteAddr, conn.RemotePort), out var v)
                ? v
                : (0ul, 0ul);

        public void SetConnectionCwndLimit(TcpConnectionInfo conn, uint limCwnd) { /* no-op in tests */ }
    }

    [Fact]
    public void Constructor_ThrowsOnNullDataSource()
    {
        Assert.Throws<ArgumentNullException>(() => new NetworkMonitorService(null!));
    }

    [Fact]
    public void Start_And_Stop_DoNotThrow()
    {
        var mock = CreateMock();
        using var svc = new NetworkMonitorService(mock.Object);
        svc.Start();
        Thread.Sleep(50);
        svc.Stop();
    }

    [Fact]
    public void NetworkDataUpdated_FiresAfterStart()
    {
        var tcp = new List<TcpConnectionInfo>
        {
            new TcpConnectionInfo(1234, 0, 80, 0, 0, 1)
        };
        var mock = CreateMock(tcp: tcp, iface: new List<InterfaceStats>
        {
            new InterfaceStats("Ethernet", 1000, 500)
        });

        using var svc = new NetworkMonitorService(mock.Object) { IntervalMs = 50 };
        IReadOnlyList<AppNetworkInfo>? receivedApps = null;
        NetworkStats? receivedStats = null;

        var evt = new ManualResetEventSlim(false);
        svc.NetworkDataUpdated += (apps, stats) =>
        {
            receivedApps = apps;
            receivedStats = stats;
            evt.Set();
        };

        svc.Start();
        bool fired = evt.Wait(TimeSpan.FromSeconds(2));
        svc.Stop();

        Assert.True(fired, "NetworkDataUpdated event did not fire within timeout");
        Assert.NotNull(receivedApps);
        Assert.NotNull(receivedStats);
    }

    [Fact]
    public void GlobalStats_AdapterName_FromBusiestInterface()
    {
        var ifaces = new List<InterfaceStats>
        {
            new InterfaceStats("Wi-Fi", 100, 50),
            new InterfaceStats("Ethernet", 10000, 5000),
        };
        var mock = CreateMock(iface: ifaces);

        using var svc = new NetworkMonitorService(mock.Object) { IntervalMs = 50 };
        NetworkStats? stats = null;
        var evt = new ManualResetEventSlim(false);
        svc.NetworkDataUpdated += (_, s) => { stats = s; evt.Set(); };
        svc.Start();
        evt.Wait(TimeSpan.FromSeconds(2));
        svc.Stop();

        Assert.NotNull(stats);
        Assert.Equal("Ethernet", stats!.AdapterName);
    }

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        var mock = CreateMock();
        var svc = new NetworkMonitorService(mock.Object);
        svc.Dispose();
        svc.Dispose(); // should not throw
    }

    [Fact]
    public void Start_AfterDispose_Throws()
    {
        var mock = CreateMock();
        var svc = new NetworkMonitorService(mock.Object);
        svc.Dispose();
        Assert.Throws<ObjectDisposedException>(() => svc.Start());
    }

    [Fact]
    public void EmptyConnections_ProducesEmptyAppList()
    {
        var mock = CreateMock();
        using var svc = new NetworkMonitorService(mock.Object) { IntervalMs = 50 };
        IReadOnlyList<AppNetworkInfo>? apps = null;
        var evt = new ManualResetEventSlim(false);
        svc.NetworkDataUpdated += (a, _) => { apps = a; evt.Set(); };
        svc.Start();
        evt.Wait(TimeSpan.FromSeconds(2));
        svc.Stop();

        Assert.NotNull(apps);
        Assert.Empty(apps!);
    }

    [Fact]
    public void PerConnectionStats_ZeroOnFirstTick_ThenNonZeroOnSecondTick()
    {
        // Arrange: a fake data source where conn1 accumulates bytes across ticks.
        var conn = new TcpConnectionInfo(12345, 0x0100007F, 54321, 0x08080808, 443, 5);
        var fakeSource = new FakeDataSource(new List<TcpConnectionInfo> { conn });

        // First tick: no previous counter → rate should be 0.
        fakeSource.SetBytes(conn, 0, 0);

        using var svc = new NetworkMonitorService(fakeSource) { IntervalMs = 50 };

        IReadOnlyList<AppNetworkInfo>? firstApps = null;
        IReadOnlyList<AppNetworkInfo>? secondApps = null;
        int tickCount = 0;

        var evt = new ManualResetEventSlim(false);
        svc.NetworkDataUpdated += (apps, _) =>
        {
            tickCount++;
            if (tickCount == 1)
            {
                firstApps = apps;
                // Simulate traffic arriving before the second tick.
                fakeSource.SetBytes(conn, 10_000, 50_000); // 10KB sent, 50KB received
            }
            else if (tickCount == 2)
            {
                secondApps = apps;
                evt.Set();
            }
        };

        svc.Start();
        bool fired = evt.Wait(TimeSpan.FromSeconds(3));
        svc.Stop();

        Assert.True(fired, "Second tick did not fire within timeout");
        Assert.NotNull(firstApps);
        Assert.NotNull(secondApps);

        // After first tick (first reading), there is no previous counter so rate = 0.
        var firstApp = Assert.Single(firstApps!);
        Assert.Equal(0, firstApp.SendRateBps);
        Assert.Equal(0, firstApp.ReceiveRateBps);

        // After second tick the delta bytes > 0 so rates must be positive.
        var secondApp = Assert.Single(secondApps!);
        Assert.True(secondApp.SendRateBps > 0,
            $"Expected SendRateBps > 0 after byte delta, got {secondApp.SendRateBps}");
        Assert.True(secondApp.ReceiveRateBps > 0,
            $"Expected ReceiveRateBps > 0 after byte delta, got {secondApp.ReceiveRateBps}");
    }
}

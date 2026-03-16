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
}

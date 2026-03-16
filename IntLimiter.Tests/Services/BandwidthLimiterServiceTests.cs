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
}

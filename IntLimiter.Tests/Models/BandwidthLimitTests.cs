using IntLimiter.Models;
using Xunit;

namespace IntLimiter.Tests.Models;

public class BandwidthLimitTests
{
    [Fact]
    public void BandwidthLimit_DefaultValues()
    {
        var limit = new BandwidthLimit();
        Assert.Equal(0, limit.ProcessId);
        Assert.Equal(string.Empty, limit.AppPath);
        Assert.Equal(0.0, limit.MaxUploadBps);
        Assert.Equal(0.0, limit.MaxDownloadBps);
        Assert.True(limit.IsEnabled);
    }

    [Fact]
    public void BandwidthLimit_SetValues()
    {
        var limit = new BandwidthLimit
        {
            ProcessId = 9876,
            AppPath = @"C:\Program Files\App\app.exe",
            MaxUploadBps = 1_000_000,
            MaxDownloadBps = 5_000_000,
            IsEnabled = true
        };

        Assert.Equal(9876, limit.ProcessId);
        Assert.Equal(1_000_000, limit.MaxUploadBps);
        Assert.Equal(5_000_000, limit.MaxDownloadBps);
        Assert.True(limit.IsEnabled);
    }

    [Fact]
    public void BandwidthLimit_ZeroMeansUnlimited()
    {
        var limit = new BandwidthLimit { MaxUploadBps = 0, MaxDownloadBps = 0 };
        Assert.Equal(0.0, limit.MaxUploadBps);
        Assert.Equal(0.0, limit.MaxDownloadBps);
    }

    [Fact]
    public void BandwidthLimit_CanDisable()
    {
        var limit = new BandwidthLimit { IsEnabled = false };
        Assert.False(limit.IsEnabled);
    }
}

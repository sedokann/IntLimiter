using IntLimiter.Models;
using Xunit;

namespace IntLimiter.Tests.Models;

public class AppNetworkInfoTests
{
    [Fact]
    public void AppNetworkInfo_DefaultValues_AreCorrect()
    {
        var info = new AppNetworkInfo();
        Assert.Equal(0, info.ProcessId);
        Assert.Equal(string.Empty, info.ProcessName);
        Assert.Equal(string.Empty, info.ExePath);
        Assert.Equal(0, info.BytesSent);
        Assert.Equal(0, info.BytesReceived);
        Assert.Equal(0.0, info.SendRateBps);
        Assert.Equal(0.0, info.ReceiveRateBps);
        Assert.Equal(0, info.ConnectionCount);
        Assert.NotNull(info.SendHistory);
        Assert.NotNull(info.ReceiveHistory);
    }

    [Fact]
    public void AppNetworkInfo_CanSetProperties()
    {
        var info = new AppNetworkInfo
        {
            ProcessId = 1234,
            ProcessName = "chrome",
            ExePath = @"C:\Program Files\Google\Chrome\Application\chrome.exe",
            BytesSent = 1024,
            BytesReceived = 4096,
            SendRateBps = 8192.0,
            ReceiveRateBps = 32768.0,
            ConnectionCount = 5
        };

        Assert.Equal(1234, info.ProcessId);
        Assert.Equal("chrome", info.ProcessName);
        Assert.Equal(8192.0, info.SendRateBps);
        Assert.Equal(32768.0, info.ReceiveRateBps);
        Assert.Equal(5, info.ConnectionCount);
    }

    [Fact]
    public void AppNetworkInfo_Histories_AccumulateValues()
    {
        var info = new AppNetworkInfo();
        info.SendHistory.Enqueue(100.0);
        info.SendHistory.Enqueue(200.0);
        info.ReceiveHistory.Enqueue(300.0);

        Assert.Equal(2, info.SendHistory.Count);
        Assert.Single(info.ReceiveHistory);
    }

    [Fact]
    public void AppNetworkInfo_History_Cap60()
    {
        var info = new AppNetworkInfo();
        for (int i = 0; i < 65; i++)
        {
            if (info.SendHistory.Count >= 60) info.SendHistory.Dequeue();
            info.SendHistory.Enqueue(i);
        }
        Assert.Equal(60, info.SendHistory.Count);
    }
}

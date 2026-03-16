using System.Collections.Generic;

namespace IntLimiter.Models;

public class AppNetworkInfo
{
    public int ProcessId { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public string ExePath { get; set; } = string.Empty;
    public long BytesSent { get; set; }
    public long BytesReceived { get; set; }
    public double SendRateBps { get; set; }
    public double ReceiveRateBps { get; set; }
    public int ConnectionCount { get; set; }
    public Queue<double> SendHistory { get; } = new(60);
    public Queue<double> ReceiveHistory { get; } = new(60);
}

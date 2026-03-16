using System;

namespace IntLimiter.Models;

public class NetworkStats
{
    public string AdapterName { get; set; } = string.Empty;
    public double SendRateBps { get; set; }
    public double ReceiveRateBps { get; set; }
    public long TotalBytesSent { get; set; }
    public long TotalBytesReceived { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

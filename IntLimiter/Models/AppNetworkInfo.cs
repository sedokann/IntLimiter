using System.Collections.Generic;
using IntLimiter.Services;

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
    /// <summary>Active TCP connections for this process — used by the limiter for CWND throttling.</summary>
    public IReadOnlyList<TcpConnectionInfo> TcpConnections { get; set; } = System.Array.Empty<TcpConnectionInfo>();
    public Queue<double> SendHistory { get; } = new(60);
    public Queue<double> ReceiveHistory { get; } = new(60);
}

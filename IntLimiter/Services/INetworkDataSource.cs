using System.Collections.Generic;
using IntLimiter.Models;

namespace IntLimiter.Services;

public record TcpConnectionInfo(int OwningPid, uint LocalAddr, ushort LocalPort, uint RemoteAddr, ushort RemotePort, uint State);
public record UdpEndpointInfo(int OwningPid, uint LocalAddr, ushort LocalPort);
public record InterfaceStats(string Name, long InOctets, long OutOctets);

public interface INetworkDataSource
{
    IReadOnlyList<TcpConnectionInfo> GetTcpConnections();
    IReadOnlyList<UdpEndpointInfo> GetUdpConnections();
    IReadOnlyList<InterfaceStats> GetNetworkInterfaceStats();
}

/// <summary>
/// Optional interface implemented by <see cref="WindowsNetworkDataSource"/> that
/// provides per-connection byte-count statistics via the Windows TCP eStats APIs.
/// When the data source does not implement this interface the monitor falls back to
/// reporting zero per-process rates (same as before).
/// </summary>
public interface IPerConnectionStats
{
    /// <summary>
    /// Returns the cumulative DataBytesOut / DataBytesIn for <paramref name="conn"/>.
    /// Also enables stats collection on first call for this connection.
    /// </summary>
    (ulong bytesOut, ulong bytesIn) GetConnectionByteStats(TcpConnectionInfo conn);

    /// <summary>
    /// Applies (or clears) a TCP congestion-window limit to throttle a single connection.
    /// </summary>
    void SetConnectionCwndLimit(TcpConnectionInfo conn, uint limCwnd);
}

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

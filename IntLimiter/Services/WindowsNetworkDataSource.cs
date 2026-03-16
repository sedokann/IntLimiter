using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.NetworkInformation;
using IntLimiter.Native;

namespace IntLimiter.Services;

public class WindowsNetworkDataSource : INetworkDataSource
{
    public IReadOnlyList<TcpConnectionInfo> GetTcpConnections()
    {
        var result = new List<TcpConnectionInfo>();
#if WINDOWS7_0_OR_GREATER
        try
        {
            result.AddRange(IpHelperNative.GetExtendedTcpTable());
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"GetTcpConnections error: {ex.Message}");
        }
#endif
        return result;
    }

    public IReadOnlyList<UdpEndpointInfo> GetUdpConnections()
    {
        var result = new List<UdpEndpointInfo>();
#if WINDOWS7_0_OR_GREATER
        try
        {
            result.AddRange(IpHelperNative.GetExtendedUdpTable());
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"GetUdpConnections error: {ex.Message}");
        }
#endif
        return result;
    }

    public IReadOnlyList<InterfaceStats> GetNetworkInterfaceStats()
    {
        var result = new List<InterfaceStats>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                var stats = nic.GetIPStatistics();
                result.Add(new InterfaceStats(nic.Name, stats.BytesReceived, stats.BytesSent));
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"GetNetworkInterfaceStats error: {ex.Message}");
        }
        return result;
    }
}

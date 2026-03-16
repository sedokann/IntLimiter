using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using IntLimiter.Services;

namespace IntLimiter.Native;

#if WINDOWS7_0_OR_GREATER
public static class IpHelperNative
{
    private const int AF_INET = 2;
    private const int TCP_TABLE_OWNER_PID_ALL = 5;
    private const int UDP_TABLE_OWNER_PID = 1;

    [StructLayout(LayoutKind.Sequential)]
    private struct MIB_TCPROW_OWNER_PID
    {
        public uint dwState;
        public uint dwLocalAddr;
        public uint dwLocalPort;
        public uint dwRemoteAddr;
        public uint dwRemotePort;
        public uint dwOwningPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MIB_UDPROW_OWNER_PID
    {
        public uint dwLocalAddr;
        public uint dwLocalPort;
        public uint dwOwningPid;
    }

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(
        IntPtr pTcpTable, ref uint pdwSize, bool bOrder, uint ulAf, uint TableClass, uint Reserved);

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedUdpTable(
        IntPtr pUdpTable, ref uint pdwSize, bool bOrder, uint ulAf, uint TableClass, uint Reserved);

    public static IEnumerable<TcpConnectionInfo> GetExtendedTcpTable()
    {
        uint size = 0;
        GetExtendedTcpTable(IntPtr.Zero, ref size, false, AF_INET, TCP_TABLE_OWNER_PID_ALL, 0);
        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            uint result = GetExtendedTcpTable(buffer, ref size, false, AF_INET, TCP_TABLE_OWNER_PID_ALL, 0);
            if (result != 0) yield break;
            int count = Marshal.ReadInt32(buffer);
            int rowSize = Marshal.SizeOf<MIB_TCPROW_OWNER_PID>();
            for (int i = 0; i < count; i++)
            {
                var row = Marshal.PtrToStructure<MIB_TCPROW_OWNER_PID>(buffer + 4 + i * rowSize);
                yield return new TcpConnectionInfo(
                    (int)row.dwOwningPid,
                    row.dwLocalAddr,
                    (ushort)((row.dwLocalPort >> 8) | ((row.dwLocalPort & 0xFF) << 8)),
                    row.dwRemoteAddr,
                    (ushort)((row.dwRemotePort >> 8) | ((row.dwRemotePort & 0xFF) << 8)),
                    row.dwState);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public static IEnumerable<UdpEndpointInfo> GetExtendedUdpTable()
    {
        uint size = 0;
        GetExtendedUdpTable(IntPtr.Zero, ref size, false, AF_INET, UDP_TABLE_OWNER_PID, 0);
        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            uint result = GetExtendedUdpTable(buffer, ref size, false, AF_INET, UDP_TABLE_OWNER_PID, 0);
            if (result != 0) yield break;
            int count = Marshal.ReadInt32(buffer);
            int rowSize = Marshal.SizeOf<MIB_UDPROW_OWNER_PID>();
            for (int i = 0; i < count; i++)
            {
                var row = Marshal.PtrToStructure<MIB_UDPROW_OWNER_PID>(buffer + 4 + i * rowSize);
                yield return new UdpEndpointInfo(
                    (int)row.dwOwningPid,
                    row.dwLocalAddr,
                    (ushort)((row.dwLocalPort >> 8) | ((row.dwLocalPort & 0xFF) << 8)));
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
#endif

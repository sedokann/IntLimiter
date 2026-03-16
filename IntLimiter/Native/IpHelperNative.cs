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

    // ── TCP Extended Statistics (GetPerTcpConnectionEStats / SetPerTcpConnectionEStats) ──

    public enum TcpEstatsType
    {
        TcpConnectionEstatsSynOpts = 0,
        TcpConnectionEstatsData    = 1,
        TcpConnectionEstatsAck     = 2,
        TcpConnectionEstatsRec     = 3,
        TcpConnectionEstatsObsRec  = 4,
        TcpConnectionEstatsBandwidth = 7,
    }

    // Used for enabling/disabling TcpConnectionEstatsData collection and reading byte counts.
    [StructLayout(LayoutKind.Sequential)]
    public struct TCP_ESTATS_DATA_RW_v0
    {
        public byte EnableCollection; // BOOLEAN
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TCP_ESTATS_DATA_ROD_v0
    {
        public ulong DataBytesOut;
        public ulong DataSegsOut;
        public ulong DataBytesIn;
        public ulong DataSegsIn;
        public ulong SegsOut;
        public ulong DataOut;
        public ulong SegsIn;
        public ulong SoftErrors;
        public ulong SoftErrorReason;
        public ulong SndUna;
        public ulong SndNxt;
        public ulong SndMax;
        public ulong ThruBytesAcked;
        public ulong RcvNxt;
        public ulong ThruBytesReceived;
    }

    // Used for CWND-based bandwidth throttling via TcpConnectionEstatsObsRec.
    // Layout matches the Windows TCP_ESTATS_OBS_REC_RW_v0 native structure:
    //   offset 0: BOOLEAN EnableCollection (1 byte)
    //   offset 1-3: implicit padding (3 bytes) to align the following ULONG
    //   offset 4: ULONG LimCwnd (4 bytes)
    // Total: 8 bytes.
    [StructLayout(LayoutKind.Explicit, Size = 8)]
    public struct TCP_ESTATS_OBS_REC_RW_v0
    {
        [FieldOffset(0)] public byte EnableCollection; // BOOLEAN
        // 3 bytes of implicit padding at offsets 1-3 (matches native struct alignment)
        [FieldOffset(4)] public uint LimCwnd;          // Congestion window limit in bytes (0 = no limit)
    }

    // MIB_TCPROW (without OwningPid) — required by the eStats APIs.
    [StructLayout(LayoutKind.Sequential)]
    public struct MIB_TCPROW
    {
        public uint dwState;
        public uint dwLocalAddr;
        public uint dwLocalPort;    // network byte order
        public uint dwRemoteAddr;
        public uint dwRemotePort;   // network byte order
    }

    [DllImport("iphlpapi.dll")]
    public static extern uint GetPerTcpConnectionEStats(
        ref MIB_TCPROW Row,
        TcpEstatsType EstatsType,
        IntPtr Rw,  uint RwVersion,  uint RwSize,
        IntPtr Ros, uint RosVersion, uint RosSize,
        IntPtr Rod, uint RodVersion, uint RodSize);

    [DllImport("iphlpapi.dll")]
    public static extern uint SetPerTcpConnectionEStats(
        ref MIB_TCPROW Row,
        TcpEstatsType EstatsType,
        IntPtr Rw, uint RwVersion, uint RwSize,
        uint Offset);

    /// <summary>
    /// Converts a <see cref="TcpConnectionInfo"/> (with host-order ports) to a
    /// <see cref="MIB_TCPROW"/> suitable for the eStats APIs (network-order ports).
    /// </summary>
    public static MIB_TCPROW ToMibTcpRow(TcpConnectionInfo conn) =>
        new MIB_TCPROW
        {
            dwState       = conn.State,
            dwLocalAddr   = conn.LocalAddr,
            dwLocalPort   = PortToNetwork(conn.LocalPort),
            dwRemoteAddr  = conn.RemoteAddr,
            dwRemotePort  = PortToNetwork(conn.RemotePort),
        };

    private static uint PortToNetwork(ushort hostPort) =>
        (uint)(((hostPort & 0xFF) << 8) | ((hostPort >> 8) & 0xFF));

    /// <summary>
    /// Enables data-byte-count collection for a TCP connection, then reads the
    /// cumulative DataBytesOut / DataBytesIn values.
    /// Returns (0, 0) on any failure (no admin, connection gone, etc.).
    /// </summary>
    public static unsafe (ulong bytesOut, ulong bytesIn) GetTcpConnectionByteStats(TcpConnectionInfo conn)
    {
        var row = ToMibTcpRow(conn);

        // Enable stats if not already enabled.
        var rw = new TCP_ESTATS_DATA_RW_v0 { EnableCollection = 1 };
        uint setResult = SetPerTcpConnectionEStats(
            ref row,
            TcpEstatsType.TcpConnectionEstatsData,
            new IntPtr(&rw), 0, (uint)sizeof(TCP_ESTATS_DATA_RW_v0),
            0);
        // 0 = success, 0x00000057 = ERROR_INVALID_PARAMETER (connection gone/wrong state)
        if (setResult != 0 && setResult != 0x00000057) return (0, 0);

        // Read the dynamic (Rod) statistics.
        var rod = new TCP_ESTATS_DATA_ROD_v0();
        uint getResult = GetPerTcpConnectionEStats(
            ref row,
            TcpEstatsType.TcpConnectionEstatsData,
            IntPtr.Zero, 0, 0,
            IntPtr.Zero, 0, 0,
            new IntPtr(&rod), 0, (uint)sizeof(TCP_ESTATS_DATA_ROD_v0));

        return getResult == 0 ? (rod.DataBytesOut, rod.DataBytesIn) : (0, 0);
    }

    /// <summary>
    /// Sets a congestion-window limit (LimCwnd) on a TCP connection to throttle its throughput.
    /// Pass limCwnd = 0 to remove the limit.
    /// </summary>
    public static unsafe void SetTcpConnectionCwndLimit(TcpConnectionInfo conn, uint limCwnd)
    {
        var row = ToMibTcpRow(conn);

        // First ensure ObsRec collection is enabled.
        var rw = new TCP_ESTATS_OBS_REC_RW_v0 { EnableCollection = 1, LimCwnd = limCwnd };
        SetPerTcpConnectionEStats(
            ref row,
            TcpEstatsType.TcpConnectionEstatsObsRec,
            new IntPtr(&rw), 0, (uint)sizeof(TCP_ESTATS_OBS_REC_RW_v0),
            0);
    }

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

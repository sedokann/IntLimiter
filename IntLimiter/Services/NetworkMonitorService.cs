using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using IntLimiter.Models;

namespace IntLimiter.Services;

public class NetworkMonitorService : IDisposable
{
    private readonly INetworkDataSource _dataSource;
    private readonly IPerConnectionStats? _perConnStats;

    /// <summary>
    /// Exposes the optional per-connection stats interface if the underlying data source
    /// implements it (e.g. <see cref="WindowsNetworkDataSource"/>).
    /// </summary>
    public IPerConnectionStats? PerConnectionStats => _perConnStats;
    private Timer? _timer;
    private bool _disposed;

    // Per-connection byte counters: key = (localAddr, localPort, remoteAddr, remotePort)
    private readonly Dictionary<(uint, ushort, uint, ushort), (ulong sent, ulong recv, DateTime time)> _prevPerConn = new();

    private (long inOctets, long outOctets, DateTime time) _prevGlobal;
    private bool _hasPrevGlobal;
    private DateTime _lastTick = DateTime.UtcNow;

    public event Action<IReadOnlyList<AppNetworkInfo>, NetworkStats>? NetworkDataUpdated;

    public int IntervalMs { get; set; } = 1000;

    public NetworkMonitorService(INetworkDataSource dataSource)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        _perConnStats = dataSource as IPerConnectionStats;
    }

    public void Start()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(NetworkMonitorService));
        _timer?.Dispose();
        _timer = new Timer(Tick, null, 0, IntervalMs);
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    private void Tick(object? state)
    {
        try
        {
            var now = DateTime.UtcNow;
            var elapsed = (now - _lastTick).TotalSeconds;
            if (elapsed <= 0) elapsed = IntervalMs / 1000.0;
            _lastTick = now;

            var tcpConns = _dataSource.GetTcpConnections();
            var udpConns = _dataSource.GetUdpConnections();
            var ifaceStats = _dataSource.GetNetworkInterfaceStats();

            // Aggregate connection counts per PID
            var pidTcpConns = tcpConns.GroupBy(c => c.OwningPid).ToDictionary(g => g.Key, g => g.ToList());
            var pidUdpCount = udpConns.GroupBy(c => c.OwningPid).ToDictionary(g => g.Key, g => g.Count());

            var allPids = pidTcpConns.Keys.Union(pidUdpCount.Keys).ToHashSet();

            // ── Per-connection byte deltas (requires admin + Windows TCP eStats API) ──
            var pidSentDelta = new Dictionary<int, long>();
            var pidRecvDelta = new Dictionary<int, long>();
            var activeConnKeys = new HashSet<(uint, ushort, uint, ushort)>();

            if (_perConnStats != null)
            {
                foreach (var conn in tcpConns)
                {
                    var key = (conn.LocalAddr, conn.LocalPort, conn.RemoteAddr, conn.RemotePort);
                    activeConnKeys.Add(key);

                    var (bytesOut, bytesIn) = _perConnStats.GetConnectionByteStats(conn);

                    if (_prevPerConn.TryGetValue(key, out var prev))
                    {
                        long deltaSent = (long)(bytesOut - prev.sent);
                        long deltaRecv = (long)(bytesIn  - prev.recv);
                        // Counters wrap or reset on reconnect — guard against negatives.
                        if (deltaSent < 0) deltaSent = 0;
                        if (deltaRecv < 0) deltaRecv = 0;

                        pidSentDelta[conn.OwningPid] = pidSentDelta.GetValueOrDefault(conn.OwningPid) + deltaSent;
                        pidRecvDelta[conn.OwningPid] = pidRecvDelta.GetValueOrDefault(conn.OwningPid) + deltaRecv;
                    }
                    // Always store current reading as baseline for the next tick.
                    _prevPerConn[key] = (bytesOut, bytesIn, now);
                }

                // Remove entries for connections that no longer exist.
                var staleKeys = _prevPerConn.Keys.Except(activeConnKeys).ToList();
                foreach (var k in staleKeys) _prevPerConn.Remove(k);
            }

            // ── Build per-app info ──
            var appInfos = new List<AppNetworkInfo>();

            foreach (var pid in allPids)
            {
                var info = new AppNetworkInfo { ProcessId = pid };
                try
                {
                    var proc = Process.GetProcessById(pid);
                    info.ProcessName = proc.ProcessName;
                    try { info.ExePath = proc.MainModule?.FileName ?? string.Empty; } catch { }
                }
                catch { info.ProcessName = $"PID {pid}"; }

                pidTcpConns.TryGetValue(pid, out var connsForPid);
                info.TcpConnections = connsForPid ?? (IReadOnlyList<TcpConnectionInfo>)Array.Empty<TcpConnectionInfo>();
                info.ConnectionCount = info.TcpConnections.Count + pidUdpCount.GetValueOrDefault(pid);

                long sentDelta = pidSentDelta.GetValueOrDefault(pid, 0);
                long recvDelta = pidRecvDelta.GetValueOrDefault(pid, 0);

                info.BytesSent     += sentDelta;
                info.BytesReceived += recvDelta;
                info.SendRateBps    = sentDelta * 8.0 / elapsed;
                info.ReceiveRateBps = recvDelta * 8.0 / elapsed;

                appInfos.Add(info);
            }

            // ── Global stats from interface counters ──
            long totalIn  = ifaceStats.Sum(s => s.InOctets);
            long totalOut = ifaceStats.Sum(s => s.OutOctets);
            var bestIface = ifaceStats.OrderByDescending(s => s.InOctets + s.OutOctets).FirstOrDefault();

            var globalStats = new NetworkStats
            {
                AdapterName       = bestIface?.Name ?? "Unknown",
                TotalBytesSent    = totalOut,
                TotalBytesReceived = totalIn,
                Timestamp         = now
            };

            if (_hasPrevGlobal)
            {
                var globalElapsed = (now - _prevGlobal.time).TotalSeconds;
                if (globalElapsed > 0)
                {
                    globalStats.SendRateBps    = Math.Max(0, (totalOut - _prevGlobal.outOctets) * 8.0 / globalElapsed);
                    globalStats.ReceiveRateBps = Math.Max(0, (totalIn  - _prevGlobal.inOctets)  * 8.0 / globalElapsed);
                }
            }

            _prevGlobal = (totalIn, totalOut, now);
            _hasPrevGlobal = true;

            NetworkDataUpdated?.Invoke(appInfos, globalStats);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"NetworkMonitorService tick error: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer?.Dispose();
        _timer = null;
    }
}

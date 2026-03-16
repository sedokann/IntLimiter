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
    private Timer? _timer;
    private bool _disposed;

    private readonly Dictionary<int, (long sent, long recv, DateTime time)> _prevPerProcess = new();
    private (long inOctets, long outOctets, DateTime time) _prevGlobal;
    private bool _hasPrevGlobal;

    public event Action<IReadOnlyList<AppNetworkInfo>, NetworkStats>? NetworkDataUpdated;

    public int IntervalMs { get; set; } = 1000;

    public NetworkMonitorService(INetworkDataSource dataSource)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
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
            var tcpConns = _dataSource.GetTcpConnections();
            var udpConns = _dataSource.GetUdpConnections();
            var ifaceStats = _dataSource.GetNetworkInterfaceStats();

            // Aggregate connection counts per PID
            var pidTcpCount = tcpConns.GroupBy(c => c.OwningPid).ToDictionary(g => g.Key, g => g.Count());
            var pidUdpCount = udpConns.GroupBy(c => c.OwningPid).ToDictionary(g => g.Key, g => g.Count());

            var allPids = pidTcpCount.Keys.Union(pidUdpCount.Keys).ToHashSet();

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

                info.ConnectionCount = (pidTcpCount.GetValueOrDefault(pid)) + (pidUdpCount.GetValueOrDefault(pid));

                if (_prevPerProcess.TryGetValue(pid, out var prev))
                {
                    // Per-process byte counts require ETW/npcap; rates remain 0 without that data.
                    info.BytesSent = prev.sent;
                    info.BytesReceived = prev.recv;
                    info.SendRateBps = 0;
                    info.ReceiveRateBps = 0;
                }

                _prevPerProcess[pid] = (info.BytesSent, info.BytesReceived, now);
                appInfos.Add(info);
            }

            // Compute global stats from interfaces
            long totalIn = ifaceStats.Sum(s => s.InOctets);
            long totalOut = ifaceStats.Sum(s => s.OutOctets);
            var bestIface = ifaceStats.OrderByDescending(s => s.InOctets + s.OutOctets).FirstOrDefault();

            var globalStats = new NetworkStats
            {
                AdapterName = bestIface?.Name ?? "Unknown",
                TotalBytesSent = totalOut,
                TotalBytesReceived = totalIn,
                Timestamp = now
            };

            if (_hasPrevGlobal)
            {
                var elapsed = (now - _prevGlobal.time).TotalSeconds;
                if (elapsed > 0)
                {
                    // InterfaceStats carries byte counts (octets); multiply by 8 to convert to bits per second.
                    globalStats.SendRateBps = Math.Max(0, (totalOut - _prevGlobal.outOctets) * 8.0 / elapsed);
                    globalStats.ReceiveRateBps = Math.Max(0, (totalIn - _prevGlobal.inOctets) * 8.0 / elapsed);
                }
            }

            _prevGlobal = (totalIn, totalOut, now);
            _hasPrevGlobal = true;

            // Remove stale PIDs
            var stalePids = _prevPerProcess.Keys.Except(allPids).ToList();
            foreach (var p in stalePids) _prevPerProcess.Remove(p);

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

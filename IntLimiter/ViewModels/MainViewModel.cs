using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IntLimiter.Models;
using IntLimiter.Services;

namespace IntLimiter.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private const double MbpsToBps = 1_000_000.0;
    private readonly NetworkMonitorService _monitorService;
    private readonly BandwidthLimiterService _limiterService;
    private readonly Dispatcher _dispatcher;
    private bool _disposed;

    public ObservableCollection<AppNetworkViewModel> Apps { get; } = new();

    [ObservableProperty]
    private string _globalSendRate = "0 bps";

    [ObservableProperty]
    private string _globalReceiveRate = "0 bps";

    [ObservableProperty]
    private string _adapterName = "Unknown";

    [ObservableProperty]
    private int _selectedTabIndex;

    [ObservableProperty]
    private double _globalMaxUploadMbps;

    [ObservableProperty]
    private double _globalMaxDownloadMbps;

    [ObservableProperty]
    private int _refreshIntervalMs = 1000;

    [ObservableProperty]
    private bool _isDarkTheme = true;

    private readonly Queue<double> _globalSendHistory = new(60);
    private readonly Queue<double> _globalReceiveHistory = new(60);

    public Queue<double> GlobalSendHistory => _globalSendHistory;
    public Queue<double> GlobalReceiveHistory => _globalReceiveHistory;

    public MainViewModel(NetworkMonitorService monitorService, BandwidthLimiterService limiterService, Dispatcher dispatcher)
    {
        _monitorService = monitorService;
        _limiterService = limiterService;
        _dispatcher = dispatcher;
        _monitorService.NetworkDataUpdated += OnNetworkDataUpdated;
        _monitorService.Start();
    }

    private void OnNetworkDataUpdated(IReadOnlyList<AppNetworkInfo> apps, NetworkStats global)
    {
        _dispatcher.BeginInvoke(() =>
        {
            UpdateGlobalStats(global);
            UpdateApps(apps);
        });
    }

    private void UpdateGlobalStats(NetworkStats stats)
    {
        GlobalSendRate = FormatBps(stats.SendRateBps);
        GlobalReceiveRate = FormatBps(stats.ReceiveRateBps);
        AdapterName = stats.AdapterName;

        if (_globalSendHistory.Count >= 60) _globalSendHistory.Dequeue();
        _globalSendHistory.Enqueue(stats.SendRateBps);

        if (_globalReceiveHistory.Count >= 60) _globalReceiveHistory.Dequeue();
        _globalReceiveHistory.Enqueue(stats.ReceiveRateBps);

        OnPropertyChanged(nameof(GlobalSendHistory));
        OnPropertyChanged(nameof(GlobalReceiveHistory));
    }

    private void UpdateApps(IReadOnlyList<AppNetworkInfo> apps)
    {
        var existingByPid = Apps.ToDictionary(a => a.ProcessId);
        var newPids = new HashSet<int>(apps.Select(a => a.ProcessId));

        // Remove gone apps
        var toRemove = Apps.Where(a => !newPids.Contains(a.ProcessId)).ToList();
        foreach (var vm in toRemove) Apps.Remove(vm);

        // Update or add
        foreach (var info in apps)
        {
            if (existingByPid.TryGetValue(info.ProcessId, out var vm))
                vm.Update(info);
            else
                Apps.Add(new AppNetworkViewModel(info));
        }

        // Sort by total rate descending
        var sorted = Apps.OrderByDescending(a => a.SendRateBps + a.ReceiveRateBps).ToList();
        for (int i = 0; i < sorted.Count; i++)
        {
            int current = Apps.IndexOf(sorted[i]);
            if (current != i) Apps.Move(current, i);
        }
    }

    [RelayCommand]
    private void ApplyGlobalLimit()
    {
        _limiterService.SetGlobalLimit(
            GlobalMaxUploadMbps * MbpsToBps,
            GlobalMaxDownloadMbps * MbpsToBps);
    }

    [RelayCommand]
    private void ApplyAppLimit(AppNetworkViewModel? vm)
    {
        if (vm == null) return;
        _limiterService.SetLimit(vm.ProcessId, vm.ExePath, vm.MaxUploadBps, vm.MaxDownloadBps);
    }

    [RelayCommand]
    private void RemoveAppLimit(AppNetworkViewModel? vm)
    {
        if (vm == null) return;
        _limiterService.RemoveLimit(vm.ProcessId);
    }

    private static string FormatBps(double bps)
    {
        if (bps >= 1_000_000_000) return $"{bps / 1_000_000_000:F1} Gbps";
        if (bps >= 1_000_000) return $"{bps / 1_000_000:F1} Mbps";
        if (bps >= 1_000) return $"{bps / 1_000:F1} Kbps";
        return $"{bps:F0} bps";
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _monitorService.NetworkDataUpdated -= OnNetworkDataUpdated;
        _monitorService.Stop();
        _monitorService.Dispose();
        _limiterService.Dispose();
    }
}

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
    private readonly IPerConnectionStats? _perConnStats;
    private readonly Dispatcher _dispatcher;
    private bool _disposed;

    public ObservableCollection<AppNetworkViewModel> Apps { get; } = new();

    public IEnumerable<AppNetworkViewModel> LimitedApps => Apps.Where(a => a.IsLimitEnabled);

    public bool HasNoLimitedApps => !Apps.Any(a => a.IsLimitEnabled);

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

    [ObservableProperty]
    private bool _hasGlobalLimit;

    private readonly Queue<double> _globalSendHistory = new(60);
    private readonly Queue<double> _globalReceiveHistory = new(60);

    public Queue<double> GlobalSendHistory => _globalSendHistory;
    public Queue<double> GlobalReceiveHistory => _globalReceiveHistory;

    public MainViewModel(NetworkMonitorService monitorService, BandwidthLimiterService limiterService, Dispatcher dispatcher)
    {
        _monitorService = monitorService;
        _limiterService = limiterService;
        _dispatcher = dispatcher;

        // Use the public PerConnectionStats property exposed by NetworkMonitorService.
        _perConnStats = monitorService.PerConnectionStats;

        _monitorService.NetworkDataUpdated += OnNetworkDataUpdated;
        _monitorService.Start();
    }

    partial void OnIsDarkThemeChanged(bool value)
    {
        App.SwitchTheme(value);
    }

    private void OnNetworkDataUpdated(IReadOnlyList<AppNetworkInfo> apps, NetworkStats global)
    {
        // Apply throttling on the background thread before dispatching UI updates.
        _limiterService.ApplyThrottling(apps, _perConnStats);

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

        // Sort by connection count then total rate descending
        var sorted = Apps.OrderByDescending(a => a.ConnectionCount)
                         .ThenByDescending(a => a.SendRateBps + a.ReceiveRateBps)
                         .ToList();
        for (int i = 0; i < sorted.Count; i++)
        {
            int current = Apps.IndexOf(sorted[i]);
            if (current != i) Apps.Move(current, i);
        }

        OnPropertyChanged(nameof(LimitedApps));
        OnPropertyChanged(nameof(HasNoLimitedApps));
    }

    [RelayCommand]
    private void ApplyGlobalLimit()
    {
        _limiterService.SetGlobalLimit(
            GlobalMaxUploadMbps * MbpsToBps,
            GlobalMaxDownloadMbps * MbpsToBps);
        HasGlobalLimit = GlobalMaxUploadMbps > 0 || GlobalMaxDownloadMbps > 0;
    }

    [RelayCommand]
    private void RemoveGlobalLimit()
    {
        GlobalMaxUploadMbps = 0;
        GlobalMaxDownloadMbps = 0;
        _limiterService.SetGlobalLimit(0, 0);
        HasGlobalLimit = false;
    }

    [RelayCommand]
    private void ApplyAppLimit(AppNetworkViewModel? vm)
    {
        if (vm == null) return;
        vm.IsLimitEnabled = true;
        _limiterService.SetLimit(vm.ProcessId, vm.ExePath, vm.MaxUploadBps, vm.MaxDownloadBps);
        OnPropertyChanged(nameof(LimitedApps));
        OnPropertyChanged(nameof(HasNoLimitedApps));
    }

    [RelayCommand]
    private void RemoveAppLimit(AppNetworkViewModel? vm)
    {
        if (vm == null) return;
        vm.IsLimitEnabled = false;
        _limiterService.RemoveLimit(vm.ProcessId);
        OnPropertyChanged(nameof(LimitedApps));
        OnPropertyChanged(nameof(HasNoLimitedApps));
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

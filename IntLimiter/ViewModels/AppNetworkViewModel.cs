using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IntLimiter.Models;

namespace IntLimiter.ViewModels;

public partial class AppNetworkViewModel : ObservableObject
{
    private readonly AppNetworkInfo _info;

    [ObservableProperty]
    private double _sendRateBps;

    [ObservableProperty]
    private double _receiveRateBps;

    [ObservableProperty]
    private int _connectionCount;

    [ObservableProperty]
    private string _sendRateDisplay = "0 B/s";

    [ObservableProperty]
    private string _receiveRateDisplay = "0 B/s";

    [ObservableProperty]
    private double _maxUploadBps;

    [ObservableProperty]
    private double _maxDownloadBps;

    [ObservableProperty]
    private bool _isLimitEnabled;

    [ObservableProperty]
    private Queue<double> _sendHistory = new(60);

    [ObservableProperty]
    private Queue<double> _receiveHistory = new(60);

    public int ProcessId => _info.ProcessId;
    public string ProcessName => _info.ProcessName;
    public string ExePath => _info.ExePath;

    public AppNetworkViewModel(AppNetworkInfo info)
    {
        _info = info;
        Update(info);
    }

    public void Update(AppNetworkInfo info)
    {
        SendRateBps = info.SendRateBps;
        ReceiveRateBps = info.ReceiveRateBps;
        ConnectionCount = info.ConnectionCount;
        SendRateDisplay = FormatBps(info.SendRateBps);
        ReceiveRateDisplay = FormatBps(info.ReceiveRateBps);

        // Append current rates to history (service does not pre-populate info histories)
        if (SendHistory.Count >= 60) SendHistory.Dequeue();
        SendHistory.Enqueue(info.SendRateBps);
        if (ReceiveHistory.Count >= 60) ReceiveHistory.Dequeue();
        ReceiveHistory.Enqueue(info.ReceiveRateBps);

        OnPropertyChanged(nameof(SendHistory));
        OnPropertyChanged(nameof(ReceiveHistory));
    }

    private static string FormatBps(double bps)
    {
        if (bps >= 1_000_000_000) return $"{bps / 1_000_000_000:F1} Gbps";
        if (bps >= 1_000_000) return $"{bps / 1_000_000:F1} Mbps";
        if (bps >= 1_000) return $"{bps / 1_000:F1} Kbps";
        return $"{bps:F0} bps";
    }
}

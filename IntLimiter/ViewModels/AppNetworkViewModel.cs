using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
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

    /// <summary>
    /// The application's icon loaded asynchronously from its executable path.
    /// Falls back to <c>null</c> (no image) when the icon cannot be extracted.
    /// </summary>
    [ObservableProperty]
    private ImageSource? _appIcon;

    public int ProcessId => _info.ProcessId;
    public string ProcessName => _info.ProcessName;
    public string ExePath => _info.ExePath;

    public AppNetworkViewModel(AppNetworkInfo info)
    {
        _info = info;
        Update(info);
        _ = LoadIconAsync(info.ExePath);
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

    private async Task LoadIconAsync(string exePath)
    {
        if (string.IsNullOrEmpty(exePath)) return;
        if (AppIcon != null) return; // already loaded

        try
        {
            var icon = await Task.Run(() => ExtractIcon(exePath));
            if (icon != null)
                AppIcon = icon;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Icon load failed for {exePath}: {ex.Message}");
        }
    }

    private static ImageSource? ExtractIcon(string exePath)
    {
        try
        {
            if (!System.IO.File.Exists(exePath)) return null;

#if WINDOWS7_0_OR_GREATER
            var shfi = new ShellFileInfo();
            uint flags = SHGFI_ICON | SHGFI_SMALLICON | SHGFI_USEFILEATTRIBUTES;
            IntPtr result = SHGetFileInfo(exePath, 0, ref shfi, (uint)System.Runtime.InteropServices.Marshal.SizeOf(shfi), flags);
            if (result == IntPtr.Zero || shfi.hIcon == IntPtr.Zero) return null;

            try
            {
                var src = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                    shfi.hIcon,
                    System.Windows.Int32Rect.Empty,
                    System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
                src.Freeze();
                return src;
            }
            finally
            {
                DestroyIcon(shfi.hIcon);
            }
#else
            return null;
#endif
        }
        catch
        {
            // Icon extraction is best-effort. Swallow all exceptions to avoid disrupting
            // the UI update pipeline when a process exits or lacks a shell icon.
            return null;
        }
    }

#if WINDOWS7_0_OR_GREATER
    private const uint SHGFI_ICON             = 0x000000100;
    private const uint SHGFI_SMALLICON        = 0x000000001;
    private const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    private struct ShellFileInfo
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref ShellFileInfo psfi, uint cbFileInfo, uint uFlags);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);
#endif

    private static string FormatBps(double bps)
    {
        if (bps >= 1_000_000_000) return $"{bps / 1_000_000_000:F1} Gbps";
        if (bps >= 1_000_000) return $"{bps / 1_000_000:F1} Mbps";
        if (bps >= 1_000) return $"{bps / 1_000:F1} Kbps";
        return $"{bps:F0} bps";
    }
}

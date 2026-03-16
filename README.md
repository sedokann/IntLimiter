# IntLimiter

A Windows 11-style network activity monitor and bandwidth limiter for per-app and global traffic control.

## Features

- **Real-time network monitoring** — per-process send/receive rates and connection counts updated every second
- **Global bandwidth stats** — total upload/download speeds with a 60-second history sparkline chart
- **Bandwidth limiter** — per-app and global rate limits enforced via Windows Filtering Platform (WFP)
- **Windows 11 UI** — dark Fluent Design with rounded cards, accent colours, toggle switches, and a custom sparkline control
- **Graceful degradation** — monitoring works without admin rights; WFP limiting requires elevation and logs a warning when unavailable

## Architecture

```
IntLimiter/                        WPF application (net8.0-windows)
├── Models/                        AppNetworkInfo · NetworkStats · BandwidthLimit
├── Services/
│   ├── INetworkDataSource         Interface (mockable for tests)
│   ├── WindowsNetworkDataSource   P/Invoke via iphlpapi.dll + System.Net.NetworkInformation
│   ├── NetworkMonitorService      Timer-based monitor; fires NetworkDataUpdated event
│   └── BandwidthLimiterService    Token-bucket engine; applies WFP block filters per app
├── Native/
│   ├── IpHelperNative             GetExtendedTcpTable / GetExtendedUdpTable P/Invoke
│   └── WfpNative                  FwpmEngineOpen0 / FwpmFilterDeleteById0 P/Invoke
├── ViewModels/                    MVVM (CommunityToolkit.Mvvm)
├── Controls/SparklineControl      Custom FrameworkElement chart
├── Converters/ValueConverters     Bps/bytes → human-readable strings
└── Themes/Windows11Styles.xaml    ResourceDictionary (colours, card/button/toggle styles)

IntLimiter.Tests/                  xUnit test project (net8.0, cross-platform)
├── Models/                        AppNetworkInfo and BandwidthLimit unit tests
└── Services/                      NetworkMonitorService and BandwidthLimiterService tests (Moq)
```

## Requirements

- Windows 10 / Windows 11
- .NET 8 Runtime ([download](https://dotnet.microsoft.com/download/dotnet/8.0))
- **Administrator privileges** are required for bandwidth limiting (WFP sublayer registration)

## Building

```bash
dotnet build IntLimiter.sln
```

## Running Tests

```bash
dotnet test IntLimiter.Tests/IntLimiter.Tests.csproj
```

## Technical Notes

### Network Monitoring
Per-process byte rates require kernel-level hooks (ETW / npcap driver) that are not available in pure user-space .NET. The current implementation tracks **connection counts** per PID via `GetExtendedTcpTable` / `GetExtendedUdpTable` (iphlpapi.dll) and measures **global** upload/download rates from `System.Net.NetworkInformation.NetworkInterface`. A future enhancement can replace `WindowsNetworkDataSource` with an ETW-backed data source to obtain per-process byte rates.

### Bandwidth Limiting
`BandwidthLimiterService` opens a WFP engine session and registers a persistent sublayer (`EnsureSubLayer`). It uses a token-bucket algorithm (100 ms tick) to decide when a process has exceeded its configured rate. When a bucket is empty the service adds a WFP block filter for that application path; when tokens refill it removes the filter via `FwpmFilterDeleteById0`. This implements coarse stop-and-go throttling entirely from user space without a kernel callout driver.

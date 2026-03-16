using System.Windows;
using System.Windows.Input;
using IntLimiter.Services;
using IntLimiter.ViewModels;

namespace IntLimiter;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        var dataSource = new WindowsNetworkDataSource();
        var monitorService = new NetworkMonitorService(dataSource);
        var limiterService = new BandwidthLimiterService();
        _viewModel = new MainViewModel(monitorService, limiterService, Dispatcher);
        DataContext = _viewModel;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }
        else
        {
            DragMove();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close(); // triggers OnClosed, which calls _viewModel.Dispose()
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MonitorTab_Checked(object sender, RoutedEventArgs e)
    {
        if (MonitorPanel == null) return;
        MonitorPanel.Visibility = Visibility.Visible;
        LimiterPanel.Visibility = Visibility.Collapsed;
        SettingsPanel.Visibility = Visibility.Collapsed;
    }

    private void LimiterTab_Checked(object sender, RoutedEventArgs e)
    {
        if (LimiterPanel == null) return;
        MonitorPanel.Visibility = Visibility.Collapsed;
        LimiterPanel.Visibility = Visibility.Visible;
        SettingsPanel.Visibility = Visibility.Collapsed;
    }

    private void SettingsTab_Checked(object sender, RoutedEventArgs e)
    {
        if (SettingsPanel == null) return;
        MonitorPanel.Visibility = Visibility.Collapsed;
        LimiterPanel.Visibility = Visibility.Collapsed;
        SettingsPanel.Visibility = Visibility.Visible;
    }

    protected override void OnClosed(System.EventArgs e)
    {
        // CloseButton_Click already calls Dispose; OnClosed covers Alt-F4 / OS close paths.
        _viewModel.Dispose();
        base.OnClosed(e);
    }
}

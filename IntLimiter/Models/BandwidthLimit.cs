namespace IntLimiter.Models;

public class BandwidthLimit
{
    public int ProcessId { get; set; }
    public string AppPath { get; set; } = string.Empty;
    public double MaxUploadBps { get; set; }    // 0 = unlimited
    public double MaxDownloadBps { get; set; }  // 0 = unlimited
    public bool IsEnabled { get; set; } = true;
}

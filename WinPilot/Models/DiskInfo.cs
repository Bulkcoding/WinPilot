namespace WinPilot.Models;

public class DiskInfo
{
    public string DriveLetter { get; set; } = "";
    public string Label { get; set; } = "";
    public long TotalBytes { get; set; }
    public long FreeBytes { get; set; }
    public long UsedBytes => TotalBytes - FreeBytes;
    public double UsagePercent => TotalBytes > 0 ? (double)UsedBytes / TotalBytes * 100 : 0;
    public string TotalText => FormatBytes(TotalBytes);
    public string FreeText => FormatBytes(FreeBytes);
    public string UsedText => FormatBytes(UsedBytes);
    public string DisplayName => string.IsNullOrEmpty(Label) ? DriveLetter : $"{Label} ({DriveLetter})";

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1_073_741_824L) return $"{bytes / 1_073_741_824.0:F1} GB";
        if (bytes >= 1_048_576L) return $"{bytes / 1_048_576.0:F1} MB";
        return $"{bytes / 1024.0:F1} KB";
    }
}

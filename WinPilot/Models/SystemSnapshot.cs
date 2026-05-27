namespace WinPilot.Models;

public class SystemSnapshot
{
    public float CpuUsage { get; set; }
    public float RamUsagePercent { get; set; }
    public double RamUsedGb { get; set; }
    public double RamTotalGb { get; set; }
    public TimeSpan Uptime { get; set; }
    public string OsVersion { get; set; } = "";
    public string OsBuild { get; set; } = "";
    public string CpuName { get; set; } = "";
    public double CpuSpeedGhz { get; set; }
    public int CpuCores { get; set; }
    public int CpuThreads { get; set; }
    public string SystemType { get; set; } = "";
    public string ComputerName { get; set; } = "";
    public string UserName { get; set; } = "";

    public string RamText => $"{RamUsedGb:F1} / {RamTotalGb:F1} GB";
    public string UptimeText
    {
        get
        {
            var u = Uptime;
            if (u.TotalDays >= 1) return $"{(int)u.TotalDays}일 {u.Hours}시간 {u.Minutes}분";
            return $"{u.Hours}시간 {u.Minutes}분";
        }
    }
}

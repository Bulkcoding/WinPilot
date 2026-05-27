using System.Diagnostics;
using System.IO;
using System.Management;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using WinPilot.Models;

namespace WinPilot.Services;

public class SystemInfoService : IDisposable
{
    private PerformanceCounter? _cpuCounter;
    private bool _disposed;

    public SystemInfoService()
    {
        try
        {
            _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            _cpuCounter.NextValue();
        }
        catch
        {
            _cpuCounter = null;
        }
    }

    private float GetCpuUsage()
    {
        if (_cpuCounter != null)
        {
            try { return _cpuCounter.NextValue(); }
            catch { _cpuCounter = null; }
        }
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT LoadPercentage FROM Win32_Processor");
            foreach (ManagementObject obj in searcher.Get())
                return Convert.ToSingle(obj["LoadPercentage"] ?? 0);
        }
        catch { }
        return 0f;
    }

    public async Task<SystemSnapshot> GetSnapshotAsync()
    {
        return await Task.Run(() =>
        {
            var snap = new SystemSnapshot
            {
                CpuUsage = GetCpuUsage(),
                ComputerName = Environment.MachineName,
                UserName = Environment.UserName
            };

            try
            {
                using var osSearcher = new ManagementObjectSearcher("SELECT * FROM Win32_OperatingSystem");
                foreach (ManagementObject obj in osSearcher.Get())
                {
                    snap.OsVersion = obj["Caption"]?.ToString() ?? "";
                    snap.OsBuild = obj["BuildNumber"]?.ToString() ?? "";
                    snap.SystemType = obj["OSArchitecture"]?.ToString() ?? "";

                    ulong totalKb = (ulong)(obj["TotalVisibleMemorySize"] ?? 0UL);
                    ulong freeKb = (ulong)(obj["FreePhysicalMemory"] ?? 0UL);
                    snap.RamTotalGb = totalKb / 1_048_576.0;
                    snap.RamUsedGb = (totalKb - freeKb) / 1_048_576.0;
                    snap.RamUsagePercent = totalKb > 0 ? (float)((totalKb - freeKb) * 100.0 / totalKb) : 0;

                    var bootStr = obj["LastBootUpTime"]?.ToString();
                    if (!string.IsNullOrEmpty(bootStr))
                        snap.Uptime = DateTime.Now - ManagementDateTimeConverter.ToDateTime(bootStr);
                    break;
                }
            }
            catch { }

            try
            {
                using var cpuSearcher = new ManagementObjectSearcher("SELECT * FROM Win32_Processor");
                foreach (ManagementObject obj in cpuSearcher.Get())
                {
                    snap.CpuName = obj["Name"]?.ToString()?.Trim() ?? "";
                    snap.CpuSpeedGhz = (uint)(obj["MaxClockSpeed"] ?? 0u) / 1000.0;
                    snap.CpuCores = (int)(uint)(obj["NumberOfCores"] ?? 0u);
                    snap.CpuThreads = (int)(uint)(obj["NumberOfLogicalProcessors"] ?? 0u);
                    break;
                }
            }
            catch { }

            return snap;
        });
    }

    public List<DiskInfo> GetDiskInfo()
    {
        var result = new List<DiskInfo>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (drive.DriveType != DriveType.Fixed) continue;
            try
            {
                result.Add(new DiskInfo
                {
                    DriveLetter = drive.Name,
                    Label = drive.VolumeLabel,
                    TotalBytes = drive.TotalSize,
                    FreeBytes = drive.TotalFreeSpace
                });
            }
            catch { }
        }
        return result;
    }

    public List<NetworkInfo> GetNetworkInfo()
    {
        var result = new List<NetworkInfo>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            if (nic.OperationalStatus is not (OperationalStatus.Up or OperationalStatus.Down)) continue;

            var ipProps = nic.GetIPProperties();
            var unicast = ipProps.UnicastAddresses
                .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork);
            var gateway = ipProps.GatewayAddresses.FirstOrDefault();

            var mac = nic.GetPhysicalAddress().GetAddressBytes();
            result.Add(new NetworkInfo
            {
                AdapterName = nic.Name,
                Description = nic.Description,
                IpAddress = unicast?.Address.ToString() ?? "N/A",
                SubnetMask = unicast?.IPv4Mask.ToString() ?? "N/A",
                Gateway = gateway?.Address.ToString() ?? "N/A",
                MacAddress = mac.Length > 0 ? BitConverter.ToString(mac).Replace("-", ":") : "",
                IsConnected = nic.OperationalStatus == OperationalStatus.Up
            });
        }
        return result;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _cpuCounter?.Dispose();
        _disposed = true;
    }
}

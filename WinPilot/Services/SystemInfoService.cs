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
    private PerformanceCounter? _ramAvailableCounter;  // Available MBytes (물리 RAM)
    private double _totalRamMb;
    private bool _disposed;

    public SystemInfoService()
    {
        try
        {
            _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            _cpuCounter.NextValue();
        }
        catch { _cpuCounter = null; }

        try
        {
            // "Available MBytes" = 물리 RAM 남은 용량 (MB)
            // "% Committed Bytes In Use" 는 페이지파일 포함 가상 메모리 → 물리 RAM %와 다름
            _ramAvailableCounter = new PerformanceCounter("Memory", "Available MBytes");
            _ramAvailableCounter.NextValue();
            // 총 물리 RAM 한 번만 조회
            _totalRamMb = GetTotalPhysicalRamMb();
        }
        catch { _ramAvailableCounter = null; }
    }

    private static double GetTotalPhysicalRamMb()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT TotalVisibleMemorySize FROM Win32_OperatingSystem");
            foreach (ManagementObject obj in searcher.Get())
            {
                var kb = (ulong)(obj["TotalVisibleMemorySize"] ?? 0UL);
                return kb / 1024.0;  // KB → MB
            }
        }
        catch { }
        return 0;
    }

    public async Task<float> GetCpuUsageAsync() => await Task.Run(GetCpuUsage);

    /// <summary>
    /// 물리 RAM 사용률 (0~100%)을 빠르게 반환합니다.
    /// PerformanceCounter "Available MBytes" 기반이므로 WMI보다 경량합니다.
    /// </summary>
    public async Task<float> GetRamUsagePercentAsync() => await Task.Run(() =>
    {
        try
        {
            if (_ramAvailableCounter == null || _totalRamMb <= 0) return 0f;
            var availableMb = _ramAvailableCounter.NextValue();
            return (float)Math.Max(0, Math.Min(100, (1.0 - availableMb / _totalRamMb) * 100.0));
        }
        catch { return 0f; }
    });

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

    public List<Models.GpuInfo> GetGpuInfo()
    {
        var result = new List<Models.GpuInfo>();
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_VideoController");
            foreach (ManagementObject obj in searcher.Get())
            {
                var name = obj["Name"]?.ToString()?.Trim() ?? "";
                if (string.IsNullOrEmpty(name)) continue;

                // AdapterRAM은 32bit 한계로 4GB 초과분은 0으로 표시되는 경우 있음
                var vramBytes = Convert.ToUInt64(obj["AdapterRAM"] ?? 0UL);
                var vramText  = vramBytes >= 1_073_741_824
                    ? $"{vramBytes / 1_073_741_824.0:F1} GB"
                    : vramBytes > 0
                        ? $"{vramBytes / 1_048_576} MB"
                        : "공유 메모리";

                var driverVer = obj["DriverVersion"]?.ToString() ?? "";
                var upper     = name.ToUpperInvariant();
                var vendor    = upper.Contains("NVIDIA")                       ? "NVIDIA"
                              : upper.Contains("AMD") || upper.Contains("RADEON") ? "AMD"
                              : upper.Contains("INTEL")                        ? "Intel"
                              : "";
                var driverUrl = vendor switch
                {
                    "NVIDIA" => "https://www.nvidia.com/en-us/software/nvidia-app/",
                    "AMD"    => "https://www.amd.com/en/products/software/adrenalin.html",
                    "Intel"  => "https://www.intel.com/content/www/us/en/support/detect.html",
                    _        => ""
                };

                result.Add(new Models.GpuInfo
                {
                    Name          = name,
                    VramText      = vramText,
                    DriverVersion = driverVer,
                    Vendor        = vendor,
                    DriverPageUrl = driverUrl
                });
            }
        }
        catch { }
        return result;
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
        _ramAvailableCounter?.Dispose();
        _disposed = true;
    }
}

namespace WinPilot.Models;

public class GpuInfo
{
    public string Name          { get; set; } = "";
    public string VramText      { get; set; } = "";
    public string DriverVersion { get; set; } = "";
    public string Vendor        { get; set; } = ""; // "NVIDIA" | "AMD" | "Intel" | ""
    public string DriverPageUrl { get; set; } = "";
    public string SearchQuery   => $"{Name} 드라이버 다운로드";
    public bool   HasDriverPage => !string.IsNullOrEmpty(DriverPageUrl);
}

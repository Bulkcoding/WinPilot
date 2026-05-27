namespace WinPilot.Models;

public class NetworkInfo
{
    public string AdapterName { get; set; } = "";
    public string Description { get; set; } = "";
    public string IpAddress { get; set; } = "N/A";
    public string SubnetMask { get; set; } = "N/A";
    public string Gateway { get; set; } = "N/A";
    public string MacAddress { get; set; } = "";
    public bool IsConnected { get; set; }
    public string StatusText => IsConnected ? "연결됨" : "연결 안 됨";
}

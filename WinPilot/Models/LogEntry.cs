namespace WinPilot.Models;

public enum LogLevel { Error, Warning, Information }

public class LogEntry
{
    public LogLevel Level { get; set; }
    public DateTime DateTime { get; set; }
    public string LogName { get; set; } = "";
    public int EventId { get; set; }
    public string Source { get; set; } = "";
    public string Description { get; set; } = "";

    public string LevelText => Level switch
    {
        LogLevel.Error => "오류",
        LogLevel.Warning => "경고",
        _ => "정보"
    };
    public string DateTimeText => DateTime.ToString("yyyy-MM-dd HH:mm:ss");
}

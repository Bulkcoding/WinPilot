using System.Diagnostics.Eventing.Reader;
using WinPilot.Models;

namespace WinPilot.Services;

public class EventLogService
{
    private static readonly string[] LogNames = ["System", "Application"];

    public List<LogEntry> GetEntries(DateTime from, DateTime to, string? logFilter = null, int maxPerLog = 500)
    {
        var result = new List<LogEntry>();
        var logs = logFilter != null && logFilter != "모든 로그"
            ? new[] { logFilter }
            : LogNames;

        string fromStr = from.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        string toStr = to.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        string xpath = $"*[System[(Level=1 or Level=2 or Level=3) and TimeCreated[@SystemTime >= '{fromStr}' and @SystemTime <= '{toStr}']]]";

        foreach (var logName in logs)
        {
            try
            {
                var query = new EventLogQuery(logName, PathType.LogName, xpath)
                {
                    ReverseDirection = true
                };

                using var reader = new EventLogReader(query);
                int count = 0;

                EventRecord? record;
                while ((record = reader.ReadEvent()) != null && count < maxPerLog)
                {
                    using (record)
                    {
                        var level = (byte)(record.Level ?? 4) switch
                        {
                            1 or 2 => LogLevel.Error,
                            3 => LogLevel.Warning,
                            _ => LogLevel.Information
                        };

                        string desc = "";
                        try { desc = record.FormatDescription() ?? ""; }
                        catch { desc = $"이벤트 ID: {record.Id}"; }
                        if (desc.Length > 300) desc = desc[..300] + "...";

                        result.Add(new LogEntry
                        {
                            Level = level,
                            DateTime = record.TimeCreated?.ToLocalTime() ?? DateTime.Now,
                            LogName = logName,
                            EventId = record.Id,
                            Source = record.ProviderName ?? "",
                            Description = desc
                        });
                        count++;
                    }
                }
            }
            catch { }
        }

        return [.. result.OrderByDescending(e => e.DateTime)];
    }

    public (int Errors, int Warnings, int Infos) GetSummary(DateTime from, DateTime to)
    {
        var entries = GetEntries(from, to, maxPerLog: 200);
        return (
            entries.Count(e => e.Level == LogLevel.Error),
            entries.Count(e => e.Level == LogLevel.Warning),
            entries.Count(e => e.Level == LogLevel.Information)
        );
    }
}

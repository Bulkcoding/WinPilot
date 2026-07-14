using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json.Serialization;
using System.Windows;

namespace WinPilot.Services;

/// <param name="IsInstaller">true = WinPilotSetup.exe, false = 단일 EXE</param>
public record UpdateInfo(string Version, string DownloadUrl, bool IsInstaller, List<string> ReleaseNotes, DateTime? PublishedAt = null)
{
    public string PublishedAtDisplay => PublishedAt?.ToString("yyyy-MM-dd") ?? "";
}

file class GitHubRelease
{
    [JsonPropertyName("tag_name")]   public string?            TagName     { get; set; }
    [JsonPropertyName("body")]       public string?            Body        { get; set; }
    [JsonPropertyName("assets")]     public List<GitHubAsset>? Assets      { get; set; }
    [JsonPropertyName("published_at")] public string?         PublishedAt { get; set; }
}
file class GitHubAsset
{
    [JsonPropertyName("name")]                 public string? Name               { get; set; }
    [JsonPropertyName("browser_download_url")] public string? BrowserDownloadUrl { get; set; }
}

public static class UpdateService
{
    private static readonly HttpClient Http = new()
    {
        DefaultRequestHeaders = { { "User-Agent", "WinPilot-Updater" } },
        Timeout = TimeSpan.FromSeconds(15)
    };

    private const string ApiUrl = "https://api.github.com/repos/Bulkcoding/WinPilot/releases/latest";
    private const string AllReleasesUrl = "https://api.github.com/repos/Bulkcoding/WinPilot/releases?per_page=30";

    private static List<UpdateInfo>? _allReleasesCache;

    // AssemblyVersion은 빌드 시 고정될 수 있으므로 FileVersion으로 읽음
    public static Version CurrentVersion
    {
        get
        {
            try
            {
                // Process.MainModule이 가장 신뢰할 수 있는 실행 파일 경로
                var path = Process.GetCurrentProcess().MainModule?.FileName ?? "";
                if (!string.IsNullOrEmpty(path))
                {
                    var fv = FileVersionInfo.GetVersionInfo(path).FileVersion;
                    if (Version.TryParse(fv, out var v)) return v;
                }
            }
            catch { }
            return Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);
        }
    }

    public static string CurrentVersionText
        => $"v{CurrentVersion.Major}.{CurrentVersion.Minor}.{CurrentVersion.Build}";

    /// <summary>
    /// GitHub Releases API로 최신 버전 확인.
    /// WinPilotSetup.exe(인스톨러) 우선, 없으면 WinPilot.exe(포터블) 반환.
    /// </summary>
    public static async Task<UpdateInfo?> CheckAsync()
    {
        try
        {
            var release = await Http.GetFromJsonAsync<GitHubRelease>(ApiUrl);
            if (release?.TagName == null) return null;

            var tagText = release.TagName.TrimStart('v');
            if (!Version.TryParse(tagText, out var latest)) return null;
            if (latest <= CurrentVersion) return null;

            // 인스톨러 우선, 없으면 포터블 EXE
            var setup = release.Assets?.FirstOrDefault(a =>
                string.Equals(a.Name, "WinPilotSetup.exe", StringComparison.OrdinalIgnoreCase));
            var exe   = release.Assets?.FirstOrDefault(a =>
                string.Equals(a.Name, "WinPilot.exe", StringComparison.OrdinalIgnoreCase));

            var chosen = setup ?? exe;
            if (chosen?.BrowserDownloadUrl == null) return null;

            return new UpdateInfo(release.TagName, chosen.BrowserDownloadUrl, setup != null,
                ParseReleaseNotes(release.Body));
        }
        catch { return null; }
    }

    /// <summary>
    /// GitHub Releases API로 전체 릴리스 목록 조회 (업데이트 내역 표시용).
    /// 최대 30개, 메모리 캐시 적용.
    /// </summary>
    public static async Task<List<UpdateInfo>> GetAllReleasesAsync()
    {
        if (_allReleasesCache != null) return _allReleasesCache;

        try
        {
            var releases = await Http.GetFromJsonAsync<List<GitHubRelease>>(AllReleasesUrl);
            if (releases == null) return [];

            _allReleasesCache = releases
                .Where(r => r.TagName != null && Version.TryParse(r.TagName.TrimStart('v'), out _))
                .Select(r =>
                {
                    DateTime? published = null;
                    if (DateTime.TryParse(r.PublishedAt, out var dt))
                        published = dt;
                    return new UpdateInfo(r.TagName!, "", false, ParseReleaseNotes(r.Body), published);
                })
                .ToList();

            return _allReleasesCache;
        }
        catch
        {
            return [];  // 네트워크 오류 등 무시
        }
    }

    private static List<string> ParseReleaseNotes(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return [];
        var lines = body.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        // "- " 또는 "* " 또는 "• "로 시작하는 줄을 불릿 항목으로 파싱 (마크다운 서식 제거)
        var bullets = lines
            .Select(l => l.Trim())
            .Where(l => l.StartsWith("- ") || l.StartsWith("* ") || l.StartsWith("• "))
            .Select(l => CleanMarkdown(l[2..].Trim()))
            .Where(l => l.Length > 0)
            .Take(8)
            .ToList();
        if (bullets.Count > 0) return bullets;

        // fallback: 헤더(#)·테이블(|)·인용구(>)·이미지(!) 제외 첫 5줄
        return lines
            .Select(l => l.Trim())
            .Where(l => l.Length > 2
                     && !l.StartsWith("#")
                     && !l.StartsWith("|")
                     && !l.StartsWith(">")
                     && !l.StartsWith("!"))
            .Select(CleanMarkdown)
            .Where(l => l.Length > 0)
            .Take(5)
            .ToList();
    }

    // **bold**, *italic*, `code` 등 마크다운 서식 제거
    private static string CleanMarkdown(string text)
    {
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\*\*(.*?)\*\*", "$1");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\*(.*?)\*",     "$1");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"`(.*?)`",       "$1");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\[(.*?)\]\(.*?\)", "$1"); // [링크](url) → 링크
        return text.Trim();
    }

    /// <summary>
    /// 다운로드 + 적용.
    /// 인스톨러(WinPilotSetup.exe): /VERYSILENT 로 자동 설치 후 재시작.
    /// 포터블 EXE: 배치 스크립트로 교체 후 재시작.
    /// </summary>
    public static async Task DownloadAndApplyAsync(
        UpdateInfo info,
        IProgress<int>? progress = null,
        bool autoApply = true)
    {
        var tempPath = Path.Combine(Path.GetTempPath(),
            info.IsInstaller ? "WinPilotSetup_update.exe" : "WinPilot_update.exe");

        // 1. 다운로드
        using var response = await Http.GetAsync(info.DownloadUrl, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? -1L;
        await using var src  = await response.Content.ReadAsStreamAsync();
        await using var dest = File.Create(tempPath);
        var buf  = new byte[81920];
        long done = 0;
        int  read;
        while ((read = await src.ReadAsync(buf)) > 0)
        {
            await dest.WriteAsync(buf.AsMemory(0, read));
            done += read;
            if (total > 0) progress?.Report((int)(done * 100 / total));
        }
        dest.Close();

        if (!autoApply) return;

        var currentExe = Process.GetCurrentProcess().MainModule?.FileName
                         ?? Path.Combine(AppContext.BaseDirectory, "WinPilot.exe");
        var updaterBat = Path.Combine(Path.GetTempPath(), "WinPilot_updater.bat");

        string script;
        if (info.IsInstaller)
        {
            // 인스톨러: 조용히 업그레이드 설치 후 앱 재시작
            // /VERYSILENT: 창 없음  /SUPPRESSMSGBOXES: 팝업 없음
            // /NORESTART: 설치 후 OS 재시작 안 함
            script = $"""
                @echo off
                timeout /t 2 /nobreak >nul
                "{tempPath}" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART
                timeout /t 3 /nobreak >nul
                del "{tempPath}"
                start "" "{currentExe}"
                del "%~f0"
                """;
        }
        else
        {
            // 포터블: 단일 EXE 교체
            script = $"""
                @echo off
                timeout /t 2 /nobreak >nul
                copy /Y "{tempPath}" "{currentExe}"
                del "{tempPath}"
                start "" "{currentExe}"
                del "%~f0"
                """;
        }

        await File.WriteAllTextAsync(updaterBat, script, System.Text.Encoding.Default);

        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{updaterBat}\"")
        {
            UseShellExecute = true,
            WindowStyle     = ProcessWindowStyle.Hidden
        });

        Application.Current.Shutdown();
    }
}

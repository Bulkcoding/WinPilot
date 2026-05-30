using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json.Serialization;
using System.Windows;

namespace WinPilot.Services;

public record UpdateInfo(string Version, string DownloadUrl);

file class GitHubRelease
{
    [JsonPropertyName("tag_name")]  public string?           TagName { get; set; }
    [JsonPropertyName("assets")]    public List<GitHubAsset>? Assets  { get; set; }
}
file class GitHubAsset
{
    [JsonPropertyName("name")]                  public string? Name                { get; set; }
    [JsonPropertyName("browser_download_url")] public string? BrowserDownloadUrl { get; set; }
}

public static class UpdateService
{
    private static readonly HttpClient Http = new()
    {
        DefaultRequestHeaders = { { "User-Agent", "WinPilot-Updater" } },
        Timeout = TimeSpan.FromSeconds(15)
    };

    private const string ApiUrl      = "https://api.github.com/repos/Bulkcoding/WinPilot/releases/latest";
    private static readonly string   PendingExe = Path.Combine(Path.GetTempPath(), "WinPilot_update.exe");
    private static readonly string   UpdaterBat = Path.Combine(Path.GetTempPath(), "WinPilot_updater.bat");

    // 현재 실행 파일에서 버전 읽기 (csproj <Version> 태그와 연동)
    public static Version CurrentVersion
        => Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);

    public static string CurrentVersionText
        => $"v{CurrentVersion.Major}.{CurrentVersion.Minor}.{CurrentVersion.Build}";

    /// <summary>
    /// GitHub Releases API로 최신 버전 확인.
    /// 새 버전이 있으면 UpdateInfo 반환, 없으면 null.
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

            // WinPilot.exe asset 찾기
            var asset = release.Assets?.FirstOrDefault(a =>
                string.Equals(a.Name, "WinPilot.exe", StringComparison.OrdinalIgnoreCase));
            return new UpdateInfo(release.TagName, asset?.BrowserDownloadUrl ?? "");
        }
        catch { return null; }
    }

    /// <summary>
    /// 새 버전을 다운로드합니다.
    /// autoApply=true : 다운로드 완료 후 즉시 배치 스크립트로 교체 + 재시작.
    /// autoApply=false: 다운로드만 하고 PendingExe를 보관 (나중에 재호출로 적용).
    /// progress: 0~100 다운로드 진행률
    /// </summary>
    public static async Task DownloadAndApplyAsync(string downloadUrl,
        IProgress<int>? progress = null, bool autoApply = true)
    {
        // 1. 다운로드
        using var response = await Http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? -1L;
        await using var src  = await response.Content.ReadAsStreamAsync();
        await using var dest = File.Create(PendingExe);
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

        if (!autoApply) return;   // 다운로드만 하고 종료

        // 2. 배치 스크립트 생성 (앱 종료 후 교체 → 재시작)
        var currentExe = Process.GetCurrentProcess().MainModule?.FileName
                         ?? Path.Combine(AppContext.BaseDirectory, "WinPilot.exe");

        var script = $"""
            @echo off
            timeout /t 2 /nobreak >nul
            copy /Y "{PendingExe}" "{currentExe}"
            del "{PendingExe}"
            start "" "{currentExe}"
            del "%~f0"
            """;
        await File.WriteAllTextAsync(UpdaterBat, script, System.Text.Encoding.Default);

        // 3. 배치 실행 후 앱 종료
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{UpdaterBat}\"")
        {
            UseShellExecute = true,
            WindowStyle     = ProcessWindowStyle.Hidden
        });

        Application.Current.Shutdown();
    }
}

using System.Diagnostics;
using System.Text;
using System.Threading;
using Windows.Media.Ocr;

namespace WinPilot.Services;

public sealed record OcrBootstrapStatus(bool Ready, bool Changed, string Message);


public static class OcrCapabilityService
{
    private sealed record OcrCapabilitySpec(string LanguageTag, string CapabilityName, string DisplayName);
    private static readonly OcrCapabilitySpec[] _requiredCapabilities =
    [
        new("ko-KR", "Language.OCR~~~ko-KR~0.0.1.0", "한국어"),
        new("en-US", "Language.OCR~~~en-US~0.0.1.0", "영어")
    ];

    private static readonly object _sync = new();
    private static readonly SemaphoreSlim _gate = new(1, 1);
    private static readonly HashSet<string> _installedByApp = [];

    private static Task<OcrBootstrapStatus>? _bootstrapTask;

    public static void StartBootstrap() => _ = EnsureReadyAsync();

    public static Task<OcrBootstrapStatus> EnsureReadyAsync()
    {
        lock (_sync)
            return _bootstrapTask ??= SafeBootstrapAsync();
    }

    public static async Task RestoreAsync()
    {
        var bootstrapTask = _bootstrapTask;
        if (bootstrapTask != null)
        {
            if (!bootstrapTask.IsCompleted)
                return;

            await bootstrapTask;
        }

        await _gate.WaitAsync();
        try
        {
            foreach (var spec in _requiredCapabilities.Where(spec => _installedByApp.Contains(spec.CapabilityName)))
            {
                try
                {
                    await RunDismAsync("/Online", "/Remove-Capability", $"/CapabilityName:{spec.CapabilityName}", "/NoRestart");
                }
                catch
                {
                    // 종료 시 복구는 best-effort로 처리
                }
            }

            _installedByApp.Clear();
        }
        finally
        {
            _gate.Release();
        }
    }

    private static async Task<OcrBootstrapStatus> BootstrapAsync()
    {
        await _gate.WaitAsync();
        try
        {
            var missing = GetMissingCapabilities();
            if (missing.Count == 0)
                return new OcrBootstrapStatus(Ready: true, Changed: false, Message: "");

            var failures = new List<string>();
            bool changed = false;

            foreach (var spec in missing)
            {
                try
                {
                    await RunDismAsync("/Online", "/Add-Capability", $"/CapabilityName:{spec.CapabilityName}", "/NoRestart");
                    if (IsLanguageSupported(spec.LanguageTag))
                    {
                        _installedByApp.Add(spec.CapabilityName);
                        changed = true;
                    }
                    else
                    {
                        failures.Add($"{spec.DisplayName} OCR 언어팩 적용을 확인하지 못했습니다.");
                    }
                }
                catch (Exception ex)
                {
                    failures.Add($"{spec.DisplayName} OCR 언어팩 설치 실패: {TrimError(ex.Message)}");
                }
            }

            var remaining = GetMissingCapabilities();
            if (remaining.Count == 0)
                return new OcrBootstrapStatus(Ready: true, Changed: changed, Message: "");

            if (failures.Count == 0)
                failures.Add($"{string.Join(", ", remaining.Select(spec => spec.DisplayName))} OCR 언어팩 구성이 완료되지 않았습니다.");

            return new OcrBootstrapStatus(Ready: false, Changed: changed, Message: string.Join(" / ", failures));
        }
        finally
        {
            _gate.Release();
        }
    }

    private static async Task<OcrBootstrapStatus> SafeBootstrapAsync()
    {
        try
        {
            return await BootstrapAsync();
        }
        catch (Exception ex)
        {
            return new OcrBootstrapStatus(Ready: false, Changed: false,
                Message: $"OCR 언어 구성 초기화 실패: {TrimError(ex.Message)}");
        }
    }

    private static List<OcrCapabilitySpec> GetMissingCapabilities()
        => _requiredCapabilities.Where(spec => !IsLanguageSupported(spec.LanguageTag)).ToList();

    private static bool IsLanguageSupported(string languageTag)
    {
        try
        {
            return OcrEngine.IsLanguageSupported(new Windows.Globalization.Language(languageTag));
        }
        catch
        {
            return false;
        }
    }

    public static bool HasUsableRecognizer()
    {
        try
        {
            return IsLanguageSupported("ko-KR")
                || IsLanguageSupported("en-US")
                || OcrEngine.AvailableRecognizerLanguages.Any();
        }
        catch
        {
            return IsLanguageSupported("ko-KR") || IsLanguageSupported("en-US");
        }
    }

    private static async Task RunDismAsync(params string[] arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dism.exe",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            }
        };

        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);

        process.Start();
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (process.ExitCode == 0)
            return;

        throw new InvalidOperationException(BuildProcessError(stdout, stderr, process.ExitCode));
    }

    private static string BuildProcessError(string stdout, string stderr, int exitCode)
    {
        var text = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
        text = TrimError(text);
        return string.IsNullOrWhiteSpace(text) ? $"DISM exited with code {exitCode}." : text;
    }

    private static string TrimError(string text)
    {
        text = text.Replace("\r", " ").Replace("\n", " ").Trim();
        return text.Length <= 260 ? text : text[^260..];
    }
}

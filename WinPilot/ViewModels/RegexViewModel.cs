using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace WinPilot.ViewModels;

public partial class RegexViewModel : ObservableObject
{
    private static readonly HttpClient _http = new();   // OcrViewModel과 동일 패턴

    // ─── AI 생성 ──────────────────────────────────────────
    [ObservableProperty] private string _nlPrompt    = "";
    [ObservableProperty] private bool   _isGenerating;
    [ObservableProperty] private string _genStatus   = "";
    [ObservableProperty] private string _explanation = "";

    partial void OnIsGeneratingChanged(bool value) => GenerateCommand.NotifyCanExecuteChanged();

    private bool CanGenerate() => !IsGenerating;

    [RelayCommand(CanExecute = nameof(CanGenerate))]
    private async Task GenerateAsync()
    {
        var key = SettingsViewModel.Current.DeepSeekApiKey?.Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            GenStatus = "설정 탭에서 DeepSeek API 키를 먼저 등록하세요.";
            return;
        }
        if (string.IsNullOrWhiteSpace(NlPrompt)) return;

        IsGenerating = true;
        GenStatus    = "생성 중...";
        try
        {
            var (pat, expl) = await CallDeepSeekRegexAsync(NlPrompt.Trim(), key);
            Pattern     = pat;     // 테스터로 자동 전달 → 즉시 매칭됨
            Explanation = expl;
            GenStatus   = string.IsNullOrWhiteSpace(pat) ? "정규식을 생성하지 못했습니다." : "생성 완료";
        }
        catch (Exception ex) { GenStatus = $"오류: {ex.Message}"; }
        finally { IsGenerating = false; }
    }

    // ─── 오프라인 테스터 ──────────────────────────────────
    [ObservableProperty] private string _pattern       = "";
    [ObservableProperty] private string _testInput     = "";
    [ObservableProperty] private bool   _ignoreCase;
    [ObservableProperty] private bool   _multiline;
    [ObservableProperty] private bool   _singleline;
    [ObservableProperty] private string _patternError  = "";
    [ObservableProperty] private string _matchSummary  = "";
    [ObservableProperty] private string _matchDetails  = "";
    [ObservableProperty] private string _replacement   = "";
    [ObservableProperty] private string _replaceResult = "";

    private IReadOnlyList<(int Index, int Length)> _matchRanges = [];
    public IReadOnlyList<(int Index, int Length)> MatchRanges
    {
        get => _matchRanges;
        private set { _matchRanges = value; OnPropertyChanged(); }
    }

    partial void OnPatternChanged(string value)     => Evaluate();
    partial void OnTestInputChanged(string value)   => Evaluate();
    partial void OnIgnoreCaseChanged(bool value)    => Evaluate();
    partial void OnMultilineChanged(bool value)     => Evaluate();
    partial void OnSinglelineChanged(bool value)    => Evaluate();
    partial void OnReplacementChanged(string value) => Evaluate();

    private void Evaluate()
    {
        PatternError = ""; MatchSummary = ""; MatchDetails = ""; ReplaceResult = "";
        MatchRanges = [];
        if (string.IsNullOrEmpty(Pattern)) return;

        var opts = RegexOptions.None;
        if (IgnoreCase) opts |= RegexOptions.IgnoreCase;
        if (Multiline)  opts |= RegexOptions.Multiline;
        if (Singleline) opts |= RegexOptions.Singleline;

        try
        {
            var rx = new Regex(Pattern, opts);
            var ms = rx.Matches(TestInput);
            MatchSummary = $"{ms.Count}개 일치";

            var ranges = new List<(int, int)>(ms.Count);
            foreach (Match m in ms) ranges.Add((m.Index, m.Length));
            MatchRanges = ranges;

            var sb = new StringBuilder();
            int shown = 0;
            foreach (Match m in ms)
            {
                if (shown >= 100) { sb.AppendLine($"… (그 외 {ms.Count - shown}개)"); break; }
                sb.AppendLine($"[{shown + 1}] \"{m.Value}\"  (위치 {m.Index}, 길이 {m.Length})");

                // 그룹(0번 = 전체 일치는 생략)
                for (int g = 1; g < m.Groups.Count; g++)
                {
                    var grp = m.Groups[g];
                    if (!grp.Success) continue;
                    var name = rx.GroupNameFromNumber(g);
                    var label = name == g.ToString() ? $"그룹{g}" : $"{name}";
                    sb.AppendLine($"      └ {label}: \"{grp.Value}\"");
                }
                shown++;
            }
            MatchDetails = sb.ToString().TrimEnd();

            if (!string.IsNullOrEmpty(Replacement))
                ReplaceResult = rx.Replace(TestInput, Replacement);
        }
        catch (ArgumentException ex) { PatternError = $"정규식 오류: {ex.Message}"; }
        catch (RegexMatchTimeoutException) { PatternError = "정규식 평가 시간 초과"; }
    }

    [RelayCommand]
    private void CopyPattern()
    {
        if (string.IsNullOrEmpty(Pattern)) return;
        try { Clipboard.SetText(Pattern); GenStatus = "정규식이 복사되었습니다."; } catch { }
    }

    [RelayCommand]
    private void Clear()
    {
        Pattern = TestInput = Replacement = Explanation = "";
        NlPrompt = GenStatus = "";
    }

    // ─── DeepSeek API 호출 (OpenAI 호환) ─────────────────
    private async Task<(string Pattern, string Explanation)> CallDeepSeekRegexAsync(string prompt, string apiKey)
    {
        var body = JsonSerializer.Serialize(new
        {
            model  = "deepseek-chat",
            stream = false,
            messages = new[]
            {
                new
                {
                    role    = "system",
                    content = "당신은 정규식 생성기입니다. 사용자의 자연어 설명을 보고 .NET"
                            + "(System.Text.RegularExpressions) 호환 정규식 하나를 만듭니다. "
                            + "반드시 JSON {\"pattern\":\"...\",\"explanation\":\"한국어 설명\"} 형식으로만 응답하고, "
                            + "코드블록이나 추가 설명 없이 JSON만 출력합니다."
                },
                new { role = "user", content = prompt }
            }
        });

        var req = new HttpRequestMessage(HttpMethod.Post, "https://api.deepseek.com/chat/completions");
        req.Headers.Add("Authorization", $"Bearer {apiKey}");
        req.Content = new StringContent(body, Encoding.UTF8, "application/json");

        var resp = await _http.SendAsync(req);
        resp.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var content = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? "";

        return ParseRegexJson(content);
    }

    // 응답에서 pattern/explanation 추출. 코드펜스 제거 후 JSON 파싱, 실패 시 전체를 pattern으로 폴백.
    private static (string Pattern, string Explanation) ParseRegexJson(string content)
    {
        var t = content.Trim();
        if (t.StartsWith("```"))
        {
            int nl = t.IndexOf('\n');
            if (nl >= 0) t = t[(nl + 1)..];
            if (t.EndsWith("```")) t = t[..^3];
            t = t.Trim();
        }

        try
        {
            using var doc = JsonDocument.Parse(t);
            var root = doc.RootElement;
            var pat = root.TryGetProperty("pattern", out var p) ? p.GetString() ?? "" : "";
            var exp = root.TryGetProperty("explanation", out var e) ? e.GetString() ?? "" : "";
            if (!string.IsNullOrEmpty(pat)) return (pat, exp);
        }
        catch { /* JSON 아님 → 폴백 */ }

        return (t, "");
    }
}

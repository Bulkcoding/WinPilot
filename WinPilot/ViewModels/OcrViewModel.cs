using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace WinPilot.ViewModels;

public partial class OcrViewModel : ObservableObject
{
    private static readonly HttpClient _http = new();

    private static readonly string _apiKeyPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "WinPilot", "claude_api_key.txt");

    private static readonly Dictionary<string, string> _langCodes = new()
    {
        ["한국어"]                  = "ko",
        ["English (영어)"]          = "en",
        ["日本語 (일본어)"]           = "ja",
        ["中文 简体 (중국어 간체)"]   = "zh-cn",
        ["Tiếng Việt (베트남어)"]    = "vi",
        ["Español (스페인어)"]       = "es",
        ["Deutsch (독일어)"]         = "de",
        ["Français (프랑스어)"]      = "fr",
    };

    public List<string> Languages { get; } = [.. _langCodes.Keys];

    [ObservableProperty] private string _extractedText  = "";
    [ObservableProperty] private string _translatedText = "";
    [ObservableProperty] private string _statusText     = "이미지를 캡처한 뒤 Ctrl+V로 붙여넣으세요.";
    [ObservableProperty] private bool   _isProcessing;
    [ObservableProperty] private bool   _isCorrecting;
    [ObservableProperty] private string _selectedLanguage = "한국어";
    [ObservableProperty] private string _claudeApiKey    = "";

    public bool IsApiKeySet => !string.IsNullOrWhiteSpace(ClaudeApiKey);

    partial void OnClaudeApiKeyChanged(string value) => OnPropertyChanged(nameof(IsApiKeySet));

    public OcrViewModel()
    {
        _claudeApiKey = LoadApiKey();
    }

    // ─── OCR ─────────────────────────────────────────────
    public async Task ExtractTextAsync(System.Windows.Media.Imaging.BitmapSource bitmap)
    {
        IsProcessing = true;
        StatusText   = "텍스트 인식 중...";
        ExtractedText  = "";
        TranslatedText = "";

        try
        {
            var pngBytes  = ToPngBytes(bitmap);
            var korEngine = OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("ko-KR"));
            var engEngine = OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("en-US"));

            if (korEngine == null && engEngine == null)
            {
                var fallback = OcrEngine.TryCreateFromUserProfileLanguages();
                if (fallback == null) { StatusText = "OCR 엔진을 초기화할 수 없습니다. 언어 팩을 확인하세요."; return; }
                var r = await RunOcrAsync(fallback, pngBytes);
                ExtractedText = r != null ? ReconstructSpatialText(r.Lines) : "";
                StatusText    = string.IsNullOrWhiteSpace(ExtractedText) ? "텍스트를 인식하지 못했습니다." : $"인식 완료  ·  {r!.Lines.Count}줄";
                return;
            }

            var korTask = korEngine != null ? RunOcrAsync(korEngine, pngBytes) : Task.FromResult<OcrResult?>(null);
            var engTask = engEngine != null ? RunOcrAsync(engEngine, pngBytes) : Task.FromResult<OcrResult?>(null);
            await Task.WhenAll(korTask, engTask);

            var korResult = korTask.Result;
            var engResult = engTask.Result;
            string text; int lineCount;

            if (korResult != null && engResult != null)
            {
                var korLines = ReconstructSpatialText(korResult.Lines).Split('\n').ToList();
                var engLines = ReconstructSpatialText(engResult.Lines).Split('\n').ToList();
                var merged   = MergeOcrLines(korLines, engLines);
                text = string.Join("\n", merged);
                lineCount = merged.Count(l => !string.IsNullOrWhiteSpace(l));
            }
            else
            {
                var result = (korResult ?? engResult)!;
                text = ReconstructSpatialText(result.Lines);
                lineCount = result.Lines.Count;
            }

            ExtractedText = text;
            StatusText    = string.IsNullOrWhiteSpace(text) ? "텍스트를 인식하지 못했습니다." : $"인식 완료  ·  {lineCount}줄";
        }
        catch (Exception ex) { StatusText = $"오류: {ex.Message}"; }
        finally { IsProcessing = false; }
    }

    // ─── 교정 (규칙 → Claude API) ─────────────────────────
    [RelayCommand(CanExecute = nameof(CanCorrect))]
    private async Task CorrectTextAsync()
    {
        IsCorrecting = true;

        try
        {
            // Step 1: 규칙 기반 교정 (즉시)
            var ruleFixed = ApplyRuleBasedCorrections(ExtractedText);

            // Step 2: Claude API 교정 (키가 있을 때)
            if (IsApiKeySet)
            {
                StatusText = "AI 교정 중...";
                var aiFixed = await CallClaudeApiAsync(ruleFixed);
                ExtractedText = aiFixed;
                StatusText = "AI 교정 완료";
            }
            else
            {
                ExtractedText = ruleFixed;
                StatusText = "규칙 교정 완료  (API 키 미설정 — AI 교정 생략)";
            }
        }
        catch (Exception ex) { StatusText = $"교정 오류: {ex.Message}"; }
        finally { IsCorrecting = false; }
    }

    private bool CanCorrect() => !IsProcessing && !IsCorrecting && !string.IsNullOrWhiteSpace(ExtractedText);

    partial void OnExtractedTextChanged(string value)
    {
        TranslateCommand.NotifyCanExecuteChanged();
        CorrectTextCommand.NotifyCanExecuteChanged();
    }
    partial void OnIsProcessingChanged(bool value)
    {
        TranslateCommand.NotifyCanExecuteChanged();
        CorrectTextCommand.NotifyCanExecuteChanged();
    }
    partial void OnIsCorrectingChanged(bool value) => CorrectTextCommand.NotifyCanExecuteChanged();

    // ─── Claude API 저장 ──────────────────────────────────
    [RelayCommand]
    private void SaveApiKey()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_apiKeyPath)!);
            File.WriteAllText(_apiKeyPath, ClaudeApiKey.Trim());
            StatusText = "API 키가 저장되었습니다.";
        }
        catch (Exception ex) { StatusText = $"저장 실패: {ex.Message}"; }
    }

    // ─── 번역 ─────────────────────────────────────────────
    [RelayCommand(CanExecute = nameof(CanTranslate))]
    private async Task TranslateAsync()
    {
        IsProcessing = true;
        StatusText   = "번역 중...";
        try
        {
            var code    = _langCodes.GetValueOrDefault(SelectedLanguage, "ko");
            var encoded = Uri.EscapeDataString(ExtractedText);
            var url     = $"https://translate.googleapis.com/translate_a/single?client=gtx&sl=auto&tl={code}&dt=t&q={encoded}";
            TranslatedText = ParseGoogleTranslate(await _http.GetStringAsync(url));
            StatusText = "번역 완료";
        }
        catch (Exception ex) { StatusText = $"번역 오류: {ex.Message}"; }
        finally { IsProcessing = false; }
    }

    private bool CanTranslate() => !IsProcessing && !string.IsNullOrWhiteSpace(ExtractedText);

    // ─── 규칙 기반 교정 ───────────────────────────────────
    private static string ApplyRuleBasedCorrections(string text)
    {
        // 1. Unicode 리가처 복원
        text = text
            .Replace("ﬁ", "fi").Replace("ﬂ", "fl")
            .Replace("ﬀ", "ff").Replace("ﬃ", "ffi")
            .Replace("ﬄ", "ffl");

        // 2. 한글 음절 + CJK(모음 오인식) 합성
        //    예) 조 + 卜(≈ㅏ) → ㅗ+ㅏ=ㅘ → 좌
        text = MergeHangulCjkVowels(text);

        // 3. 라틴 오인식 (필요 시 추가)
        text = text.Replace("쥐", "fi");

        // 4. 연속 공백 정리
        text = System.Text.RegularExpressions.Regex.Replace(text, @" {4,}", "   ");

        return text;
    }

    // CJK 문자 → 한글 중성 인덱스 (시각적으로 유사한 것만)
    private static readonly Dictionary<char, int> _cjkToVowelIdx = new()
    {
        ['卜'] = 0,   // 卜 ≈ ㅏ (index 0)
        ['丨'] = 20,  // 丨 ≈ ㅣ (index 20)
        ['一'] = 18,  // 一 ≈ ㅡ (index 18)
    };

    // (기존 중성 idx, 추가 중성 idx) → 합성 중성 idx  (유니코드 중성 순서 기준)
    private static readonly Dictionary<(int, int), int> _vowelMerge = new()
    {
        [(8, 0)]  = 9,   // ㅗ+ㅏ=ㅘ
        [(8, 1)]  = 10,  // ㅗ+ㅐ=ㅙ
        [(8, 20)] = 11,  // ㅗ+ㅣ=ㅚ
        [(13, 4)] = 14,  // ㅜ+ㅓ=ㅝ
        [(13, 5)] = 15,  // ㅜ+ㅔ=ㅞ
        [(13, 20)]= 16,  // ㅜ+ㅣ=ㅟ
        [(18, 20)]= 19,  // ㅡ+ㅣ=ㅢ
    };

    private static string MergeHangulCjkVowels(string text)
    {
        var sb = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            // 한글 완성형 음절(종성 없음) + CJK 모음 유사 문자
            if (c is >= '가' and <= '힣'
                && i + 1 < text.Length
                && _cjkToVowelIdx.TryGetValue(text[i + 1], out int addIdx))
            {
                int code    = c - 0xAC00;
                int finalC  = code % 28;
                int vowelC  = (code / 28) % 21;
                int initC   = code / 588;

                // 종성이 없어야 모음 합성 가능
                if (finalC == 0 && _vowelMerge.TryGetValue((vowelC, addIdx), out int merged))
                {
                    sb.Append((char)(0xAC00 + initC * 588 + merged * 28));
                    i++; // CJK 문자 건너뜀
                    continue;
                }
            }
            sb.Append(c);
        }
        return sb.ToString();
    }

    // ─── Claude API 호출 ──────────────────────────────────
    private async Task<string> CallClaudeApiAsync(string text)
    {
        var body = JsonSerializer.Serialize(new
        {
            model      = "claude-haiku-4-5-20251001",
            max_tokens = 4096,
            messages   = new[]
            {
                new
                {
                    role    = "user",
                    content = $"""
                        다음은 OCR로 추출한 텍스트입니다. 문맥을 보고 오인식 문자를 교정해 주세요.

                        규칙:
                        - 원문의 언어, 구조(줄바꿈, 들여쓰기), 의미를 유지하세요
                        - fi→fi, fl→fl 리가처 오인식, 한글⟷라틴 혼동 같은 OCR 특유 오류만 수정하세요
                        - 내용을 추가하거나 삭제하지 마세요
                        - 교정된 텍스트만 그대로 반환하세요 (설명, 마크다운 코드블록 없이)

                        OCR 텍스트:
                        {text}
                        """
                }
            }
        });

        var req = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
        req.Headers.Add("x-api-key", ClaudeApiKey.Trim());
        req.Headers.Add("anthropic-version", "2023-06-01");
        req.Content = new StringContent(body, Encoding.UTF8, "application/json");

        var resp = await _http.SendAsync(req);
        resp.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        return doc.RootElement
            .GetProperty("content")[0]
            .GetProperty("text")
            .GetString() ?? text;
    }

    // ─── 유틸 ─────────────────────────────────────────────
    private static string ParseGoogleTranslate(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var sb = new StringBuilder();
        foreach (var seg in doc.RootElement[0].EnumerateArray())
        {
            if (seg.GetArrayLength() > 0 && seg[0].ValueKind == JsonValueKind.String)
                sb.Append(seg[0].GetString());
        }
        return sb.ToString();
    }

    private static string ReconstructSpatialText(IReadOnlyList<OcrLine> lines)
    {
        if (lines.Count == 0) return "";
        var sortedLines = lines.Where(l => l.Words.Count > 0)
            .OrderBy(l => l.Words.Min(w => w.BoundingRect.Y)).ToList();
        if (sortedLines.Count == 0) return "";

        var avgHeight = sortedLines.SelectMany(l => l.Words).Average(w => w.BoundingRect.Height);
        var sb = new StringBuilder();
        for (int li = 0; li < sortedLines.Count; li++)
        {
            if (li > 0)
            {
                var prevBottom = sortedLines[li - 1].Words.Max(w => w.BoundingRect.Y + w.BoundingRect.Height);
                var currTop    = sortedLines[li].Words.Min(w => w.BoundingRect.Y);
                if (currTop - prevBottom > avgHeight * 1.0) sb.AppendLine();
            }
            var words = sortedLines[li].Words.OrderBy(w => w.BoundingRect.X).ToList();
            for (int wi = 0; wi < words.Count; wi++)
            {
                if (wi > 0)
                {
                    var prev   = words[wi - 1];
                    var curr   = words[wi];
                    var gap    = curr.BoundingRect.X - (prev.BoundingRect.X + prev.BoundingRect.Width);
                    var cw     = prev.BoundingRect.Width / Math.Max(prev.Text.Length, 1.0);
                    sb.Append(new string(' ', (int)Math.Max(1, Math.Min(Math.Round(gap / cw), 8))));
                }
                sb.Append(words[wi].Text);
            }
            sb.AppendLine();
        }
        return sb.ToString().TrimEnd();
    }

    private static List<string> MergeOcrLines(List<string> korLines, List<string> engLines)
    {
        var merged = new List<string>(Math.Max(korLines.Count, engLines.Count));
        for (int i = 0; i < Math.Max(korLines.Count, engLines.Count); i++)
        {
            var kor = i < korLines.Count ? korLines[i] : "";
            var eng = i < engLines.Count ? engLines[i] : "";
            merged.Add(ChooseLine(kor, eng));
        }
        return merged;
    }

    private static string ChooseLine(string kor, string eng)
    {
        bool korHasCJK    = kor.Any(c => c is >= '一' and <= '鿿');
        bool korHasHangul = kor.Any(c => c is >= '가' and <= '힣');
        bool korHasAscii  = kor.Any(c => c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9'));
        bool engHasCJK    = eng.Any(c => c is >= '一' and <= '鿿');
        bool engHasHangul = eng.Any(c => c is >= '가' and <= '힣');
        bool engIsAscii   = !engHasCJK && !engHasHangul && eng.Length > 0;

        if (korHasCJK && !korHasHangul && !engHasCJK) return eng;

        if (korHasHangul && korHasAscii && engIsAscii)
        {
            double hangulRatio = kor.Count(c => c is >= '가' and <= '힣') / (double)Math.Max(kor.Length, 1);
            if (hangulRatio < 0.3) return eng;
        }
        return kor;
    }

    private static byte[] ToPngBytes(System.Windows.Media.Imaging.BitmapSource source)
    {
        using var ms = new System.IO.MemoryStream();
        var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
        enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(source));
        enc.Save(ms);
        return ms.ToArray();
    }

    private static async Task<OcrResult?> RunOcrAsync(OcrEngine engine, byte[] pngBytes)
    {
        using var ras    = new InMemoryRandomAccessStream();
        using var writer = new DataWriter(ras.GetOutputStreamAt(0));
        writer.WriteBytes(pngBytes);
        await writer.StoreAsync();
        var decoder = await BitmapDecoder.CreateAsync(ras);
        using var softBitmap = await decoder.GetSoftwareBitmapAsync();
        return await engine.RecognizeAsync(softBitmap);
    }

    private static string LoadApiKey()
    {
        try { return File.Exists(_apiKeyPath) ? File.ReadAllText(_apiKeyPath).Trim() : ""; }
        catch { return ""; }
    }
}

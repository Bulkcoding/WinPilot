using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace WinPilot.ViewModels;

public partial class OcrViewModel : ObservableObject
{
    private static readonly HttpClient _http = new();

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

    // ─── OCR ─────────────────────────────────────────────
    public async Task ExtractTextAsync(BitmapSource bitmap)
    {
        IsProcessing   = true;
        StatusText     = "텍스트 인식 중...";
        ExtractedText  = "";
        TranslatedText = "";

        try
        {
            var pngVariants = BuildPreprocessedPngVariants(bitmap);
            await ExtractWithWindowsAsync(pngVariants);

            // OCR 후 자동 교정 (규칙 → DeepSeek)
            await AutoCorrectAsync();
        }
        catch (Exception ex) { StatusText = $"오류: {ex.Message}"; }
        finally { IsProcessing = false; }
    }

    private async Task ExtractWithWindowsAsync(IReadOnlyList<byte[]> pngVariants)
    {
        var korEngine = OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("ko-KR"));
        var engEngine = OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("en-US"));

        if (korEngine == null && engEngine == null)
        {
            var fallback = OcrEngine.TryCreateFromUserProfileLanguages();
            if (fallback == null) { StatusText = "OCR 엔진을 초기화할 수 없습니다. 언어 팩을 확인하세요."; return; }

            var result = await TryRecognizeBestAsync(fallback, pngVariants);
            ExtractedText = result != null ? ReconstructSpatialText(result.Lines) : "";
            StatusText = string.IsNullOrWhiteSpace(ExtractedText)
                ? "텍스트를 인식하지 못했습니다. 더 큰 글씨나 밝은 배경으로 다시 시도해 보세요."
                : $"인식 완료  ·  {result!.Lines.Count}줄";
            return;
        }

        var korTask = korEngine != null
            ? TryRecognizeBestAsync(korEngine, pngVariants)
            : Task.FromResult<OcrResult?>(null);
        var engTask = engEngine != null
            ? TryRecognizeBestAsync(engEngine, pngVariants)
            : Task.FromResult<OcrResult?>(null);
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
        else if (korResult != null || engResult != null)
        {
            var result = (korResult ?? engResult)!;
            text = ReconstructSpatialText(result.Lines);
            lineCount = result.Lines.Count;
        }
        else
        {
            text = "";
            lineCount = 0;
        }

        ExtractedText = text;
        StatusText = string.IsNullOrWhiteSpace(text)
            ? "텍스트를 인식하지 못했습니다. 더 큰 글씨나 밝은 배경으로 다시 시도해 보세요."
            : $"인식 완료  ·  {lineCount}줄";
    }

    // ─── 이미지 전처리 ────────────────────────────────────
    private static List<byte[]> BuildPreprocessedPngVariants(BitmapSource source)
    {
        return
        [
            PreprocessToPngBytes(source),
            PreprocessThresholdToPngBytes(source, threshold: 160, invert: false),
            PreprocessThresholdToPngBytes(source, threshold: 160, invert: true),
        ];
    }

    private static byte[] PreprocessToPngBytes(BitmapSource source)
    {
        var gray = new FormatConvertedBitmap(PrepareBaseBitmap(source), PixelFormats.Gray8, null, 0);
        return EncodeGrayBitmapToPng(gray);
    }

    private static byte[] PreprocessThresholdToPngBytes(BitmapSource source, byte threshold, bool invert)
    {
        var gray = new FormatConvertedBitmap(PrepareBaseBitmap(source), PixelFormats.Gray8, null, 0);
        int width = gray.PixelWidth;
        int height = gray.PixelHeight;
        int stride = width;
        var pixels = new byte[stride * height];
        gray.CopyPixels(pixels, stride, 0);

        for (int i = 0; i < pixels.Length; i++)
        {
            byte value = pixels[i] >= threshold ? (byte)255 : (byte)0;
            pixels[i] = invert ? (byte)(255 - value) : value;
        }

        var binary = BitmapSource.Create(width, height, 96, 96, PixelFormats.Gray8, null, pixels, stride);
        return EncodeGrayBitmapToPng(binary);
    }

    private static BitmapSource PrepareBaseBitmap(BitmapSource source)
    {
        BitmapSource img = source;
        double maxDim = Math.Max(img.PixelWidth, img.PixelHeight);
        if (maxDim > 0 && maxDim < 2200)
        {
            double scale = Math.Min(4.0, 2200.0 / maxDim);
            img = new TransformedBitmap(img, new ScaleTransform(scale, scale));
        }
        return img;
    }

    private static byte[] EncodeGrayBitmapToPng(BitmapSource source)
    {
        using var ms = new MemoryStream();
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(source));
        enc.Save(ms);
        return ms.ToArray();
    }

    private static async Task<OcrResult?> TryRecognizeBestAsync(OcrEngine engine, IReadOnlyList<byte[]> pngVariants)
    {
        OcrResult? best = null;
        int bestScore = -1;

        foreach (var pngBytes in pngVariants)
        {
            var result = await RunOcrAsync(engine, pngBytes);
            int score = ScoreOcrResult(result);
            if (score > bestScore)
            {
                best = result;
                bestScore = score;
            }
        }

        return bestScore > 0 ? best : null;
    }

    private static int ScoreOcrResult(OcrResult? result)
    {
        if (result == null) return 0;
        int wordCount = result.Lines.Sum(line => line.Words.Count);
        int charCount = result.Text?.Count(c => !char.IsWhiteSpace(c)) ?? 0;
        return (wordCount * 1000) + charCount;
    }

    // ─── 자동 교정 (규칙 → DeepSeek) ─────────────────────
    private async Task AutoCorrectAsync()
    {
        if (string.IsNullOrWhiteSpace(ExtractedText)) return;

        // Step 1: 규칙 기반 교정 (즉시)
        var ruleFixed = ApplyRuleBasedCorrections(ExtractedText);
        ExtractedText = ruleFixed;

        // Step 2: DeepSeek API 교정 (설정 탭에서 키가 설정된 경우)
        var apiKey = SettingsViewModel.Current.DeepSeekApiKey?.Trim();
        if (string.IsNullOrWhiteSpace(apiKey)) return;

        try
        {
            IsCorrecting = true;
            StatusText   = "AI 교정 중...";
            var aiFixed  = await CallDeepSeekApiAsync(ruleFixed, apiKey);
            if (!string.IsNullOrWhiteSpace(aiFixed))
                ExtractedText = aiFixed.Trim();
            StatusText = "AI 교정 완료";
        }
        catch (Exception ex) { StatusText = $"교정 오류: {ex.Message}"; }
        finally { IsCorrecting = false; }
    }

    partial void OnExtractedTextChanged(string value) => TranslateCommand.NotifyCanExecuteChanged();
    partial void OnIsProcessingChanged(bool value)    => TranslateCommand.NotifyCanExecuteChanged();

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

    // ─── DeepSeek API 호출 (OpenAI 호환) ─────────────────
    private async Task<string> CallDeepSeekApiAsync(string text, string apiKey)
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
                    content = "당신은 OCR 텍스트 교정기입니다. 입력 텍스트의 오인식 문자만 교정하고, "
                            + "원문의 언어·구조(줄바꿈/들여쓰기)·의미를 그대로 유지합니다. "
                            + "내용을 추가하거나 삭제하지 않으며, 설명이나 마크다운 코드블록 없이 교정된 텍스트만 출력합니다."
                },
                new
                {
                    role    = "user",
                    content = $"다음 OCR 텍스트를 교정해줘:\n\n{text}"
                }
            }
        });

        var req = new HttpRequestMessage(HttpMethod.Post, "https://api.deepseek.com/chat/completions");
        req.Headers.Add("Authorization", $"Bearer {apiKey}");
        req.Content = new StringContent(body, Encoding.UTF8, "application/json");

        var resp = await _http.SendAsync(req);
        resp.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        return doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
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

    private static async Task<OcrResult?> RunOcrAsync(OcrEngine engine, byte[] pngBytes)
    {
        using var ras    = new InMemoryRandomAccessStream();
        using var writer = new DataWriter(ras.GetOutputStreamAt(0));
        writer.WriteBytes(pngBytes);
        await writer.StoreAsync();
        var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(ras);
        using var softBitmap = await decoder.GetSoftwareBitmapAsync();
        return await engine.RecognizeAsync(softBitmap);
    }
}

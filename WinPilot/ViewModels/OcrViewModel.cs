using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tesseract;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace WinPilot.ViewModels;

public partial class OcrViewModel : ObservableObject
{
    private static readonly HttpClient _http = new();
    private static readonly string _tessDataPath = Path.Combine(AppContext.BaseDirectory, "tessdata");
    private static TesseractEngine? _engine;

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

    // ─── OCR: Windows OCR이 이미 설치돼 있으면 그걸 쓰고(더 빠름),
    //          없으면 앱에 내장된 Tesseract(fast)로 대체 — 설치 시도/재부팅 없음 ──
    public async Task ExtractTextAsync(BitmapSource bitmap)
    {
        IsProcessing   = true;
        StatusText     = "텍스트 인식 중...";
        ExtractedText  = "";
        TranslatedText = "";

        try
        {
            var pngVariants = BuildPreprocessedPngVariants(bitmap);
            var korEngine = OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("ko-KR"));
            var engEngine = OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("en-US"));

            if (korEngine != null || engEngine != null)
                await ExtractWithWindowsOcrAsync(korEngine, engEngine, pngVariants);
            else
                await ExtractWithTesseractAsync(pngVariants);

            // OCR 후 자동 교정 (규칙 → DeepSeek)
            await AutoCorrectAsync();
        }
        catch (Exception ex) { StatusText = $"오류: {ex.Message}"; }
        finally { IsProcessing = false; }
    }

    // ─── Windows OCR 경로 (언어팩이 이미 설치된 PC — 더 빠름) ──
    private async Task ExtractWithWindowsOcrAsync(OcrEngine? korEngine, OcrEngine? engEngine, IReadOnlyList<byte[]> pngVariants)
    {
        var korTask = korEngine != null
            ? TryRecognizeBestWindowsAsync(korEngine, pngVariants)
            : Task.FromResult<OcrResult?>(null);
        var engTask = engEngine != null
            ? TryRecognizeBestWindowsAsync(engEngine, pngVariants)
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

    private static async Task<OcrResult?> TryRecognizeBestWindowsAsync(OcrEngine engine, IReadOnlyList<byte[]> pngVariants)
    {
        OcrResult? best = null;
        int bestScore = -1;

        foreach (var pngBytes in pngVariants)
        {
            var result = await RunWindowsOcrAsync(engine, pngBytes);
            int score = ScoreWindowsOcrResult(result);
            if (score > bestScore)
            {
                best = result;
                bestScore = score;
            }
        }

        return bestScore > 0 ? best : null;
    }

    private static async Task<OcrResult?> RunWindowsOcrAsync(OcrEngine engine, byte[] pngBytes)
    {
        using var ras    = new InMemoryRandomAccessStream();
        using var writer = new DataWriter(ras.GetOutputStreamAt(0));
        writer.WriteBytes(pngBytes);
        await writer.StoreAsync();
        var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(ras);
        using var softBitmap = await decoder.GetSoftwareBitmapAsync();
        return await engine.RecognizeAsync(softBitmap);
    }

    private static int ScoreWindowsOcrResult(OcrResult? result)
    {
        if (result == null) return 0;
        int lineCount = result.Lines.Count;
        int wordCount = result.Lines.Sum(line => line.Words.Count);
        int charCount = result.Text?.Count(c => !char.IsWhiteSpace(c)) ?? 0;
        return (lineCount * 10000) + (wordCount * 100) + charCount;
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

    // ─── Tesseract 경로 (Windows OCR 언어팩이 없는 PC — 앱에 내장, 설치/재부팅 불필요) ──
    private async Task ExtractWithTesseractAsync(IReadOnlyList<byte[]> pngVariants)
    {
        TesseractEngine engine;
        try
        {
            engine = GetEngine();
        }
        catch (Exception ex)
        {
            StatusText = $"OCR 엔진을 초기화할 수 없습니다: {ex.Message}";
            return;
        }

        var (text, lineCount) = await Task.Run(() => RecognizeBest(engine, pngVariants));
        ExtractedText = text;
        StatusText = string.IsNullOrWhiteSpace(text)
            ? "텍스트를 인식하지 못했습니다. 더 큰 글씨나 밝은 배경으로 다시 시도해 보세요."
            : $"인식 완료  ·  {lineCount}줄";
    }

    private static TesseractEngine GetEngine()
        => _engine ??= new TesseractEngine(_tessDataPath, "kor+eng", EngineMode.Default);

    // 전처리된 후보 이미지들을 모두 인식해보고 가장 결과가 좋은 것을 채택
    private static (string text, int lineCount) RecognizeBest(TesseractEngine engine, IReadOnlyList<byte[]> pngVariants)
    {
        string bestText = "";
        int bestScore = -1;

        foreach (var pngBytes in pngVariants)
        {
            using var pix = Pix.LoadFromMemory(pngBytes);
            using var page = engine.Process(pix);
            var text = page.GetText()?.TrimEnd() ?? "";
            int score = ScoreText(text);
            if (score > bestScore)
            {
                bestScore = score;
                bestText  = text;
            }
        }

        if (bestScore <= 0) return ("", 0);
        int lineCount = bestText.Split('\n').Count(l => !string.IsNullOrWhiteSpace(l));
        return (bestText, lineCount);
    }

    private static int ScoreText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        var lines = text.Split('\n').Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        int wordCount = lines.Sum(l => l.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length);
        int charCount = text.Count(c => !char.IsWhiteSpace(c));
        return (lines.Count * 10000) + (wordCount * 100) + charCount;
    }

    // ─── 이미지 전처리 ────────────────────────────────────
    private static List<byte[]> BuildPreprocessedPngVariants(BitmapSource source)
    {
        var prepared = PrepareBaseBitmap(source);
        return
        [
            PreprocessColorToPngBytes(prepared),
            PreprocessToPngBytes(prepared),
            PreprocessContrastToPngBytes(prepared, invert: false),
            PreprocessContrastToPngBytes(prepared, invert: true),
            PreprocessThresholdToPngBytes(prepared, threshold: 128, invert: false),
            PreprocessThresholdToPngBytes(prepared, threshold: 176, invert: false),
            PreprocessThresholdToPngBytes(prepared, threshold: 128, invert: true),
            PreprocessThresholdToPngBytes(prepared, threshold: 176, invert: true),
        ];
    }

    private static byte[] PreprocessColorToPngBytes(BitmapSource source)
    {
        return EncodeBitmapToPng(source);
    }

    private static byte[] PreprocessToPngBytes(BitmapSource source)
    {
        var gray = new FormatConvertedBitmap(source, PixelFormats.Gray8, null, 0);
        return EncodeBitmapToPng(gray);
    }

    private static byte[] PreprocessContrastToPngBytes(BitmapSource source, bool invert)
    {
        var gray = new FormatConvertedBitmap(source, PixelFormats.Gray8, null, 0);
        var normalized = NormalizeGrayBitmap(gray, invert);
        return EncodeBitmapToPng(normalized);
    }

    private static byte[] PreprocessThresholdToPngBytes(BitmapSource source, byte threshold, bool invert)
    {
        var gray = new FormatConvertedBitmap(source, PixelFormats.Gray8, null, 0);
        var normalized = NormalizeGrayBitmap(gray, invert: false);
        int width = normalized.PixelWidth;
        int height = normalized.PixelHeight;
        int stride = width;
        var pixels = new byte[stride * height];
        normalized.CopyPixels(pixels, stride, 0);

        for (int i = 0; i < pixels.Length; i++)
        {
            byte value = pixels[i] >= threshold ? (byte)255 : (byte)0;
            pixels[i] = invert ? (byte)(255 - value) : value;
        }

        var binary = BitmapSource.Create(width, height, 96, 96, PixelFormats.Gray8, null, pixels, stride);
        binary.Freeze();
        return EncodeBitmapToPng(binary);
    }

    private static BitmapSource PrepareBaseBitmap(BitmapSource source)
    {
        BitmapSource img = source;
        double maxDim = Math.Max(img.PixelWidth, img.PixelHeight);
        if (maxDim > 0 && maxDim < 2600)
        {
            double scale = Math.Min(5.0, 2600.0 / maxDim);
            img = new TransformedBitmap(img, new ScaleTransform(scale, scale));
        }

        int width = Math.Max(img.PixelWidth, 1);
        int height = Math.Max(img.PixelHeight, 1);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new System.Windows.Rect(0, 0, width, height));
            dc.DrawImage(img, new System.Windows.Rect(0, 0, width, height));
        }

        var rendered = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        rendered.Render(visual);
        rendered.Freeze();
        return rendered;
    }

    private static BitmapSource NormalizeGrayBitmap(BitmapSource source, bool invert)
    {
        int width = source.PixelWidth;
        int height = source.PixelHeight;
        int stride = width;
        var pixels = new byte[stride * height];
        source.CopyPixels(pixels, stride, 0);

        byte min = 255;
        byte max = 0;
        for (int i = 0; i < pixels.Length; i++)
        {
            if (pixels[i] < min) min = pixels[i];
            if (pixels[i] > max) max = pixels[i];
        }

        int range = Math.Max(max - min, 1);
        for (int i = 0; i < pixels.Length; i++)
        {
            int value = (pixels[i] - min) * 255 / range;
            pixels[i] = invert ? (byte)(255 - value) : (byte)value;
        }

        var normalized = BitmapSource.Create(width, height, 96, 96, PixelFormats.Gray8, null, pixels, stride);
        normalized.Freeze();
        return normalized;
    }

    private static byte[] EncodeBitmapToPng(BitmapSource source)
    {
        using var ms = new MemoryStream();
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(source));
        enc.Save(ms);
        return ms.ToArray();
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
}

using System.Text;
using System.Text.Json;
using System.Windows;
using System.Xml;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace WinPilot.ViewModels;

public partial class ParserViewModel : ObservableObject
{
    // ─── JWT ───────────────────────────────────────────────
    [ObservableProperty] private string _jwtInput     = "";
    [ObservableProperty] private string _jwtHeader    = "";
    [ObservableProperty] private string _jwtPayload   = "";
    [ObservableProperty] private string _jwtSignature = "";
    [ObservableProperty] private string _jwtError     = "";

    [RelayCommand]
    private void ParseJwt()
    {
        JwtHeader = JwtPayload = JwtSignature = JwtError = "";
        var parts = JwtInput.Trim().Split('.');
        if (parts.Length != 3) { JwtError = "유효하지 않은 JWT (헤더.페이로드.서명 3부분 필요)"; return; }
        try
        {
            JwtHeader    = PrettyJson(DecodeBase64Url(parts[0]));
            JwtPayload   = PrettyJson(DecodeBase64Url(parts[1]));
            JwtSignature = parts[2];
        }
        catch (Exception ex) { JwtError = $"파싱 오류: {ex.Message}"; }
    }

    [RelayCommand] private void ClearJwt() =>
        JwtInput = JwtHeader = JwtPayload = JwtSignature = JwtError = "";

    [RelayCommand] private void CopyJwtPayload() => TrySetClipboard(JwtPayload);
    [RelayCommand] private void CopyJwtHeader()  => TrySetClipboard(JwtHeader);

    // ─── Base64 ────────────────────────────────────────────
    [ObservableProperty] private string _base64Input  = "";
    [ObservableProperty] private string _base64Output = "";
    [ObservableProperty] private string _base64Error  = "";

    [RelayCommand]
    private void EncodeBase64()
    {
        Base64Error = "";
        try { Base64Output = Convert.ToBase64String(Encoding.UTF8.GetBytes(Base64Input)); }
        catch (Exception ex) { Base64Error = $"인코딩 오류: {ex.Message}"; }
    }

    [RelayCommand]
    private void DecodeBase64()
    {
        Base64Error = "";
        try { Base64Output = Encoding.UTF8.GetString(Convert.FromBase64String(Base64Input.Trim())); }
        catch (Exception ex) { Base64Error = $"디코딩 오류: {ex.Message}"; }
    }

    [RelayCommand] private void ClearBase64()    => Base64Input = Base64Output = Base64Error = "";
    [RelayCommand] private void CopyBase64Output() => TrySetClipboard(Base64Output);

    // ─── JSON ──────────────────────────────────────────────
    [ObservableProperty] private string _jsonInput  = "";
    [ObservableProperty] private string _jsonOutput = "";
    [ObservableProperty] private string _jsonError  = "";

    [RelayCommand]
    private void FormatJson()
    {
        JsonError = "";
        try
        {
            using var doc = JsonDocument.Parse(JsonInput,
                new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            JsonOutput = JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex) { JsonError = $"JSON 오류: {ex.Message}"; }
    }

    [RelayCommand]
    private void MinifyJson()
    {
        JsonError = "";
        try
        {
            using var doc = JsonDocument.Parse(JsonInput,
                new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            JsonOutput = JsonSerializer.Serialize(doc);
        }
        catch (Exception ex) { JsonError = $"JSON 오류: {ex.Message}"; }
    }

    [RelayCommand] private void ClearJson()       => JsonInput = JsonOutput = JsonError = "";
    [RelayCommand] private void CopyJsonOutput()  => TrySetClipboard(JsonOutput);

    // ─── XML ───────────────────────────────────────────────
    [ObservableProperty] private string _xmlInput  = "";
    [ObservableProperty] private string _xmlOutput = "";
    [ObservableProperty] private string _xmlError  = "";

    [RelayCommand]
    private void FormatXml()
    {
        XmlError = "";
        try
        {
            var doc = new XmlDocument();
            doc.LoadXml(XmlInput.Trim());
            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { Indent = true, IndentChars = "  ", NewLineOnAttributes = false };
            using var writer = XmlWriter.Create(sb, settings);
            doc.Save(writer);
            XmlOutput = sb.ToString();
        }
        catch (Exception ex) { XmlError = $"XML 오류: {ex.Message}"; }
    }

    [RelayCommand] private void ClearXml()      => XmlInput = XmlOutput = XmlError = "";
    [RelayCommand] private void CopyXmlOutput() => TrySetClipboard(XmlOutput);

    // ─── cm ↔ px ──────────────────────────────────────────
    public List<int> DpiPresets { get; } = [72, 96, 120, 144, 192, 300];

    [ObservableProperty] private int    _selectedDpi = 96;
    [ObservableProperty] private int    _customDpi   = 0;
    [ObservableProperty] private string _cmInput   = "";
    [ObservableProperty] private string _pxInput   = "";
    [ObservableProperty] private string _cmOutput  = "";
    [ObservableProperty] private string _pxOutput  = "";
    [ObservableProperty] private string _dpiError  = "";

    partial void OnSelectedDpiChanged(int value) => DpiError = "";

    partial void OnCustomDpiChanged(int value)
    {
        if (value < 0) CustomDpi = 0;
        DpiError = "";
    }

    private int CurrentDpi => CustomDpi > 0 ? CustomDpi : SelectedDpi;

    [RelayCommand]
    private void CmToPx()
    {
        DpiError = "";
        var dpi = CurrentDpi;
        if (!double.TryParse(CmInput, out var cm) || cm < 0)
        { DpiError = "올바른 cm 값을 입력하세요"; return; }
        PxOutput = $"{cm * dpi / 2.54:F2}";
    }

    [RelayCommand]
    private void PxToCm()
    {
        DpiError = "";
        var dpi = CurrentDpi;
        if (!double.TryParse(PxInput, out var px) || px < 0)
        { DpiError = "올바른 px 값을 입력하세요"; return; }
        CmOutput = $"{px * 2.54 / dpi:F2}";
    }

    [RelayCommand] private void ClearDpi() =>
        CmInput = PxInput = CmOutput = PxOutput = DpiError = "";

    [RelayCommand] private void CopyCmOutput() => TrySetClipboard(CmOutput);
    [RelayCommand] private void CopyPxOutput() => TrySetClipboard(PxOutput);

    // ─── 공통 유틸 ─────────────────────────────────────────
    private static string DecodeBase64Url(string input)
    {
        input = input.Replace('-', '+').Replace('_', '/');
        var pad = (4 - input.Length % 4) % 4;
        input += new string('=', pad);
        return Encoding.UTF8.GetString(Convert.FromBase64String(input));
    }

    private static string PrettyJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
    }

    private static void TrySetClipboard(string text)
    {
        if (!string.IsNullOrEmpty(text))
            try { Clipboard.SetText(text); } catch { }
    }
}

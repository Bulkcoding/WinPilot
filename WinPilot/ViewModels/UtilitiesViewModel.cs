using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinPilot.Models;

namespace WinPilot.ViewModels;

/// <summary>파싱 결과 1건 (항목 : 값)</summary>
public class EntityField
{
    public string Key   { get; init; } = "";
    public string Value { get; init; } = "";
}

public partial class UtilitiesViewModel : ObservableObject
{
    // Converter(구 파서) 탭을 Utiles에 통합 — ParserView가 이 VM을 DataContext로 사용
    public ParserViewModel Parser { get; } = new();

    // ─── 검진 종류 / 검사 항목 (연동 콤보박스) ─────────────────
    private static readonly Dictionary<string, string[]> SubTypeMap = new()
    {
        ["일반검진"] = new[]
        {
            "rst01", "rst03", "rst04", "문진",
            "생활습관(흡연)", "생활습관(음주)", "생활습관(운동)", "생활습관(영양)",
            "생활습관(비만)", "생활습관(우울증)", "생활습관(조기정신증)", "생활습관(처방전)",
        },
        ["암검진"] = new[]
        {
            "문진", "위암", "대장암1차", "대장암2차", "간암1차", "간암2차", "유방암", "자궁경부암",
        },
    };

    public ObservableCollection<string> Categories { get; } = new() { "일반검진", "암검진" };
    public ObservableCollection<string> SubTypes   { get; } = new();

    [ObservableProperty] private string  _selectedCategory = "일반검진";
    [ObservableProperty] private string? _selectedSubType;

    // 현재 선택된 항목의 엔티티 구조 (내부 전용, 화면 미표시)
    private List<SamContext>? _schema;

    // ─── 검진 입력/결과 ────────────────────────────────────────
    [ObservableProperty] private string _checkupInput = "";
    [ObservableProperty] private string _checkupError = "";

    /// <summary>정리된 결과 (항목:값)</summary>
    public ObservableCollection<EntityField> CheckupFields { get; } = new();

    public UtilitiesViewModel() => OnSelectedCategoryChanged(SelectedCategory);

    // 검진 종류 변경 → 검사 항목 목록 갱신 후 첫 항목 선택
    partial void OnSelectedCategoryChanged(string value)
    {
        SubTypes.Clear();
        if (SubTypeMap.TryGetValue(value, out var subs))
            foreach (var s in subs) SubTypes.Add(s);
        SelectedSubType = SubTypes.FirstOrDefault();
    }

    // 검사 항목 변경 → 해당 엔티티 구조 로드 후(있으면) 재변환
    partial void OnSelectedSubTypeChanged(string? value)
    {
        _schema = string.IsNullOrEmpty(value) ? null : SamSchemas.Get(SelectedCategory, value);
        if (!string.IsNullOrWhiteSpace(CheckupInput)) ParseCheckup();
    }

    [RelayCommand]
    private void ParseCheckup()
    {
        CheckupError = "";
        CheckupFields.Clear();

        if (string.IsNullOrWhiteSpace(CheckupInput))
        {
            CheckupError = "SamFile을 붙여넣어 주세요.";
            return;
        }
        if (_schema is null || _schema.Count == 0)
        {
            CheckupError = $"'{SelectedSubType}' 항목의 구조가 아직 정의되지 않았습니다.";
            return;
        }

        // 첫 번째 실제 데이터 줄을 레코드로 사용 (여러 줄이면 첫 레코드).
        var record = CheckupInput
            .Replace("\r", "")
            .Split('\n')
            .FirstOrDefault(l => l.Trim().Length > 0) ?? "";

        // 각 필드를 앞 필드 길이 누적 위치에서 잘라 변환.
        int offset = 0;
        foreach (var ctx in _schema)
        {
            var raw = Slice(record, offset, ctx.Length);
            CheckupFields.Add(new EntityField { Key = ctx.Name, Value = ctx.Format(raw) });
            offset += ctx.Length;
        }
    }

    // offset부터 length만큼. 범위를 벗어나면 가능한 만큼만 반환.
    private static string Slice(string s, int start, int length)
    {
        if (start < 0 || start >= s.Length || length <= 0) return "";
        int len = Math.Min(length, s.Length - start);
        return s.Substring(start, len);
    }

    [RelayCommand]
    private void ClearCheckup()
    {
        CheckupInput = CheckupError = "";
        CheckupFields.Clear();
    }

    [RelayCommand]
    private void CopyCheckup()
    {
        if (CheckupFields.Count == 0) return;
        var sb = new StringBuilder();
        foreach (var f in CheckupFields)
            sb.AppendLine($"{f.Key}: {f.Value}");
        TrySetClipboard(sb.ToString().TrimEnd());
    }

    private static void TrySetClipboard(string text)
    {
        if (!string.IsNullOrEmpty(text))
            try { Clipboard.SetText(text); } catch { }
    }
}

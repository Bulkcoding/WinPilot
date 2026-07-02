namespace WinPilot.Models;

/// <summary>
/// 검진 종류/검사 항목별 SamFile 엔티티 구조 정의.
/// 키 = "검진종류/검사항목" (예: "암검진/간암1차"). 필드는 나열 순서대로 누적 배치된다.
/// </summary>
public static class SamSchemas
{
    private static readonly Dictionary<string, List<SamContext>> Map = new()
    {
        ["암검진/간암1차"] = LiverCancer(),
        ["암검진/간암2차"] = LiverCancer(),
    };

    /// <summary>정의된 스키마 반환. 없으면 null.</summary>
    public static List<SamContext>? Get(string category, string subType)
        => Map.TryGetValue($"{category}/{subType}", out var s) ? s : null;

    // ─── 암검진 · 간암 ─────────────────────────────────────────
    private static List<SamContext> LiverCancer() => new()
    {
        new PropertySamContext(1, "검사소견_1"),
        new PropertySamContext(1, "검사소견_2"),
        new PropertySamContext(1, "검사소견_3"),
        new PropertySamContext(1, "검사소견_4"),
        new PropertySamContext(1, "검사소견_5"),
        new PropertySamContext(1, "검사소견_6"),
        new PropertySamContext(1, "검사소견_7"),
        new BooleanStringSamContext("일cm미만_1"),
        new BooleanStringSamContext("일cm미만_2"),
        new BooleanStringSamContext("일cm미만_3"),
        new BooleanStringSamContext("일cm미만_4"),
        new BooleanStringSamContext("일cm미만_5"),
        new BooleanStringSamContext("일cm미만_6"),
        new BooleanStringSamContext("일cm미만_7"),
        new BooleanStringSamContext("일cm미만_8"),
        new BooleanStringSamContext("일cm이상_1"),
        new BooleanStringSamContext("일cm이상_2"),
        new BooleanStringSamContext("일cm이상_3"),
        new BooleanStringSamContext("일cm이상_4"),
        new BooleanStringSamContext("일cm이상_5"),
        new BooleanStringSamContext("일cm이상_6"),
        new BooleanStringSamContext("일cm이상_7"),
        new BooleanStringSamContext("일cm이상_8"),
        new PropertySamContext(3, "병변크기_1"),
        new PropertySamContext(3, "병변크기_2"),
        new PropertySamContext(3, "병변크기_3"),
        new BooleanStringSamContext("기타_담관확장"),
        new BooleanStringSamContext("기타_간내담관결석"),
        new BooleanStringSamContext("기타_복수"),
        new BooleanStringSamContext("기타_비장종대"),
        new BooleanStringSamContext("기타_간문맥_간정맥혈전"),
        new BooleanStringSamContext("기타_담낭이상"),
        new BooleanStringSamContext("기타_기타"),
        new PropertySamContext(40, "기타_담낭이상_텍스트"),
        new PropertySamContext(40, "기타_기타_텍스트"),
        new PropertySamContext(1, "관찰소견_검사방법"),
        new PropertySamContext(1, "관찰소견_일반"),
        new PropertySamContext(6, "관찰소견_정밀_결과"),
        new PropertySamContext(1, "관찰소견_정밀_단위"),
        new PropertySamContext(6, "관찰소견_기준치"),
        new PropertySamContext(1, "검진장소"),
        new DateTimeSamContext("yyyyMMdd", "검진일자"),
        new PropertySamContext(1, "판정구분_판정"),
        new BooleanStringSamContext("기존간암환자"),
        new PropertySamContext(40, "판정구분_기타세부사항"),
        new PropertySamContext(600, "권고사항"),
        new DateTimeSamContext("yyyyMMdd", "판정일자"),
        new PropertySamContext(10, "판정의사.doctLicense"),
        new PropertySamContext(12, "판정의사.emplNm"),
        new PropertySamContext(13, "판정의사.peopid"),
        new PropertySamContext(1, "상담료"),
        new PropertySamContext(10, "검사의사_면허번호"),
        new PropertySamContext(12, "검사의사_이름"),
        new PropertySamContext(1, "장애인_안전편의관리"),
    };
}

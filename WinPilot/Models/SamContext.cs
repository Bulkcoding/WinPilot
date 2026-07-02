using System.Globalization;

namespace WinPilot.Models;

/// <summary>
/// SamFile 필드 1건. 필드는 순차적으로 나열되며 시작 위치 = 앞 필드들의 Length 누적합.
/// </summary>
public abstract class SamContext
{
    public string Name   { get; }
    public int    Length { get; }

    protected SamContext(string name, int length)
    {
        Name   = name;
        Length = length;
    }

    /// <summary>잘라낸 원본 문자열을 표시용 값으로 변환.</summary>
    public virtual string Format(string raw) => raw.Trim();
}

/// <summary>고정 길이 문자열 필드.</summary>
public sealed class PropertySamContext : SamContext
{
    public PropertySamContext(int length, string name) : base(name, length) { }
}

/// <summary>1자리 불리언 문자열 필드. "1"/"Y" → Y, "0"/공백 → N.</summary>
public sealed class BooleanStringSamContext : SamContext
{
    public BooleanStringSamContext(string name) : base(name, 1) { }

    public override string Format(string raw)
    {
        var v = raw.Trim();
        return v switch
        {
            "1" or "Y" or "y" or "T" or "t" => "Y",
            "0" or "N" or "n" or "F" or "f" or "" => "N",
            _ => v,
        };
    }
}

/// <summary>날짜 필드. 길이 = 포맷 길이. 파싱 성공 시 yyyy-MM-dd로 표시.</summary>
public sealed class DateTimeSamContext : SamContext
{
    private readonly string _format;

    public DateTimeSamContext(string format, string name) : base(name, format.Length)
        => _format = format;

    public override string Format(string raw)
    {
        var v = raw.Trim();
        return DateTime.TryParseExact(v, _format, CultureInfo.InvariantCulture,
                                      DateTimeStyles.None, out var dt)
            ? dt.ToString("yyyy-MM-dd")
            : v;
    }
}

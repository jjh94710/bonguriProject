using System;

/// <summary>FR-07: 판독 결과만 검사한다. 송장 정답 필드는 참조하지 않는다.</summary>
public static class LabelValidator
{
    public static LabelCheck Validate(string number, string region, CellSettings settings, out int code, out Destination destination)
    {
        code = -1; destination = Destination.NG;
        if (settings == null || settings.regionCodes == null || settings.regionNames == null || settings.terminals == null ||
            settings.regionCodes.Length != settings.regionNames.Length || settings.regionCodes.Length != settings.terminals.Length)
            throw new ArgumentException("지역 코드·이름·터미널 대응표가 올바르지 않습니다.");
        if (number == null || number.Length != 5) return LabelCheck.DigitError;
        foreach (char c in number) if (c < '0' || c > '9') return LabelCheck.DigitError;
        int prefix = (number[0] - '0') * 10 + number[1] - '0';
        int index = Array.IndexOf(settings.regionCodes, prefix);
        if (index < 0) return LabelCheck.RegionError;
        if (!string.Equals(settings.regionNames[index], region, StringComparison.Ordinal)) return LabelCheck.AddressMismatch;
        code = prefix; destination = settings.terminals[index];
        return LabelCheck.OK;
    }

    public static void Apply(BoxInfo box, CellSettings settings)
    {
        if(box.reason != ExceptionReason.None){box.destination=Destination.NG;return;}
        box.regionCode = -1;
        box.check = Validate(box.readText, box.readRegion, settings, out int code, out Destination destination);
        if (box.check != LabelCheck.OK) { box.Mark(ExceptionReason.RuleViolation); return; }
        box.regionCode = code; box.destination = destination;
    }
}

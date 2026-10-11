namespace PzlEv.Shared.Utils.Mapping;

/// <summary>
/// Klucze porównań elementów P1S/CES: kody i PSPNR bez spacji i bez rozróżniania wielkości liter; numer złożony z cyfr bez zer
/// wiodących (PSPNR z PZLPROD bywa zapisany „00012345”, a w Excelu „12345”).
/// </summary>
public static class MappingKeys
{
    public static string Key(string? value)
    {
        var text = value?.Trim() ?? "";
        if (text.Length > 0 && text.All(char.IsAsciiDigit))
        {
            var trimmed = text.TrimStart('0');
            return trimmed.Length == 0 ? "0" : trimmed;
        }
        return text.ToUpperInvariant();
    }

}

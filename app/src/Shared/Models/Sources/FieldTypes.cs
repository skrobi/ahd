namespace PzlEv.Shared.Models.Sources;

/// <summary>Typy pól parsera – rodzaj slotu w CAN_Row (CanonicalSlots) i sprawdzenie wartości przy imporcie.</summary>
public static class FieldTypes
{
    public const string Text = "text";
    public const string Decimal = "decimal";
    public const string Integer = "integer";
    public const string Date = "date";

    public const int DefaultTextLength = 400;
    public const int MaxTextLength = 4000;

    public static readonly IReadOnlyList<string> All = [Text, Decimal, Integer, Date];

    public static string Label(string type) => type switch
    {
        Text => "tekst",
        Decimal => "kwota / liczba",
        Integer => "liczba całkowita",
        Date => "data",
        _ => type,
    };

    /// <summary>Typ wartości zapisywanej do bazy (SqlBulkCopy).</summary>
    public static Type ClrType(string type) => type switch
    {
        Decimal => typeof(decimal),
        Integer => typeof(int),
        Date => typeof(DateTime),
        _ => typeof(string),
    };
}

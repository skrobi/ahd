namespace PzlEv.Shared.Models.Dictionaries;

public enum RowChangeKind
{
    Added,
    Updated,
    Removed,
}

/// <summary>Zmiana wiersza przekazywana do magazynu (jedna operacja zapisu = lista zmian).</summary>
public sealed record RowChange(
    RowChangeKind Kind,
    long? RowId,
    int? ExpectedVersion,
    string Key,
    IReadOnlyDictionary<string, string?>? Values);

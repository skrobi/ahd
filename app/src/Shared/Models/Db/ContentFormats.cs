namespace PzlEv.Shared.Models.Db;

/// <summary>Format treści wersji pliku (META_SourceFileContent.Format).</summary>
public static class ContentFormats
{
    /// <summary>Plik tekstowy (CSV, TXT) skompresowany GZip – DECOMPRESS w SQL Server zwraca oryginał.</summary>
    public const string GZip = "gzip";

    /// <summary>Plik Excel bez zmian (.xlsx, .xlsm są już archiwum ZIP).</summary>
    public const string Raw = "raw";

    /// <summary>Dawne wiersze surowe przeniesione migracją 007 (JSON na wiersz, UTF-16, GZip).</summary>
    public const string LegacyRawRows = "jsonl-utf16-gzip";
}

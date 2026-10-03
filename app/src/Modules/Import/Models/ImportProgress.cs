namespace PzlEv.Modules.Import.Models;

/// <summary>
/// Postęp importu dla ekranu: komunikat; BatchId – import rozpoczęty (wpis „w toku” w historii);
/// Location / FileName / Status – zmiana statusu pliku (etap importu z postępem i czasem, potem decyzja).
/// </summary>
public sealed record ImportProgress(string Message, long? BatchId = null, string? Location = null, string? FileName = null, string? Status = null);

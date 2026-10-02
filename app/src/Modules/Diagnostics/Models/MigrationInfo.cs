namespace PzlEv.Modules.Diagnostics.Models;

/// <summary>Wiersz listy „Migracje bazy” – skrypt sql/mssql i jego stan w bazie.</summary>
public sealed record MigrationInfo(string Name, string Status, bool Applied);

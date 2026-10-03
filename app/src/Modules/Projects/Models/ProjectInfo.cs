namespace PzlEv.Modules.Projects.Models;

/// <summary>Bieżąca wersja projektu (META_Project): kod unikalny, nazwa, typ (ProjectTypes).</summary>
public sealed record ProjectInfo(long ProjectId, int Version, string Code, string Name, string Type, DateTimeOffset RecordedAt, string RecordedBy);

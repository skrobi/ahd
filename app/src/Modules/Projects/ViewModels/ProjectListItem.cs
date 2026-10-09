namespace PzlEv.Modules.Projects.ViewModels;

/// <summary>Wiersz listy Projekty: kod, nazwa, typ, aktywny przebieg, folder (szczegóły – na ekranie projektu).</summary>
public sealed record ProjectListItem(string Code, string Name, string Type, string ActiveRun, string Folder);

using PzlEv.Shared.Models;

namespace PzlEv.Modules.Projects.ViewModels;

/// <summary>Wiersz listy Projekty: typ, rozmiar nakładki, gotowość, aktywny przebieg, folder.</summary>
public sealed record ProjectListItem(string Code, string Name, string Type, string Objectives, Pill Readiness, string ActiveRun, string Folder);

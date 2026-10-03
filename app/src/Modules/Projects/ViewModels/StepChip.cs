namespace PzlEv.Modules.Projects.ViewModels;

/// <summary>Krok kreatora na pasku kroków: bieżący / wykonany / następny.</summary>
public sealed record StepChip(string Title, bool IsCurrent, bool IsPast);

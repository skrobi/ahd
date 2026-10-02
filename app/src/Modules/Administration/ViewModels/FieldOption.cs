namespace PzlEv.Modules.Administration.ViewModels;

/// <summary>Pozycja listy pól parsera w mapowaniu; Field = "" – kolumna tylko w wierszach surowych.</summary>
public sealed record FieldOption(string Field, string Label, string Type);

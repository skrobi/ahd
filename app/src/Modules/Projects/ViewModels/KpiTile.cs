namespace PzlEv.Modules.Projects.ViewModels;

/// <summary>Kafelek zakładki Wskaźniki: nazwa, wartość i opis; Pending – wskaźnik jeszcze nieliczony (np. EV przed przebiegami).</summary>
public sealed record KpiTile(string Label, string Value, string Hint, bool Pending = false);

/// <summary>Grupa kafelków wskaźników – nazwy grup jak w tabeli struktury (<see cref="Services.StructureColumns"/>).</summary>
public sealed record KpiGroup(string Name, IReadOnlyList<KpiTile> Tiles);

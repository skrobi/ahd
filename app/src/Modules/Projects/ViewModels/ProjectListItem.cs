namespace PzlEv.Modules.Projects.ViewModels;

/// <summary>
/// Wiersz listy Projekty: kod, nazwa, typ, ostatni przebieg (kod, numer uruchomienia, stan), problemy przebiegu
/// (błędy, ostrzeżenia; Level: crit / warn / ok / muted – kolor), opis do podpowiedzi, folder.
/// </summary>
public sealed record ProjectListItem(string Code, string Name, string Type, string ActiveRun, string Folder,
    string Problems = "", string ProblemsLevel = "muted", string RunDetails = "");

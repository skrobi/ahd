namespace PzlEv.Modules.Import.Models;

/// <summary>Wynik testu dostępu: środowisko (konto, strefa, proxy, usługa WebClient), metody A–H, wniosek, raport.</summary>
public sealed record AccessTestResult(IReadOnlyList<string> Environment, IReadOnlyList<AccessMethodResult> Methods, string Conclusion, string Report);

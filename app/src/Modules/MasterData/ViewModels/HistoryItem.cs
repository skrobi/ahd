namespace PzlEv.Modules.MasterData.ViewModels;

/// <summary>Wersja wiersza w historii: kiedy, kto, wartości, czym się skończyła.</summary>
public sealed record HistoryItem(string When, string Who, string Values, string Note);

namespace PzlEv.Modules.Mapping.Models;

/// <summary>
/// Element WBS CES z danych kanonicznych ACTUALS (WBS Element, Project Definition): czy ma koszt (kwota różna od zera)
/// i w którym imporcie pojawił się pierwszy i ostatni raz.
/// </summary>
public sealed record CesElement(string WbsElement, string Project, bool HasCost, long FirstBatchId, long LastBatchId);

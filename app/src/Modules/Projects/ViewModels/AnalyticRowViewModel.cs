using System.Windows;
using PzlEv.Modules.Projects.Models;

namespace PzlEv.Modules.Projects.ViewModels;

/// <summary>Wiersz bazy analitycznej na ekranie: wcięcie według głębokości, WP i CAM jako tekst.</summary>
public sealed class AnalyticRowViewModel(AnalyticRow row)
{
    public Thickness Indent => new(row.Depth * 16, 0, 0, 0);

    public string Name => row.IsVirtual ? $"[węzeł] {row.Name}" : row.Name;

    public string? WbsElement => row.WbsElement;

    public string P1s => row.P1s;

    public string Wps => string.Join(", ", row.Wps);

    public string Cams => string.Join(", ", row.Cams);

    public decimal BacHours => row.BacHours;

    public decimal BacMaterial => row.BacMaterial;

    public string? Start => row.Start;

    public string? Finish => row.Finish;

    public string? Gap => row.Gap;
}

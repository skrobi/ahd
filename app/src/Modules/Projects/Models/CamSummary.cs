namespace PzlEv.Modules.Projects.Models;

/// <summary>Zestawienie bazy analitycznej według CAM: liczba WP, budżet godzin i materiałów, daty.</summary>
public sealed record CamSummary(string Cam, int Wps, decimal BacHours, decimal BacMaterial, string? Start, string? Finish);

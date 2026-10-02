using System.Windows;
using PzlEv.Shared.Models.Pipeline;

namespace PzlEv.Shared.Utils.Modularity;

/// <summary>
/// Kontrakt modułu funkcjonalnego (docs/architektura.md, rozdz. 5.2–5.3). Moduł to folder Modules/&lt;Nazwa&gt;/
/// z jednym plikiem wejścia &lt;Nazwa&gt;Module.cs implementującym ten interfejs. Shell zna moduły wyłącznie
/// przez ten kontrakt – lista modułów jest w Shell/ModuleCatalog.cs.
/// </summary>
public interface IModule
{
    /// <summary>Klucz z ModuleKeys.</summary>
    string Key { get; }

    /// <summary>Etykieta w menu; null – moduł bez własnego ekranu (jego etapy pokazuje ekran Przebiegu).</summary>
    string? NavLabel { get; }

    /// <summary>Licznik przy pozycji menu (opcjonalny).</summary>
    string? NavBadge => null;

    /// <summary>Dokumentacja modułu (docs/…).</summary>
    string Doc { get; }

    /// <summary>Etapy realizowane przez moduł (G1–G3, P0–P9, Z) – docs/pipeline-fazy.md.</summary>
    IReadOnlyList<StageDescriptor> Stages => [];

    /// <summary>Tworzy ekran modułu z ustawionym DataContext. Shell woła raz i przechowuje ekran.</summary>
    FrameworkElement CreateView(INavigator navigator);
}

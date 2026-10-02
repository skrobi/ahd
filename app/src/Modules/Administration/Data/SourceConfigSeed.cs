using PzlEv.Modules.Administration.Models;
using PzlEv.Modules.Administration.Services;
using PzlEv.Shared.Models.Sources;

namespace PzlEv.Modules.Administration.Data;

/// <summary>
/// Dane startowe konfiguracji importu (tylko pusta konfiguracja): definicje ACTUALS_PAF i ACTUALS_CES
/// (układ docs/zrodla-danych.md, rozdz. 4) i lokalizacja E456659 – nieaktywna, do włączenia na stanowisku z dostępem.
/// </summary>
public static class SourceConfigSeed
{
    public const string RabitE456659 = @"\\lmsp4-intl.external.lmco.com@SSL\DavWWWRoot\sites\RabbitReporting\Shared Documents\E456659";

    public static int EnsureSeeded(SourceConfigService service)
    {
        var added = 0;
        if (service.Definitions().Count == 0)
        {
            foreach (var (code, description) in new[] { ("ACTUALS_PAF", "Koszty rzeczywiste CES – PAF"), ("ACTUALS_CES", "Koszty rzeczywiste CES") })
            {
                var result = service.SaveDefinition(new DefinitionInput(
                    null, null, code, code, description, SourceParsers.ActualsColumns,
                    Grain: "pozycja kosztowa elementu WBS i elementu kosztowego w okresie",
                    KeyColumns: [], // klucz ACTUALS_* do ustalenia (O27) – bez kontroli duplikatów
                    PeriodMeaning: "koszt okresu – kolumny Fiscal Year i Period",
                    Currency: "Value TranCurr – waluta transakcji; Value in Obj. Crcy – PLN; Val.in rep.cur. – USD",
                    NumberFormat: "polski (spacja tysięcy, przecinek dziesiętny) albo liczba z Excela",
                    Parser: SourceParsers.Actuals,
                    Active: true));
                if (result.Success)
                    added++;
            }
        }
        if (service.Locations().Count == 0 && service.SaveLocation(new LocationInput(null, null, "RABIT E456659", RabitE456659, Active: false)).Success)
            added++;
        return added;
    }
}

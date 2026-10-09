using PzlEv.Shared.Models.PzlProd;

namespace PzlEv.Shared.Utils.Data;

/// <summary>
/// Odczyt struktury P1S z PZLPROD (tylko odczyt, bez kopiowania – docs/zrodla-danych.md, rozdz. 5): elementy LOG.WBS
/// i grupy LOG.WBS_DIC. Raport mapowań SAP↔CES nie jest czytany stąd – to słownik globalny w bazie PZL-EV.
/// </summary>
public interface IPzlProdSource
{
    IReadOnlyList<P1sElement> Elements();

    IReadOnlyList<P1sGroup> Groups();

    PzlProdCheck Check();

    /// <summary>
    /// Godziny (LOG.vAHDD, produktywność LOG.vAHDD_PL_CPI, DJK) i materiały dostarczone (LOG.vAPD) elementów P1S o podanych
    /// PSPNR (zakres projektu) – zsumowane po PSPNR (ProductionValues).
    /// </summary>
    IReadOnlyList<ProductionValues> Production(IReadOnlyCollection<string> pspnrs);
}

using System.Globalization;
using PzlEv.Modules.MasterData.Models;
using PzlEv.Modules.MasterData.Services;

namespace PzlEv.Modules.MasterData.Data;

/// <summary>
/// Dane startowe słowników globalnych (tylko pusty słownik): kalendarz okresów na rok bieżący i następny
/// (tygodnie ISO, okres wg czwartku tygodnia, ostatni tydzień okresu zamykający – O18) oraz Cost Category
/// z załącznika A (docs/slowniki.md). Stawki, kursy i osoby – bez danych startowych.
/// </summary>
public static class MasterDataSeed
{
    public static int EnsureSeeded(IDictionaryStore store, int year)
    {
        var added = 0;
        if (store.Current(GlobalDictionaries.Calendar).Count == 0)
            added += Add(store, GlobalDictionaries.Calendar, CalendarRows(year).Concat(CalendarRows(year + 1)));
        if (store.Current(GlobalDictionaries.CostCategory).Count == 0)
            added += Add(store, GlobalDictionaries.CostCategory, CostCategoryRows());
        return added;
    }

    public static IEnumerable<Dictionary<string, string?>> CalendarRows(int year)
    {
        var weeks = Enumerable.Range(1, ISOWeek.GetWeeksInYear(year))
            .Select(week =>
            {
                var monday = DateOnly.FromDateTime(ISOWeek.ToDateTime(year, week, DayOfWeek.Monday));
                return (Week: week, From: monday, To: monday.AddDays(6), Period: monday.AddDays(3).ToString("yyyy-MM", CultureInfo.InvariantCulture));
            })
            .ToList();
        for (var i = 0; i < weeks.Count; i++)
        {
            var w = weeks[i];
            var closing = i == weeks.Count - 1 || weeks[i + 1].Period != w.Period;
            yield return new Dictionary<string, string?>
            {
                ["Rok"] = year.ToString(CultureInfo.InvariantCulture),
                ["Tydzień"] = w.Week.ToString(CultureInfo.InvariantCulture),
                ["Okres"] = w.Period,
                ["Od"] = w.From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["Do"] = w.To.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["Zamykający"] = closing ? "tak" : "nie",
            };
        }
    }

    /// <summary>Załącznik A (docs/slowniki.md): numer, opis, obszar, Cost Category („(brak)” = pusta).</summary>
    public static IEnumerable<Dictionary<string, string?>> CostCategoryRows()
    {
        (string Number, string Description, string? Area, string? Category)[] rows =
        [
            ("0051105550", "PZL Mat Consump", null, "Direct Materials"),
            ("0057100000", "Proj Sttlmnt Bill", null, null),
            ("0051110550", "PZL Oth Dir Serv ODS", null, "Direct Services"),
            ("0057511550", "PZL ODC NVA", null, "Other Direct Cost"),
            ("0057712550", "PZL Direct MTS BFM", null, "Chemicals"),
            ("0057714550", "PZL Dir Packaging NV", null, "Packaging Materials"),
            ("0057120550", "PZL Travel", null, "Travel costs"),
            ("0092210550", "PZL Quality Control", "Quality Control", "Manufacturing and QA labor"),
            ("0092211550", "PZL Pain Spec Proc", "Manufacturing", "Manufacturing and QA labor"),
            ("0092212550", "PZL Machining (W30)", "Manufacturing", "Manufacturing and QA labor"),
            ("0092213550", "PZL Sheet metl W40", "Manufacturing", "Manufacturing and QA labor"),
            ("0092214550", "PZL Sub assem W51", "Manufacturing", "Manufacturing and QA labor"),
            ("0092216550", "PZL Fnl assem W53", "Manufacturing", "Manufacturing and QA labor"),
            ("0092215550", "PZL Sub assem W52", "Manufacturing", "Manufacturing and QA labor"),
            ("0092217550", "PZL Hangar Ops W60", "Manufacturing", "Manufacturing and QA labor"),
            ("0092218550", "PZL Svc ctr W70 DUS", "Manufacturing", "Manufacturing and QA labor"),
            ("0092219550", "Tooling", "Manufacturing", "Manufacturing and QA labor"),
            ("0092223550", "PZL LM Aero Coop W54", "Manufacturing", "Manufacturing and QA labor"),
            ("0094410550", "PZL Des Eng/Proc Eng", "Engineering", "Engineering labor"),
            ("0094414550", "PZL Des Industrial E", "Engineering", "Engineering labor"),
            ("0094412550", "PZL Programs", "Programs", "Programs labor"),
            ("0094490550", "PZL LL Des Eng/Proc", "Engineering", "Engineering labor"),
            ("0096606550", "PZL Gen Svcs labor", "General Services", "General Services"),
            ("9221X550", "PZL MFG Indirect", "Manufacturing", "Manufacturing and QA labor"),
            ("9222X550", "PZL Quality Indirect", "Quality Control", "Manufacturing and QA labor"),
            ("9229X550", "LL Mfg Indirect", "Manufacturing", "Manufacturing and QA labor"),
            ("9229D550", "PZL LL LM Aero C W54", "LL Manufacturing", "Manufacturing and QA LL labor"),
            ("9441X550", "PZL ENG Indirect", "Engineering", "Engineering labor"),
            ("9660R550", "PZL Mfg Svcs labor", "Manufacturing Services", "Manufacturing Services"),
            ("0096610550", "Procurement", "Procurement", "Procurement Labor"),
            ("9662R550", "PZL Customs and Tran", "Manufacturing Services", "Manufacturing Services"),
            ("0096626550", "PZL Shipping", "General Services", "General Services"),
            ("0096616550", "PZL Finance", "Finance", "Finance"),
        ];
        return rows.Select(r => new Dictionary<string, string?>
        {
            ["Numer elementu kosztowego"] = r.Number,
            ["Opis"] = r.Description,
            ["Obszar"] = r.Area,
            ["Cost Category"] = r.Category,
        });
    }

    private static int Add(IDictionaryStore store, string code, IEnumerable<Dictionary<string, string?>> rows)
    {
        var spec = GlobalDictionaries.Get(code);
        var changes = rows.Select(v => new RowChange(RowChangeKind.Added, null, null, spec.KeyOf(v), v)).ToList();
        var result = store.Save(code, null, changes);
        return result.Added;
    }
}

using Dapper;
using PzlEv.Shared.Models.Dictionaries;
using PzlEv.Shared.Models.Hr;
using PzlEv.Shared.Utils.Config;
using PzlEv.Shared.Utils.Data.Sql;
using PzlEv.Shared.Utils.Dictionaries;
using PzlEv.Tests.TestSupport;
using Xunit;

namespace PzlEv.Tests.Shared;

/// <summary>Słownik Osoby z PZLHRPROD (HR.ORG): odczyt, wiersze słownika, podgląd i zapis jak przy wczytaniu z Excela.</summary>
public sealed class HrPersonsTests
{
    private static HrPerson Person(string usrid, string first, string last, string costCenter = "1234") =>
        new(usrid, "100" + usrid, first, last, $"{first}.{last}@lmco.com", costCenter, "PGGF", "Dział finansów", "Analityk", "FIN", "N");

    [Fact]
    public void Hr_persons_become_dictionary_rows_with_usrid_key()
    {
        var rows = GlobalDictionaries.PersonRows([Person("e2", "Jan", "Nowak"), Person("e1", "Anna", "Kowalska"), Person("E2", "Jan", "Nowak", "9999"), Person("", "Bez", "Usrid")]);

        Assert.Equal(["e1", "e2"], rows.Select(r => r["USRID"]));          // sortowanie po nazwisku, USRID raz, bez pustego
        Assert.Equal(("Anna Kowalska", "1234", "Dział finansów"), (rows[0]["Imię i nazwisko"], rows[0]["MPK"], rows[0]["Dział – pełna nazwa"]));
        var spec = GlobalDictionaries.Get(GlobalDictionaries.Persons);
        Assert.Empty(DictionaryValidator.Validate(spec, rows));
    }

    [SqlFact]
    public void Persons_are_read_from_hr_and_replace_dictionary_after_preview()
    {
        using var database = new TestDatabase();
        var services = new TestServices();
        var hr = new SqlDatabase(new SqlSettings("", "", TestDatabase.Schema, $"H{Guid.NewGuid():N}"[..9] + "_", ConnectionString: TestDatabase.ConnectionString!), "test");
        using var connection = hr.Open();
        connection.Execute($"""
            CREATE TABLE {hr.Table("ORG")} (PERNR INT NULL, VORNA NVARCHAR(40) NULL, NACHN NVARCHAR(40) NULL, EMAIL NVARCHAR(100) NULL, KOSTL NVARCHAR(10) NULL,
                SHORT NVARCHAR(20) NULL, STEXT NVARCHAR(80) NULL, LONG NVARCHAR(100) NULL, ISMANAGER BIT NULL, PION NVARCHAR(20) NULL, USRID NVARCHAR(20) NULL);
            INSERT INTO {hr.Table("ORG")} VALUES (1001, N'Dawid', N'Moraniec', N'd.m@lmco.com', N'4711', N'PGGF', N'Kontroler', N'Dział kontrolingu', 1, N'FIN', N'e334541'),
                                                 (1002, N'Bez', N'Znaczka', NULL, NULL, NULL, NULL, NULL, 0, NULL, N''),
                                                 (1003, N'Anna', N'Nowak', NULL, N'4712', NULL, NULL, NULL, 0, NULL, N' e100200 ');
            """);
        try
        {
            var people = new SqlHrSource(hr).Persons();
            Assert.Equal(["e334541", "e100200"], people.Select(p => p.Usrid));
            Assert.Equal(("Dawid Moraniec", "4711", "1001", "1"), (people[0].FullName, people[0].CostCenter, people[0].Pernr, people[0].IsManager));

            var store = new SqlDictionaryStore(database.Sql, services.Clock, services.User, GlobalDictionaries.Tables);
            var service = new DictionaryService(store, new SqlJournal(database.Sql, services.Clock, services.User));
            var spec = GlobalDictionaries.Get(GlobalDictionaries.Persons);
            var preview = service.PreviewRows(spec, GlobalDictionaries.PersonRows(people), "PZLHRPROD HR.ORG");
            Assert.Equal(2, preview.Added.Count);
            Assert.Equal(SaveStatus.Saved, service.ApplyImport(spec, preview).Status);
            var saved = service.Load(spec).Single(r => r["USRID"] == "e334541");
            Assert.Equal(("Dawid Moraniec", "Dawid", "Moraniec", "4711", "Kontroler", "FIN"),
                (saved["Imię i nazwisko"], saved["Imię"], saved["Nazwisko"], saved["MPK"], saved["Stanowisko"], saved["Pion"]));

            // Osoby spoza HR są usuwane przy kolejnym wczytaniu (historia zostaje).
            var next = service.PreviewRows(spec, GlobalDictionaries.PersonRows(people.Take(1)), "PZLHRPROD HR.ORG");
            Assert.Equal(["e100200"], next.Removed);
        }
        finally
        {
            connection.Execute($"DROP TABLE {hr.Table("ORG")}");
        }
    }
}

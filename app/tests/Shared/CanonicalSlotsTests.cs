using PzlEv.Shared.Models.Sources;
using Xunit;

namespace PzlEv.Tests.Shared;

/// <summary>Przydział slotów CAN_Row polom parsera (bez bazy).</summary>
public sealed class CanonicalSlotsTests
{
    [Fact]
    public void Slots_cover_all_kinds_in_table_column_order()
    {
        var all = CanonicalSlots.All.ToList();
        Assert.Equal(85, all.Count);
        Assert.Equal(["T01", "T40", "L01", "L05", "N01", "N20", "I01", "I10", "D01", "D10"],
            [all[0], all[39], all[40], all[44], all[45], all[64], all[65], all[74], all[75], all[84]]);
        Assert.Equal('T', CanonicalSlots.Kind(new ParserField("A", "A", FieldTypes.Text, 400)));
        Assert.Equal('L', CanonicalSlots.Kind(new ParserField("A", "A", FieldTypes.Text, 401)));
        Assert.Equal('N', CanonicalSlots.Kind(new ParserField("A", "A", FieldTypes.Decimal)));
        Assert.False(CanonicalSlots.IsSlot("T41"));
        Assert.False(CanonicalSlots.IsSlot(null));
    }

    [Fact]
    public void Known_field_keeps_its_slot_and_new_field_takes_first_free_slot_of_its_kind()
    {
        ParserField[] history = [new("Wbs", "WBS", FieldTypes.Text, 50, Slot: "T01"), new("Old", "Old", FieldTypes.Text, 50, Slot: "T02"), new("Amount", "Kwota", FieldTypes.Decimal, Slot: "N01")];

        var (fields, errors, assigned) = CanonicalSlots.Assign(
            [new("Amount", "Kwota", FieldTypes.Decimal), new("wbs", "WBS", FieldTypes.Text, 400), new("New", "Nowa", FieldTypes.Text, 20)], history);

        Assert.Empty(errors);
        Assert.Equal(["N01", "T01", "T03"], fields.Select(f => f.Slot));   // T02 zostaje przy danych usuniętego pola Old
        Assert.Equal(["nowe pole New → T03"], assigned);
    }

    [Fact]
    public void Change_of_kind_and_too_many_fields_are_errors()
    {
        var (_, kindErrors, _) = CanonicalSlots.Assign([new("Amount", "Kwota", FieldTypes.Integer)], [new("Amount", "Kwota", FieldTypes.Decimal, Slot: "N01")]);
        Assert.Equal(["Pole Amount: dane zapisane jako kwota / liczba (slot N01) – zmiana na liczba całkowita niemożliwa; dodaj nowe pole"], kindErrors);

        var longTexts = Enumerable.Range(1, 6).Select(i => new ParserField($"Opis{i}", $"Opis {i}", FieldTypes.Text, 4000)).ToList();
        var (fields, limitErrors, _) = CanonicalSlots.Assign(longTexts, []);
        Assert.Equal(["Pole Opis6: brak wolnego slotu – tekst do 4000 znaków: najwięcej 5 pól w parserze"], limitErrors);
        Assert.Null(fields[5].Slot);
    }

    [Fact]
    public void Usage_counts_occupied_slots_by_kind()
    {
        Assert.Equal("T 2/40 · N 1/20", CanonicalSlots.Usage([new("A", "A", FieldTypes.Text, Slot: "T01"), new("B", "B", FieldTypes.Text, Slot: "T05"), new("C", "C", FieldTypes.Decimal, Slot: "N01")]));
        Assert.Equal("", CanonicalSlots.Usage([new("A", "A", FieldTypes.Text)]));
    }
}

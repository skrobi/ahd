using System.Globalization;
using PzlEv.Shared.Models.Sources;
using PzlEv.Shared.Utils.Ui.Mvvm;

namespace PzlEv.Modules.Administration.ViewModels;

/// <summary>Wiersz pól parsera w edycji (liczby jako tekst – błędną wartość zgłasza walidacja przy zapisie).</summary>
public sealed class ParserFieldRowViewModel : ObservableObject
{
    private string _field = "", _label = "", _type = FieldTypes.Text, _length = "", _padDigits = "";

    public string Field { get => _field; set => SetProperty(ref _field, value ?? ""); }
    public string Label { get => _label; set => SetProperty(ref _label, value ?? ""); }

    public string Type
    {
        get => _type;
        set
        {
            if (SetProperty(ref _type, value ?? FieldTypes.Text))
                OnPropertyChanged(nameof(IsText));
        }
    }

    public string Length { get => _length; set => SetProperty(ref _length, value ?? ""); }
    public string PadDigits { get => _padDigits; set => SetProperty(ref _padDigits, value ?? ""); }

    /// <summary>Długość i dopełnianie zerami dotyczą tylko tekstu.</summary>
    public bool IsText => Type == FieldTypes.Text;

    public static ParserFieldRowViewModel From(ParserField field) => new()
    {
        Field = field.Field,
        Label = field.Label,
        Type = field.Type,
        Length = field.Length?.ToString(CultureInfo.InvariantCulture) ?? "",
        PadDigits = field.PadDigits?.ToString(CultureInfo.InvariantCulture) ?? "",
    };

    public ParserField ToField() => new(Field, Label, Type, Number(Length), Number(PadDigits));

    private static int? Number(string text) =>
        text.Trim().Length == 0 ? null : int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : -1;
}

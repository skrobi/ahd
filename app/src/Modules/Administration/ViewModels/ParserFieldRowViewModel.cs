using System.Globalization;
using PzlEv.Shared.Models.Sources;
using PzlEv.Shared.Utils.Ui.Mvvm;

namespace PzlEv.Modules.Administration.ViewModels;

/// <summary>
/// Wiersz pól parsera w edycji: pole w bazie, kolumna w pliku, typ, długość, dopełnianie zerami, wymagane; przykład
/// wartości z pliku wczytanego przyciskiem „Kolumny z pliku…”. Liczby jako tekst – błędną wartość zgłasza walidacja.
/// </summary>
public sealed class ParserFieldRowViewModel : ObservableObject
{
    private string _field = "", _column = "", _type = FieldTypes.Text, _length = "", _padDigits = "", _sample = "";
    private bool _required;

    public string Field { get => _field; set => SetProperty(ref _field, value ?? ""); }

    public string Column
    {
        get => _column;
        set
        {
            if (!SetProperty(ref _column, value ?? ""))
                return;
            if (_column.Trim().Length == 0)
                Required = false;
            OnPropertyChanged(nameof(HasColumn));
        }
    }

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
    public bool Required { get => _required; set => SetProperty(ref _required, value); }

    /// <summary>Przykład z pliku albo informacja, że kolumny nie ma w pliku.</summary>
    public string Sample { get => _sample; set => SetProperty(ref _sample, value ?? ""); }

    /// <summary>Długość i dopełnianie zerami dotyczą tylko tekstu.</summary>
    public bool IsText => Type == FieldTypes.Text;

    /// <summary>Pole czytane z pliku – może być wymagane.</summary>
    public bool HasColumn => Column.Trim().Length > 0;

    public static ParserFieldRowViewModel From(ParserField field) => new()
    {
        Field = field.Field,
        Column = field.Column,
        Type = field.Type,
        Length = field.Length?.ToString(CultureInfo.InvariantCulture) ?? "",
        PadDigits = field.PadDigits?.ToString(CultureInfo.InvariantCulture) ?? "",
        Required = field.Required,
    };

    public ParserField ToField() => new(Field, Column, Type, Number(Length), Number(PadDigits), Required);

    private static int? Number(string text) =>
        text.Trim().Length == 0 ? null : int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : -1;
}

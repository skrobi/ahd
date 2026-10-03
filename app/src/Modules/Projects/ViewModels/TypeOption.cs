using PzlEv.Modules.Projects.Models;
using PzlEv.Modules.Projects.Services;

namespace PzlEv.Modules.Projects.ViewModels;

/// <summary>Typ projektu do wyboru w kreatorze ze słownikami projektu wymaganymi dla typu.</summary>
public sealed record TypeOption(string Code)
{
    public string Label => ProjectTypes.Label(Code);

    public string Description => ProjectTypes.Description(Code);

    public string Required => "Wymagane słowniki projektu: "
        + string.Join(", ", ProjectDictionaries.ForType(Code).Where(i => i.IsRequired(Code)).Select(i => i.Name));
}

using PzlEv.Modules.Projects.Models;

namespace PzlEv.Modules.Projects.ViewModels;

/// <summary>Typ projektu do wyboru w kreatorze.</summary>
public sealed record TypeOption(string Code)
{
    public string Label => ProjectTypes.Label(Code);

    public string Description => ProjectTypes.Description(Code);
}

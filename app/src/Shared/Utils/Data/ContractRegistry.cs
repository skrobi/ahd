namespace PzlEv.Shared.Utils.Data;

/// <summary>
/// Kontrakty modułów dla innych modułów (interfejsy z Shared): moduł rejestruje implementację w Initialize, inny moduł
/// pobiera ją przy użyciu – moduły nie znają się nawzajem (docs/architektura.md, rozdz. 5.2).
/// </summary>
public sealed class ContractRegistry
{
    private readonly Dictionary<Type, object> _contracts = [];

    public void Register<T>(T implementation) where T : class => _contracts[typeof(T)] = implementation;

    /// <summary>Implementacja kontraktu; null – moduł jej nie zarejestrował.</summary>
    public T? Get<T>() where T : class => _contracts.GetValueOrDefault(typeof(T)) as T;
}

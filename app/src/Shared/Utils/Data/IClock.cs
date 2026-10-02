namespace PzlEv.Shared.Utils.Data;

/// <summary>Bieżący czas – w testach zastępowany zegarem o ustalonym czasie.</summary>
public interface IClock
{
    DateTimeOffset Now { get; }
}

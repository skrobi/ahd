using PzlEv.Shared.Utils.Data;

namespace PzlEv.Tests.TestSupport;

/// <summary>Zegar o ustalonym czasie, przesuwany ręcznie.</summary>
public sealed class TestClock(DateTimeOffset start) : IClock
{
    public DateTimeOffset Now { get; private set; } = start;

    public void Advance(TimeSpan by) => Now += by;
}

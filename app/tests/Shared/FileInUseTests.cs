using PzlEv.Shared.Utils.Files;
using Xunit;

namespace PzlEv.Tests.Shared;

/// <summary>Plik otwarty w innym programie (np. Excel) – komunikat dla użytkownika zamiast błędu systemu.</summary>
public sealed class FileInUseTests
{
    [Fact]
    public void Sharing_violation_is_recognised_and_described()
    {
        var locked = new IOException("The process cannot access the file 'PAF2_koszty.xlsx' because it is being used by another process.", unchecked((int)0x80070020));
        Assert.True(FileInUse.Is(locked));
        Assert.StartsWith("Plik jest otwarty w innym programie (np. w Excelu)", FileInUse.Describe(locked));
        Assert.False(FileInUse.Is(new IOException("Brak miejsca na dysku", unchecked((int)0x80070070))));
        Assert.False(FileInUse.Is(new InvalidOperationException("inny błąd")));
    }
}

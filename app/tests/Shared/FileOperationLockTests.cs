using PzlEv.Shared.Utils.Data;
using PzlEv.Tests.TestSupport;
using Xunit;

namespace PzlEv.Tests.Shared;

/// <summary>Blokada operacji w pliku na dysku sieciowym – druga osoba widzi, kto trzyma blokadę.</summary>
public sealed class FileOperationLockTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("pzl-ev-lock-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private FileOperationLock Lock(string account) => new(_folder, new TestServices().Clock, new TestUser(account));

    [Fact]
    public void Second_person_cannot_acquire_and_sees_holder_until_release()
    {
        var first = Lock(@"PZL\anna");
        var second = Lock(@"PZL\jan");

        using (var lease = first.TryAcquire("import", out var none))
        {
            Assert.NotNull(lease);
            Assert.Null(none);

            Assert.Null(second.TryAcquire("import", out var holder));
            Assert.Equal(@"PZL\anna", holder!.User);
            Assert.Equal(@"PZL\anna", second.Holder("import")!.User);
            using var export = second.TryAcquire("eksport", out _);   // inna operacja – osobna blokada
            Assert.NotNull(export);
        }

        Assert.Null(second.Holder("import"));
        using var again = second.TryAcquire("import", out _);
        Assert.NotNull(again);
    }

    [Fact]
    public void Lock_file_left_without_owner_is_free()
    {
        File.WriteAllText(Path.Combine(_folder, "import.lock"), "");   // np. po awarii aplikacji

        Assert.Null(Lock(@"PZL\jan").Holder("import"));
        using var lease = Lock(@"PZL\jan").TryAcquire("import", out _);
        Assert.NotNull(lease);
    }
}

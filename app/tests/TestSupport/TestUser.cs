using PzlEv.Shared.Utils.Data;

namespace PzlEv.Tests.TestSupport;

public sealed class TestUser(string account = @"PZL\analityk") : ICurrentUser
{
    public string Account { get; set; } = account;

    public string Role => "Analityk";
}

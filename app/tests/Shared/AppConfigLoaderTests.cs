using PzlEv.Shared.Utils.Config;
using Xunit;

namespace PzlEv.Tests.Shared;

public class AppConfigLoaderTests
{
    [Fact]
    public void Missing_file_gives_defaults()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var config = AppConfigLoader.Load(dir);
        Assert.Equal("TEST", config.Environment);
        Assert.Equal(DataMode.InMemory, config.DataMode);
        Assert.EndsWith(Path.Combine("00_Global", "RABIT", "Do_importu"), config.ImportFolder);
    }

    [Fact]
    public void File_values_override_defaults()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        File.WriteAllText(Path.Combine(dir, AppConfigLoader.FileName),
            """
            {
              // komentarz dozwolony
              "Environment": "PROD",
              "NetworkRoot": "\\\\serwer\\udzial\\PZL-EV",
              "DataMode": "inmemory",
            }
            """);
        var config = AppConfigLoader.Load(dir);
        Assert.Equal("PROD", config.Environment);
        Assert.Equal(@"\\serwer\udzial\PZL-EV", config.NetworkRoot);
    }

    [Fact]
    public void Invalid_data_mode_is_reported()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        File.WriteAllText(Path.Combine(dir, AppConfigLoader.FileName), """{ "DataMode": "Oracle" }""");
        var ex = Assert.Throws<InvalidOperationException>(() => AppConfigLoader.Load(dir));
        Assert.Contains("DataMode", ex.Message);
    }
}

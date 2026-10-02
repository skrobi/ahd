using PzlEv.Shared.Utils.Config;
using Xunit;

namespace PzlEv.Tests.Shared;

public class AppConfigLoaderTests
{
    private static AppConfig LoadJson(string json)
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        File.WriteAllText(Path.Combine(dir, AppConfigLoader.FileName), json);
        return AppConfigLoader.Load(dir);
    }

    [Fact]
    public void Shipped_configuration_file_is_valid_for_test()
    {
        // app/pzl-ev.json – wzór kopiowany obok exe (z komentarzami).
        var config = AppConfigLoader.Load(AppContext.BaseDirectory);

        Assert.Equal("TEST", config.Environment);
        Assert.Equal(new SqlSettings("pzltestdb.intl.lmco.com", "PZLTEST", "FINOP", "PZLEV_"), config.Sql);
        if (OperatingSystem.IsWindows())
            Assert.DoesNotContain("%", config.NetworkRoot);   // %LOCALAPPDATA% rozwinięte
        Assert.EndsWith(Path.Combine("00_Global", "RABIT", "Do_importu"), config.ImportFolder);
    }

    [Fact]
    public void Missing_file_is_an_error_with_path_and_template()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;

        var ex = Assert.Throws<InvalidOperationException>(() => AppConfigLoader.Load(dir));

        Assert.Contains(Path.Combine(dir, AppConfigLoader.FileName), ex.Message);
        Assert.Contains(@"app\pzl-ev.json", ex.Message);
    }

    [Fact]
    public void Env_selects_environment_section()
    {
        var config = LoadJson(
            """
            {
              // komentarz dozwolony
              "Env": "prod",
              "Environments": {
                "TEST": { "NetworkRoot": "C:\\test", "Sql": { "Server": "t", "Database": "PZLTEST", "Schema": "FINOP" } },
                "PROD": { "NetworkRoot": "\\\\serwer\\udzial\\PZL-EV", "Sql": { "Server": "prod", "Database": "PZLPROD", "Schema": "EV", "TrustServerCertificate": true } },
              }
            }
            """);

        Assert.Equal("PROD", config.Environment);
        Assert.Equal(@"\\serwer\udzial\PZL-EV", config.NetworkRoot);
        Assert.Equal(new SqlSettings("prod", "PZLPROD", "EV", "PZLEV_", TrustServerCertificate: true), config.Sql);
    }

    [Theory]
    [InlineData("""{ "Environments": {} }""", "Env")]
    [InlineData("""{ "Env": "PROD", "Environments": { "TEST": {} } }""", "Environments.PROD")]
    [InlineData("""{ "Env": "TEST", "Environments": { "TEST": { "Sql": { "Server": "s", "Database": "d", "Schema": "FINOP" } } } }""", "NetworkRoot")]
    [InlineData("""{ "Env": "TEST", "Environments": { "TEST": { "NetworkRoot": "C:\\x" } } }""", "Sql")]
    [InlineData("""{ "Env": "TEST", "Environments": { "TEST": { "NetworkRoot": "C:\\x", "Sql": { "Server": "s", "Database": "d" } } } }""", "Schema")]
    [InlineData("""{ "Env": "TEST", "Environments": { "TEST": { "NetworkRoot": "C:\\x", "Sql": { "Server": "s", "Database": "d", "Schema": "FINOP]; DROP" } } } }""", "dozwolone litery")]
    [InlineData("""{ "Env": "TEST", """, "niepoprawny JSON")]
    public void Invalid_configuration_is_reported(string json, string message) =>
        Assert.Contains(message, Assert.Throws<InvalidOperationException>(() => LoadJson(json)).Message);
}

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

    private static AppConfig LoadJson(string json)
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        File.WriteAllText(Path.Combine(dir, AppConfigLoader.FileName), json);
        return AppConfigLoader.Load(dir);
    }

    [Fact]
    public void Env_selects_environment_section_with_sql_database()
    {
        var config = LoadJson(
            """
            {
              "Env": "TEST",
              "Environments": {
                "TEST": {
                  "NetworkRoot": "\\\\serwer\\udzial\\PZL-EV-TEST",
                  "DataMode": "Sql",
                  "Sql": { "Server": "pzltestdb.intl.lmco.com", "Database": "PZLTEST", "Schema": "FINOP", "TablePrefix": "PZLEV_" }
                },
                "PROD": { "DataMode": "Sql", "Sql": { "Server": "prod", "Database": "PZLPROD", "Schema": "EV" } }
              }
            }
            """);

        Assert.Equal("TEST", config.Environment);
        Assert.Equal(DataMode.Sql, config.DataMode);
        Assert.Equal(@"\\serwer\udzial\PZL-EV-TEST", config.NetworkRoot);
        Assert.Equal(new SqlSettings("pzltestdb.intl.lmco.com", "PZLTEST", "FINOP", "PZLEV_"), config.Sql);
    }

    [Fact]
    public void Prod_switch_and_default_table_prefix()
    {
        var config = LoadJson("""{ "Env": "prod", "Environments": { "PROD": { "DataMode": "Sql", "Sql": { "Server": "s", "Database": "d", "Schema": "EV" } } } }""");

        Assert.Equal("PROD", config.Environment);
        Assert.Equal("PZLEV_", config.Sql!.TablePrefix);
        Assert.Equal("EV", config.Sql.Schema);
    }

    [Theory]
    [InlineData("""{ "Env": "PROD", "Environments": { "TEST": {} } }""", "Environments.PROD")]
    [InlineData("""{ "Env": "TEST", "Environments": { "TEST": { "DataMode": "Sql" } } }""", "sekcji Sql")]
    [InlineData("""{ "Env": "TEST", "Environments": { "TEST": { "Sql": { "Server": "s", "Database": "d" } } } }""", "Schema")]
    [InlineData("""{ "Env": "TEST", "Environments": { "TEST": { "Sql": { "Server": "s", "Database": "d", "Schema": "FINOP]; DROP" } } } }""", "dozwolone litery")]
    public void Invalid_environment_configuration_is_reported(string json, string message) =>
        Assert.Contains(message, Assert.Throws<InvalidOperationException>(() => LoadJson(json)).Message);
}

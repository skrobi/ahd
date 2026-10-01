using System.Reflection;
using PzlEv.Test.Models;

namespace PzlEv.Test;

/// <summary>
/// Sedno testu: potwierdza, że pakiety z założeń faktycznie wczytały się w runtime
/// z samodzielnego .exe (również uruchomionego z dysku sieciowego). Dotykamy po jednym
/// typie z każdego pakietu, żeby wymusić załadowanie assembly, i czytamy jego wersję.
/// </summary>
public static class PackageDiagnostics
{
    public static IReadOnlyList<PackageInfo> Collect()
    {
        var types = new (string Name, Type Type)[]
        {
            ("Dapper", typeof(Dapper.SqlMapper)),
            ("Microsoft.Data.SqlClient", typeof(Microsoft.Data.SqlClient.SqlConnection)),
            ("ClosedXML", typeof(ClosedXML.Excel.XLWorkbook)),
            ("Serilog", typeof(Serilog.Log)),
        };

        var list = new List<PackageInfo>();
        foreach (var (name, type) in types)
        {
            var asm = type.Assembly;
            list.Add(new PackageInfo(name, VersionOf(asm), LocationOf(asm)));
        }
        return list;
    }

    public static string Runtime =>
        $".NET {Environment.Version} · {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}";

    public static string Os =>
        System.Runtime.InteropServices.RuntimeInformation.OSDescription.Trim();

    public static string ExePath => Environment.ProcessPath ?? AppContext.BaseDirectory;

    private static string VersionOf(Assembly asm)
    {
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info))
        {
            // Obetnij metadane commita po '+' dla czytelności.
            var plus = info.IndexOf('+');
            return plus > 0 ? info[..plus] : info;
        }
        return asm.GetName().Version?.ToString() ?? "?";
    }

    private static string LocationOf(Assembly asm)
    {
        // W publikacji single-file Location jest pusty – assembly jest w bundlu.
        // Pusta wartość jest tu właśnie sygnałem, którego szukamy (stąd wyłączone IL3000).
#pragma warning disable IL3000
        try
        {
            return string.IsNullOrEmpty(asm.Location) ? "w bundlu single-file" : asm.Location;
        }
        catch
        {
            return "w bundlu single-file";
        }
#pragma warning restore IL3000
    }
}

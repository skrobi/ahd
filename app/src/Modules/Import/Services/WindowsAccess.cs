using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace PzlEv.Modules.Import.Services;

/// <summary>
/// Części testu dostępu działające tylko w Windows: folder WebDAV (UNC, usługa WebClient), połączenie sieciowe
/// (WNetAddConnection2 – jak „Mapuj dysk sieciowy”), provider OLEDB listy SharePoint (ADODB, jak Excel),
/// stan usługi WebClient i jej parametry, proxy WinHTTP, strefa zabezpieczeń adresu.
/// </summary>
internal static class WindowsAccess
{
    private static readonly string[] OleDbConnections =
    [
        "Provider=Microsoft.Office.List.OLEDB.2.0;Data Source=\"\";ApplicationName=Excel;Version=12.0.0.0",
        "Provider=Microsoft.Office.List.OLEDB.2.0;",
    ];

    /// <summary>Wykonuje akcję w osobnym wątku z limitem czasu (WebDAV potrafi długo nie odpowiadać).</summary>
    public static (T? Value, string? Error) Run<T>(Func<T> action, TimeSpan timeout, bool sta)
    {
        T? value = default;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                value = action();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        }) { IsBackground = true };
        if (sta && OperatingSystem.IsWindows())
            thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(timeout))
            return (default, $"Brak odpowiedzi w {timeout.TotalSeconds:0} s (usługa WebClient wyłączona, zablokowana albo czeka na logowanie)");
        return error is null ? (value, null) : (default, Describe(error));
    }

    public static string Describe(Exception ex)
    {
        while (ex is AggregateException { InnerException: { } inner })
            ex = inner;
        while (ex is System.Reflection.TargetInvocationException { InnerException: { } inner })
            ex = inner;
        return $"{ex.GetType().Name}: {ex.Message.Trim()} (0x{ex.HResult:X8})";
    }

    /// <summary>Pliki i podfoldery folderu (UNC).</summary>
    public static (List<string> Files, List<string> Folders) ListFolder(string path)
    {
        var dir = new DirectoryInfo(path);
        var files = dir.EnumerateFiles().Select(f => f.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        var folders = dir.EnumerateDirectories().Select(d => d.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        return (files, folders);
    }

    /// <summary>Połączenie sieciowe bieżącym kontem (jak „net use” bez litery dysku), lista plików, rozłączenie.</summary>
    public static (List<string> Files, List<string> Folders) MapAndList(string unc)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException();
        const int sessionCredentialConflict = 1219;   // połączenie z serwerem już istnieje – lista i tak możliwa
        var code = WNetAddConnection2(new NetResource { RemoteName = unc }, null, null, 0);
        if (code != 0 && code != sessionCredentialConflict)
            throw new IOException($"WNetAddConnection2 – błąd {code}: {new Win32Exception(code).Message}");
        try
        {
            return ListFolder(unc);
        }
        finally
        {
            if (code == 0)
                _ = WNetCancelConnection2(unc, 0, true);
        }
    }

    /// <summary>
    /// Lista plików przez provider Microsoft.Office.List.OLEDB.2.0 (ADODB przez COM) – jak połączenie w Excelu.
    /// Zwraca połączenie, typ polecenia, kolumny i nazwy; błędy prób dopisuje do <paramref name="attempts"/>.
    /// </summary>
    public static (string Connection, int CommandType, List<string> Fields, List<string> Names) ListOleDb(string listXml, List<string> attempts)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException();
        var connectionType = Type.GetTypeFromProgID("ADODB.Connection") ?? throw new InvalidOperationException("ADODB niezarejestrowane w tym systemie");
        var recordsetType = Type.GetTypeFromProgID("ADODB.Recordset") ?? throw new InvalidOperationException("ADODB.Recordset niezarejestrowane");
        foreach (var connectionString in OleDbConnections)
        {
            dynamic connection = Activator.CreateInstance(connectionType)!;
            try
            {
                connection.Open(connectionString);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                attempts.Add($"Otwarcie połączenia ({connectionString[..Math.Min(60, connectionString.Length)]}…): {ex.Message.Trim()}");
                continue;
            }
            try
            {
                // Excel zapisuje tekst polecenia <LIST>; próbowane typowe typy polecenia ADO.
                foreach (var commandType in new[] { -1, 1, 2, 512 })
                {
                    dynamic recordset = Activator.CreateInstance(recordsetType)!;
                    try
                    {
                        recordset.Open(listXml, connection, 0, 1, commandType);
                    }
                    catch (Exception ex) when (ex is not OutOfMemoryException)
                    {
                        attempts.Add($"Zapytanie (CommandType={commandType}): {ex.Message.Trim()}");
                        continue;
                    }
                    var fields = new List<string>();
                    int count = recordset.Fields.Count;
                    for (var i = 0; i < count; i++)
                        fields.Add((string)recordset.Fields.Item(i).Name);
                    var nameField = fields.FirstOrDefault(f => f.ToLowerInvariant() is "name" or "nazwa" or "filename" or "linkfilename" or "fileleafref");
                    var names = new List<string>();
                    while (!(bool)recordset.EOF)
                    {
                        names.Add(nameField is null ? "?" : Convert.ToString(recordset.Fields.Item(nameField).Value, CultureInfo.InvariantCulture) ?? "");
                        recordset.MoveNext();
                    }
                    recordset.Close();
                    return (connectionString, commandType, fields, names);
                }
            }
            finally
            {
                try
                {
                    connection.Close();
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    // zamknięcie nieudanego połączenia – bez znaczenia dla wyniku
                }
            }
        }
        throw new InvalidOperationException(attempts.LastOrDefault() ?? "provider OLEDB nie zwrócił danych");
    }

    /// <summary>Stan i typ uruchamiania usługi WebClient, jej parametry z rejestru, proxy WinHTTP, platforma Office.</summary>
    public static List<string> WebClientInfo()
    {
        if (!OperatingSystem.IsWindows())
            return [];
        var lines = new List<string>
        {
            "Usługa WebClient: " + string.Join("; ", Command("sc.exe", "query WebClient").Concat(Command("sc.exe", "qc WebClient"))
                .Where(l => l.StartsWith("STATE", StringComparison.OrdinalIgnoreCase) || l.StartsWith("START_TYPE", StringComparison.OrdinalIgnoreCase) || l.Contains("FAILED", StringComparison.OrdinalIgnoreCase))
                .Select(l => string.Join(' ', l.Split(' ', StringSplitOptions.RemoveEmptyEntries)))),
            "Proxy WinHTTP (używa go usługa WebClient): " + string.Join("; ", Command("netsh.exe", "winhttp show proxy").Skip(1)),
        };
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\WebClient\Parameters");
            if (key is null)
            {
                lines.Add("Parametry WebClient: brak klucza w rejestrze");
            }
            else
            {
                var forward = key.GetValue("AuthForwardServerList") as string[];
                lines.Add("AuthForwardServerList: " + (forward is { Length: > 0 }
                    ? string.Join(", ", forward.Where(s => s.Length > 0))
                    : "brak – WebClient przekazuje logowanie Windows tylko do strefy Intranet lokalny"));
                lines.Add($"BasicAuthLevel: {key.GetValue("BasicAuthLevel") ?? "brak"}, FileSizeLimitInBytes: {key.GetValue("FileSizeLimitInBytes") ?? "brak"}");
            }
            using var office = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Office\ClickToRun\Configuration");
            lines.Add($"Office (Click-to-Run): platforma {office?.GetValue("Platform") ?? "nieznana"}");
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            lines.Add($"Rejestr: {ex.Message}");
        }
        return lines;
    }

    /// <summary>Strefa zabezpieczeń adresu (Opcje internetowe) – od niej zależy, czy WebClient wyśle logowanie Windows.</summary>
    public static string Zone(string url)
    {
        if (!OperatingSystem.IsWindows())
            return "nieznana";
        try
        {
            var hr = CoInternetCreateSecurityManager(IntPtr.Zero, out var manager, 0);
            if (hr != 0)
                return $"nieznana (0x{hr:X8})";
            try
            {
                hr = manager.MapUrlToZone(url, out var zone, 0);
                if (hr != 0)
                    return $"nieznana (0x{hr:X8})";
                var name = zone switch
                {
                    0 => "Komputer lokalny",
                    1 => "Intranet lokalny",
                    2 => "Zaufane witryny",
                    3 => "Internet",
                    4 => "Witryny z ograniczeniami",
                    _ => "inna",
                };
                return $"{name} ({zone})";
            }
            finally
            {
                Marshal.ReleaseComObject(manager);
            }
        }
        catch (Exception ex) when (ex is COMException or DllNotFoundException or EntryPointNotFoundException or InvalidCastException)
        {
            return $"nieznana ({ex.Message})";
        }
    }

    private static List<string> Command(string file, string arguments)
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var start = new ProcessStartInfo(file, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                StandardOutputEncoding = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage),
            };
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync();
            if (!process.WaitForExit(10_000))
            {
                process.Kill();
                return [$"{file}: brak odpowiedzi"];
            }
            return output.GetAwaiter().GetResult().Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException or ArgumentException)
        {
            return [$"{file}: {ex.Message}"];
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private sealed class NetResource
    {
        public int Scope;
        public int Type = 1;   // RESOURCETYPE_DISK
        public int DisplayType;
        public int Usage;
        public string? LocalName;
        public string? RemoteName;
        public string? Comment;
        public string? Provider;
    }

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    private static extern int WNetAddConnection2(NetResource resource, string? password, string? user, int flags);

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    private static extern int WNetCancelConnection2(string name, int flags, [MarshalAs(UnmanagedType.Bool)] bool force);

    [DllImport("urlmon.dll")]
    private static extern int CoInternetCreateSecurityManager(IntPtr serviceProvider, [MarshalAs(UnmanagedType.Interface)] out IInternetSecurityManager manager, uint reserved);

    [ComImport]
    [Guid("79eac9ee-baf9-11ce-8c82-00aa004ba90b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IInternetSecurityManager
    {
        [PreserveSig]
        int SetSecuritySite(IntPtr site);

        [PreserveSig]
        int GetSecuritySite(out IntPtr site);

        [PreserveSig]
        int MapUrlToZone([MarshalAs(UnmanagedType.LPWStr)] string url, out uint zone, uint flags);
    }
}

using System.IO;
using System.IO.Compression;

namespace PzlEv.Shared.Utils.Files;

/// <summary>Kompresja treści pliku do zapisu w bazie (GZip – ten sam format co COMPRESS / DECOMPRESS w SQL Server).</summary>
public static class FileCompression
{
    public static byte[] GZip(byte[] content)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
            gzip.Write(content);
        return output.ToArray();
    }

    public static byte[] GUnzip(byte[] compressed)
    {
        using var input = new GZipStream(new MemoryStream(compressed), CompressionMode.Decompress);
        using var output = new MemoryStream();
        input.CopyTo(output);
        return output.ToArray();
    }
}

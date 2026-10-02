using System.Security.Cryptography;
using System.Text;

namespace PzlEv.Shared.Utils.Files;

/// <summary>
/// Sygnatura kolumn – odcisk układu nagłówków (docs/zrodla-danych.md, rozdz. 2). Ten sam algorytm co narzędzie
/// w Pythonie (header_signature): SHA-1 nazw kolumn (przycięte, małe litery) złączonych „|”, pierwsze 16 znaków.
/// </summary>
public static class HeaderSignature
{
    public static string Compute(IEnumerable<string> columns)
    {
        var normalized = string.Join("|", columns.Select(c => c.Trim().ToLowerInvariant()));
        var hash = SHA1.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexStringLower(hash)[..16];
    }
}

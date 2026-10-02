using System.Security.Cryptography;

namespace PzlEv.Shared.Utils.Files;

/// <summary>SHA-256 treści pliku – identyfikator wersji pliku (docs/model-danych.md, rozdz. 3.3).</summary>
public static class FileHash
{
    public static string Sha256(byte[] content) => Convert.ToHexStringLower(SHA256.HashData(content));
}

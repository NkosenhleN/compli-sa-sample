// Copyright (c) 2026 Nkosenhle. Licensed under AGPL-3.0.
// Source: CompliSA — Compliance & Tender Readiness Platform

using System.Security.Cryptography;
using System.Text;

namespace complisa.application.Common;

/// <summary>
/// Single-use secrets sent by email (e.g. company invitations). Only the hash is persisted, so the
/// raw token exists solely in the recipient's inbox.
/// </summary>
public static class SecureTokens
{
    /// <summary>256 random bits, base64url-encoded (URL-safe, no padding).</summary>
    public static string Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    /// <summary>Lower-case hex SHA-256 of the token — what gets stored and looked up.</summary>
    public static string Hash(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token.Trim())));
    }
}

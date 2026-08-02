using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Bakery.Application.Interfaces;

namespace Bakery.Infrastructure.Services;

public sealed class OwnerResetCodeVerifier : IOwnerResetCodeVerifier
{
    private const int IterationCount = 210_000;
    private static readonly TimeSpan AuthorizationLifetime = TimeSpan.FromMinutes(2);

    private static readonly byte[] Salt =
    [
        0xD9, 0x74, 0xA0, 0x23, 0x4F, 0x7A, 0xED, 0xC6,
        0x01, 0x83, 0x30, 0x85, 0x57, 0xBB, 0x78, 0x97
    ];

    private static readonly byte[] ExpectedHash =
    [
        0x45, 0x58, 0xF3, 0x3B, 0x9A, 0x10, 0x1F, 0x15,
        0x8F, 0x9B, 0xEC, 0xF1, 0xEB, 0x29, 0x5A, 0xF1,
        0x93, 0xB1, 0xF2, 0xEE, 0x23, 0xBB, 0x07, 0x6E,
        0x08, 0xA9, 0xEA, 0x7D, 0x16, 0x9F, 0xF9, 0xBE
    ];

    private readonly ConcurrentDictionary<Guid, DateTime> _authorizations = new();

    public bool Verify(string? enteredCode)
    {
        if (enteredCode is null) return false;

        var normalized = enteredCode.Trim();
        byte[]? inputBytes = null;
        byte[]? actualHash = null;
        try
        {
            inputBytes = Encoding.UTF8.GetBytes(normalized);
            actualHash = Rfc2898DeriveBytes.Pbkdf2(
                inputBytes,
                Salt,
                IterationCount,
                HashAlgorithmName.SHA256,
                ExpectedHash.Length);
            return CryptographicOperations.FixedTimeEquals(actualHash, ExpectedHash);
        }
        finally
        {
            if (inputBytes is not null) CryptographicOperations.ZeroMemory(inputBytes);
            if (actualHash is not null) CryptographicOperations.ZeroMemory(actualHash);
        }
    }

    public IOwnerResetAuthorization? Authorize(string? enteredCode)
    {
        if (!Verify(enteredCode)) return null;

        var authorization = new ResetAuthorization(Guid.NewGuid());
        _authorizations[authorization.Id] = DateTime.UtcNow.Add(AuthorizationLifetime);
        RemoveExpiredAuthorizations();
        return authorization;
    }

    public bool TryConsumeAuthorization(IOwnerResetAuthorization authorization)
    {
        if (authorization is not ResetAuthorization resetAuthorization ||
            !_authorizations.TryRemove(resetAuthorization.Id, out var expiresAtUtc))
        {
            return false;
        }

        return expiresAtUtc >= DateTime.UtcNow;
    }

    private void RemoveExpiredAuthorizations()
    {
        var now = DateTime.UtcNow;
        foreach (var entry in _authorizations)
        {
            if (entry.Value < now) _authorizations.TryRemove(entry.Key, out _);
        }
    }

    private sealed record ResetAuthorization(Guid Id) : IOwnerResetAuthorization;
}

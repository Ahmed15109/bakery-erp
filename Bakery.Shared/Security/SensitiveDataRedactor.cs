using System.Text.RegularExpressions;

namespace Bakery.Shared.Security;

/// <summary>
/// Last-line protection for diagnostic text. Application code should still avoid
/// passing credentials to logging APIs in the first place.
/// </summary>
public static partial class SensitiveDataRedactor
{
    public const string Replacement = "[REDACTED]";
    private static readonly HashSet<string> SensitiveNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "password",
        "pwd",
        "clientsecret",
        "accesstoken",
        "refreshtoken",
        "authorization",
        "secret",
        "token",
        "credential",
        "credentials",
        "connectionstring"
    };

    public static bool IsSensitiveName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        var normalized = name.Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal);
        return SensitiveNames.Contains(normalized);
    }

    public static string Redact(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value ?? string.Empty;

        var redacted = BearerTokenRegex().Replace(value, $"$1{Replacement}");
        return NamedSecretRegex().Replace(
            redacted,
            match => $"{match.Groups["name"].Value}{match.Groups["separator"].Value}{Replacement}");
    }

    [GeneratedRegex(
        @"(?i)(\bBearer\s+)[A-Za-z0-9._~+/=-]+",
        RegexOptions.CultureInvariant)]
    private static partial Regex BearerTokenRegex();

    [GeneratedRegex(
        "(?ix)(?<name>\\\"?(?:password|pwd|clientsecret|client_secret|access_token|refresh_token|authorization|secret|token)\\\"?)(?<separator>\\s*(?::|=)\\s*\\\"?)(?<value>[^\\\",;\\s}]+)",
        RegexOptions.CultureInvariant)]
    private static partial Regex NamedSecretRegex();
}

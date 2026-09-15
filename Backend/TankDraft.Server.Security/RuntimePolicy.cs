using System.Net;

namespace TankDraft.Server.Security;

/// <summary>
/// Prevents a local QA host from accidentally being exposed outside the machine.
/// This policy intentionally has no production or cloud override.
/// </summary>
public static class RuntimePolicy
{
    public const string LocalOnlyMode = "LocalOnly";

    public static void ValidateLocalOnly(string mode, IEnumerable<string> urls)
    {
        ArgumentNullException.ThrowIfNull(mode);
        ArgumentNullException.ThrowIfNull(urls);

        if (!string.Equals(mode, LocalOnlyMode, StringComparison.Ordinal))
        {
            throw new RuntimePolicyViolationException("Only LocalOnly runtime mode is permitted in this security slice.");
        }

        var endpointCount = 0;
        foreach (var url in urls)
        {
            endpointCount++;
            ValidateEndpoint(url);
        }

        if (endpointCount == 0)
        {
            throw new RuntimePolicyViolationException("At least one local endpoint is required.");
        }
    }

    private static void ValidateEndpoint(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || !Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment)
            || !string.Equals(uri.AbsolutePath, "/", StringComparison.Ordinal)
            || !HasExplicitPort(value)
            || !IPAddress.TryParse(uri.Host, out var address)
            || !IPAddress.IsLoopback(address))
        {
            throw new RuntimePolicyViolationException("LocalOnly endpoints must be HTTP loopback IP addresses with an explicit port.");
        }
    }

    private static bool HasExplicitPort(string value)
    {
        const string scheme = "http://";
        if (!value.StartsWith(scheme, StringComparison.Ordinal))
        {
            return false;
        }

        var authorityEnd = value.IndexOfAny(['/', '?', '#'], scheme.Length);
        var authority = authorityEnd >= 0 ? value[scheme.Length..authorityEnd] : value[scheme.Length..];
        if (authority.StartsWith("[", StringComparison.Ordinal))
        {
            var closingBracket = authority.IndexOf(']');
            return closingBracket >= 0
                && closingBracket + 1 < authority.Length
                && authority[closingBracket + 1] == ':'
                && IsPort(authority[(closingBracket + 2)..]);
        }

        var separator = authority.LastIndexOf(':');
        return separator > 0 && IsPort(authority[(separator + 1)..]);
    }

    private static bool IsPort(string value)
    {
        return int.TryParse(value, out var port) && port is > 0 and <= 65535;
    }
}

public sealed class RuntimePolicyViolationException : InvalidOperationException
{
    public RuntimePolicyViolationException(string message)
        : base(message)
    {
    }
}

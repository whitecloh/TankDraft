using System.Text.Json;
using System.Text.RegularExpressions;

namespace TankDraft.RemoteHost;

/// <summary>Validated operator deployment contract for Edgegap TLS Upgrade; it is not remote attestation.</summary>
internal sealed class EdgegapIngress
{
    static readonly Regex RequestIdPattern = new("^[0-9a-f]{12,64}$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    static readonly Regex MappingName = new("^[A-Za-z0-9_-]{1,64}$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    EdgegapIngress(string requestId, int externalPort) => (RequestId, ExternalPort) = (requestId, externalPort);
    internal string RequestId { get; }
    internal int ExternalPort { get; }

    internal static string DiagnosticShape()
    {
        var raw = Environment.GetEnvironmentVariable("ARBITRIUM_PORTS_MAPPING");
        if (raw is null || raw.Length > 4096) return "INGRESS mapping=absent-or-oversized";
        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            var hasPorts = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("ports", out _);
            var ports = hasPorts ? root.GetProperty("ports") : root;
            var count = ports.ValueKind == JsonValueKind.Object ? ports.EnumerateObject().Count() : -1;
            var entry = count > 0 ? ports.EnumerateObject().FirstOrDefault(e => e.Value.ValueKind == JsonValueKind.Object && e.Value.TryGetProperty("internal", out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) && i == 8080).Value : default;
            var protocol = entry.ValueKind == JsonValueKind.Object && entry.TryGetProperty("protocol", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
            var safeProtocol = protocol is "WS" or "WSS" or "TCP" or "HTTP" or "HTTPS" or "UDP" ? protocol : "other";
            var port8080 = entry.ValueKind == JsonValueKind.Object && entry.TryGetProperty("internal", out var n) && n.ValueKind == JsonValueKind.Number && n.TryGetInt32(out var port) && port == 8080;
            return $"INGRESS wrapped={hasPorts} count={count} protocol={safeProtocol} internal8080={port8080} flag={Environment.GetEnvironmentVariable("TANKDRAFT_EDGEGAP_TLS_UPGRADE") == "1"}";
        }
        catch { return "INGRESS mapping=invalid-json"; }
    }

    internal static EdgegapIngress FromEnvironment() => Validate(
        Environment.GetEnvironmentVariable("TANKDRAFT_EDGEGAP_TLS_UPGRADE"),
        Environment.GetEnvironmentVariable("ARBITRIUM_REQUEST_ID"),
        Environment.GetEnvironmentVariable("ARBITRIUM_PORTS_MAPPING"));

    internal static EdgegapIngress Validate(string? operatorFlag, string? requestId, string? portsMapping)
    {
        if (operatorFlag != "1" || string.IsNullOrEmpty(requestId) || !RequestIdPattern.IsMatch(requestId) || string.IsNullOrEmpty(portsMapping) || portsMapping.Length > 4096)
            throw new InvalidOperationException("Invalid managed Edgegap ingress contract.");
        try
        {
            using var document = JsonDocument.Parse(portsMapping, new JsonDocumentOptions { AllowTrailingCommas = false, AllowDuplicateProperties = false, CommentHandling = JsonCommentHandling.Disallow, MaxDepth = 8 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 1 || !root.TryGetProperty("ports", out var ports) || ports.ValueKind != JsonValueKind.Object || ports.EnumerateObject().Count() is < 1 or > 16)
                throw new InvalidOperationException("Invalid managed Edgegap ingress contract.");
            // TLS Upgrade may inject additional proxy ports. Bind only the unique authored
            // listener; those extra entries do not create listeners or authorize requests.
            var listeners = ports.EnumerateObject().Where(e => e.Value.ValueKind == JsonValueKind.Object && e.Value.TryGetProperty("internal", out var port) && port.ValueKind == JsonValueKind.Number && port.TryGetInt32(out var value) && value == 8080).ToArray();
            if (listeners.Length != 1) throw new InvalidOperationException("Invalid managed Edgegap ingress contract.");
            var mapping = listeners[0];
            if (!MappingName.IsMatch(mapping.Name) || mapping.Value.ValueKind != JsonValueKind.Object || !mapping.Value.TryGetProperty("internal", out var internalPort) || !internalPort.TryGetInt32(out var internalValue) || internalValue != 8080
                || !mapping.Value.TryGetProperty("external", out var externalPort) || !externalPort.TryGetInt32(out var externalValue) || externalValue is < 1 or > 65535
                || !mapping.Value.TryGetProperty("protocol", out var protocol) || protocol.ValueKind != JsonValueKind.String || protocol.GetString() != "WS")
                throw new InvalidOperationException("Invalid managed Edgegap ingress contract.");
            return new EdgegapIngress(requestId, externalValue);
        }
        catch (JsonException) { throw new InvalidOperationException("Invalid managed Edgegap ingress contract."); }
    }
}

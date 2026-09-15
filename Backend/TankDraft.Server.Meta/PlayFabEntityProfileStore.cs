using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace TankDraft.Server.Meta;

/// <summary>Entity Objects profile storage shared by supported inventory adapters.</summary>
public sealed class PlayFabEntityProfileStore
{
    const string ObjectName = "tankdraft_profile_v1";
    readonly PlayFabMetaTransport transport;
    static readonly JsonSerializerOptions ProfileJson = new() { AllowDuplicateProperties = false, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 16 };

    public PlayFabEntityProfileStore(PlayFabMetaTransport transport)
    {
        this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
    }

    // Call only with an authenticated, allowlisted classic PlayFab id, never with an id supplied in a client payload.
    public async Task<PlayerEntity> ResolveAsync(string verifiedAccountId, CancellationToken ct)
    {
        if (!Regex.IsMatch(verifiedAccountId ?? "", "^[A-Za-z0-9]{5,32}$")) throw new MetaFailureException("invalid_identity");
        var data = await transport.CallAsync("Server/GetUserAccountInfo", new { PlayFabId = verifiedAccountId }, ct);
        try
        {
            var info = data.GetProperty("UserInfo");
            if (info.GetProperty("PlayFabId").GetString() != verifiedAccountId) throw new MetaFailureException("invalid_identity");
            var entity = info.GetProperty("TitleInfo").GetProperty("TitlePlayerAccount");
            var id = entity.GetProperty("Id").GetString();
            if (entity.GetProperty("Type").GetString() != "title_player_account" || !Regex.IsMatch(id ?? "", "^[A-Za-z0-9]{5,64}$")) throw new MetaFailureException("invalid_identity");
            return new PlayerEntity(verifiedAccountId!, id!);
        }
        catch (Exception e) when (e is KeyNotFoundException or InvalidOperationException) { throw new MetaFailureException("provider_unavailable"); }
    }

    static object Entity(PlayerEntity actor)
    {
        if (!Regex.IsMatch(actor.EntityId ?? "", "^[A-Za-z0-9]{5,64}$")) throw new MetaFailureException("invalid_identity");
        return new { Id = actor.EntityId, Type = "title_player_account" };
    }

    public async Task<StoredProfile> ReadProfileAsync(PlayerEntity actor, CancellationToken ct)
    {
        var data = await transport.CallAsync("Object/GetObjects", new { Entity = Entity(actor), EscapeObject = false }, ct);
        try
        {
            int version = data.GetProperty("ProfileVersion").GetInt32();
            if (version < 0) throw new MetaFailureException("invalid_profile");
            var objects = data.GetProperty("Objects");
            if (!objects.TryGetProperty(ObjectName, out var stored)) return new StoredProfile(version, null);
            var profile = stored.GetProperty("DataObject").Deserialize<PlayerProfile>(ProfileJson) ?? throw new MetaFailureException("invalid_profile");
            return new StoredProfile(version, profile);
        }
        catch (Exception e) when (e is KeyNotFoundException or InvalidOperationException or JsonException or FormatException) { throw new MetaFailureException("invalid_profile"); }
    }

    public async Task<StoredProfile> WriteProfileAsync(PlayerEntity actor, PlayerProfile profile, int expectedVersion, CancellationToken ct)
    {
        if (expectedVersion < 0) throw new MetaFailureException("version_conflict");
        var data = await transport.CallAsync("Object/SetObjects", new { Entity = Entity(actor), ExpectedProfileVersion = expectedVersion, Objects = new[] { new { ObjectName, DataObject = profile } } }, ct);
        try
        {
            var results = data.GetProperty("SetResults");
            if (!results.EnumerateArray().Any(r => r.GetProperty("ObjectName").GetString() == ObjectName && r.GetProperty("SetResult").GetString() is "Created" or "Updated" or "None"))
                throw new MetaFailureException("provider_unavailable");
            var version = data.GetProperty("ProfileVersion").GetInt32();
            if (version < expectedVersion) throw new MetaFailureException("provider_unavailable");
            return new StoredProfile(version, profile);
        }
        catch (Exception e) when (e is KeyNotFoundException or InvalidOperationException or FormatException) { throw new MetaFailureException("provider_unavailable"); }
    }
}

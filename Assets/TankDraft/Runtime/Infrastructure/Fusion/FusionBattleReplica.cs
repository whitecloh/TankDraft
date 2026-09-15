using System;
using System.IO;
using System.Text;
using Fusion;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TankDraft.Contracts.Battle;
using UnityEngine;

namespace TankDraft.Infrastructure.FusionTransport
{
    public struct FusionBattleEntity : INetworkStruct
    {
        public int Id, Side, Kind, Hp, MaxHp, TargetId, ShieldHp, ShieldMaxHp, FirstHitBlocks, Ammo, MagazineSize;
        public int Definition;
        public Vector2 Position, Previous, Facing;
        public float Radius, Progress, ReloadRemaining, ReloadDuration;
        public int TransformationStage;
    }
    public struct FusionBattleEvent : INetworkStruct
    {
        public long Sequence, Tick;
        public int Round, Kind, EntityId, Side, Amount;
        public Vector2 Position;
        public float Radius, TimeWithinTick;
    }
    public struct FusionBattleHeader : INetworkStruct
    {
        public int Epoch, Serial, EntityCount, MetadataBytes, Round, Phase, StateVersion;
        public long FirstEvent, LastEvent;
        public float SourcePollMs, SourceGapMs;
    }

    // One private projection per authenticated player; domain authority remains in the .NET server.
    public sealed class FusionBattleReplica : NetworkBehaviour
    {
        public const int MaxEntities = 256, MaxEvents = 128, MaxMetadata = 8192;
        [Networked] public FusionBattleHeader Header { get; set; }
        [Networked, Capacity(MaxEntities)] public NetworkArray<FusionBattleEntity> Entities => default;
        [Networked, Capacity(MaxEvents)] public NetworkArray<FusionBattleEvent> Events => default;
        [Networked, Capacity(MaxMetadata / 4)] public NetworkArray<uint> Metadata => default;
        FusionSnapshotInbox inbox;
        JObject pending, rendered;
        int epoch, lastSerial;
        float sourcePollMs, sourceGapMs;
        PlayerRef recipient;
        readonly System.Collections.Generic.List<string> definitions = new System.Collections.Generic.List<string>();
        string[] renderedDefinitions;
        public bool HasPending => pending != null;
        public bool Faulted { get; private set; }
        readonly System.Collections.Generic.Dictionary<int, FusionBattleEntity> future = new System.Collections.Generic.Dictionary<int, FusionBattleEntity>();
        public void Configure(PlayerRef target, int streamEpoch)
        {
            epoch = streamEpoch; recipient = target;
        }
        public override void Spawned()
        {
            if (Runner.IsServer) { ReplicateToAll(false); ReplicateTo(recipient, true); }
            else inbox = Runner.GetComponent<FusionRunnerContext>().Owner.SnapshotInbox;
        }
        public void Publish(JObject envelope, float pollMs, float gapMs) { pending = envelope; sourcePollMs = pollMs; sourceGapMs = gapMs; }
        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority || pending == null) return;
            try { ApplyPending(); }
            catch (Exception error) { pending = null; Faulted = true; Debug.LogError("FUSION_NATIVE_STATE_FAILED " + error.GetType().Name); }
        }
        void ApplyPending()
        {
            var value = pending; pending = null;
            var header = Header;
            header.Epoch = epoch; header.Serial++;
            header.SourcePollMs = sourcePollMs; header.SourceGapMs = sourceGapMs;
            var metadata = (JObject)value.DeepClone();
            if (metadata["Snapshot"] is JObject snapshot)
            {
                if (snapshot.Value<int>("BattleStateVersion") != BattleEntityState.WireVersion)
                    throw new InvalidDataException("Native battle state version mismatch.");
                header.StateVersion = BattleEntityState.WireVersion;
                header.Round = snapshot.Value<int>("Round"); header.Phase = snapshot.Value<string>("Phase") == "Battle" ? 1 : 0;
                var entities = (JArray)snapshot["Entities"];
                if (entities.Count > MaxEntities) throw new InvalidDataException("Native entity capacity exceeded.");
                header.EntityCount = entities.Count;
                for (int i = 0; i < entities.Count; i++) Entities.Set(i, ReadEntity(entities[i]));
                foreach (var wrapper in (JArray)snapshot["Events"])
                {
                    var sequence = wrapper.Value<long>("Sequence");
                    if (sequence <= header.LastEvent) continue;
                    var e = wrapper["Value"];
                    Events.Set((int)(sequence % MaxEvents), new FusionBattleEvent { Sequence = sequence, Round = wrapper.Value<int>("Round"), Tick = e.Value<long>("Tick"), Kind = e.Value<int>("Kind"), EntityId = e.Value<int>("EntityId"), Side = e.Value<int>("Side"), Amount = e.Value<int>("Amount"), Position = Vec(e["Position"]), Radius = e.Value<float>("Radius"), TimeWithinTick = e.Value<float>("TimeWithinTick") });
                    if (header.FirstEvent == 0) header.FirstEvent = sequence;
                    header.LastEvent = sequence; header.FirstEvent = Math.Max(header.FirstEvent, sequence - MaxEvents + 1);
                }
                snapshot.Remove("Entities"); snapshot.Remove("Events");
            }
            else { header.StateVersion = BattleEntityState.WireVersion; header.EntityCount = 0; }
            metadata["Definitions"] = new JArray(definitions);
            var bytes = Encoding.UTF8.GetBytes(metadata.ToString(Formatting.None));
            if (bytes.Length > MaxMetadata) throw new InvalidDataException("Native metadata capacity exceeded.");
            for (int i = 0; i < (bytes.Length + 3) / 4; i++)
            { uint word = 0; for (int j = 0; j < 4 && i * 4 + j < bytes.Length; j++) word |= (uint)bytes[i * 4 + j] << (8 * j); Metadata.Set(i, word); }
            // Clear trailing bytes to avoid retaining previous private metadata in a shorter snapshot.
            for (int i = (bytes.Length + 3) / 4; i < (header.MetadataBytes + 3) / 4; i++) Metadata.Set(i, 0);
            header.MetadataBytes = bytes.Length; Header = header;
        }
        public override void Render()
        {
            if (Runner.IsServer || inbox == null || !TryGetSnapshotsBuffers(out var from, out var to, out var alpha)) return;
            var reader = GetPropertyReader<FusionBattleHeader>(nameof(Header));
            var a = reader.Read(from); var b = reader.Read(to);
            if (a.Serial != 0 && Object.InputAuthority != Runner.LocalPlayer) throw new InvalidDataException("Private native state reached a different player.");
            if ((a.Serial != 0 && a.StateVersion != BattleEntityState.WireVersion) ||
                (b.Serial != 0 && b.StateVersion != BattleEntityState.WireVersion))
                throw new InvalidDataException("Native battle state version mismatch.");
            if (!inbox.AcceptRenderState(a.Epoch, a.Serial)) return;
            var entities = GetArrayReader<FusionBattleEntity>(nameof(Entities)).Read(from);
            var next = GetArrayReader<FusionBattleEntity>(nameof(Entities)).Read(to);
            if (lastSerial != a.Serial)
            {
                var decodeStarted = System.Diagnostics.Stopwatch.GetTimestamp();
                var metadata = GetArrayReader<uint>(nameof(Metadata)).Read(from);
                var bytes = new byte[a.MetadataBytes]; for (int i = 0; i < bytes.Length; i++) bytes[i] = (byte)(metadata[i / 4] >> (8 * (i % 4)));
                rendered = FusionQaProtocol.Read(bytes, MaxMetadata);
                renderedDefinitions = rendered["Definitions"].ToObject<string[]>(); rendered.Remove("Definitions");
                if (rendered["Snapshot"] is JObject s)
                {
                    var list = new JArray(); for (int i = 0; i < a.EntityCount; i++) list.Add(WriteEntity(entities[i], renderedDefinitions[entities[i].Definition])); s["Entities"] = list;
                    var events = GetArrayReader<FusionBattleEvent>(nameof(Events)).Read(from); var output = new JArray();
                    for (long seq = a.FirstEvent; seq > 0 && seq <= a.LastEvent; seq++)
                    {
                        var e = events[(int)(seq % MaxEvents)];
                        if (e.Sequence != seq) throw new InvalidDataException("Native event ring gap.");
                        output.Add(new JObject { ["Sequence"] = seq, ["Round"] = e.Round, ["Value"] = new JObject { ["Tick"] = e.Tick, ["Kind"] = e.Kind, ["EntityId"] = e.EntityId, ["Side"] = e.Side, ["Amount"] = e.Amount, ["Position"] = Json(e.Position), ["Radius"] = e.Radius, ["TimeWithinTick"] = e.TimeWithinTick } });
                    }
                    s["Events"] = output;
                }
                inbox.Publish(a.Epoch, rendered); lastSerial = a.Serial;
                inbox.MaxSourcePollMs = Math.Max(inbox.MaxSourcePollMs, a.SourcePollMs);
                inbox.MaxSourceGapMs = Math.Max(inbox.MaxSourceGapMs, a.SourceGapMs);
                inbox.MaxDecodeMs = Math.Max(inbox.MaxDecodeMs, (System.Diagnostics.Stopwatch.GetTimestamp() - decodeStarted) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
            }
            inbox.Poses.Clear();
            if (!(rendered?["Snapshot"] is JObject frame)) return;
            inbox.RenderMatch = frame.Value<string>("MatchId"); inbox.RenderRound = frame.Value<int>("Round");
            bool blend = a.Epoch == b.Epoch && a.Serial != b.Serial && a.Round == b.Round && a.Phase == 1 && b.Phase == 1;
            future.Clear(); if (blend) for (int i = 0; i < b.EntityCount; i++) future[next[i].Id] = next[i];
            for (int i = 0; i < a.EntityCount; i++)
            {
                var e = entities[i]; var pos = e.Position; var facing = e.Facing;
                if (future.TryGetValue(e.Id, out var n) && n.Definition.Equals(e.Definition)) { pos = Vector2.Lerp(pos, n.Position, alpha); facing = Vector2.Lerp(facing, n.Facing, alpha); }
                inbox.Poses[e.Id] = new FusionPose(pos.x, pos.y, facing.x, facing.y);
            }
            inbox.Interpolating = blend;
        }
        static Vector2 Vec(JToken v) => new Vector2(v.Value<float>("X"), v.Value<float>("Y"));
        static JObject Json(Vector2 v) => new JObject { ["X"] = v.x, ["Y"] = v.y };
        FusionBattleEntity ReadEntity(JToken e)
        {
            if (!(e is JObject entity)) throw new InvalidDataException("Native battle entity is malformed.");
            var definition = entity.Value<string>("DefinitionId"); if (definition == null || definition.Length > 64) throw new InvalidDataException();
            int index = definitions.IndexOf(definition);
            if (index < 0) { if (definitions.Count >= 64) throw new InvalidDataException("Native definition capacity exceeded."); index = definitions.Count; definitions.Add(definition); }
            return new FusionBattleEntity { Id = entity.Value<int>("Id"), Side = entity.Value<int>("Side"), Kind = entity.Value<int>("Kind"), Hp = entity.Value<int>("Hp"), MaxHp = entity.Value<int>("MaxHp"), TargetId = entity.Value<int>("TargetId"), ShieldHp = RequiredInt(entity, "ShieldHp"), ShieldMaxHp = RequiredInt(entity, "ShieldMaxHp"), FirstHitBlocks = RequiredInt(entity, "FirstHitBlocks"), Ammo = RequiredInt(entity, "Ammo"), MagazineSize = RequiredInt(entity, "MagazineSize"), ReloadRemaining = RequiredFloat(entity, "ReloadRemaining"), ReloadDuration = RequiredFloat(entity, "ReloadDuration"), TransformationStage = RequiredInt(entity, "TransformationStage"), Definition = index, Position = Vec(entity["Position"]), Previous = Vec(entity["PreviousPosition"]), Facing = Vec(entity["Facing"]), Radius = entity.Value<float>("Radius"), Progress = entity.Value<float>("Progress") };
        }
        static int RequiredInt(JObject value, string name)
        {
            var token = value[name];
            if (token == null || token.Type != JTokenType.Integer) throw new InvalidDataException("Missing native integer: " + name);
            return token.Value<int>();
        }
        static float RequiredFloat(JObject value, string name)
        {
            var token = value[name];
            if (token == null || (token.Type != JTokenType.Integer && token.Type != JTokenType.Float)) throw new InvalidDataException("Missing native float: " + name);
            var result = token.Value<float>();
            if (float.IsNaN(result) || float.IsInfinity(result)) throw new InvalidDataException("Invalid native float: " + name);
            return result;
        }
        static JObject WriteEntity(FusionBattleEntity e, string definition) => new JObject { ["Id"] = e.Id, ["Side"] = e.Side, ["Kind"] = e.Kind, ["Hp"] = e.Hp, ["MaxHp"] = e.MaxHp, ["TargetId"] = e.TargetId, ["ShieldHp"] = e.ShieldHp, ["ShieldMaxHp"] = e.ShieldMaxHp, ["FirstHitBlocks"] = e.FirstHitBlocks, ["Ammo"] = e.Ammo, ["MagazineSize"] = e.MagazineSize, ["ReloadRemaining"] = e.ReloadRemaining, ["ReloadDuration"] = e.ReloadDuration, ["TransformationStage"] = e.TransformationStage, ["DefinitionId"] = definition, ["Position"] = Json(e.Position), ["PreviousPosition"] = Json(e.Previous), ["Facing"] = Json(e.Facing), ["Radius"] = e.Radius, ["Progress"] = e.Progress };
    }
}

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TankDraft.Contracts.Battle;
using TankDraft.Match.Domain;

namespace TankDraft.Match.ServerClient
{
    // Detached client projection. No simulation or authority is constructed on the client.
    public sealed class ServerFrame
    {
        public readonly string MatchId, ContentVersion, Phase, Fault;
        public readonly long Revision, ChoiceToken, SimulationTick, EventSequence;
        public readonly int Round, ChoiceNumber, BonusSide, Wins0, Wins1, LastWinner, OrderCharges;
        public readonly bool IsComeback, Committed, CanUseOrder, CatchingUp, ResyncRequired;
        public readonly DateTimeOffset ServerNow;
        public readonly double RemainingSeconds;
        public readonly ReadOnlyCollection<BattleEntityState> Entities;
        public readonly ReadOnlyCollection<BattleEvent> Events;
        public readonly ReadOnlyCollection<ArmyEntry> Army;
        public readonly ReadOnlyCollection<MatchOffer> Offers;
        public readonly int ResultCount;
        public ServerFrame(JObject value)
        {
            if (RequiredInt(value, "BattleStateVersion") != BattleEntityState.WireVersion)
                throw new InvalidDataException("Unsupported battle state version.");
            MatchId = value.Value<string>("MatchId"); ContentVersion = value.Value<string>("ContentVersion");
            Phase = value.Value<string>("Phase"); Fault = value.Value<string>("Fault");
            if (string.IsNullOrEmpty(MatchId) || string.IsNullOrEmpty(ContentVersion) || !Enum.TryParse<MatchPhase>(Phase, out _)) throw new InvalidDataException("Invalid snapshot identity/phase.");
            Revision = value.Value<long>("Revision"); ChoiceToken = value.Value<long>("ChoiceToken");
            SimulationTick = value.Value<long>("SimulationTick"); EventSequence = value.Value<long>("EventSequence");
            Round = value.Value<int>("Round"); ChoiceNumber = value.Value<int>("ChoiceNumber"); BonusSide = value.Value<int>("BonusSide");
            Wins0 = value.Value<int>("Wins0"); Wins1 = value.Value<int>("Wins1"); LastWinner = value.Value<int>("LastWinner"); OrderCharges = value.Value<int>("OrderCharges");
            IsComeback = value.Value<bool>("IsComeback"); Committed = value.Value<bool>("Committed"); CanUseOrder = value.Value<bool>("CanUseOrder");
            CatchingUp = value.Value<bool>("CatchingUp"); ResyncRequired = value.Value<bool>("ResyncRequired");
            ServerNow = DateTimeOffset.Parse(value.Value<string>("ServerNow"), CultureInfo.InvariantCulture);
            var deadline = value.Value<string>("DeadlineAt");
            RemainingSeconds = deadline == null ? 0 : Math.Max(0, (DateTimeOffset.Parse(deadline, CultureInfo.InvariantCulture) - ServerNow).TotalSeconds);
            var entities = new List<BattleEntityState>();
            foreach (var token in Array(value, "Entities", 2048))
            {
                if (!(token is JObject s)) throw new InvalidDataException("Invalid battle entity.");
                entities.Add(new BattleEntityState(RequiredInt(s, "Id"), RequiredInt(s, "Side"), (BattleEntityKind)RequiredInt(s, "Kind"), RequiredString(s, "DefinitionId"),
                    Vec(s["Position"]), Vec(s["PreviousPosition"]), Vec(s["Facing"]), RequiredInt(s, "Hp"), RequiredInt(s, "MaxHp"), RequiredFloat(s, "Radius"), RequiredFloat(s, "Progress"), RequiredInt(s, "TargetId"),
                    RequiredInt(s, "ShieldHp"), RequiredInt(s, "ShieldMaxHp"), RequiredInt(s, "FirstHitBlocks"), RequiredInt(s, "Ammo"), RequiredInt(s, "MagazineSize"), RequiredFloat(s, "ReloadRemaining"), RequiredFloat(s, "ReloadDuration"), RequiredInt(s, "TransformationStage")));
            }
            Entities = entities.AsReadOnly();
            var events = new List<BattleEvent>();
            foreach (var wrapper in Array(value, "Events", 8192))
            {
                if (wrapper.Value<int>("Round") != Round) continue;
                var e = wrapper["Value"];
                // Use global transport sequence, not the simulation's round-local sequence.
                events.Add(new BattleEvent(wrapper.Value<long>("Sequence"), e.Value<long>("Tick"), (BattleEventKind)e.Value<int>("Kind"), e.Value<int>("EntityId"), e.Value<int>("Side"),
                    Vec(e["Position"]), e.Value<int>("Amount"), e.Value<float>("Radius"), e.Value<float>("TimeWithinTick")));
            }
            Events = events.AsReadOnly();
            var army = new List<ArmyEntry>();
            foreach (var a in Array(value, "Army", 4)) army.Add(new ArmyEntry(a.Value<string>("UnitId"), a.Value<int>("Count"), a.Value<int>("UpgradeLevel")));
            Army = army.AsReadOnly();
            var offers = new List<MatchOffer>();
            foreach (var o in Array(value, "Offers", 3)) offers.Add(new MatchOffer(o.Value<string>("Id"), o.Value<string>("UnitId"), (DraftActionKind)o.Value<int>("Kind"), o.Value<int>("Amount")));
            Offers = offers.AsReadOnly(); ResultCount = Array(value, "Results", 16).Count;
        }
        static JArray Array(JObject o, string name, int max)
        {
            var array = o[name] as JArray;
            if (array == null || array.Count > max) throw new InvalidDataException("Invalid snapshot collection.");
            return array;
        }
        static BattleVec Vec(JToken token)
        {
            if (!(token is JObject vector)) throw new InvalidDataException("Invalid battle vector.");
            return new BattleVec(RequiredFloat(vector, "X"), RequiredFloat(vector, "Y"));
        }
        static string RequiredString(JObject value, string name)
        {
            var token = value[name];
            if (token == null || token.Type != JTokenType.String || string.IsNullOrWhiteSpace(token.Value<string>()))
                throw new InvalidDataException("Missing required string: " + name);
            return token.Value<string>();
        }
        static int RequiredInt(JObject value, string name)
        {
            var token = value[name];
            if (token == null || token.Type != JTokenType.Integer)
                throw new InvalidDataException("Missing required integer: " + name);
            try { return token.Value<int>(); }
            catch (Exception error) { throw new InvalidDataException("Invalid integer: " + name, error); }
        }
        static float RequiredFloat(JObject value, string name)
        {
            var token = value[name];
            if (token == null || (token.Type != JTokenType.Integer && token.Type != JTokenType.Float))
                throw new InvalidDataException("Missing required float: " + name);
            float result;
            try { result = token.Value<float>(); }
            catch (Exception error) { throw new InvalidDataException("Invalid float: " + name, error); }
            if (float.IsNaN(result) || float.IsInfinity(result)) throw new InvalidDataException("Invalid float: " + name);
            return result;
        }
    }

    public static class ServerWire
    {
        public static JObject Parse(string json)
        {
            using (var reader = new JsonTextReader(new StringReader(json)) { MaxDepth = 32, DateParseHandling = DateParseHandling.None })
            {
                var value = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                if (reader.Read()) throw new InvalidDataException("Trailing JSON.");
                return value;
            }
        }
        public static bool ConsumesSequence(string code) => code != "CatchingUp" && code != "Unauthorized" && code != "MalformedCommand" && code != "MatchMismatch" &&
            code != "CommandNotAllowed" && code != "UnexpectedSequence" && code != "ReceiptCapacityReached" && code != "OperationConflict";
    }

    // No bearer tokens in this journal. Persist the exact intent before sending it; ACK loss can be retried after process restart.
    public sealed class ClientIntentJournal
    {
        readonly string _path;
        public long Sequence { get; private set; }
        public string StreamId { get; private set; }
        public JObject Pending { get; private set; }
        public ClientIntentJournal(string path)
        {
            _path = path;
            if (!File.Exists(path)) return;
            if (new FileInfo(path).Length > 16384) throw new InvalidDataException("Intent journal too large.");
            var state = ServerWire.Parse(File.ReadAllText(path));
            Sequence = state.Value<long>("Sequence"); Pending = state["Pending"] as JObject; StreamId = state.Value<string>("StreamId");
            if (Sequence < 0 || Pending != null && Pending.Value<long>("Sequence") != Sequence + 1) throw new InvalidDataException("Invalid intent sequence.");
        }
        public void BindStream(string streamId, long nextSequence)
        {
            if (string.IsNullOrEmpty(streamId) || streamId.Length > 128 || nextSequence < 1) throw new InvalidDataException("Invalid command stream.");
            if (StreamId != null && StreamId != streamId) throw new InvalidDataException("Command stream changed; pending intent retained.");
            if (StreamId == null && (Sequence != 0 || Pending != null)) throw new InvalidDataException("Existing intent lacks a stream binding.");
            if (Pending == null && nextSequence < Sequence + 1 || Pending != null && nextSequence != Sequence + 1 && nextSequence != Sequence + 2)
                throw new InvalidDataException("Server/client command sequence diverged.");
            var authoritativeSequence = Pending == null ? nextSequence - 1 : Sequence;
            var beforeStream = StreamId;
            var beforeSequence = Sequence;
            try
            {
                StreamId = streamId;
                Write(authoritativeSequence, Pending);
                Sequence = authoritativeSequence;
            }
            catch
            {
                StreamId = beforeStream;
                Sequence = beforeSequence;
                throw;
            }
        }
        public void Begin(JObject command)
        {
            if (Pending != null || command.Value<long>("Sequence") != Sequence + 1) throw new InvalidOperationException("An intent is already pending.");
            Write(Sequence, command); Pending = (JObject)command.DeepClone();
        }
        public void Acknowledge(string operationId, string code)
        {
            if (Pending == null || Pending.Value<string>("OperationId") != operationId) throw new InvalidDataException("Unexpected acknowledgement.");
            if (code == "CatchingUp") return;
            if (!ServerWire.ConsumesSequence(code)) throw new InvalidDataException("Server rejected the command protocol: " + code);
            var next = Pending.Value<long>("Sequence"); Write(next, null); Sequence = next; Pending = null;
        }
        void Write(long sequence, JObject pending)
        {
            var json = new JObject { ["StreamId"] = StreamId, ["Sequence"] = sequence, ["Pending"] = pending == null ? JValue.CreateNull() : pending.DeepClone() }.ToString(Formatting.None);
            Directory.CreateDirectory(Path.GetDirectoryName(_path));
            var temp = _path + ".tmp";
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(json); stream.Write(bytes, 0, bytes.Length); stream.Flush(true);
            }
            if (File.Exists(_path)) File.Replace(temp, _path, null); else File.Move(temp, _path);
        }
    }
}

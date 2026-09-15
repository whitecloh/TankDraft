using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TankDraft.Contracts.Battle;
using TankDraft.Match.Domain;
using TankDraft.Simulation;

namespace TankDraft.Match.Networking
{
    public enum NetCommandKind
    {
        Choose,
        Order,
        Continue
    }

    public sealed class NetCommand
    {
        public string MatchId;
        public string ContentVersion;
        public int Epoch = 1;
        public int Round;
        public int OfferIndex;
        public long Token;
        public long Sequence;
        public NetCommandKind Kind;
    }

    public sealed class NetAck
    {
        public long Sequence;
        public bool Accepted;
        public bool Duplicate;
        public string Reason;
    }

    public sealed class NetFrame
    {
        public string MatchId;
        public string ContentVersion;
        public int Epoch = 1;
        public int Round, Side, ChoiceNumber, BonusSide, WinnerSide, Wins0, Wins1, Count0, Count1, OrderCharges;
        public long Revision, Tick, ChoiceToken, EventWatermark;
        public double PresentAt;
        public MatchPhase Phase;
        public bool IsComeback, Committed0, Committed1, NextReady0, NextReady1, CanUseOrder, Recovery;
        public bool RandomTieBreak;
        public uint TieBreakSeed;
        public MatchOffer[] Offers = Array.Empty<MatchOffer>();
        public ArmyEntry[] Army = Array.Empty<ArmyEntry>();
        public BattleEntityState[] States = Array.Empty<BattleEntityState>();
        public BattleEvent[] Events = Array.Empty<BattleEvent>();
        public string StateHash;
    }

    public static class NetCodec
    {
        const int MaxPayload = 256 * 1024, MaxOffers = 3, MaxArmy = 4, MaxStates = 1024, MaxEvents = 4096, MaxId = 256, MaxReason = 4096;
        const ulong FnvOffset = 14695981039346656037UL, FnvPrime = 1099511628211UL;
        static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);
        public static byte[] EncodeCommand(NetCommand value)
        {
            if (value == null)
                throw new ArgumentNullException(nameof(value));
            ValidateCommand(value);
            return Write(w =>
            {
                WriteId(w, value.MatchId);
                WriteId(w, value.ContentVersion);
                w.Write(value.Epoch);
                w.Write(value.Round);
                w.Write(value.OfferIndex);
                w.Write(value.Token);
                w.Write(value.Sequence);
                w.Write((int)value.Kind);
            });
        }

        public static NetCommand DecodeCommand(byte[] bytes)
        {
            return Read(bytes, r =>
            {
                var value = new NetCommand
                {
                    MatchId = ReadId(r),
                    ContentVersion = ReadId(r),
                    Epoch = r.ReadInt32(),
                    Round = r.ReadInt32(),
                    OfferIndex = r.ReadInt32(),
                    Token = r.ReadInt64(),
                    Sequence = r.ReadInt64(),
                    Kind = (NetCommandKind)r.ReadInt32()
                };
                ValidateCommand(value);
                return value;
            });
        }

        public static byte[] EncodeAck(NetAck value)
        {
            if (value == null)
                throw new ArgumentNullException(nameof(value));
            if (value.Sequence < 0)
                throw new ArgumentOutOfRangeException(nameof(value));
            return Write(w =>
            {
                w.Write(value.Sequence);
                w.Write(value.Accepted);
                w.Write(value.Duplicate);
                WriteString(w, value.Reason ?? string.Empty, MaxReason);
            });
        }

        public static NetAck DecodeAck(byte[] bytes)
        {
            return Read(bytes, r =>
            {
                var value = new NetAck
                {
                    Sequence = r.ReadInt64(),
                    Accepted = r.ReadBoolean(),
                    Duplicate = r.ReadBoolean(),
                    Reason = ReadString(r, MaxReason)
                };
                if (value.Sequence < 0)
                    throw new InvalidDataException("ack-sequence");
                return value;
            });
        }

        public static byte[] EncodeFrame(NetFrame value)
        {
            ValidateFrame(value, false);
            value.StateHash = HashShared(value);
            return Write(w =>
            {
                w.Write(BattleEntityState.WireVersion);
                WriteId(w, value.MatchId);
                WriteId(w, value.ContentVersion);
                w.Write(value.Epoch);
                w.Write(value.Round);
                w.Write(value.Side);
                w.Write(value.ChoiceNumber);
                w.Write(value.BonusSide);
                w.Write(value.WinnerSide);
                w.Write(value.Wins0);
                w.Write(value.Wins1);
                w.Write(value.Count0);
                w.Write(value.Count1);
                w.Write(value.OrderCharges);
                w.Write(value.Revision);
                w.Write(value.Tick);
                w.Write(value.ChoiceToken);
                w.Write(value.EventWatermark);
                w.Write(value.PresentAt);
                w.Write((int)value.Phase);
                w.Write(value.IsComeback);
                w.Write(value.Committed0);
                w.Write(value.Committed1);
                w.Write(value.NextReady0);
                w.Write(value.NextReady1);
                w.Write(value.CanUseOrder);
                w.Write(value.Recovery);
                w.Write(value.RandomTieBreak);
                w.Write(value.TieBreakSeed);
                WriteOffers(w, value.Offers);
                WriteArmy(w, value.Army);
                WriteStates(w, value.States);
                WriteEvents(w, value.Events);
                WriteId(w, value.StateHash);
            });
        }

        public static NetFrame DecodeFrame(byte[] bytes)
        {
            var value = Read(bytes, r =>
            {
                if (r.ReadInt32() != BattleEntityState.WireVersion)
                    throw new InvalidDataException("state-version");
                var v = new NetFrame
                {
                    MatchId = ReadId(r),
                    ContentVersion = ReadId(r),
                    Epoch = r.ReadInt32(),
                    Round = r.ReadInt32(),
                    Side = r.ReadInt32(),
                    ChoiceNumber = r.ReadInt32(),
                    BonusSide = r.ReadInt32(),
                    WinnerSide = r.ReadInt32(),
                    Wins0 = r.ReadInt32(),
                    Wins1 = r.ReadInt32(),
                    Count0 = r.ReadInt32(),
                    Count1 = r.ReadInt32(),
                    OrderCharges = r.ReadInt32(),
                    Revision = r.ReadInt64(),
                    Tick = r.ReadInt64(),
                    ChoiceToken = r.ReadInt64(),
                    EventWatermark = r.ReadInt64(),
                    PresentAt = r.ReadDouble(),
                    Phase = (MatchPhase)r.ReadInt32(),
                    IsComeback = r.ReadBoolean(),
                    Committed0 = r.ReadBoolean(),
                    Committed1 = r.ReadBoolean(),
                    NextReady0 = r.ReadBoolean(),
                    NextReady1 = r.ReadBoolean(),
                    CanUseOrder = r.ReadBoolean(),
                    Recovery = r.ReadBoolean(),
                    RandomTieBreak = r.ReadBoolean(),
                    TieBreakSeed = r.ReadUInt32()
                };
                v.Offers = ReadOffers(r);
                v.Army = ReadArmy(r);
                v.States = ReadStates(r);
                v.Events = ReadEvents(r);
                v.StateHash = ReadId(r);
                return v;
            });
            ValidateFrame(value, true);
            if (!string.Equals(value.StateHash, HashShared(value), StringComparison.Ordinal))
                throw new InvalidDataException("state-hash");
            return value;
        }

        public static string HashShared(NetFrame value)
        {
            if (value == null)
                throw new ArgumentNullException(nameof(value));
            ulong hash = FnvOffset;
            Add(ref hash, BattleEntityState.WireVersion);
            Add(ref hash, value.MatchId);
            Add(ref hash, value.ContentVersion);
            Add(ref hash, value.Epoch);
            Add(ref hash, value.Round);
            Add(ref hash, value.Tick);
            Add(ref hash, (int)value.Phase);
            Add(ref hash, value.ChoiceNumber);
            Add(ref hash, value.BonusSide);
            Add(ref hash, value.WinnerSide);
            Add(ref hash, value.Wins0);
            Add(ref hash, value.Wins1);
            Add(ref hash, value.Count0);
            Add(ref hash, value.Count1);
            Add(ref hash, value.IsComeback);
            Add(ref hash, value.Committed0);
            Add(ref hash, value.Committed1);
            Add(ref hash, value.NextReady0);
            Add(ref hash, value.NextReady1);
            Add(ref hash, value.ChoiceToken);
            Add(ref hash, value.EventWatermark);
            Add(ref hash, value.RandomTieBreak);
            Add(ref hash, value.TieBreakSeed);
            foreach (var state in (value.States ?? Array.Empty<BattleEntityState>()).OrderBy(x => x.Id).ThenBy(x => (int)x.Kind))
                Add(ref hash, state);
            foreach (var evt in (value.Events ?? Array.Empty<BattleEvent>()).OrderBy(x => x.Sequence))
                Add(ref hash, evt);
            return hash.ToString("x16");
        }

        static byte[] Write(Action<BinaryWriter> action)
        {
            using (var stream = new MemoryStream())
            {
                using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
                    action(writer);
                if (stream.Length > MaxPayload)
                    throw new InvalidDataException("payload-too-large");
                return stream.ToArray();
            }
        }

        static T Read<T>(byte[] bytes, Func<BinaryReader, T> read)
        {
            if (bytes == null || bytes.Length == 0 || bytes.Length > MaxPayload)
                throw new InvalidDataException("payload-size");
            using (var stream = new MemoryStream(bytes, false))
            using (var reader = new BinaryReader(stream, Encoding.UTF8, true))
            {
                T value = read(reader);
                if (stream.Position != stream.Length)
                    throw new InvalidDataException("payload-trailing");
                return value;
            }
        }

        static void WriteString(BinaryWriter w, string value, int max)
        {
            if (value == null || value.Length > max)
                throw new InvalidDataException("string");
            w.Write(value);
        }

        static string ReadString(BinaryReader r, int max)
        {
            int byteLength = ReadByteLength(r, checked(max * 4));
            if (byteLength > r.BaseStream.Length - r.BaseStream.Position)
                throw new InvalidDataException("string-truncated");
            byte[] bytes = r.ReadBytes(byteLength);
            if (bytes.Length != byteLength)
                throw new InvalidDataException("string-truncated");
            try
            {
                string value = StrictUtf8.GetString(bytes);
                if (value.Length > max)
                    throw new InvalidDataException("string");
                return value;
            }
            catch (DecoderFallbackException exception)
            {
                throw new InvalidDataException("string-utf8", exception);
            }
        }

        static int ReadByteLength(BinaryReader r, int max)
        {
            uint value = 0;
            for (int index = 0; index < 5; index++)
            {
                if (r.BaseStream.Position >= r.BaseStream.Length)
                    throw new InvalidDataException("string-prefix-truncated");
                byte current = r.ReadByte();
                if (index == 4 && ((current & 0xF0) != 0 || (current & 0x80) != 0))
                    throw new InvalidDataException("string-prefix-overflow");
                value |= (uint)(current & 0x7F) << (index * 7);
                if ((current & 0x80) == 0)
                {
                    if (value > int.MaxValue || value > max)
                        throw new InvalidDataException("string-length");
                    return (int)value;
                }
            }

            throw new InvalidDataException("string-prefix-overflow");
        }

        static void WriteId(BinaryWriter w, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidDataException("id");
            WriteString(w, value, MaxId);
        }

        static string ReadId(BinaryReader r)
        {
            string value = ReadString(r, MaxId);
            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidDataException("id");
            return value;
        }

        static void WriteOffers(BinaryWriter w, MatchOffer[] values)
        {
            values = values ?? Array.Empty<MatchOffer>();
            if (values.Length > MaxOffers)
                throw new InvalidDataException("offers");
            w.Write(values.Length);
            foreach (var x in values)
            {
                if (x == null)
                    throw new InvalidDataException("offer");
                WriteId(w, x.Id);
                WriteId(w, x.UnitId);
                w.Write((int)x.Kind);
                w.Write(x.Amount);
            }
        }

        static MatchOffer[] ReadOffers(BinaryReader r)
        {
            int n = Count(r, MaxOffers);
            var result = new MatchOffer[n];
            for (int i = 0; i < n; i++)
                result[i] = new MatchOffer(ReadId(r), ReadId(r), (DraftActionKind)r.ReadInt32(), r.ReadInt32());
            return result;
        }

        static void WriteArmy(BinaryWriter w, ArmyEntry[] values)
        {
            values = values ?? Array.Empty<ArmyEntry>();
            if (values.Length > MaxArmy)
                throw new InvalidDataException("army");
            w.Write(values.Length);
            foreach (var x in values)
            {
                if (x == null || x.Count < 0 || x.Count > 128 || x.UpgradeLevel < 0 || x.UpgradeLevel > 1000000)
                    throw new InvalidDataException("army");
                WriteId(w, x.UnitId);
                w.Write(x.Count);
                w.Write(x.UpgradeLevel);
            }
        }

        static ArmyEntry[] ReadArmy(BinaryReader r)
        {
            int n = Count(r, MaxArmy);
            var result = new ArmyEntry[n];
            for (int i = 0; i < n; i++)
            {
                string id = ReadId(r);
                int count = r.ReadInt32(), upgrade = r.ReadInt32();
                if (count < 0 || count > 128 || upgrade < 0 || upgrade > 1000000)
                    throw new InvalidDataException("army");
                result[i] = new ArmyEntry(id, count, upgrade);
            }

            return result;
        }

        static void WriteStates(BinaryWriter w, BattleEntityState[] values)
        {
            values = values ?? Array.Empty<BattleEntityState>();
            if (values.Length > MaxStates)
                throw new InvalidDataException("states");
            w.Write(values.Length);
            foreach (var x in values)
            {
                w.Write(x.Id);
                w.Write(x.Side);
                w.Write((int)x.Kind);
                WriteId(w, x.DefinitionId);
                Vec(w, x.Position);
                Vec(w, x.PreviousPosition);
                Vec(w, x.Facing);
                w.Write(x.Hp);
                w.Write(x.MaxHp);
                Float(w, x.Radius);
                Float(w, x.Progress);
                w.Write(x.TargetId);
                w.Write(x.ShieldHp);
                w.Write(x.ShieldMaxHp);
                w.Write(x.FirstHitBlocks);
                w.Write(x.Ammo);
                w.Write(x.MagazineSize);
                Float(w, x.ReloadRemaining);
                Float(w, x.ReloadDuration);
                w.Write(x.TransformationStage);
            }
        }

        static BattleEntityState[] ReadStates(BinaryReader r)
        {
            int n = Count(r, MaxStates);
            var result = new BattleEntityState[n];
            for (int i = 0; i < n; i++)
                result[i] = new BattleEntityState(r.ReadInt32(), r.ReadInt32(), (BattleEntityKind)r.ReadInt32(), ReadId(r), ReadVec(r), ReadVec(r), ReadVec(r), r.ReadInt32(), r.ReadInt32(), ReadFloat(r), ReadFloat(r), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), ReadFloat(r), ReadFloat(r), r.ReadInt32());
            return result;
        }

        static void WriteEvents(BinaryWriter w, BattleEvent[] values)
        {
            values = values ?? Array.Empty<BattleEvent>();
            if (values.Length > MaxEvents)
                throw new InvalidDataException("events");
            w.Write(values.Length);
            foreach (var x in values)
            {
                w.Write(x.Sequence);
                w.Write(x.Tick);
                w.Write((int)x.Kind);
                w.Write(x.EntityId);
                w.Write(x.Side);
                Vec(w, x.Position);
                w.Write(x.Amount);
                Float(w, x.Radius);
                Float(w, x.TimeWithinTick);
            }
        }

        static BattleEvent[] ReadEvents(BinaryReader r)
        {
            int n = Count(r, MaxEvents);
            var result = new BattleEvent[n];
            for (int i = 0; i < n; i++)
                result[i] = new BattleEvent(r.ReadInt64(), r.ReadInt64(), (BattleEventKind)r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), ReadVec(r), r.ReadInt32(), ReadFloat(r), ReadFloat(r));
            return result;
        }

        static int Count(BinaryReader r, int max)
        {
            int n = r.ReadInt32();
            if (n < 0 || n > max)
                throw new InvalidDataException("count");
            return n;
        }

        static void Vec(BinaryWriter w, BattleVec v)
        {
            Float(w, v.X);
            Float(w, v.Y);
        }

        static BattleVec ReadVec(BinaryReader r)
        {
            return new BattleVec(ReadFloat(r), ReadFloat(r));
        }

        static void Float(BinaryWriter w, float x)
        {
            if (!Finite(x))
                throw new InvalidDataException("float");
            w.Write(x);
        }

        static float ReadFloat(BinaryReader r)
        {
            float x = r.ReadSingle();
            if (!Finite(x))
                throw new InvalidDataException("float");
            return x;
        }

        static bool Finite(float x)
        {
            return !float.IsNaN(x) && !float.IsInfinity(x);
        }

        static bool Finite(double x)
        {
            return !double.IsNaN(x) && !double.IsInfinity(x);
        }

        static void ValidateCommand(NetCommand x)
        {
            Id(x.MatchId);
            Id(x.ContentVersion);
            if (x.Epoch != 1 || x.Round < 1 || x.Token < 0 || x.Sequence < 0 || !Enum.IsDefined(typeof(NetCommandKind), x.Kind))
                throw new ArgumentOutOfRangeException(nameof(x));
        }

        static void ValidateFrame(NetFrame x, bool hasHash)
        {
            if (x == null)
                throw new ArgumentNullException(nameof(x));
            Id(x.MatchId);
            Id(x.ContentVersion);
            if (x.Epoch != 1 || x.Round < 1 || x.Side < 0 || x.Side > 1 || x.Revision < 0 || x.Tick < 0 || x.ChoiceToken < 0 || x.EventWatermark < 0 || !Finite(x.PresentAt) || !Enum.IsDefined(typeof(MatchPhase), x.Phase) || (!x.RandomTieBreak && x.TieBreakSeed != 0))
                throw new InvalidDataException("frame");
            BattleEntityState[] states = x.States ?? Array.Empty<BattleEntityState>();
            BattleEvent[] events = x.Events ?? Array.Empty<BattleEvent>();
            ArmyEntry[] army = x.Army ?? Array.Empty<ArmyEntry>();
            if ((x.Offers ?? Array.Empty<MatchOffer>()).Length > MaxOffers || army.Length > MaxArmy || states.Length > MaxStates || events.Length > MaxEvents)
                throw new InvalidDataException("count");
            for (int i = 0; i < army.Length; i++)
            {
                ArmyEntry entry = army[i];
                if (entry == null || entry.Count < 0 || entry.Count > 128 || entry.UpgradeLevel < 0 || entry.UpgradeLevel > 1000000)
                    throw new InvalidDataException("army");
                Id(entry.UnitId);
            }

            for (int i = 0; i < states.Length; i++)
                ValidateState(states[i]);
            long previousSequence = 0;
            for (int i = 0; i < events.Length; i++)
            {
                ValidateEvent(events[i]);
                if (events[i].Sequence <= previousSequence)
                    throw new InvalidDataException("event-sequence");
                previousSequence = events[i].Sequence;
            }

            if (hasHash)
                Id(x.StateHash);
        }

        static void ValidateState(BattleEntityState state)
        {
            if (state.Id < 1 || state.Side < 0 || state.Side > 1 || !Enum.IsDefined(typeof(BattleEntityKind), state.Kind))
                throw new InvalidDataException("state");
            Id(state.DefinitionId);
            ValidateVec(state.Position);
            ValidateVec(state.PreviousPosition);
            ValidateVec(state.Facing);
            if (!Finite(state.Radius) || state.Radius < 0 || !Finite(state.Progress) || state.Progress < 0 || state.Progress > 1 || state.TargetId < 0)
                throw new InvalidDataException("state-float");
            if (state.Kind == BattleEntityKind.Unit)
            {
                if (state.MaxHp < 1 || state.Hp < 0 || state.Hp > state.MaxHp)
                    throw new InvalidDataException("state-hp");
            }
            else if (state.Hp != 0 || state.MaxHp != 0)
                throw new InvalidDataException("state-hp");
        }

        static void ValidateEvent(BattleEvent value)
        {
            if (value.Sequence < 1 || value.Tick < 0 || !Enum.IsDefined(typeof(BattleEventKind), value.Kind))
                throw new InvalidDataException("event");
            bool outcome = value.Kind == BattleEventKind.Outcome;
            if ((outcome && (value.Side != -1 || value.EntityId != 0)) || (!outcome && (value.Side < 0 || value.Side > 1 || value.EntityId < 1)))
                throw new InvalidDataException("event-side");
            ValidateVec(value.Position);
            if (!Finite(value.Radius) || value.Radius < 0 || !Finite(value.TimeWithinTick) || value.TimeWithinTick < 0 || value.TimeWithinTick > 1)
                throw new InvalidDataException("event-float");
        }

        static void ValidateVec(BattleVec value)
        {
            if (!Finite(value.X) || !Finite(value.Y))
                throw new InvalidDataException("vector");
        }

        static void Id(string x)
        {
            if (string.IsNullOrWhiteSpace(x) || x.Length > MaxId)
                throw new InvalidDataException("id");
        }

        static void Add(ref ulong h, bool x)
        {
            Add(ref h, x ? 1 : 0);
        }

        static void Add(ref ulong h, int x)
        {
            unchecked
            {
                for (int i = 0; i < 4; i++)
                {
                    h ^= (byte)(x >> (i * 8));
                    h *= FnvPrime;
                }
            }
        }

        static void Add(ref ulong h, long x)
        {
            unchecked
            {
                for (int i = 0; i < 8; i++)
                {
                    h ^= (byte)(x >> (i * 8));
                    h *= FnvPrime;
                }
            }
        }

        static void Add(ref ulong h, uint x)
        {
            unchecked
            {
                for (int i = 0; i < 4; i++)
                {
                    h ^= (byte)(x >> (i * 8));
                    h *= FnvPrime;
                }
            }
        }

        static void Add(ref ulong h, float x)
        {
            if (!Finite(x))
                throw new InvalidDataException("float");
            Add(ref h, BitConverter.ToInt32(BitConverter.GetBytes(x), 0));
        }

        static void Add(ref ulong h, string x)
        {
            Id(x);
            byte[] b = Encoding.UTF8.GetBytes(x);
            Add(ref h, b.Length);
            unchecked
            {
                foreach (byte v in b)
                {
                    h ^= v;
                    h *= FnvPrime;
                }
            }
        }

        static void Add(ref ulong h, BattleVec x)
        {
            Add(ref h, x.X);
            Add(ref h, x.Y);
        }

        static void Add(ref ulong h, BattleEntityState x)
        {
            Add(ref h, x.Id);
            Add(ref h, x.Side);
            Add(ref h, (int)x.Kind);
            Add(ref h, x.DefinitionId);
            Add(ref h, x.Position);
            Add(ref h, x.PreviousPosition);
            Add(ref h, x.Facing);
            Add(ref h, x.Hp);
            Add(ref h, x.MaxHp);
            Add(ref h, x.Radius);
            Add(ref h, x.Progress);
            Add(ref h, x.TargetId);
            Add(ref h, x.ShieldHp);
            Add(ref h, x.ShieldMaxHp);
            Add(ref h, x.FirstHitBlocks);
            Add(ref h, x.Ammo);
            Add(ref h, x.MagazineSize);
            Add(ref h, x.ReloadRemaining);
            Add(ref h, x.ReloadDuration);
            Add(ref h, x.TransformationStage);
        }

        static void Add(ref ulong h, BattleEvent x)
        {
            Add(ref h, x.Sequence);
            Add(ref h, x.Tick);
            Add(ref h, (int)x.Kind);
            Add(ref h, x.EntityId);
            Add(ref h, x.Side);
            Add(ref h, x.Position);
            Add(ref h, x.Amount);
            Add(ref h, x.Radius);
            Add(ref h, x.TimeWithinTick);
        }
    }

    public sealed class NetMatchAuthority : IDisposable
    {
        const int AckCacheSize = 64;
        readonly BattleDefinitions _definitions;
        readonly BattleRules _battleRules;
        readonly int[] _actors;
        readonly Dictionary<long, NetAck>[] _acks =
        {
            new Dictionary<long, NetAck>(),
            new Dictionary<long, NetAck>()
        };
        readonly Queue<long>[] _ackOrder =
        {
            new Queue<long>(),
            new Queue<long>()
        };
        readonly long[] _lastSequence =
        {
            -1,
            -1
        };
        readonly List<BattleEvent> _publishedEvents = new List<BattleEvent>();
        readonly List<BattleEvent> _drained = new List<BattleEvent>();
        readonly List<BattleEntityState> _states = new List<BattleEntityState>();
        bool _disposed, _resolved;
        long _eventWatermark;
        public NetMatchAuthority(BattleDefinitions definitions, BattleRules battleRules, MatchRules matchRules, MatchUnitDefinition[] units, int actor0, int actor1, string[] deck0, string[] deck1, string order0, string order1, uint seed, string matchId, string contentVersion)
        {
            _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
            _battleRules = battleRules ?? throw new ArgumentNullException(nameof(battleRules));
            if (actor0 == actor1)
                throw new ArgumentException("actors");
            _actors = new[]
            {
                actor0,
                actor1
            };
            MatchId = Required(matchId);
            ContentVersion = Required(contentVersion);
            Match = new MatchService(matchRules, units, deck0, deck1, order0, seed, order1);
        }

        public string MatchId { get; }
        public string ContentVersion { get; }
        public MatchService Match { get; }
        public BattleSimulation Simulation { get; private set; }
        public bool NextReady0 { get; private set; }
        public bool NextReady1 { get; private set; }

        public NetAck Handle(int senderActor, NetCommand command)
        {
            ThrowIfDisposed();
            if (command == null)
                return new NetAck
                {
                    Sequence = 0,
                    Reason = "command-null"
                };
            if (command.Sequence < 0)
                return new NetAck
                {
                    Sequence = 0,
                    Reason = "sequence"
                };
            if (!TrySide(senderActor, out var side))
                return new NetAck
                {
                    Sequence = command.Sequence,
                    Reason = "sender-actor"
                };
            if (_acks[side].TryGetValue(command.Sequence, out var prior))
                return Copy(prior, true);
            if (command.Sequence <= _lastSequence[side])
                return new NetAck
                {
                    Sequence = command.Sequence,
                    Reason = "sequence-stale"
                };
            _lastSequence[side] = command.Sequence;
            NetAck ack;
            if (!ValidEnvelope(command, out var reason))
                ack = Reject(command, reason);
            else if (command.Kind == NetCommandKind.Choose)
                ack = ApplyChoose(side, command);
            else if (command.Kind == NetCommandKind.Order)
                ack = ApplyOrder(side, command);
            else
                ack = ApplyContinue(side, command);
            Remember(side, ack);
            return ack;
        }

        public void Advance(int ticks)
        {
            ThrowIfDisposed();
            if (ticks < 0)
                throw new ArgumentOutOfRangeException(nameof(ticks));
            if (Match.Phase != MatchPhase.Battle)
                return;
            for (int i = 0; i < ticks && Simulation != null && Simulation.Outcome == BattleOutcome.Running; i++)
            {
                Simulation.Step();
                Drain();
            }

            if (Simulation != null && !_resolved && Simulation.Outcome != BattleOutcome.Running)
            {
                _resolved = true;
                Match.ResolveBattle(Simulation.Outcome);
                Drain();
            }
        }

        public NetFrame Capture(int side, long revision, double presentAt, bool recovery)
        {
            ThrowIfDisposed();
            if (side != 0 && side != 1)
                throw new ArgumentOutOfRangeException(nameof(side));
            if (revision < 0 || double.IsNaN(presentAt) || double.IsInfinity(presentAt))
                throw new ArgumentOutOfRangeException(nameof(revision));
            _states.Clear();
            long tick = 0;
            if (Simulation != null)
            {
                Simulation.Capture(_states);
                tick = Simulation.Tick;
            }

            var frame = new NetFrame
            {
                MatchId = MatchId,
                ContentVersion = ContentVersion,
                Round = Match.RoundNumber,
                Side = side,
                ChoiceNumber = Match.ChoiceNumber,
                BonusSide = Match.BonusSide,
                WinnerSide = Match.LastWinner,
                Wins0 = Match.Wins(0),
                Wins1 = Match.Wins(1),
                Count0 = Count(0),
                Count1 = Count(1),
                OrderCharges = Match.Charges(side),
                Revision = revision,
                Tick = tick,
                ChoiceToken = Match.ChoiceToken,
                EventWatermark = _eventWatermark,
                PresentAt = presentAt,
                Phase = Match.Phase,
                IsComeback = Match.IsComeback,
                Committed0 = Match.HasCommitted(0),
                Committed1 = Match.HasCommitted(1),
                NextReady0 = NextReady0,
                NextReady1 = NextReady1,
                CanUseOrder = Match.CanUseOrder(side),
                Recovery = recovery,
                RandomTieBreak = Simulation != null && Simulation.ResolutionUsedRandomTieBreak,
                TieBreakSeed = Simulation == null ? 0u : Simulation.TieBreakSeed,
                Offers = Match.HasCommitted(side) ? Array.Empty<MatchOffer>() : Match.GetOffers(side).ToArray(),
                Army = Match.GetArmy(side).ToArray(),
                States = _states.ToArray(),
                Events = recovery ? Array.Empty<BattleEvent>() : _publishedEvents.ToArray()
            };
            frame.StateHash = NetCodec.HashShared(frame);
            return frame;
        }

        public void ClearPublishedEvents()
        {
            ThrowIfDisposed();
            _publishedEvents.Clear();
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            Simulation?.Dispose();
            Simulation = null;
            _publishedEvents.Clear();
        }

        NetAck ApplyChoose(int side, NetCommand c)
        {
            if (c.Round != Match.RoundNumber || c.Token != Match.ChoiceToken)
                return Reject(c, "round-or-token");
            long token = Match.ChoiceToken;
            MatchPhase phase = Match.Phase;
            if (!Match.TryChoose(side, c.Token, c.OfferIndex, out var reason))
                return Reject(c, reason);
            AfterDraftBarrier(token, phase);
            return Accept(c);
        }

        NetAck ApplyOrder(int side, NetCommand c)
        {
            if (c.Round != Match.RoundNumber || c.Token != Match.ChoiceToken)
                return Reject(c, "round-or-token");
            long token = Match.ChoiceToken;
            MatchPhase phase = Match.Phase;
            if (!Match.TryUseOrder(side, c.Token, out var reason))
                return Reject(c, reason);
            AfterDraftBarrier(token, phase);
            return Accept(c);
        }

        NetAck ApplyContinue(int side, NetCommand c)
        {
            if (c.Round != Match.RoundNumber || c.Token != Match.ChoiceToken || Match.Phase != MatchPhase.RoundResult)
                return Reject(c, "continue-unavailable");
            if (side == 0 ? NextReady0 : NextReady1)
                return Reject(c, "already-ready");
            if (side == 0)
                NextReady0 = true;
            else
                NextReady1 = true;
            if (NextReady0 && NextReady1)
            {
                Match.Continue();
                NextReady0 = NextReady1 = false;
                ResetPreview();
            }

            return Accept(c);
        }

        void AfterDraftBarrier(long token, MatchPhase phase)
        {
            if (Match.ChoiceToken != token || Match.Phase != phase)
                ResetPreview();
        }

        void ResetPreview()
        {
            Simulation?.Dispose();
            Simulation = null;
            _resolved = false;
            _eventWatermark = 0;
            _publishedEvents.Clear();
            _drained.Clear();
            if (Match.GetArmy(0).Count > 0 && Match.GetArmy(1).Count > 0)
            {
                Simulation = new BattleSimulation(_definitions, _battleRules, Match.CreateScenario());
                Drain();
            }
        }

        void Drain()
        {
            if (Simulation == null)
                return;
            Simulation.DrainEvents(_drained);
            foreach (var e in _drained)
            {
                _publishedEvents.Add(e);
                if (e.Sequence > _eventWatermark)
                    _eventWatermark = e.Sequence;
            }
        }

        bool ValidEnvelope(NetCommand c, out string reason)
        {
            if (c.Epoch != 1 || !string.Equals(c.MatchId, MatchId, StringComparison.Ordinal) || !string.Equals(c.ContentVersion, ContentVersion, StringComparison.Ordinal))
            {
                reason = "envelope";
                return false;
            }

            if (c.Round < 1 || c.Token < 0 || c.Sequence < 0 || !Enum.IsDefined(typeof(NetCommandKind), c.Kind))
            {
                reason = "command";
                return false;
            }

            reason = null;
            return true;
        }

        bool TrySide(int actor, out int side)
        {
            if (actor == _actors[0])
            {
                side = 0;
                return true;
            }

            if (actor == _actors[1])
            {
                side = 1;
                return true;
            }

            side = -1;
            return false;
        }

        void Remember(int side, NetAck ack)
        {
            _acks[side][ack.Sequence] = Copy(ack, false);
            _ackOrder[side].Enqueue(ack.Sequence);
            while (_ackOrder[side].Count > AckCacheSize)
                _acks[side].Remove(_ackOrder[side].Dequeue());
        }

        int Count(int side)
        {
            return Match.GetArmy(side).Sum(x => x.Count);
        }

        static NetAck Accept(NetCommand c)
        {
            return new NetAck
            {
                Sequence = c.Sequence,
                Accepted = true,
                Reason = string.Empty
            };
        }

        static NetAck Reject(NetCommand c, string reason)
        {
            return new NetAck
            {
                Sequence = c.Sequence,
                Accepted = false,
                Reason = reason ?? "rejected"
            };
        }

        static NetAck Copy(NetAck x, bool duplicate)
        {
            return new NetAck
            {
                Sequence = x.Sequence,
                Accepted = x.Accepted,
                Duplicate = duplicate,
                Reason = x.Reason
            };
        }

        static string Required(string x)
        {
            if (string.IsNullOrWhiteSpace(x) || x.Length > 256)
                throw new ArgumentException("id");
            return x;
        }

        void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(NetMatchAuthority));
        }
    }
}

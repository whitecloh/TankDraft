using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using TankDraft.Contracts;
using TankDraft.Infrastructure.FusionTransport;

namespace TankDraft.Infrastructure.FusionGameplay
{
    // Transport-only projection client. It sends player intent and never calculates prices, grants, or outcomes.
    public sealed class FusionProgressionClient : IServerProgressionClient, IDisposable
    {
        private readonly FusionSessionContext context;
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);
        private bool disposed;
        private bool lobbyReady;
        private string lobbyOperation = Guid.NewGuid().ToString("N");
        public string JournalScope => context.ProgressionJournalScope;

        public FusionProgressionClient(FusionSessionContext context)
        {
            this.context = context ?? throw new ArgumentNullException(nameof(context));
        }

        public async Task<ProgressionSnapshot> GetAsync(CancellationToken token)
        {
            using (CancellationTokenSource stop = CreateStop(token))
            {
                await gate.WaitAsync(stop.Token).ConfigureAwait(false);
                try
                {
                    return ReadEnabledSnapshot(await SendAfterLobbyAsync("ProgressionGet", new JObject
                    {
                        ["InstanceId"] = context.InstanceId,
                        ["ContentVersion"] = context.ContentVersion
                    }, stop.Token).ConfigureAwait(false));
                }
                finally { gate.Release(); }
            }
        }

        public async Task<ProgressionSnapshot> ExecuteAsync(string kind, string targetId, Guid operationId, long expectedSequence, CancellationToken token)
        {
            ValidateIntent(kind, targetId, operationId, expectedSequence);
            using (CancellationTokenSource stop = CreateStop(token))
            {
                await gate.WaitAsync(stop.Token).ConfigureAwait(false);
                try
                {
                    return ReadEnabledSnapshot(await SendAfterLobbyAsync("ProgressionExecute", new JObject
                    {
                        ["InstanceId"] = context.InstanceId,
                        ["ContentVersion"] = context.ContentVersion,
                        ["Kind"] = kind,
                        ["TargetId"] = targetId,
                        ["OperationId"] = operationId.ToString("N"),
                        ["ExpectedSequence"] = expectedSequence
                    }, stop.Token).ConfigureAwait(false), operationId);
                }
                catch (FusionAuthorityException error) when (IsDefinitiveRejection(error.Code))
                { throw new ProgressionRejectedException(error.Code); }
                finally { gate.Release(); }
            }
        }

        private CancellationTokenSource CreateStop(CancellationToken token)
        {
            if (disposed) throw new ObjectDisposedException(nameof(FusionProgressionClient));
            return CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
        }

        private async Task<JObject> SendAfterLobbyAsync(string operation, JObject body, CancellationToken token)
        {
            await EnsureLobbyAsync(token).ConfigureAwait(false);
            try { return await context.Requests.Send(operation, body, token).ConfigureAwait(false); }
            catch (FusionAuthorityException error) when (error.Status == 403 && error.Code == null)
            {
                lobbyReady = false;
                lobbyOperation = Guid.NewGuid().ToString("N");
                await EnsureLobbyAsync(token).ConfigureAwait(false);
                return await context.Requests.Send(operation, body, token).ConfigureAwait(false);
            }
        }

        private async Task EnsureLobbyAsync(CancellationToken token)
        {
            if (lobbyReady) return;
            await context.Requests.Send("Lobby", new JObject { ["OperationId"] = lobbyOperation }, token).ConfigureAwait(false);
            lobbyReady = true;
        }

        private static ProgressionSnapshot ReadEnabledSnapshot(JObject response, Guid? expectedOperation = null)
        {
            if (!RequiredBoolean(response, "Enabled")) throw new InvalidDataException("Fusion progression is disabled.");
            JObject state = RequiredObject(response, "State");
            if (RequiredInt(state, "SchemaVersion", 1, 1) != 1) throw new InvalidDataException("Unsupported progression schema.");
            long sequence = RequiredLong(state, "AppliedSequence", 0, long.MaxValue);
            UnitProgressSnapshot[] units = RequiredUnits(state);
            int masteryChips = RequiredInt(state, "MasteryChips", 0, int.MaxValue);
            string[] milestones = RequiredIds(state, "ClaimedMilestones");
            string status = null;
            string[] resolved = null;
            if (expectedOperation.HasValue && response["Receipt"] == null) throw new InvalidDataException("Missing progression receipt.");
            if (response["Receipt"] != null)
            {
                JObject receipt = RequiredObject(response, "Receipt");
                status = RequiredString(receipt, "Status");
                resolved = RequiredIds(receipt, "ResolvedUnitIds");
                if (expectedOperation.HasValue && (!Guid.TryParse(RequiredString(receipt, "OperationId"), out Guid receiptId) || receiptId != expectedOperation.Value || RequiredLong(receipt, "Sequence", 1, sequence) < 1))
                    throw new InvalidDataException("Progression receipt does not match intent.");
                if (status != "Prepared" && status != "ProfileApplied" && status != "WalletSending" && status != "Completed" && status != "NeedsReview")
                    throw new InvalidDataException("Invalid progression operation status.");
            }
            return new ProgressionSnapshot(sequence, units, masteryChips, status, milestones, RequiredString(response, "RulesVersion"), resolved);
        }

        private static UnitProgressSnapshot[] RequiredUnits(JObject state)
        {
            JObject source = RequiredObject(state, "Units");
            if (source.Count > ProgressionSnapshot.MaximumUnits) throw new InvalidDataException("Fusion progression has too many units.");
            var result = new List<UnitProgressSnapshot>(source.Count);
            foreach (JProperty property in source.Properties())
            {
                if (!ProgressionSnapshot.IsValidId(property.Name) || property.Value.Type != JTokenType.Object)
                    throw new InvalidDataException("Invalid Fusion progression unit.");
                JObject value = (JObject)property.Value;
                result.Add(new UnitProgressSnapshot(property.Name,
                    RequiredInt(value, "Level", 1, 31),
                    RequiredInt(value, "Bits", 0, int.MaxValue),
                    RequiredInt(value, "MasteryXp", 0, int.MaxValue)));
            }
            return result.ToArray();
        }

        private static string[] RequiredIds(JObject source, string name)
        {
            JToken token;
            if (source == null || !source.TryGetValue(name, out token) || token.Type != JTokenType.Array)
                throw new InvalidDataException("Invalid Fusion progression ids.");
            JArray values = (JArray)token;
            if (values.Count > ProgressionSnapshot.MaximumUnits) throw new InvalidDataException("Fusion progression has too many ids.");
            string[] result = new string[values.Count];
            for (int index = 0; index < result.Length; index++)
            {
                if (values[index].Type != JTokenType.String) throw new InvalidDataException("Invalid Fusion progression id.");
                string id = values[index].Value<string>();
                if (!ProgressionSnapshot.IsValidId(id)) throw new InvalidDataException("Invalid Fusion progression id.");
                for (int earlier = 0; earlier < index; earlier++)
                    if (result[earlier] == id) throw new InvalidDataException("Duplicate Fusion progression id.");
                result[index] = id;
            }
            return result;
        }

        private static JObject RequiredObject(JObject source, string name)
        {
            JToken token;
            if (source == null || !source.TryGetValue(name, out token) || token.Type != JTokenType.Object)
                throw new InvalidDataException("Invalid Fusion progression response.");
            return (JObject)token;
        }

        private static bool RequiredBoolean(JObject source, string name)
        {
            JToken token;
            if (source == null || !source.TryGetValue(name, out token) || token.Type != JTokenType.Boolean)
                throw new InvalidDataException("Invalid Fusion progression response.");
            return token.Value<bool>();
        }

        private static int RequiredInt(JObject source, string name, int min, int max)
        {
            long value = RequiredLong(source, name, min, max);
            return (int)value;
        }

        private static long RequiredLong(JObject source, string name, long min, long max)
        {
            JToken token;
            if (source == null || !source.TryGetValue(name, out token) || token.Type != JTokenType.Integer)
                throw new InvalidDataException("Invalid Fusion progression response.");
            long value = token.Value<long>();
            if (value < min || value > max) throw new InvalidDataException("Fusion progression value is out of range.");
            return value;
        }

        private static string RequiredString(JObject source, string name)
        {
            JToken token;
            if (source == null || !source.TryGetValue(name, out token) || token.Type != JTokenType.String)
                throw new InvalidDataException("Invalid Fusion progression response.");
            string value = token.Value<string>();
            if (!ProgressionSnapshot.IsValidId(value)) throw new InvalidDataException("Invalid Fusion progression string.");
            return value;
        }

        private static void ValidateIntent(string kind, string targetId, Guid operationId, long expectedSequence)
        {
            switch (kind)
            {
                case "UpgradeUnit": case "BuyOffer": case "BoostMastery": case "BuyMasteryLevel": case "ClaimMilestone": break;
                default: throw new ArgumentException("Unsupported progression operation.", nameof(kind));
            }
            if (!ProgressionSnapshot.IsValidId(targetId)) throw new ArgumentException("Progression target is invalid.", nameof(targetId));
            if (operationId == Guid.Empty) throw new ArgumentException("Progression operation id is invalid.", nameof(operationId));
            if (expectedSequence < 0) throw new ArgumentOutOfRangeException(nameof(expectedSequence));
        }
        private static bool IsDefinitiveRejection(string code)
        {
            switch (code)
            {
                case "insufficient_funds": case "insufficient_bits": case "insufficient_chips":
                case "stale_progression_sequence": case "unit_max_level": case "mastery_max_level":
                case "unknown_offer": case "unit_unavailable": case "milestone_unavailable":
                case "invalid_milestone": case "pack_unavailable": return true;
                default: return false;
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            lifetime.Cancel();
            lifetime.Dispose();
        }
    }
}

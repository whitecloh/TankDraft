using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using TankDraft.Contracts;
using TankDraft.Infrastructure.FusionTransport;

namespace TankDraft.Infrastructure.FusionGameplay
{
    // Menu meta traffic uses the same authenticated Fusion gateway as queue and
    // match commands. This class owns only a projection; it never stores rights.
    public sealed class FusionProfileClient : IServerProfileClient, IDisposable
    {
        const int MaximumIds = 128;
        const int MaximumIdLength = 128;
        readonly FusionSessionContext context;
        readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);
        bool disposed, lobbyReady;
        int profileVersion = -1;
        string lobbyOperation = Guid.NewGuid().ToString("N");
        string pendingOperation;
        string[] pendingUnits, pendingOrders;
        int pendingExpectedVersion = -1;

        public FusionProfileClient(FusionSessionContext context)
        {
            this.context = context ?? throw new ArgumentNullException(nameof(context));
        }

        public async Task<ProfileSnapshot> GetAsync(CancellationToken token)
        {
            using (var stop = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token))
            {
                await gate.WaitAsync(stop.Token).ConfigureAwait(false);
                try
                {
                    if (pendingOperation != null)
                    {
                        try { await RetryPendingSaveAsync(stop.Token).ConfigureAwait(false); }
                        catch (FusionAuthorityException error) when (IsDefinitiveSaveRejection(error))
                        {
                            ClearPending(invalidateProfileVersion: true);
                        }
                    }
                    return await GetCoreAsync(stop.Token).ConfigureAwait(false);
                }
                finally { gate.Release(); }
            }
        }

        public async Task<ProfileSnapshot> SaveLoadoutAsync(string[] units, string[] orders, CancellationToken token)
        {
            ValidateIntent(units, orders);
            using (var stop = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token))
            {
                await gate.WaitAsync(stop.Token).ConfigureAwait(false);
                try
                {
                    if (pendingOperation == null && profileVersion < 0)
                    {
                        ProfileSnapshot current = await GetCoreAsync(stop.Token).ConfigureAwait(false);
                        if (current == null) throw new InvalidOperationException("fusion_profile_legacy_mode");
                        throw new ServerProfileRefreshRequiredException(current);
                    }

                    if (pendingOperation != null && !SameIntent(units, orders, pendingUnits, pendingOrders))
                        throw new InvalidOperationException("fusion_profile_save_pending");

                    if (pendingOperation == null)
                    {
                        pendingOperation = Guid.NewGuid().ToString("N");
                        pendingUnits = Copy(units);
                        pendingOrders = Copy(orders);
                        pendingExpectedVersion = profileVersion;
                    }
                    try { return await RetryPendingSaveAsync(stop.Token).ConfigureAwait(false); }
                    catch (FusionAuthorityException error) when (IsDefinitiveSaveRejection(error))
                    {
                        ClearPending(invalidateProfileVersion: true);
                        throw;
                    }
                }
                finally { gate.Release(); }
            }
        }

        async Task<ProfileSnapshot> GetCoreAsync(CancellationToken token)
        {
            JObject response = await SendAfterLobbyAsync("ProfileGet", new JObject
            {
                ["InstanceId"] = context.InstanceId,
                ["ContentVersion"] = context.ContentVersion
            }, token).ConfigureAwait(false);
            if (RequiredBoolean(response, "Enabled") == false) return null;
            return ReadEnabledProfile(response);
        }

        async Task<ProfileSnapshot> RetryPendingSaveAsync(CancellationToken token)
        {
            if (pendingOperation == null || pendingUnits == null || pendingOrders == null || pendingExpectedVersion < 0)
                throw new InvalidOperationException("fusion_profile_pending_invalid");
            JObject body = new JObject
            {
                ["InstanceId"] = context.InstanceId,
                ["ContentVersion"] = context.ContentVersion,
                ["ExpectedProfileVersion"] = pendingExpectedVersion,
                ["OperationId"] = pendingOperation,
                ["UnitIds"] = new JArray(pendingUnits),
                ["OrderIds"] = new JArray(pendingOrders)
            };
            JObject response = await SendAfterLobbyAsync("ProfileSave", body, token).ConfigureAwait(false);
            ProfileSnapshot saved = ReadEnabledProfile(response);
            ClearPending(invalidateProfileVersion: false);
            return saved;
        }

        static bool IsDefinitiveSaveRejection(FusionAuthorityException error) => error.Status == 403 || error.Status == 409;

        void ClearPending(bool invalidateProfileVersion)
        {
            pendingOperation = null;
            pendingUnits = null;
            pendingOrders = null;
            pendingExpectedVersion = -1;
            if (invalidateProfileVersion) profileVersion = -1;
        }

        async Task<JObject> SendAfterLobbyAsync(string operation, JObject body, CancellationToken token)
        {
            await EnsureLobbyAsync(token).ConfigureAwait(false);
            try { return await context.Requests.Send(operation, body, token).ConfigureAwait(false); }
            catch (FusionAuthorityException error) when (error.Status == 403)
            {
                lobbyReady = false;
                lobbyOperation = Guid.NewGuid().ToString("N");
                await EnsureLobbyAsync(token).ConfigureAwait(false);
                return await context.Requests.Send(operation, body, token).ConfigureAwait(false);
            }
        }

        async Task EnsureLobbyAsync(CancellationToken token)
        {
            if (lobbyReady) return;
            await context.Requests.Send("Lobby", new JObject { ["OperationId"] = lobbyOperation }, token).ConfigureAwait(false);
            lobbyReady = true;
        }

        ProfileSnapshot ReadEnabledProfile(JObject response)
        {
            if (RequiredBoolean(response, "Enabled") == false) throw new InvalidDataException("Fusion profile save was disabled.");
            int receivedVersion = RequiredInt(response, "ProfileVersion", 0, int.MaxValue);
            JObject profile = RequiredObject(response, "Profile");
            JObject inventory = RequiredObject(response, "Inventory");
            if (RequiredInt(profile, "SchemaVersion", 1, 1) != 1) throw new InvalidDataException("Unsupported profile schema.");

            string name = RequiredString(profile, "Name", false);
            int commander = RequiredInt(profile, "CommanderLevel", 1, int.MaxValue);
            int arena = RequiredInt(profile, "ArenaLevel", 1, int.MaxValue);
            int progress = RequiredInt(profile, "ArenaProgress", 0, int.MaxValue);
            int mastery = RequiredInt(profile, "Mastery", 0, int.MaxValue);
            string[] owned = RequiredIds(inventory, "OwnedContentIds", false);
            JObject balances = RequiredObject(inventory, "Balances");
            int energy = RequiredInt(balances, "energy", 0, int.MaxValue);
            int gems = RequiredInt(balances, "gems", 0, int.MaxValue);
            int coins = RequiredInt(balances, "coins", 0, int.MaxValue);
            string[] units = RequiredIds(profile, "UnitIds", false);
            string[] orders = RequiredIds(profile, "OrderIds", true);
            RecentMatchResult[] recentResults = OptionalRecentResults(response);
            ProfileSnapshot snapshot = new ProfileSnapshot(name, commander, arena, progress, energy, gems, coins, mastery, owned, units, orders, recentResults);
            profileVersion = receivedVersion;
            WriteQaProfileEvidence(snapshot, receivedVersion);
            return snapshot;
        }

        void WriteQaProfileEvidence(ProfileSnapshot snapshot, int version)
        {
            if (Environment.GetEnvironmentVariable("TD_FUSION_CAPTURE_SCREENSHOTS") != "1") return;
            try
            {
                var results = new JArray();
                foreach (var result in snapshot.RecentResults)
                    results.Add(new JObject { ["ResultId"] = result.ResultId, ["MatchId"] = result.MatchId,
                        ["State"] = result.State.ToString(), ["Amount"] = result.Amount });
                var value = new JObject { ["Utc"] = DateTimeOffset.UtcNow.ToString("O"), ["ProfileVersion"] = version,
                    ["Coins"] = snapshot.Coins, ["UnitIds"] = new JArray(snapshot.UnitIds), ["RecentResults"] = results };
                File.AppendAllText(Path.Combine(context.RunDirectory, "profiles.jsonl"), value.ToString(Newtonsoft.Json.Formatting.None) + Environment.NewLine);
            }
            catch { /* Diagnostic output never changes the server profile or availability. */ }
        }

        RecentMatchResult[] OptionalRecentResults(JObject response)
        {
            JToken token;
            if (!response.TryGetValue("RecentResults", out token)) return Array.Empty<RecentMatchResult>();
            if (token.Type != JTokenType.Array) throw new InvalidDataException("Invalid Fusion recent results.");
            JArray values = (JArray)token;
            if (values.Count > ProfileSnapshot.MaximumRecentResults) throw new InvalidDataException("Fusion profile has too many recent results.");

            RecentMatchResult[] results = new RecentMatchResult[values.Count];
            for (int index = 0; index < results.Length; index++)
            {
                if (values[index].Type != JTokenType.Object) throw new InvalidDataException("Invalid Fusion recent result.");
                JObject value = (JObject)values[index];
                string resultId = RequiredHex64(value, "ResultId");
                for (int earlier = 0; earlier < index; earlier++)
                {
                    if (results[earlier].ResultId == resultId) throw new InvalidDataException("Duplicate Fusion recent result.");
                }

                string matchId = RequiredString(value, "MatchId", false);
                RequiredString(value, "ContentVersion", false);
                int wins0 = RequiredInt(value, "Wins0", 0, int.MaxValue);
                int wins1 = RequiredInt(value, "Wins1", 0, int.MaxValue);
                int winner = RequiredInt(value, "Winner", 0, 1);
                int side = RequiredInt(value, "Side", 0, 1);
                if (wins0 == wins1 || winner != (wins0 > wins1 ? 0 : 1)) throw new InvalidDataException("Fusion recent result winner is invalid.");

                string opponentKind = RequiredString(value, "OpponentKind", false);
                bool isBot;
                if (opponentKind == "Bot") isBot = true;
                else if (opponentKind == "Human") isBot = false;
                else throw new InvalidDataException("Fusion recent result opponent is invalid.");

                JObject reward = RequiredObject(value, "Reward");
                string currency = RequiredString(reward, "Currency", false);
                int amount = RequiredInt(reward, "Amount", 0, int.MaxValue);
                RewardDeliveryState state = RequiredRewardState(reward);
                int ownWins = side == 0 ? wins0 : wins1;
                int opponentWins = side == 0 ? wins1 : wins0;
                string[] participants = null;
                int masteryXp = 0;
                bool masteryApplied = false;
                if (value["ParticipantReward"] != null && value["ParticipantReward"].Type != JTokenType.Null)
                {
                    JObject participant = RequiredObject(value, "ParticipantReward");
                    if (!(participant["UnitIds"] is JArray ids) || ids.Count != 4) throw new InvalidDataException("Invalid reward participants.");
                    participants = new string[4];
                    for (int n = 0; n < 4; n++)
                    {
                        if (ids[n].Type != JTokenType.String || !ProgressionSnapshot.IsValidId(ids[n].Value<string>())) throw new InvalidDataException("Invalid reward participant.");
                        participants[n] = ids[n].Value<string>();
                    }
                    masteryXp = RequiredInt(participant, "MasteryXp", 0, 10000);
                    if (participant["Applied"]?.Type != JTokenType.Boolean) throw new InvalidDataException("Invalid mastery delivery status.");
                    masteryApplied = participant["Applied"].Value<bool>();
                }
                results[index] = new RecentMatchResult(resultId, matchId, ownWins, opponentWins, winner == side, isBot, currency, amount, state, participants, masteryXp, masteryApplied);
            }
            return results;
        }

        static string RequiredHex64(JObject source, string name)
        {
            string value = RequiredString(source, name, false);
            if (value.Length != 64) throw new InvalidDataException("Invalid Fusion recent result id.");
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                if (!((character >= '0' && character <= '9') || (character >= 'a' && character <= 'f') || (character >= 'A' && character <= 'F')))
                    throw new InvalidDataException("Invalid Fusion recent result id.");
            }
            return value;
        }

        static RewardDeliveryState RequiredRewardState(JObject reward)
        {
            string value = RequiredString(reward, "State", false);
            switch (value)
            {
                case "Pending": return RewardDeliveryState.Pending;
                case "Sending": return RewardDeliveryState.Sending;
                case "Applied": return RewardDeliveryState.Applied;
                case "NeedsReview": return RewardDeliveryState.NeedsReview;
                case "Skipped": return RewardDeliveryState.Skipped;
                default: throw new InvalidDataException("Fusion recent result reward state is invalid.");
            }
        }

        static JObject RequiredObject(JObject source, string name)
        {
            JToken token;
            if (source == null || !source.TryGetValue(name, out token) || token.Type != JTokenType.Object) throw new InvalidDataException("Invalid Fusion profile response.");
            return (JObject)token;
        }

        static bool RequiredBoolean(JObject source, string name)
        {
            JToken token;
            if (source == null || !source.TryGetValue(name, out token) || token.Type != JTokenType.Boolean) throw new InvalidDataException("Invalid Fusion profile response.");
            return token.Value<bool>();
        }

        static int RequiredInt(JObject source, string name, int min, int max)
        {
            JToken token;
            if (source == null || !source.TryGetValue(name, out token) || token.Type != JTokenType.Integer) throw new InvalidDataException("Invalid Fusion profile response.");
            long value = token.Value<long>();
            if (value < min || value > max) throw new InvalidDataException("Fusion profile value is out of range.");
            return (int)value;
        }

        static string RequiredString(JObject source, string name, bool allowEmpty)
        {
            JToken token;
            if (source == null || !source.TryGetValue(name, out token) || token.Type != JTokenType.String) throw new InvalidDataException("Invalid Fusion profile response.");
            string value = token.Value<string>();
            if (value == null || value.Length > MaximumIdLength || (value.Length == 0 ? !allowEmpty : string.IsNullOrWhiteSpace(value))) throw new InvalidDataException("Invalid Fusion profile string.");
            return value;
        }

        static string[] RequiredIds(JObject source, string name, bool allowEmpty)
        {
            JToken token;
            if (source == null || !source.TryGetValue(name, out token) || token.Type != JTokenType.Array) throw new InvalidDataException("Invalid Fusion profile response.");
            JArray values = (JArray)token;
            if (values.Count > MaximumIds) throw new InvalidDataException("Fusion profile has too many ids.");
            string[] result = new string[values.Count];
            for (int index = 0; index < result.Length; index++)
            {
                if (values[index].Type != JTokenType.String) throw new InvalidDataException("Invalid Fusion profile id.");
                string value = values[index].Value<string>();
                if (value == null || value.Length > MaximumIdLength || (value.Length == 0 ? !allowEmpty : string.IsNullOrWhiteSpace(value))) throw new InvalidDataException("Invalid Fusion profile id.");
                if (allowEmpty && value.Length == 0) { result[index] = value; continue; }
                for (int earlier = 0; earlier < index; earlier++) if (result[earlier] == value) throw new InvalidDataException("Duplicate Fusion profile id.");
                result[index] = value;
            }
            return result;
        }

        static void ValidateIntent(string[] units, string[] orders)
        {
            if (units == null || orders == null || units.Length > MaximumIds || orders.Length > MaximumIds) throw new ArgumentException("Invalid profile intent.");
            ValidateIntentIds(units, false); ValidateIntentIds(orders, true);
        }

        static void ValidateIntentIds(string[] values, bool allowEmpty)
        {
            for (int index = 0; index < values.Length; index++)
            {
                string value = values[index];
                if (value == null || value.Length > MaximumIdLength || (value.Length == 0 ? !allowEmpty : string.IsNullOrWhiteSpace(value))) throw new ArgumentException("Invalid profile intent.");
            }
        }

        static bool SameIntent(string[] units, string[] orders, string[] pendingUnitIds, string[] pendingOrderIds)
        {
            return SameIds(units, pendingUnitIds) && SameIds(orders, pendingOrderIds);
        }

        static bool SameIds(string[] left, string[] right)
        {
            if (left == null || right == null || left.Length != right.Length) return false;
            for (int index = 0; index < left.Length; index++) if (left[index] != right[index]) return false;
            return true;
        }

        static string[] Copy(string[] source)
        {
            string[] copy = new string[source.Length];
            Array.Copy(source, copy, source.Length);
            return copy;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            lifetime.Cancel();
            lifetime.Dispose();
            // A request may still be unwinding its finally block after cancellation.
            // Keep the gate alive until the scope is collected rather than turning a
            // normal cancellation into ObjectDisposedException on that continuation.
        }
    }
}

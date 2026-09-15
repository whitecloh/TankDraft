using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace TankDraft.Server.Settlement;

public sealed partial class SqliteSettlementStore : IDisposable
{
    private const int MaxResults = 10000;
    private const int MaxPages = 16384;
    private const int Schema = 1;
    private const int MaxEvidenceReferenceLength = 96;
    private static readonly Regex EvidenceReferencePattern = new("^[A-Za-z0-9][A-Za-z0-9._:-]{0,95}$", RegexOptions.CultureInvariant);
    private const string CompensationAuthorizedReason = "compensation_authorized";
    private const string VerifiedExternalReason = "verified_external";
    private static readonly Regex AccountPattern = new("^[A-Za-z0-9]{5,32}$", RegexOptions.CultureInvariant);
    private static readonly Regex SafeTextPattern = new("^[A-Za-z0-9._:-]+$", RegexOptions.CultureInvariant);
    private readonly object _sync = new();
    private readonly FileStream _writerLock;
    private readonly SqliteConnection _connection;
    private readonly RewardPolicy _policy;
    private bool _disposed;

    public SqliteSettlementStore(string absoluteDatabasePath, RewardPolicy policy)
    {
        ValidatePolicy(policy);
        var path = ValidatePath(absoluteDatabasePath);
        var existed = File.Exists(path);
        if (existed && new FileInfo(path).Length == 0) throw new InvalidDataException("An existing settlement database is empty.");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        FileStream? writer = null;
        SqliteConnection? connection = null;
        try
        {
            writer = new FileStream(path + ".writer.lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.None);
            connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWriteCreate, Cache = SqliteCacheMode.Private, Pooling = false }.ToString());
            connection.Open();
            Configure(connection);
            if (existed) ValidateExistingSchema(connection);
            CreateSchema(connection);
            CreateParticipantSchema(connection);
            var persisted = ReadPolicy(connection);
            if (persisted is null)
            {
                if (existed) throw new InvalidDataException("An existing settlement database is missing metadata.");
                using var transaction = connection.BeginTransaction();
                InsertPolicy(connection, transaction, policy);
                transaction.Commit();
            }
            else if (persisted != policy)
                throw new InvalidDataException("Settlement policy does not match frozen persisted policy.");
            if (existed) VerifyPersisted(connection, policy);
            using (var transaction = connection.BeginTransaction())
            {
                using var recovery = connection.CreateCommand();
                recovery.Transaction = transaction;
                recovery.CommandText = "UPDATE Rewards SET State = $review, Reason = 'interrupted' WHERE State = $sending;";
                recovery.Parameters.AddWithValue("$review", (int)RewardState.NeedsReview);
                recovery.Parameters.AddWithValue("$sending", (int)RewardState.Sending);
                recovery.ExecuteNonQuery();
                transaction.Commit();
            }
            _writerLock = writer;
            _connection = connection;
            _policy = policy;
        }
        catch { connection?.Dispose(); writer?.Dispose(); throw; }
    }

    public static string ResultIdFor(string matchId, string contentVersion)
    {
        ValidateMatchId(matchId); ValidateContentVersion(contentVersion);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(matchId + "|" + contentVersion)));
    }

    public void Record(CompletedMatch match, string[]? units0 = null, string[]? units1 = null, int? winMasteryXp = null, int? lossMasteryXp = null, string? progressionRulesVersion = null)
    {
        ArgumentNullException.ThrowIfNull(match); ValidateMatch(match);
        if (units0 is not null && winMasteryXp is null && lossMasteryXp is null && progressionRulesVersion is null)
        {
            // Compatibility for callers written before participant progression was introduced.
            winMasteryXp = 0; lossMasteryXp = 0; progressionRulesVersion = "legacy";
        }
        ValidateParticipants(match, units0, units1, winMasteryXp, lossMasteryXp, progressionRulesVersion);
        lock (_sync)
        {
            ThrowIfDisposed();
            using var transaction = _connection.BeginTransaction();
            var existing = ReadResult(match.ResultId, transaction);
            var byMatch = ReadResultByMatchId(match.MatchId, transaction);
            if (existing is not null || byMatch is not null)
            {
                if (existing == match && byMatch == match) { VerifyParticipants(transaction, match, units0, units1, winMasteryXp, lossMasteryXp, progressionRulesVersion); transaction.Commit(); return; }
                throw new InvalidOperationException("Result id or match id conflicts with an immutable result.");
            }
            if (CountResults(transaction) >= MaxResults) throw new InvalidOperationException("Settlement result limit reached.");
            using (var insert = _connection.CreateCommand())
            {
                insert.Transaction = transaction;
                insert.CommandText = "INSERT INTO Results(ResultId,MatchId,ContentVersion,Account0,Account1,Wins0,Wins1,Winner,FinalRevision) VALUES($r,$m,$v,$a0,$a1,$w0,$w1,$winner,$revision);";
                insert.Parameters.AddWithValue("$r", match.ResultId); insert.Parameters.AddWithValue("$m", match.MatchId); insert.Parameters.AddWithValue("$v", match.ContentVersion);
                insert.Parameters.AddWithValue("$a0", match.Account0); insert.Parameters.AddWithValue("$a1", (object?)match.Account1 ?? DBNull.Value); insert.Parameters.AddWithValue("$w0", match.Wins0); insert.Parameters.AddWithValue("$w1", match.Wins1); insert.Parameters.AddWithValue("$winner", match.Winner); insert.Parameters.AddWithValue("$revision", match.FinalRevision);
                insert.ExecuteNonQuery();
            }
            InsertReward(transaction, match.ResultId, match.Account0, match.Winner == 0 ? _policy.WinAmount : _policy.LossAmount);
            if (match.Account1 is not null) InsertReward(transaction, match.ResultId, match.Account1, match.Winner == 1 ? _policy.WinAmount : _policy.LossAmount);
            InsertParticipants(transaction, match, units0, units1, winMasteryXp ?? 0, lossMasteryXp ?? 0, progressionRulesVersion ?? "legacy");
            transaction.Commit();
        }
    }

    public IReadOnlyList<CompletedMatch> ReadResults(string accountId, int limit = 20)
    {
        ValidateAccount(accountId); ValidateReadLimit(limit, 100);
        lock (_sync)
        {
            ThrowIfDisposed(); using var command = _connection.CreateCommand();
            command.CommandText = "SELECT ResultId,MatchId,ContentVersion,Account0,Account1,Wins0,Wins1,Winner,FinalRevision FROM Results WHERE Account0=$account OR Account1=$account ORDER BY rowid DESC LIMIT $limit;";
            command.Parameters.AddWithValue("$account", accountId); command.Parameters.AddWithValue("$limit", limit);
            using var reader = command.ExecuteReader(); var results = new List<CompletedMatch>();
            while (reader.Read()) results.Add(ReadMatch(reader)); return results.AsReadOnly();
        }
    }

    public IReadOnlyList<RewardReceipt> ReadRewards(string accountId)
    {
        ValidateAccount(accountId); lock (_sync) { ThrowIfDisposed(); return ReadRewardsCore("WHERE AccountId=$account ORDER BY ResultId", command => command.Parameters.AddWithValue("$account", accountId)); }
    }
    public IReadOnlyList<RewardReceipt> ReadPending(int limit = 16)
    {
        ValidateReadLimit(limit, 16);
        lock (_sync)
        {
            ThrowIfDisposed();
            return ReadRewardsCore(
                "WHERE State=$state AND NOT EXISTS (SELECT 1 FROM Rewards q WHERE q.AccountId=Rewards.AccountId AND q.State IN ($sending,$review)) ORDER BY ResultId LIMIT $limit",
                command =>
                {
                    command.Parameters.AddWithValue("$state", (int)RewardState.Pending);
                    command.Parameters.AddWithValue("$sending", (int)RewardState.Sending);
                    command.Parameters.AddWithValue("$review", (int)RewardState.NeedsReview);
                    command.Parameters.AddWithValue("$limit", limit);
                });
        }
    }

    public bool TryBegin(RewardGrant grant)
    {
        ValidateGrant(grant); lock (_sync)
        {
            ThrowIfDisposed(); using var transaction = _connection.BeginTransaction(); EnsureExactGrant(grant, transaction);
            using var command = _connection.CreateCommand(); command.Transaction = transaction;
            command.CommandText = "UPDATE Rewards SET State=$sending WHERE ResultId=$result AND AccountId=$account AND State=$pending AND NOT EXISTS(SELECT 1 FROM Rewards q WHERE q.AccountId=$account AND q.ResultId<>$result AND q.State IN ($sending,$review));";
            command.Parameters.AddWithValue("$sending", (int)RewardState.Sending); command.Parameters.AddWithValue("$pending", (int)RewardState.Pending); command.Parameters.AddWithValue("$review", (int)RewardState.NeedsReview); command.Parameters.AddWithValue("$result", grant.ResultId); command.Parameters.AddWithValue("$account", grant.AccountId);
            var started = command.ExecuteNonQuery() == 1; transaction.Commit(); return started;
        }
    }
    public void MarkApplied(RewardGrant grant) => Mark(grant, RewardState.Applied, null);
    public void MarkNeedsReview(RewardGrant grant, string safeReason)
    {
        if (string.IsNullOrWhiteSpace(safeReason) || safeReason.Length > 64 || !SafeTextPattern.IsMatch(safeReason)) throw new ArgumentException("Reason is unsafe.", nameof(safeReason));
        Mark(grant, RewardState.NeedsReview, safeReason);
    }
    public bool ResolveReview(RewardGrant grant, ReviewResolution resolution, string evidenceReference)
    {
        ValidateGrant(grant); ValidateResolution(resolution, evidenceReference);
        lock (_sync)
        {
            ThrowIfDisposed(); using var transaction = _connection.BeginTransaction(); EnsureExactGrant(grant, transaction);
            var existing = ReadReviewResolutions(grant, transaction);
            if (existing.Any(x => x.Resolution == resolution && string.Equals(x.EvidenceReference, evidenceReference, StringComparison.Ordinal))) { transaction.Commit(); return false; }
            var current = ReadReward(grant, transaction);
            if (current.State != RewardState.NeedsReview) throw new InvalidOperationException("Only a needs-review reward can be resolved.");
            if (resolution == ReviewResolution.CompensateOnce && existing.Any(x => x.Resolution == ReviewResolution.CompensateOnce)) throw new InvalidOperationException("Compensation has already been authorized for this reward.");
            if (existing.Count != 0 && !(resolution == ReviewResolution.ConfirmApplied && existing.Any(x => x.Resolution == ReviewResolution.CompensateOnce))) throw new InvalidOperationException("Reward review was already resolved.");
            var reason = resolution == ReviewResolution.ConfirmApplied ? VerifiedExternalReason : CompensationAuthorizedReason;
            using (var audit = _connection.CreateCommand())
            {
                audit.Transaction = transaction;
                audit.CommandText = "INSERT INTO ReviewResolutions(ResultId,AccountId,Resolution,EvidenceReference,OriginalReason,ResolvedAtUtc) VALUES($result,$account,$resolution,$evidence,$reason,$resolved);";
                audit.Parameters.AddWithValue("$result", grant.ResultId); audit.Parameters.AddWithValue("$account", grant.AccountId); audit.Parameters.AddWithValue("$resolution", (int)resolution); audit.Parameters.AddWithValue("$evidence", evidenceReference); audit.Parameters.AddWithValue("$reason", (object?)current.Reason ?? DBNull.Value); audit.Parameters.AddWithValue("$resolved", DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture)); audit.ExecuteNonQuery();
            }
            using (var update = _connection.CreateCommand())
            {
                update.Transaction = transaction;
                update.CommandText = "UPDATE Rewards SET State=$state, Reason=$reason WHERE ResultId=$result AND AccountId=$account AND State=$review;";
                update.Parameters.AddWithValue("$state", (int)(resolution == ReviewResolution.ConfirmApplied ? RewardState.Applied : RewardState.Pending)); update.Parameters.AddWithValue("$reason", reason); update.Parameters.AddWithValue("$result", grant.ResultId); update.Parameters.AddWithValue("$account", grant.AccountId); update.Parameters.AddWithValue("$review", (int)RewardState.NeedsReview);
                if (update.ExecuteNonQuery() != 1) throw new InvalidOperationException("Reward is not in needs-review state.");
            }
            transaction.Commit(); return true;
        }
    }
    public IReadOnlyList<ReviewResolutionRecord> ReadReviewResolutions(RewardGrant grant)
    {
        ValidateGrant(grant); lock (_sync) { ThrowIfDisposed(); return ReadReviewResolutionsCore("WHERE ReviewResolutions.ResultId=$result AND ReviewResolutions.AccountId=$account ORDER BY ReviewResolutions.Id", command => { command.Parameters.AddWithValue("$result", grant.ResultId); command.Parameters.AddWithValue("$account", grant.AccountId); }); }
    }
    public IReadOnlyList<ReviewResolutionRecord> ReadReviewResolutions(string accountId)
    {
        ValidateAccount(accountId); lock (_sync) { ThrowIfDisposed(); return ReadReviewResolutionsCore("WHERE ReviewResolutions.AccountId=$account ORDER BY ReviewResolutions.Id", command => command.Parameters.AddWithValue("$account", accountId)); }
    }
    public void Dispose() { lock (_sync) { if (_disposed) return; _disposed = true; _connection.Dispose(); _writerLock.Dispose(); } }

    private void Mark(RewardGrant grant, RewardState state, string? reason)
    {
        ValidateGrant(grant); lock (_sync) { ThrowIfDisposed(); using var transaction = _connection.BeginTransaction(); EnsureExactGrant(grant, transaction); using var command = _connection.CreateCommand(); command.Transaction = transaction; command.CommandText = "UPDATE Rewards SET State=$state, Reason=CASE WHEN $state=$applied AND Reason=$compensation THEN Reason ELSE $reason END WHERE ResultId=$result AND AccountId=$account AND State=$sending;"; command.Parameters.AddWithValue("$state", (int)state); command.Parameters.AddWithValue("$applied", (int)RewardState.Applied); command.Parameters.AddWithValue("$compensation", CompensationAuthorizedReason); command.Parameters.AddWithValue("$sending", (int)RewardState.Sending); command.Parameters.AddWithValue("$reason", (object?)reason ?? DBNull.Value); command.Parameters.AddWithValue("$result", grant.ResultId); command.Parameters.AddWithValue("$account", grant.AccountId); if (command.ExecuteNonQuery() != 1) throw new InvalidOperationException("Reward is not in sending state."); transaction.Commit(); }
    }
    private void InsertReward(SqliteTransaction transaction, string resultId, string accountId, int amount)
    {
        var capped = CountActiveRewards(accountId, transaction) >= _policy.MaximumGrantsPerAccount;
        using var command = _connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "INSERT INTO Rewards(ResultId,AccountId,Currency,Amount,State,Reason) VALUES($result,$account,$currency,$amount,$state,$reason);";
        command.Parameters.AddWithValue("$result", resultId); command.Parameters.AddWithValue("$account", accountId); command.Parameters.AddWithValue("$currency", _policy.Currency); command.Parameters.AddWithValue("$amount", amount); command.Parameters.AddWithValue("$state", (int)(capped ? RewardState.Skipped : RewardState.Pending)); command.Parameters.AddWithValue("$reason", capped ? "qa_reward_cap" : DBNull.Value); command.ExecuteNonQuery();
    }
    private IReadOnlyList<RewardReceipt> ReadRewardsCore(string where, Action<SqliteCommand> bind)
    {
        using var command = _connection.CreateCommand(); command.CommandText = "SELECT ResultId,AccountId,Currency,Amount,State,Reason FROM Rewards " + where + ";"; bind(command); using var reader = command.ExecuteReader(); var values = new List<RewardReceipt>(); while (reader.Read()) { var state = reader.GetInt32(4); if (state is < 0 or > 4) throw new InvalidDataException("Reward state is malformed."); values.Add(new RewardReceipt(new RewardGrant(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3)), (RewardState)state, reader.IsDBNull(5) ? null : reader.GetString(5))); } return values.AsReadOnly();
    }
    private RewardReceipt ReadReward(RewardGrant grant, SqliteTransaction transaction)
    {
        using var command = _connection.CreateCommand(); command.Transaction = transaction; command.CommandText = "SELECT State,Reason FROM Rewards WHERE ResultId=$result AND AccountId=$account;"; command.Parameters.AddWithValue("$result", grant.ResultId); command.Parameters.AddWithValue("$account", grant.AccountId); using var reader = command.ExecuteReader(); if (!reader.Read()) throw new InvalidOperationException("Reward grant does not match the stored obligation."); return new RewardReceipt(grant, (RewardState)reader.GetInt32(0), reader.IsDBNull(1) ? null : reader.GetString(1));
    }
    private IReadOnlyList<ReviewResolutionRecord> ReadReviewResolutions(RewardGrant grant, SqliteTransaction transaction)
    {
        using var command = _connection.CreateCommand(); command.Transaction = transaction; command.CommandText = "SELECT ResultId,Resolution,EvidenceReference,OriginalReason,ResolvedAtUtc FROM ReviewResolutions WHERE ResultId=$result AND AccountId=$account ORDER BY Id;"; command.Parameters.AddWithValue("$result", grant.ResultId); command.Parameters.AddWithValue("$account", grant.AccountId); using var reader = command.ExecuteReader(); var records = new List<ReviewResolutionRecord>(); while (reader.Read()) records.Add(new ReviewResolutionRecord(reader.GetString(0), (ReviewResolution)reader.GetInt32(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3), DateTimeOffset.Parse(reader.GetString(4), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind))); return records;
    }
    private IReadOnlyList<ReviewResolutionRecord> ReadReviewResolutionsCore(string where, Action<SqliteCommand> bind)
    {
        using var command = _connection.CreateCommand(); command.CommandText = "SELECT ReviewResolutions.ResultId,Resolution,EvidenceReference,OriginalReason,ResolvedAtUtc FROM ReviewResolutions " + where + ";"; bind(command); using var reader = command.ExecuteReader(); var records = new List<ReviewResolutionRecord>(); while (reader.Read()) records.Add(new ReviewResolutionRecord(reader.GetString(0), (ReviewResolution)reader.GetInt32(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3), DateTimeOffset.Parse(reader.GetString(4), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind))); return records.AsReadOnly();
    }
    private void EnsureExactGrant(RewardGrant grant, SqliteTransaction transaction)
    { using var command = _connection.CreateCommand(); command.Transaction = transaction; command.CommandText = "SELECT Currency,Amount FROM Rewards WHERE ResultId=$result AND AccountId=$account;"; command.Parameters.AddWithValue("$result", grant.ResultId); command.Parameters.AddWithValue("$account", grant.AccountId); using var reader = command.ExecuteReader(); if (!reader.Read() || !string.Equals(reader.GetString(0), grant.Currency, StringComparison.Ordinal) || reader.GetInt32(1) != grant.Amount) throw new InvalidOperationException("Reward grant does not match the stored obligation."); }
    private int CountResults(SqliteTransaction transaction) { using var command = _connection.CreateCommand(); command.Transaction = transaction; command.CommandText = "SELECT COUNT(*) FROM Results;"; return Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture); }
    private int CountActiveRewards(string account, SqliteTransaction transaction) { using var command = _connection.CreateCommand(); command.Transaction = transaction; command.CommandText = "SELECT COUNT(*) FROM Rewards WHERE AccountId=$account AND State<>$skipped;"; command.Parameters.AddWithValue("$account", account); command.Parameters.AddWithValue("$skipped", (int)RewardState.Skipped); return Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture); }
    private CompletedMatch? ReadResult(string resultId, SqliteTransaction transaction) => ReadResult("ResultId=$value", resultId, transaction);
    private CompletedMatch? ReadResultByMatchId(string matchId, SqliteTransaction transaction) => ReadResult("MatchId=$value", matchId, transaction);
    private CompletedMatch? ReadResult(string clause, string value, SqliteTransaction transaction) { using var command = _connection.CreateCommand(); command.Transaction = transaction; command.CommandText = "SELECT ResultId,MatchId,ContentVersion,Account0,Account1,Wins0,Wins1,Winner,FinalRevision FROM Results WHERE " + clause + ";"; command.Parameters.AddWithValue("$value", value); using var reader = command.ExecuteReader(); return reader.Read() ? ReadMatch(reader) : null; }
    private static CompletedMatch ReadMatch(SqliteDataReader reader) => new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetInt32(5), reader.GetInt32(6), reader.GetInt32(7), reader.GetInt64(8));
    private static void Configure(SqliteConnection connection) { using var command = connection.CreateCommand(); command.CommandText = $"PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL; PRAGMA foreign_keys=ON; PRAGMA max_page_count={MaxPages};"; command.ExecuteNonQuery(); }
    private static void CreateSchema(SqliteConnection connection) { using var command = connection.CreateCommand(); command.CommandText = "CREATE TABLE IF NOT EXISTS Metadata(Key TEXT PRIMARY KEY, Value TEXT NOT NULL); CREATE TABLE IF NOT EXISTS Results(ResultId TEXT PRIMARY KEY,MatchId TEXT NOT NULL UNIQUE,ContentVersion TEXT NOT NULL,Account0 TEXT NOT NULL,Account1 TEXT NULL,Wins0 INTEGER NOT NULL,Wins1 INTEGER NOT NULL,Winner INTEGER NOT NULL,FinalRevision INTEGER NOT NULL); CREATE TABLE IF NOT EXISTS Rewards(ResultId TEXT NOT NULL,AccountId TEXT NOT NULL,Currency TEXT NOT NULL,Amount INTEGER NOT NULL,State INTEGER NOT NULL,Reason TEXT NULL,PRIMARY KEY(ResultId,AccountId),FOREIGN KEY(ResultId) REFERENCES Results(ResultId)); CREATE TABLE IF NOT EXISTS ReviewResolutions(Id INTEGER PRIMARY KEY AUTOINCREMENT,ResultId TEXT NOT NULL,AccountId TEXT NOT NULL,Resolution INTEGER NOT NULL,EvidenceReference TEXT NOT NULL,OriginalReason TEXT NULL,ResolvedAtUtc TEXT NOT NULL,FOREIGN KEY(ResultId,AccountId) REFERENCES Rewards(ResultId,AccountId)); CREATE INDEX IF NOT EXISTS IX_Rewards_Account ON Rewards(AccountId); CREATE INDEX IF NOT EXISTS IX_ReviewResolutions_Account ON ReviewResolutions(AccountId,Id);"; command.ExecuteNonQuery(); }
    private static void ValidateExistingSchema(SqliteConnection connection) { using var command = connection.CreateCommand(); command.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name IN ('Metadata','Results','Rewards');"; using var reader = command.ExecuteReader(); var tables = new HashSet<string>(StringComparer.Ordinal); while (reader.Read()) tables.Add(reader.GetString(0)); if (tables.Count != 3) throw new InvalidDataException("An existing settlement database has an incomplete schema."); }
    private static RewardPolicy? ReadPolicy(SqliteConnection connection) { using var command = connection.CreateCommand(); command.CommandText = "SELECT Key,Value FROM Metadata WHERE Key IN ('schema','policy');"; using var reader = command.ExecuteReader(); string? schema = null; string? value = null; while (reader.Read()) { if (reader.GetString(0) == "schema") schema = reader.GetString(1); else value = reader.GetString(1); } if (schema is null && value is null) return null; if (!string.Equals(schema, Schema.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal) || value is null) throw new InvalidDataException("Settlement metadata is malformed."); var fields = value.Split('|'); if (fields.Length != 5 || !int.TryParse(fields[2], out var win) || !int.TryParse(fields[3], out var loss) || !int.TryParse(fields[4], out var max)) throw new InvalidDataException("Settlement metadata is malformed."); var policy = new RewardPolicy(fields[0], fields[1], win, loss, max); ValidatePolicy(policy); return policy; }
    private static void InsertPolicy(SqliteConnection connection, SqliteTransaction transaction, RewardPolicy policy) { using var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = "INSERT INTO Metadata(Key,Value) VALUES('schema',$schema),('policy',$policy);"; command.Parameters.AddWithValue("$schema", Schema.ToString(System.Globalization.CultureInfo.InvariantCulture)); command.Parameters.AddWithValue("$policy", string.Join('|', policy.Version, policy.Currency, policy.WinAmount, policy.LossAmount, policy.MaximumGrantsPerAccount)); command.ExecuteNonQuery(); }
    private static void VerifyPersisted(SqliteConnection connection, RewardPolicy policy)
    {
        var expected = new Dictionary<(string ResultId, string AccountId), int>();
        var resultCount = 0;
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT ResultId,MatchId,ContentVersion,Account0,Account1,Wins0,Wins1,Winner,FinalRevision FROM Results ORDER BY ResultId;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (++resultCount > MaxResults) throw new InvalidDataException("Settlement result limit is exceeded.");
                var match = ReadMatch(reader);
                try { ValidateMatch(match); }
                catch (ArgumentException exception) { throw new InvalidDataException("Persisted match is malformed.", exception); }
                expected.Add((match.ResultId, match.Account0), match.Winner == 0 ? policy.WinAmount : policy.LossAmount);
                if (match.Account1 is not null)
                    expected.Add((match.ResultId, match.Account1), match.Winner == 1 ? policy.WinAmount : policy.LossAmount);
            }
        }

        var activeByAccount = new Dictionary<string, int>(StringComparer.Ordinal);
        var persistedRewards = new Dictionary<(string ResultId, string AccountId), (RewardState State, string? Reason)>();
        var actual = 0;
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT ResultId,AccountId,Currency,Amount,State,Reason FROM Rewards ORDER BY ResultId,AccountId;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (++actual > MaxResults * 2) throw new InvalidDataException("Settlement reward limit is exceeded.");
                var resultId = reader.GetString(0);
                var accountId = reader.GetString(1);
                var currency = reader.GetString(2);
                var amount = reader.GetInt32(3);
                var stateValue = reader.GetInt32(4);
                var reason = reader.IsDBNull(5) ? null : reader.GetString(5);
                if (!expected.Remove((resultId, accountId), out var expectedAmount)
                    || !string.Equals(currency, policy.Currency, StringComparison.Ordinal) || amount != expectedAmount)
                    throw new InvalidDataException("Persisted reward does not match a result obligation.");
                if (stateValue is < (int)RewardState.Pending or > (int)RewardState.Skipped)
                    throw new InvalidDataException("Persisted reward state is malformed.");
                var state = (RewardState)stateValue;
                if (state == RewardState.Skipped)
                {
                    if (!string.Equals(reason, "qa_reward_cap", StringComparison.Ordinal)) throw new InvalidDataException("Skipped reward reason is malformed.");
                }
                else
                {
                    if (state == RewardState.NeedsReview)
                    {
                        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 64 || !SafeTextPattern.IsMatch(reason)) throw new InvalidDataException("Needs-review reason is malformed.");
                    }
                    else if (reason is not null && !(state == RewardState.Pending && reason == CompensationAuthorizedReason) && !(state == RewardState.Sending && reason == CompensationAuthorizedReason) && !(state == RewardState.Applied && (reason == CompensationAuthorizedReason || reason == VerifiedExternalReason))) throw new InvalidDataException("Persisted reward reason is malformed.");
                    activeByAccount.TryGetValue(accountId, out var count);
                    if (count >= policy.MaximumGrantsPerAccount) throw new InvalidDataException("Persisted reward cap is exceeded.");
                    activeByAccount[accountId] = count + 1;
                }
                persistedRewards.Add((resultId, accountId), (state, reason));
            }
        }
        if (expected.Count != 0) throw new InvalidDataException("Persisted result obligation is missing.");
        VerifyReviewResolutions(connection, persistedRewards);
    }
    private static void VerifyReviewResolutions(SqliteConnection connection, IReadOnlyDictionary<(string ResultId, string AccountId), (RewardState State, string? Reason)> persistedRewards)
    {
        var entries = new Dictionary<(string ResultId, string AccountId), List<ReviewResolution>>();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT ResultId,AccountId,Resolution,EvidenceReference,OriginalReason,ResolvedAtUtc FROM ReviewResolutions ORDER BY Id;";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var resultId = reader.GetString(0); var accountId = reader.GetString(1); var value = reader.GetInt32(2); var evidence = reader.GetString(3); var originalReason = reader.IsDBNull(4) ? null : reader.GetString(4); var timestamp = reader.GetString(5);
            if (value is < (int)ReviewResolution.ConfirmApplied or > (int)ReviewResolution.CompensateOnce || string.IsNullOrWhiteSpace(evidence) || evidence.Length > MaxEvidenceReferenceLength || !EvidenceReferencePattern.IsMatch(evidence) || string.IsNullOrWhiteSpace(originalReason) || originalReason.Length > 64 || !SafeTextPattern.IsMatch(originalReason) || !DateTimeOffset.TryParse(timestamp, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed) || parsed.Offset != TimeSpan.Zero)
                throw new InvalidDataException("Persisted review resolution is malformed.");
            var key = (resultId, accountId);
            if (!persistedRewards.ContainsKey(key)) throw new InvalidDataException("Persisted review resolution has no reward.");
            if (!entries.TryGetValue(key, out var resolutions)) { resolutions = []; entries.Add(key, resolutions); }
            if (resolutions.Count >= 2 || (resolutions.Count == 1 && !(resolutions[0] == ReviewResolution.CompensateOnce && value == (int)ReviewResolution.ConfirmApplied)) || (value == (int)ReviewResolution.CompensateOnce && resolutions.Contains(ReviewResolution.CompensateOnce))) throw new InvalidDataException("Persisted review resolution sequence is malformed.");
            resolutions.Add((ReviewResolution)value);
        }
        foreach (var (key, resolutions) in entries)
        {
            var reward = persistedRewards[key];
            if (resolutions[^1] == ReviewResolution.ConfirmApplied)
            {
                if (reward.State != RewardState.Applied || reward.Reason != VerifiedExternalReason) throw new InvalidDataException("Persisted confirmation does not match reward state.");
            }
            else if (!((reward.State is RewardState.Pending or RewardState.Sending or RewardState.Applied) && reward.Reason == CompensationAuthorizedReason) && !(reward.State == RewardState.NeedsReview && !string.IsNullOrWhiteSpace(reward.Reason) && reward.Reason.Length <= 64 && SafeTextPattern.IsMatch(reward.Reason)))
                throw new InvalidDataException("Persisted compensation does not match reward state.");
        }
        foreach (var (key, reward) in persistedRewards)
        {
            if (reward.Reason == VerifiedExternalReason && (!entries.TryGetValue(key, out var matchingResolutions) || matchingResolutions[^1] != ReviewResolution.ConfirmApplied)) throw new InvalidDataException("Verified reward is missing its review audit.");
            if (reward.Reason == CompensationAuthorizedReason && (!entries.TryGetValue(key, out var matchingCompensations) || matchingCompensations[^1] != ReviewResolution.CompensateOnce)) throw new InvalidDataException("Compensated reward is missing its review audit.");
        }
    }
    private static void ValidateMatch(CompletedMatch match) { if (!string.Equals(match.ResultId, ResultIdFor(match.MatchId, match.ContentVersion), StringComparison.Ordinal)) throw new ArgumentException("Result id is not the expected match hash.", nameof(match)); ValidateAccount(match.Account0); if (match.Account1 is not null) { ValidateAccount(match.Account1); if (match.Account1 == match.Account0) throw new ArgumentException("Match participants must be distinct.", nameof(match)); } if (match.Winner is < 0 or > 1 || match.FinalRevision < 0 || match.Wins0 is < 0 or > 4 || match.Wins1 is < 0 or > 4 || (match.Winner == 0 && (match.Wins0 != 4 || match.Wins1 > 3)) || (match.Winner == 1 && (match.Wins1 != 4 || match.Wins0 > 3))) throw new ArgumentException("Match score is invalid.", nameof(match)); }
    private static void ValidateGrant(RewardGrant grant) { ArgumentNullException.ThrowIfNull(grant); if (!IsHash(grant.ResultId)) throw new ArgumentException("Result id is invalid.", nameof(grant)); ValidateAccount(grant.AccountId); if (!IsCurrency(grant.Currency) || grant.Amount is < 1 or > 100) throw new ArgumentException("Reward grant is invalid.", nameof(grant)); }
    private static void ValidateResolution(ReviewResolution resolution, string evidenceReference) { if (resolution is < ReviewResolution.ConfirmApplied or > ReviewResolution.CompensateOnce || string.IsNullOrWhiteSpace(evidenceReference) || evidenceReference.Length > MaxEvidenceReferenceLength || !EvidenceReferencePattern.IsMatch(evidenceReference)) throw new ArgumentException("Review resolution is invalid.", nameof(evidenceReference)); }
    private static void ValidatePolicy(RewardPolicy policy) { ArgumentNullException.ThrowIfNull(policy); if (string.IsNullOrWhiteSpace(policy.Version) || policy.Version.Length > 64 || !SafeTextPattern.IsMatch(policy.Version) || !IsCurrency(policy.Currency) || policy.WinAmount is < 1 or > 100 || policy.LossAmount is < 1 or > 100 || policy.MaximumGrantsPerAccount is < 1 or > 100) throw new ArgumentException("Reward policy is invalid.", nameof(policy)); }
    private static void ValidateAccount(string account) { if (account is null || !AccountPattern.IsMatch(account)) throw new ArgumentException("Account id is invalid.", nameof(account)); }
    private static void ValidateMatchId(string match) { if (string.IsNullOrWhiteSpace(match) || match.Length > 128 || !SafeTextPattern.IsMatch(match)) throw new ArgumentException("Match id is invalid.", nameof(match)); }
    private static void ValidateContentVersion(string version) { if (string.IsNullOrWhiteSpace(version) || version.Length > 128 || !SafeTextPattern.IsMatch(version)) throw new ArgumentException("Content version is invalid.", nameof(version)); }
    private static bool IsHash(string value) { if (value.Length != 64) return false; try { return Convert.FromHexString(value).Length == 32; } catch (FormatException) { return false; } }
    private static bool IsCurrency(string value) => value.Length == 2 && value.All(static c => c is >= 'A' and <= 'Z');
    private static void ValidateReadLimit(int limit, int maximum) { if (limit < 1 || limit > maximum) throw new ArgumentOutOfRangeException(nameof(limit)); }
    private static string ValidatePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) throw new ArgumentException("Database path must be absolute.", nameof(path));
        var full = Path.GetFullPath(path);
        if (full.StartsWith("\\\\", StringComparison.Ordinal) || new DriveInfo(Path.GetPathRoot(full)!).DriveType == DriveType.Network) throw new ArgumentException("Network database paths are unsupported.", nameof(path));
        for (DirectoryInfo? current = new FileInfo(full).Directory; current is not null; current = current.Parent)
            if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0) throw new ArgumentException("Reparse-point database paths are unsupported.", nameof(path));
        RejectReparsePoint(full, path);
        RejectReparsePoint(full + "-wal", path);
        RejectReparsePoint(full + "-shm", path);
        RejectReparsePoint(full + ".writer.lock", path);
        return full;
    }
    private static void RejectReparsePoint(string candidate, string parameterName)
    {
        if (File.Exists(candidate) && (File.GetAttributes(candidate) & FileAttributes.ReparsePoint) != 0)
            throw new ArgumentException("Reparse-point database paths are unsupported.", parameterName);
    }
    private void ThrowIfDisposed() { if (_disposed) throw new ObjectDisposedException(nameof(SqliteSettlementStore)); }
}

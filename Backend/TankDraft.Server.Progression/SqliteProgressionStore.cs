using System.Collections.Immutable;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace TankDraft.Server.Progression;

public sealed class ProgressionConflictException : Exception { public ProgressionConflictException() : base("progression_conflict") { } }
internal sealed record JournalOperation(string AccountId, Guid OperationId, string Fingerprint, long Sequence, ProgressionState PreparedState, string Currency, int WalletDelta, ProgressionOperationStatus Status, string ResolvedJson);

public sealed class SqliteProgressionStore : IProgressionStore, IDisposable
{
    static readonly Regex Account = new("^[A-Za-z0-9]{5,32}$", RegexOptions.CultureInvariant);
    readonly object sync = new(); readonly FileStream writerLock; readonly SqliteConnection connection; bool disposed;
    static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = false, MaxDepth = 16, UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow };
    public SqliteProgressionStore(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath) || !Path.IsPathFullyQualified(databasePath)) throw new ArgumentException("Absolute database path required.", nameof(databasePath));
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        writerLock = new FileStream(databasePath + ".writer.lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        try
        {
            connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false }.ToString()); connection.Open();
            using var command = connection.CreateCommand(); command.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL; PRAGMA max_page_count=32768; CREATE TABLE IF NOT EXISTS Progression(Account TEXT PRIMARY KEY, Version INTEGER NOT NULL, State TEXT NOT NULL); CREATE TABLE IF NOT EXISTS Operations(Account TEXT NOT NULL, OperationId TEXT NOT NULL, Fingerprint TEXT NOT NULL, Sequence INTEGER NOT NULL, PreparedState TEXT NOT NULL, Currency TEXT NOT NULL, WalletDelta INTEGER NOT NULL, Status INTEGER NOT NULL, Resolved TEXT NOT NULL, PRIMARY KEY(Account, OperationId)); CREATE UNIQUE INDEX IF NOT EXISTS IX_OperationSequence ON Operations(Account,Sequence); CREATE TABLE IF NOT EXISTS ReviewAudit(Account TEXT NOT NULL,OperationId TEXT NOT NULL,Action INTEGER NOT NULL,Evidence TEXT NOT NULL,CreatedAtUtc TEXT NOT NULL,PRIMARY KEY(Account,OperationId,Action)); UPDATE Operations SET Status=4 WHERE Status=2;"; command.ExecuteNonQuery();
        }
        catch { connection?.Dispose(); writerLock.Dispose(); throw; }
    }
    public Task<StoredProgression> ReadAsync(string accountId, CancellationToken ct) { ct.ThrowIfCancellationRequested(); lock (sync) { Check(); ValidateAccount(accountId); using var c = connection.CreateCommand(); c.CommandText = "SELECT Version,State FROM Progression WHERE Account=$a"; c.Parameters.AddWithValue("$a", accountId); using var r = c.ExecuteReader(); return Task.FromResult(r.Read() ? new StoredProgression(r.GetInt32(0), Deserialize(r.GetString(1))) : new StoredProgression(0, null)); } }
    public Task WriteAsync(string accountId, ProgressionState state, int expectedVersion, CancellationToken ct) { ct.ThrowIfCancellationRequested(); lock (sync) { Check(); ValidateAccount(accountId); if (expectedVersion < 0) throw new ArgumentOutOfRangeException(nameof(expectedVersion)); using var tx = connection.BeginTransaction(); var current = Version(accountId, tx); if (current != expectedVersion) throw new ProgressionConflictException(); using var c = connection.CreateCommand(); c.Transaction = tx; c.CommandText = "INSERT INTO Progression(Account,Version,State) VALUES($a,$v,$s) ON CONFLICT(Account) DO UPDATE SET Version=$v,State=$s"; c.Parameters.AddWithValue("$a", accountId); c.Parameters.AddWithValue("$v", expectedVersion + 1); c.Parameters.AddWithValue("$s", Serialize(state)); c.ExecuteNonQuery(); tx.Commit(); return Task.CompletedTask; } }
    internal JournalOperation? ReadOperation(string account, Guid id) { lock (sync) { Check(); using var c = connection.CreateCommand(); c.CommandText = "SELECT Fingerprint,Sequence,PreparedState,Currency,WalletDelta,Status,Resolved FROM Operations WHERE Account=$a AND OperationId=$i"; c.Parameters.AddWithValue("$a", account); c.Parameters.AddWithValue("$i", id.ToString("N")); using var r = c.ExecuteReader(); return r.Read() ? new JournalOperation(account, id, r.GetString(0), r.GetInt64(1), Deserialize(r.GetString(2)), r.GetString(3), r.GetInt32(4), (ProgressionOperationStatus)r.GetInt32(5), r.GetString(6)) : null; } }
    internal IReadOnlyList<JournalOperation> Pending() { lock (sync) { Check(); using var c = connection.CreateCommand(); c.CommandText = "SELECT Account,OperationId,Fingerprint,Sequence,PreparedState,Currency,WalletDelta,Status,Resolved FROM Operations WHERE Status<>$s ORDER BY Account,Sequence LIMIT 10000"; c.Parameters.AddWithValue("$s", (int)ProgressionOperationStatus.Completed); using var r = c.ExecuteReader(); var values = new List<JournalOperation>(); while (r.Read()) values.Add(new JournalOperation(r.GetString(0), Guid.ParseExact(r.GetString(1), "N"), r.GetString(2), r.GetInt64(3), Deserialize(r.GetString(4)), r.GetString(5), r.GetInt32(6), (ProgressionOperationStatus)r.GetInt32(7), r.GetString(8))); return values; } }
    internal IReadOnlyList<ProgressionReview> ReadReviews()
    {
        lock (sync)
        {
            Check(); using var c = connection.CreateCommand();
            c.CommandText = "SELECT o.Account,o.OperationId,o.Sequence,o.Currency,o.WalletDelta,o.Resolved,EXISTS(SELECT 1 FROM ReviewAudit a WHERE a.Account=o.Account AND a.OperationId=o.OperationId AND a.Action=$compensate) FROM Operations o WHERE o.Status=$review ORDER BY o.Account,o.Sequence LIMIT 10000";
            c.Parameters.AddWithValue("$review", (int)ProgressionOperationStatus.NeedsReview); c.Parameters.AddWithValue("$compensate", (int)ProgressionReviewAction.CompensateCreditOnce);
            using var r = c.ExecuteReader(); var values = new List<ProgressionReview>();
            while (r.Read()) values.Add(new ProgressionReview(r.GetString(0), Guid.ParseExact(r.GetString(1), "N"), r.GetInt64(2), r.GetString(3), r.GetInt32(4), Resolved(r.GetString(5)), r.GetInt32(6) == 1));
            return values;
        }
    }
    internal void Prepare(JournalOperation operation) { lock (sync) { Check(); ValidateAccount(operation.AccountId); if (operation.OperationId == Guid.Empty || Serialize(operation.PreparedState).Length > 16384) throw new InvalidDataException(); using var count = connection.CreateCommand(); count.CommandText="SELECT COUNT(*) FROM Operations"; if(Convert.ToInt64(count.ExecuteScalar())>=10000) throw new InvalidOperationException("progression_capacity"); using var c = connection.CreateCommand(); c.CommandText = "INSERT INTO Operations(Account,OperationId,Fingerprint,Sequence,PreparedState,Currency,WalletDelta,Status,Resolved) VALUES($a,$i,$f,$q,$p,$c,$d,$s,$r)"; Bind(c, operation); c.ExecuteNonQuery(); } }
    internal void SetStatus(string account, Guid id, ProgressionOperationStatus status) { lock (sync) { Check(); using var c = connection.CreateCommand(); c.CommandText = "UPDATE Operations SET Status=$s WHERE Account=$a AND OperationId=$i"; c.Parameters.AddWithValue("$s", (int)status); c.Parameters.AddWithValue("$a", account); c.Parameters.AddWithValue("$i", id.ToString("N")); if (c.ExecuteNonQuery() != 1) throw new InvalidOperationException("Missing progression operation."); } }
    internal void ResolveReview(string account, Guid id, ProgressionReviewAction action, string evidence)
    {
        lock (sync)
        {
            Check(); ValidateAccount(account); using var tx = connection.BeginTransaction();
            EnsureNeedsReview(tx, account, id);
            InsertAudit(tx, account, id, action, evidence);
            UpdateStatus(tx, account, id, ProgressionOperationStatus.Completed);
            tx.Commit();
        }
    }
    internal void BeginCreditCompensation(string account, Guid id, string evidence)
    {
        lock (sync)
        {
            Check(); ValidateAccount(account); using var tx = connection.BeginTransaction();
            var delta = EnsureNeedsReview(tx, account, id);
            if (delta >= 0) throw new InvalidOperationException("Compensation requires a credit operation.");
            using (var exists = connection.CreateCommand())
            {
                exists.Transaction = tx; exists.CommandText = "SELECT COUNT(*) FROM ReviewAudit WHERE Account=$a AND OperationId=$i AND Action=$action";
                exists.Parameters.AddWithValue("$a", account); exists.Parameters.AddWithValue("$i", id.ToString("N")); exists.Parameters.AddWithValue("$action", (int)ProgressionReviewAction.CompensateCreditOnce);
                if (Convert.ToInt64(exists.ExecuteScalar()) != 0) throw new InvalidOperationException("Compensation was already attempted.");
            }
            InsertAudit(tx, account, id, ProgressionReviewAction.CompensateCreditOnce, evidence);
            UpdateStatus(tx, account, id, ProgressionOperationStatus.WalletSending);
            tx.Commit();
        }
    }
    int EnsureNeedsReview(SqliteTransaction tx, string account, Guid id)
    {
        using var c = connection.CreateCommand(); c.Transaction = tx; c.CommandText = "SELECT Status,WalletDelta FROM Operations WHERE Account=$a AND OperationId=$i"; c.Parameters.AddWithValue("$a", account); c.Parameters.AddWithValue("$i", id.ToString("N")); using var r = c.ExecuteReader();
        if (!r.Read()) throw new InvalidOperationException("Missing progression operation.");
        if ((ProgressionOperationStatus)r.GetInt32(0) != ProgressionOperationStatus.NeedsReview) throw new InvalidOperationException("Operation does not require review.");
        return r.GetInt32(1);
    }
    void InsertAudit(SqliteTransaction tx, string account, Guid id, ProgressionReviewAction action, string evidence)
    {
        using var c = connection.CreateCommand(); c.Transaction = tx; c.CommandText = "INSERT INTO ReviewAudit(Account,OperationId,Action,Evidence,CreatedAtUtc) VALUES($a,$i,$action,$e,$at)";
        c.Parameters.AddWithValue("$a", account); c.Parameters.AddWithValue("$i", id.ToString("N")); c.Parameters.AddWithValue("$action", (int)action); c.Parameters.AddWithValue("$e", evidence); c.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture)); c.ExecuteNonQuery();
    }
    void UpdateStatus(SqliteTransaction tx, string account, Guid id, ProgressionOperationStatus status)
    {
        using var c = connection.CreateCommand(); c.Transaction = tx; c.CommandText = "UPDATE Operations SET Status=$s WHERE Account=$a AND OperationId=$i"; c.Parameters.AddWithValue("$s", (int)status); c.Parameters.AddWithValue("$a", account); c.Parameters.AddWithValue("$i", id.ToString("N")); if (c.ExecuteNonQuery() != 1) throw new InvalidOperationException("Missing progression operation.");
    }
    int Version(string account, SqliteTransaction tx) { using var c = connection.CreateCommand(); c.Transaction = tx; c.CommandText = "SELECT Version FROM Progression WHERE Account=$a"; c.Parameters.AddWithValue("$a", account); object? value = c.ExecuteScalar(); return value is null ? 0 : Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture); }
    static void Bind(SqliteCommand c, JournalOperation o) { c.Parameters.AddWithValue("$a", o.AccountId); c.Parameters.AddWithValue("$i", o.OperationId.ToString("N")); c.Parameters.AddWithValue("$f", o.Fingerprint); c.Parameters.AddWithValue("$q", o.Sequence); c.Parameters.AddWithValue("$p", Serialize(o.PreparedState)); c.Parameters.AddWithValue("$c", o.Currency); c.Parameters.AddWithValue("$d", o.WalletDelta); c.Parameters.AddWithValue("$s", (int)o.Status); c.Parameters.AddWithValue("$r", o.ResolvedJson); }
    static string Serialize(ProgressionState value) => JsonSerializer.Serialize(value.Validate(), Json);
    static ProgressionState Deserialize(string value) => (JsonSerializer.Deserialize<ProgressionState>(value, Json) ?? throw new InvalidDataException("Invalid persisted progression.")).Validate();
    static ImmutableArray<string> Resolved(string value) => value.Length == 0 ? ImmutableArray<string>.Empty : value.Split('|').ToImmutableArray();
    static void ValidateAccount(string value) { if (!Account.IsMatch(value ?? "")) throw new ArgumentException("Invalid account."); }
    void Check() { if (disposed) throw new ObjectDisposedException(nameof(SqliteProgressionStore)); }
    public void Dispose() { if (disposed) return; disposed = true; connection.Dispose(); writerLock.Dispose(); }
}

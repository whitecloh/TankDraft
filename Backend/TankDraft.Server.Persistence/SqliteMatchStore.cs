using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using TankDraft.Server.Security;

namespace TankDraft.Server.Persistence;

/// <summary>
/// An exclusive, local SQLite event log.  The file lock and epoch are deliberately
/// local fencing only: a cloud host needs a separate lease implementation.
/// </summary>
public sealed class SqliteMatchStore : IMatchStore
{
    private const int SchemaVersion = 1;
    private const string GenesisHash = "";
    private readonly object _sync = new();
    private readonly StoreLimits _limits;
    private readonly Action<StoreFaultPoint>? _faultHook;
    private readonly FileStream _writerLock;
    private readonly SqliteConnection _connection;
    private bool _disposed;

    public SqliteMatchStore(string databasePath, string recipeJson, DateTimeOffset startedUtc,
        StoreLimits? limits = null, Action<StoreFaultPoint>? faultHook = null)
    {
        if (string.IsNullOrWhiteSpace(recipeJson))
            throw new ArgumentException("A recipe is required.", nameof(recipeJson));
        _limits = limits ?? new StoreLimits();
        ValidateLimits(_limits);
        var fullPath = ValidateDatabasePath(databasePath);
        var databaseAlreadyExisted = File.Exists(fullPath);
        var directory = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(directory);

        FileStream? writerLock = null;
        SqliteConnection? connection = null;
        try
        {
            writerLock = new FileStream(fullPath + ".writer.lock", FileMode.OpenOrCreate, FileAccess.ReadWrite,
                FileShare.None, bufferSize: 1, FileOptions.None);
            connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = fullPath,
                Pooling = false,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Private
            }.ToString());
            connection.Open();
            Configure(connection, _limits);
            InitializeSchema(connection);

            var metadata = ReadMetadata(connection);
            if (metadata is null && !databaseAlreadyExisted)
            {
                using var transaction = connection.BeginTransaction();
                InsertMetadata(connection, transaction, recipeJson, startedUtc);
                transaction.Commit();
                metadata = new Metadata(SchemaVersion, recipeJson, startedUtc, 0, 0, GenesisHash);
            }
            else if (metadata is null)
            {
                throw new InvalidDataException("An existing database is missing its match metadata.");
            }
            else if (metadata.Schema != SchemaVersion || !string.Equals(metadata.RecipeJson, recipeJson, StringComparison.Ordinal))
            {
                throw new InvalidDataException("Persisted match metadata does not match this server recipe.");
            }

            using (var transaction = connection.BeginTransaction())
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = "UPDATE Meta SET Epoch = Epoch + 1 WHERE Id = 1;";
                if (command.ExecuteNonQuery() != 1)
                    throw new InvalidDataException("Persisted match metadata is missing.");
                transaction.Commit();
            }
            metadata = ReadMetadata(connection) ?? throw new InvalidDataException("Persisted match metadata is missing.");
            RecipeJson = metadata.RecipeJson;
            StartedUtc = metadata.StartedUtc;
            Epoch = metadata.Epoch;
            HeadSequence = metadata.Head;
            _writerLock = writerLock;
            _connection = connection;
            _faultHook = faultHook;
        }
        catch
        {
            connection?.Dispose();
            writerLock?.Dispose();
            throw;
        }
    }

    public string RecipeJson { get; }
    public DateTimeOffset StartedUtc { get; }
    public long Epoch { get; private set; }
    public long HeadSequence { get; private set; }

    public IReadOnlyList<StoredTransition> ReadLog()
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            var metadata = ReadMetadata(_connection) ?? throw new InvalidDataException("Persisted match metadata is missing.");
            EnsureCurrentEpoch(metadata);
            var entries = new List<StoredTransition>();
            using var command = _connection.CreateCommand();
            command.CommandText = "SELECT Sequence, Epoch, PreviousHash, Hash, CommitJson FROM Log ORDER BY Sequence;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (entries.Count >= _limits.MaxEntries)
                    throw new InvalidDataException("Persisted log exceeds its configured entry limit.");
                var commitJson = reader.GetString(4);
                EnsureRecordSize(commitJson);
                entries.Add(new StoredTransition(reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2),
                    reader.GetString(3), DecodePersisted<StoreCommit>(commitJson)));
            }
            VerifyLog(entries, metadata);
            VerifyReceipts(entries);
            VerifyOutbox(entries);
            return Array.AsReadOnly(entries.ToArray());
        }
    }

    public DurableReceipt? FindReceipt(string accountId, string operationId)
    {
        ValidateIdentifier(accountId, nameof(accountId));
        ValidateIdentifier(operationId, nameof(operationId));
        lock (_sync)
        {
            ThrowIfDisposed();
            EnsureCurrentEpoch(ReadMetadata(_connection) ?? throw new InvalidDataException("Persisted match metadata is missing."));
            using var command = _connection.CreateCommand();
            command.CommandText = "SELECT Fingerprint, ReplyJson FROM Receipts WHERE AccountId = $accountId AND OperationId = $operationId;";
            command.Parameters.AddWithValue("$accountId", accountId);
            command.Parameters.AddWithValue("$operationId", operationId);
            using var reader = command.ExecuteReader();
            if (!reader.Read()) return null;
            var replyJson = reader.GetString(1);
            EnsureRecordSize(replyJson);
            return new DurableReceipt(reader.GetString(0), DecodePersisted<CommandReply>(replyJson));
        }
    }

    public StoredTransition Commit(long expectedHead, long expectedEpoch, StoreCommit commit)
    {
        ArgumentNullException.ThrowIfNull(commit);
        ValidateCommit(commit);
        lock (_sync)
        {
            ThrowIfDisposed();
            if (expectedHead != HeadSequence || expectedEpoch != Epoch)
                throw new InvalidOperationException("Stale match store head or epoch.");
            _faultHook?.Invoke(StoreFaultPoint.BeforeTransaction);
            using var transaction = _connection.BeginTransaction();
            if (expectedHead >= _limits.MaxEntries)
                throw new InvalidOperationException("Persisted log has reached its configured entry limit.");
            var next = checked(expectedHead + 1);
            var commitJson = PersistenceJson.Encode(commit);
            EnsureRecordSize(commitJson);
            if (commit.LogicalTicks < ReadLastLogicalTicks(transaction))
                throw new InvalidOperationException("Persisted logical time cannot move backwards.");
            var previousHash = ReadHeadHash(transaction);
            var hash = TransitionHash(next, expectedEpoch, previousHash, commit);
            try
            {
                using (var update = _connection.CreateCommand())
                {
                    update.Transaction = transaction;
                    update.CommandText = "UPDATE Meta SET Head = $next, HeadHash = $hash WHERE Id = 1 AND Head = $head AND Epoch = $epoch;";
                    update.Parameters.AddWithValue("$next", next);
                    update.Parameters.AddWithValue("$hash", hash);
                    update.Parameters.AddWithValue("$head", expectedHead);
                    update.Parameters.AddWithValue("$epoch", expectedEpoch);
                    if (update.ExecuteNonQuery() != 1)
                        throw new InvalidOperationException("Stale match store head or epoch.");
                }
                using (var insert = _connection.CreateCommand())
                {
                    insert.Transaction = transaction;
                    insert.CommandText = "INSERT INTO Log(Sequence, Epoch, PreviousHash, Hash, CommitJson) VALUES($sequence, $epoch, $previousHash, $hash, $commitJson);";
                    insert.Parameters.AddWithValue("$sequence", next);
                    insert.Parameters.AddWithValue("$epoch", expectedEpoch);
                    insert.Parameters.AddWithValue("$previousHash", previousHash);
                    insert.Parameters.AddWithValue("$hash", hash);
                    insert.Parameters.AddWithValue("$commitJson", commitJson);
                    insert.ExecuteNonQuery();
                }
                InsertReceipt(transaction, commit);
                InsertOutbox(transaction, commit.Completion);
                _faultHook?.Invoke(StoreFaultPoint.BeforeCommit);
                transaction.Commit();
                _faultHook?.Invoke(StoreFaultPoint.AfterCommit);
            }
            catch
            {
                throw;
            }
            HeadSequence = next;
            return new StoredTransition(next, expectedEpoch, previousHash, hash, commit);
        }
    }

    public IReadOnlyList<MatchCompletedEvent> ReadPendingOutbox()
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            EnsureCurrentEpoch(ReadMetadata(_connection) ?? throw new InvalidDataException("Persisted match metadata is missing."));
            var events = new List<MatchCompletedEvent>();
            using var command = _connection.CreateCommand();
            command.CommandText = "SELECT EventJson FROM Outbox WHERE Delivered = 0 ORDER BY ResultId;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var eventJson = reader.GetString(0);
                EnsureRecordSize(eventJson);
                events.Add(DecodePersisted<MatchCompletedEvent>(eventJson));
            }
            return Array.AsReadOnly(events.ToArray());
        }
    }

    public void MarkOutboxDelivered(string resultId, long expectedEpoch)
    {
        ValidateIdentifier(resultId, nameof(resultId));
        lock (_sync)
        {
            ThrowIfDisposed();
            if (expectedEpoch != Epoch) throw new InvalidOperationException("Stale match store epoch.");
            using var transaction = _connection.BeginTransaction();
            using var command = _connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "UPDATE Outbox SET Delivered = 1 WHERE ResultId = $resultId AND $epoch = (SELECT Epoch FROM Meta WHERE Id = 1);";
            command.Parameters.AddWithValue("$resultId", resultId);
            command.Parameters.AddWithValue("$epoch", expectedEpoch);
            if (command.ExecuteNonQuery() == 0)
            {
                var metadata = ReadMetadata(transaction) ?? throw new InvalidDataException("Persisted match metadata is missing.");
                if (metadata.Epoch != expectedEpoch) throw new InvalidOperationException("Stale match store epoch.");
                using var exists = _connection.CreateCommand();
                exists.Transaction = transaction;
                exists.CommandText = "SELECT 1 FROM Outbox WHERE ResultId = $resultId;";
                exists.Parameters.AddWithValue("$resultId", resultId);
                if (exists.ExecuteScalar() is null) throw new InvalidOperationException("Outbox result does not exist.");
            }
            transaction.Commit();
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            _connection.Dispose();
            _writerLock.Dispose();
        }
    }

    private static void Configure(SqliteConnection connection, StoreLimits limits)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA journal_mode = WAL; PRAGMA synchronous = FULL; PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 1000; PRAGMA max_page_count = {limits.MaxDatabasePages.ToString(CultureInfo.InvariantCulture)};";
        command.ExecuteNonQuery();
    }

    private static void InitializeSchema(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS Meta (Id INTEGER PRIMARY KEY CHECK (Id = 1), Schema INTEGER NOT NULL, RecipeJson TEXT NOT NULL, StartedUtc TEXT NOT NULL, Epoch INTEGER NOT NULL, Head INTEGER NOT NULL, HeadHash TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS Log (Sequence INTEGER PRIMARY KEY, Epoch INTEGER NOT NULL, PreviousHash TEXT NOT NULL, Hash TEXT NOT NULL, CommitJson TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS Receipts (AccountId TEXT NOT NULL, OperationId TEXT NOT NULL, Fingerprint TEXT NOT NULL, ReplyJson TEXT NOT NULL, PRIMARY KEY (AccountId, OperationId));
            CREATE TABLE IF NOT EXISTS Outbox (ResultId TEXT PRIMARY KEY, EventJson TEXT NOT NULL, Delivered INTEGER NOT NULL CHECK (Delivered IN (0, 1)));
            """;
        command.ExecuteNonQuery();
    }

    private static void InsertMetadata(SqliteConnection connection, SqliteTransaction transaction, string recipeJson, DateTimeOffset startedUtc)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO Meta(Id, Schema, RecipeJson, StartedUtc, Epoch, Head, HeadHash) VALUES(1, $schema, $recipe, $started, 0, 0, $headHash);";
        command.Parameters.AddWithValue("$schema", SchemaVersion);
        command.Parameters.AddWithValue("$recipe", recipeJson);
        command.Parameters.AddWithValue("$started", startedUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$headHash", GenesisHash);
        command.ExecuteNonQuery();
    }

    private static Metadata? ReadMetadata(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Schema, RecipeJson, StartedUtc, Epoch, Head, HeadHash FROM Meta WHERE Id = 1;";
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        if (!DateTimeOffset.TryParse(reader.GetString(2), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var started))
            throw new InvalidDataException("Persisted match start time is invalid.");
        return new Metadata(reader.GetInt32(0), reader.GetString(1), started, reader.GetInt64(3), reader.GetInt64(4), reader.GetString(5));
    }

    private Metadata? ReadMetadata(SqliteTransaction transaction)
    {
        using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT Schema, RecipeJson, StartedUtc, Epoch, Head, HeadHash FROM Meta WHERE Id = 1;";
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        if (!DateTimeOffset.TryParse(reader.GetString(2), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var started))
            throw new InvalidDataException("Persisted match start time is invalid.");
        return new Metadata(reader.GetInt32(0), reader.GetString(1), started, reader.GetInt64(3), reader.GetInt64(4), reader.GetString(5));
    }

    private string ReadHeadHash(SqliteTransaction transaction)
    {
        var metadata = ReadMetadata(transaction) ?? throw new InvalidDataException("Persisted match metadata is missing.");
        EnsureCurrentEpoch(metadata);
        return metadata.HeadHash;
    }

    private long ReadLastLogicalTicks(SqliteTransaction transaction)
    {
        using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT CommitJson FROM Log ORDER BY Sequence DESC LIMIT 1;";
        var value = command.ExecuteScalar() as string;
        if (value is null) return -1;
        EnsureRecordSize(value);
        return DecodePersisted<StoreCommit>(value).LogicalTicks;
    }

    private void InsertReceipt(SqliteTransaction transaction, StoreCommit commit)
    {
        if (!commit.HasReceipt) return;
        if (commit.Command is null) throw new ArgumentException("A durable receipt requires a command.", nameof(commit));
        var actor = commit.Actor ?? throw new ArgumentException("A command requires an actor.", nameof(commit));
        var reply = commit.Reply ?? throw new ArgumentException("A command requires a reply.", nameof(commit));
        var fingerprint = PersistenceJson.Hash(PersistenceJson.Encode(commit.Command));
        var replyJson = PersistenceJson.Encode(reply);
        EnsureRecordSize(replyJson);
        using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO Receipts(AccountId, OperationId, Fingerprint, ReplyJson) VALUES($account, $operation, $fingerprint, $reply);";
        command.Parameters.AddWithValue("$account", actor.AccountId);
        command.Parameters.AddWithValue("$operation", commit.Command.OperationId);
        command.Parameters.AddWithValue("$fingerprint", fingerprint);
        command.Parameters.AddWithValue("$reply", replyJson);
        try { command.ExecuteNonQuery(); }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
        {
            throw new InvalidOperationException("A durable receipt already exists for this operation.", exception);
        }
    }

    private void InsertOutbox(SqliteTransaction transaction, MatchCompletedEvent? completion)
    {
        if (completion is null) return;
        ValidateCompletion(completion);
        var eventJson = PersistenceJson.Encode(completion);
        EnsureRecordSize(eventJson);
        using var existing = _connection.CreateCommand();
        existing.Transaction = transaction;
        existing.CommandText = "SELECT EventJson FROM Outbox WHERE ResultId = $resultId;";
        existing.Parameters.AddWithValue("$resultId", completion.ResultId);
        var prior = existing.ExecuteScalar() as string;
        if (prior is not null)
        {
            if (!string.Equals(prior, eventJson, StringComparison.Ordinal))
                throw new InvalidOperationException("A different completion already exists for this result id.");
            return;
        }
        using var insert = _connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = "INSERT INTO Outbox(ResultId, EventJson, Delivered) VALUES($resultId, $event, 0);";
        insert.Parameters.AddWithValue("$resultId", completion.ResultId);
        insert.Parameters.AddWithValue("$event", eventJson);
        insert.ExecuteNonQuery();
    }

    private void VerifyLog(IReadOnlyList<StoredTransition> entries, Metadata metadata)
    {
        if (entries.Count > _limits.MaxEntries || metadata.Head != entries.Count)
            throw new InvalidDataException("Persisted log has an invalid length.");
        var hash = GenesisHash;
        long lastTicks = -1;
        long lastEpoch = 0;
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            if (entry.Sequence != index + 1 || entry.Epoch <= 0 || entry.Epoch < lastEpoch || entry.Epoch > metadata.Epoch
                || entry.Commit.LogicalTicks < lastTicks
                || !string.Equals(entry.PreviousHash, hash, StringComparison.Ordinal))
                throw new InvalidDataException("Persisted log ordering is invalid.");
            try { ValidateCommit(entry.Commit); }
            catch (Exception exception) when (exception is ArgumentException or InvalidDataException)
            {
                throw new InvalidDataException("Persisted log transition is malformed.", exception);
            }
            var calculated = TransitionHash(entry.Sequence, entry.Epoch, entry.PreviousHash, entry.Commit);
            if (!HashesEqual(calculated, entry.Hash))
                throw new InvalidDataException("Persisted log hash is invalid.");
            hash = entry.Hash;
            lastTicks = entry.Commit.LogicalTicks;
            lastEpoch = entry.Epoch;
        }
        if (!string.Equals(hash, metadata.HeadHash, StringComparison.Ordinal))
            throw new InvalidDataException("Persisted head hash is invalid.");
    }

    private void VerifyReceipts(IReadOnlyList<StoredTransition> entries)
    {
        var expected = new Dictionary<(string Account, string Operation), DurableReceipt>();
        foreach (var entry in entries.Where(x => x.Commit.HasReceipt))
        {
            var actor = entry.Commit.Actor ?? throw new InvalidDataException("Persisted command actor is missing.");
            var command = entry.Commit.Command!;
            var reply = entry.Commit.Reply ?? throw new InvalidDataException("Persisted command reply is missing.");
            var key = (actor.AccountId, command.OperationId);
            if (!expected.TryAdd(key, new DurableReceipt(PersistenceJson.Hash(PersistenceJson.Encode(command)), reply)))
                throw new InvalidDataException("Persisted command receipt is duplicated.");
        }
        var actual = new Dictionary<(string Account, string Operation), DurableReceipt>();
        using var commandDb = _connection.CreateCommand();
        commandDb.CommandText = "SELECT AccountId, OperationId, Fingerprint, ReplyJson FROM Receipts;";
        using var reader = commandDb.ExecuteReader();
        while (reader.Read())
        {
            var replyJson = reader.GetString(3); EnsureRecordSize(replyJson);
            if (!actual.TryAdd((reader.GetString(0), reader.GetString(1)), new DurableReceipt(reader.GetString(2), DecodePersisted<CommandReply>(replyJson))))
                throw new InvalidDataException("Persisted receipt is duplicated.");
        }
        if (expected.Count != actual.Count || expected.Any(pair => !actual.TryGetValue(pair.Key, out var value) || value != pair.Value))
            throw new InvalidDataException("Persisted receipts do not match the event log.");
    }

    private void VerifyOutbox(IReadOnlyList<StoredTransition> entries)
    {
        var expected = new Dictionary<string, MatchCompletedEvent>(StringComparer.Ordinal);
        foreach (var completion in entries.Where(x => x.Commit.Completion is not null).Select(x => x.Commit.Completion!))
        {
            if (expected.TryGetValue(completion.ResultId, out var prior) && prior != completion)
                throw new InvalidDataException("Persisted completion result id conflicts.");
            expected[completion.ResultId] = completion;
        }
        var actual = new Dictionary<string, MatchCompletedEvent>(StringComparer.Ordinal);
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT ResultId, EventJson, Delivered FROM Outbox;";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var eventJson = reader.GetString(1); EnsureRecordSize(eventJson);
            if (reader.GetInt64(2) is not 0 and not 1) throw new InvalidDataException("Persisted outbox delivery state is invalid.");
            if (!actual.TryAdd(reader.GetString(0), DecodePersisted<MatchCompletedEvent>(eventJson)))
                throw new InvalidDataException("Persisted outbox result is duplicated.");
        }
        if (expected.Count != actual.Count || expected.Any(pair => !actual.TryGetValue(pair.Key, out var value) || value != pair.Value))
            throw new InvalidDataException("Persisted outbox does not match the event log.");
    }

    private static string TransitionHash(long sequence, long epoch, string previousHash, StoreCommit commit) =>
        PersistenceJson.Hash(PersistenceJson.Encode(new { Sequence = sequence, Epoch = epoch, PreviousHash = previousHash, Commit = commit }));

    private void EnsureCurrentEpoch(Metadata metadata)
    {
        if (metadata.Schema != SchemaVersion || metadata.Epoch != Epoch)
            throw new InvalidOperationException("This match store has lost ownership.");
    }

    private void ValidateCommit(StoreCommit commit)
    {
        if (commit.LogicalTicks < 0 || !IsAllowedKind(commit.Kind) || !IsSha256(commit.StateHash) || commit.Decisions is null)
            throw new ArgumentException("Persisted transition is malformed.", nameof(commit));
        if (commit.Kind == "Pump")
        {
            if (commit.Actor is not null || commit.Command is not null || commit.Reply is not null || commit.HasReceipt)
                throw new ArgumentException("Pump transitions cannot contain command data.", nameof(commit));
        }
        else
        {
            if (commit.Actor is null || commit.Reply is null) throw new ArgumentException("A command requires actor and reply.", nameof(commit));
            ValidateIdentifier(commit.Actor.AccountId, nameof(commit));
            ValidateIdentifier(commit.Actor.SessionId, nameof(commit));
            ValidateIdentifier(commit.Actor.Side, nameof(commit));
            if (commit.Command is null) throw new ArgumentException("A command transition requires a command.", nameof(commit));
            ValidateIdentifier(commit.Command.OperationId, nameof(commit));
        }
        if (commit.Completion is not null) ValidateCompletion(commit.Completion);
        EnsureRecordSize(PersistenceJson.Encode(commit));
    }

    private static void ValidateCompletion(MatchCompletedEvent completion)
    {
        ValidateIdentifier(completion.ResultId, nameof(completion));
        ValidateIdentifier(completion.MatchId, nameof(completion));
        ValidateIdentifier(completion.ContentVersion, nameof(completion));
        if (completion.Wins0 < 0 || completion.Wins1 < 0 || completion.Winner is < -1 or > 1 || completion.FinalRevision < 0)
            throw new ArgumentException("Completion is malformed.", nameof(completion));
    }

    private void EnsureRecordSize(string json)
    {
        if (System.Text.Encoding.UTF8.GetByteCount(json) > _limits.MaxRecordBytes)
            throw new InvalidDataException("Persisted record exceeds the configured limit.");
    }

    private void ThrowIfDisposed() { if (_disposed) throw new ObjectDisposedException(nameof(SqliteMatchStore)); }
    private static T DecodePersisted<T>(string json)
    {
        try { return PersistenceJson.Decode<T>(json); }
        catch (Exception exception) when (exception is System.Text.Json.JsonException or NotSupportedException)
        {
            throw new InvalidDataException("Persisted JSON is malformed.", exception);
        }
    }
    private static bool HashesEqual(string left, string right)
    {
        try
        {
            var leftBytes = Convert.FromHexString(left);
            var rightBytes = Convert.FromHexString(right);
            return leftBytes.Length == SHA256.HashSizeInBytes && rightBytes.Length == SHA256.HashSizeInBytes
                && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
        }
        catch (FormatException) { throw new InvalidDataException("Persisted log hash is malformed."); }
    }
    private static void ValidateIdentifier(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128) throw new ArgumentException("Invalid identifier.", name);
    }
    private static bool IsAllowedKind(string? value) => value is "Pump" or "Command";
    private static bool IsSha256(string? value)
    {
        if (value is null || value.Length != SHA256.HashSizeInBytes * 2) return false;
        try { return Convert.FromHexString(value).Length == SHA256.HashSizeInBytes; }
        catch (FormatException) { return false; }
    }
    private static void ValidateLimits(StoreLimits limits)
    {
        if (limits.MaxEntries is < 1 or > 500000 || limits.MaxRecordBytes is < 1024 or > 16 * 1024 * 1024 || limits.MaxDatabasePages is < 32 or > 1_000_000)
            throw new ArgumentOutOfRangeException(nameof(limits));
    }
    private static string ValidateDatabasePath(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath) || !Path.IsPathFullyQualified(databasePath))
            throw new ArgumentException("Database path must be absolute.", nameof(databasePath));
        var fullPath = Path.GetFullPath(databasePath);
        if (fullPath.StartsWith("\\\\", StringComparison.Ordinal) || new DriveInfo(Path.GetPathRoot(fullPath)!).DriveType == DriveType.Network)
            throw new ArgumentException("Network database paths are not supported.", nameof(databasePath));
        return fullPath;
    }

    private sealed record Metadata(int Schema, string RecipeJson, DateTimeOffset StartedUtc, long Epoch, long Head, string HeadHash);
}

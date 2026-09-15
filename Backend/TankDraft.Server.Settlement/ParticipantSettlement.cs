using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace TankDraft.Server.Settlement;

public sealed record ParticipantReward(string ResultId, string AccountId, string[] UnitIds, bool Won, bool Applied, int MasteryXp, string RulesVersion);

public sealed partial class SqliteSettlementStore
{
    static void CreateParticipantSchema(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE IF NOT EXISTS ParticipantRewards(ResultId TEXT NOT NULL,AccountId TEXT NOT NULL,UnitIds TEXT NOT NULL,Won INTEGER NOT NULL,Applied INTEGER NOT NULL DEFAULT 0,MasteryXp INTEGER NOT NULL DEFAULT 0,RulesVersion TEXT NOT NULL DEFAULT 'legacy',PRIMARY KEY(ResultId,AccountId),FOREIGN KEY(ResultId) REFERENCES Results(ResultId));";
        command.ExecuteNonQuery();
        AddColumnIfMissing(connection, "MasteryXp", "INTEGER NOT NULL DEFAULT 0");
        AddColumnIfMissing(connection, "RulesVersion", "TEXT NOT NULL DEFAULT 'legacy'");
    }
    static void AddColumnIfMissing(SqliteConnection connection, string name, string definition)
    {
        using var columns = connection.CreateCommand();
        columns.CommandText = "PRAGMA table_info(ParticipantRewards);";
        using var reader = columns.ExecuteReader();
        while (reader.Read()) if (string.Equals(reader.GetString(1), name, StringComparison.Ordinal)) return;
        using var alter = connection.CreateCommand();
        alter.CommandText = "ALTER TABLE ParticipantRewards ADD COLUMN " + name + " " + definition + ";";
        alter.ExecuteNonQuery();
    }
    static void ValidateParticipants(CompletedMatch match, string[]? a, string[]? b, int? winXp, int? lossXp, string? rulesVersion)
    {
        if (a is null && b is null)
        {
            if (winXp is not null || lossXp is not null || rulesVersion is not null) throw new ArgumentException("Participant progression metadata requires frozen decks.");
            return; // Historical results have no recoverable participant snapshot.
        }
        if (a is null || (match.Account1 is not null && b is null) || (match.Account1 is null && b is not null)) throw new ArgumentException("Invalid participant sides.");
        if (winXp is null || lossXp is null || string.IsNullOrWhiteSpace(rulesVersion) || rulesVersion.Length > 128 || !SafeTextPattern.IsMatch(rulesVersion) || winXp is < 0 or > 10000 || lossXp is < 0 or > 10000) throw new ArgumentException("Invalid frozen progression metadata.");
        foreach (var deck in new[] { a, b })
            if (deck is not null && (deck.Length != 4 || deck.Distinct(StringComparer.Ordinal).Count() != 4 || deck.Any(id => string.IsNullOrEmpty(id) || id.Length > 128 || !SafeTextPattern.IsMatch(id))))
                throw new ArgumentException("Expected frozen four-unit deck.");
    }
    void InsertParticipants(SqliteTransaction tx, CompletedMatch match, string[]? a, string[]? b, int winXp, int lossXp, string rulesVersion)
    {
        if (a is null) return;
        Insert(match.Account0, a, match.Winner == 0, match.Winner == 0 ? winXp : lossXp);
        if (match.Account1 is not null) Insert(match.Account1, b!, match.Winner == 1, match.Winner == 1 ? winXp : lossXp);
        void Insert(string account, string[] units, bool won, int masteryXp)
        {
            using var cmd = _connection.CreateCommand(); cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO ParticipantRewards(ResultId,AccountId,UnitIds,Won,MasteryXp,RulesVersion) VALUES($r,$a,$u,$w,$xp,$rules)";
            cmd.Parameters.AddWithValue("$r", match.ResultId); cmd.Parameters.AddWithValue("$a", account);
            cmd.Parameters.AddWithValue("$u", JsonSerializer.Serialize(units)); cmd.Parameters.AddWithValue("$w", won ? 1 : 0); cmd.Parameters.AddWithValue("$xp", masteryXp); cmd.Parameters.AddWithValue("$rules", rulesVersion); cmd.ExecuteNonQuery();
        }
    }
    void VerifyParticipants(SqliteTransaction tx, CompletedMatch match, string[]? a, string[]? b, int? winXp, int? lossXp, string? rulesVersion)
    {
        using var cmd = _connection.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = "SELECT AccountId,UnitIds,Won,MasteryXp,RulesVersion FROM ParticipantRewards WHERE ResultId=$r"; cmd.Parameters.AddWithValue("$r", match.ResultId);
        using var reader = cmd.ExecuteReader(); int found = 0;
        while (reader.Read())
        {
            var first = reader.GetString(0) == match.Account0;
            var deck = first ? a : b;
            var expectedWon = first ? match.Winner == 0 : match.Winner == 1;
            var expectedXp = expectedWon ? winXp : lossXp;
            if (deck is null || expectedXp is null || rulesVersion is null || reader.GetString(1) != JsonSerializer.Serialize(deck) || (reader.GetInt32(2) == 1) != expectedWon || reader.GetInt32(3) != expectedXp || !string.Equals(reader.GetString(4), rulesVersion, StringComparison.Ordinal)) throw new InvalidOperationException("Immutable participant conflict.");
            found++;
        }
        if (a is not null && found != (match.Account1 is null ? 1 : 2)) throw new InvalidOperationException("Historical result cannot acquire a new reward.");
    }
    public IReadOnlyList<ParticipantReward> ReadParticipantRewards(string? account = null, bool pendingOnly = false)
    {
        if (account is not null) ValidateAccount(account);
        lock (_sync)
        {
            ThrowIfDisposed(); using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT ResultId,AccountId,UnitIds,Won,Applied,MasteryXp,RulesVersion FROM ParticipantRewards WHERE ($a IS NULL OR AccountId=$a) AND ($p=0 OR Applied=0) ORDER BY rowid " + (pendingOnly ? "ASC" : "DESC") + " LIMIT 100";
            cmd.Parameters.AddWithValue("$a", (object?)account ?? DBNull.Value); cmd.Parameters.AddWithValue("$p", pendingOnly ? 1 : 0);
            using var reader = cmd.ExecuteReader(); var values = new List<ParticipantReward>();
            while (reader.Read()) values.Add(new(reader.GetString(0), reader.GetString(1), JsonSerializer.Deserialize<string[]>(reader.GetString(2)) ?? throw new InvalidDataException(), reader.GetInt32(3) == 1, reader.GetInt32(4) == 1, reader.GetInt32(5), reader.GetString(6)));
            return values;
        }
    }
    public void MarkParticipantApplied(string resultId, string account)
    {
        ValidateAccount(account);
        lock (_sync)
        {
            ThrowIfDisposed(); using var cmd = _connection.CreateCommand(); cmd.CommandText = "UPDATE ParticipantRewards SET Applied=1 WHERE ResultId=$r AND AccountId=$a";
            cmd.Parameters.AddWithValue("$r", resultId); cmd.Parameters.AddWithValue("$a", account);
            if (cmd.ExecuteNonQuery() != 1) throw new InvalidOperationException("Missing participant obligation.");
        }
    }
}

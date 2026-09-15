using System.Security.Cryptography;
using System.Text;

namespace TankDraft.Server.Security;

/// <summary>
/// Local-only, process-local command boundary. It has no persistence and must not be treated as a release service.
/// </summary>
public sealed class InMemoryCommandGate
{
    private const int MaximumIdentifierLength = 128;
    private const int MaximumCommandKindLength = 32;
    private const int MaximumPayloadLength = 16 * 1024;
    private readonly object _sync = new();
    private readonly Dictionary<string, Receipt> _receipts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _lastSequenceBySession = new(StringComparer.Ordinal);
    private readonly int _receiptCapacity;
    private readonly TimeProvider _timeProvider;
    private bool _isFaulted;

    public InMemoryCommandGate(int receiptCapacity, TimeProvider? timeProvider = null)
    {
        if (receiptCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(receiptCapacity));
        }

        _receiptCapacity = receiptCapacity;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public CommandReply Execute(CallerIdentity caller, CommandEnvelope envelope, Func<CommandReply> executeValidatedCommand)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(executeValidatedCommand);

        lock (_sync)
        {
            if (_isFaulted)
            {
                return CommandReply.Rejected("GateFaulted");
            }

            if (caller.ExpiresAt <= _timeProvider.GetUtcNow())
            {
                return CommandReply.Rejected("SessionExpired");
            }

            var malformed = Validate(caller, envelope);
            if (malformed is not null)
            {
                return CommandReply.Rejected(malformed);
            }

            var receiptKey = BuildReceiptKey(caller, envelope.OperationId);
            var fingerprint = ComputeFingerprint(envelope);
            if (_receipts.TryGetValue(receiptKey, out var receipt))
            {
                return CryptographicOperations.FixedTimeEquals(receipt.Fingerprint, fingerprint)
                    ? receipt.Reply
                    : CommandReply.Rejected("OperationConflict");
            }

            var expectedSequence = _lastSequenceBySession.TryGetValue(caller.SessionId, out var lastSequence)
                ? checked(lastSequence + 1)
                : 1;
            if (envelope.Sequence != expectedSequence)
            {
                return CommandReply.Rejected("UnexpectedSequence");
            }

            if (_receipts.Count >= _receiptCapacity)
            {
                return CommandReply.Rejected("ReceiptCapacityReached");
            }

            CommandReply reply;
            try
            {
                reply = executeValidatedCommand() ?? throw new InvalidOperationException("Command callback returned null.");
            }
            catch (Exception)
            {
                _isFaulted = true;
                return CommandReply.Rejected("GateFaulted");
            }

            _receipts.Add(receiptKey, new Receipt(fingerprint, reply));
            _lastSequenceBySession[caller.SessionId] = envelope.Sequence;
            return reply;
        }
    }

    public long NextCommandSequence(CallerIdentity caller)
    {
        ArgumentNullException.ThrowIfNull(caller);
        lock (_sync)
        {
            if (caller.ExpiresAt <= _timeProvider.GetUtcNow()) throw new UnauthorizedAccessException();
            return _lastSequenceBySession.TryGetValue(caller.SessionId, out var last) ? checked(last + 1) : 1;
        }
    }

    private static string? Validate(CallerIdentity caller, CommandEnvelope envelope)
    {
        if (!HasValidIdentifier(caller.AccountId)
            || !HasValidIdentifier(caller.SessionId)
            || !HasValidIdentifier(caller.MatchId)
            || !HasValidIdentifier(caller.Side))
        {
            return "MalformedCaller";
        }

        if (!HasValidIdentifier(envelope.MatchId)
            || !HasValidIdentifier(envelope.RoundId)
            || !HasValidIdentifier(envelope.ContentVersion)
            || !HasValidIdentifier(envelope.OperationId)
            || string.IsNullOrWhiteSpace(envelope.CommandKind)
            || envelope.CommandKind.Length > MaximumCommandKindLength
            || envelope.Payload is null
            || envelope.Payload.Length > MaximumPayloadLength
            || envelope.Sequence <= 0)
        {
            return "MalformedCommand";
        }

        if (!string.Equals(caller.MatchId, envelope.MatchId, StringComparison.Ordinal))
        {
            return "MatchMismatch";
        }

        return envelope.CommandKind is not "Choose" and not "Order"
            ? "CommandNotAllowed"
            : null;
    }

    private static bool HasValidIdentifier(string value)
    {
        return !string.IsNullOrWhiteSpace(value) && value.Length <= MaximumIdentifierLength;
    }

    private static string BuildReceiptKey(CallerIdentity caller, string operationId)
    {
        return string.Concat(caller.AccountId.Length, ":", caller.AccountId, caller.SessionId.Length, ":", caller.SessionId, operationId.Length, ":", operationId);
    }

    private static byte[] ComputeFingerprint(CommandEnvelope envelope)
    {
        using var stream = new MemoryStream();
        WriteField(stream, envelope.MatchId);
        WriteField(stream, envelope.RoundId);
        WriteField(stream, envelope.ContentVersion);
        WriteField(stream, envelope.OperationId);
        WriteField(stream, envelope.Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture));
        WriteField(stream, envelope.CommandKind);
        WriteField(stream, envelope.Payload);
        return SHA256.HashData(stream.ToArray());
    }

    private static void WriteField(Stream stream, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var length = BitConverter.GetBytes(bytes.Length);
        stream.Write(length, 0, length.Length);
        stream.Write(bytes, 0, bytes.Length);
    }

    private sealed record Receipt(byte[] Fingerprint, CommandReply Reply);
}

public sealed record CommandEnvelope(
    string MatchId,
    string RoundId,
    string ContentVersion,
    string OperationId,
    long Sequence,
    string CommandKind,
    string Payload);

public sealed record CommandReply(bool Accepted, string Code, string Payload)
{
    public static CommandReply Rejected(string code) => new(false, code, string.Empty);
}

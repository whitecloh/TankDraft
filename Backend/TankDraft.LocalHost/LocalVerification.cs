using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using TankDraft.Simulation;
using TankDraft.Contracts.Battle;
using TankDraft.Match.Domain;
using TankDraft.Server.Security;
using TankDraft.Server.Match;

namespace TankDraft.LocalHost;

internal static class LocalVerification
{
    public static async Task RunAsync(HostSettings settings, AuthoredContent content, string version,
        string token0, string token1, LocalMatchEndpoint endpoint, LocalVerificationClock clock, LocalSessionRegistry sessions)
    {
        using var client = new HttpClient(new SocketsHttpHandler { UseProxy = false })
        { BaseAddress = new Uri(settings.ListenUrl), Timeout = TimeSpan.FromSeconds(10) };
        var checks = 0;
        void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException("QA failed: " + name);
            checks++;
        }
        async Task<HttpResponseMessage> Send(string? token, string? body = null, string? origin = null)
        {
            using var request = new HttpRequestMessage(body is null ? HttpMethod.Get : HttpMethod.Post,
                body is null ? "/v1/match" : "/v1/commands");
            if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (origin is not null) request.Headers.Add("Origin", origin);
            if (body is not null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            return await client.SendAsync(request);
        }
        async Task<ServerMatchSnapshot> Snapshot(string token)
        {
            using var response = await Send(token);
            response.EnsureSuccessStatusCode();
            return JsonSerializer.Deserialize<ServerMatchSnapshot>(await response.Content.ReadAsStringAsync(), AuthoredContent.Json)!;
        }
        async Task<CommandReply> Execute(string token, CommandEnvelope command)
        {
            using var response = await Send(token, JsonSerializer.Serialize(command, AuthoredContent.Json));
            response.EnsureSuccessStatusCode();
            return JsonSerializer.Deserialize<CommandReply>(await response.Content.ReadAsStringAsync(), AuthoredContent.Json)!;
        }
        async Task<ServerMatchSnapshot> WaitForBackground(string token)
        {
            var caller = sessions.Authenticate(token) ?? throw new InvalidOperationException("QA session expired.");
            var wallLimit = System.Diagnostics.Stopwatch.StartNew();
            while (wallLimit.Elapsed < TimeSpan.FromSeconds(10))
            {
                // Capture is read-only: only BackgroundService can catch up in this wait, no HTTP/Pump calls.
                var state = endpoint.Capture(caller);
                if (state.Fault is not null) throw new InvalidOperationException("Server background match faulted.");
                if (!state.CatchingUp) return state;
                await Task.Delay(10);
            }
            throw new InvalidOperationException("Background scheduler did not catch up.");
        }
        string Reconnect(string account, string side, string oldToken)
        {
            var previous = sessions.Authenticate(oldToken);
            if (previous is not null) sessions.Revoke(previous.SessionId);
            return sessions.Create(account, LocalMatchEndpoint.MatchId, side,
                TimeSpan.FromSeconds(settings.SessionTtlSeconds)).Token;
        }
        static string Payload(long choiceToken, int index = 0) => JsonSerializer.Serialize(new ChoosePayload
            { Token = choiceToken, OfferIndex = index }, AuthoredContent.Json);
        CommandEnvelope Command(ServerMatchSnapshot state, long sequence, string? payload = null) =>
            new(state.MatchId, state.Round.ToString(System.Globalization.CultureInfo.InvariantCulture), version,
                Guid.NewGuid().ToString("N"), sequence, "Choose", payload ?? Payload(state.ChoiceToken));

        using (var missing = await Send(null)) Check(missing.StatusCode == HttpStatusCode.Unauthorized, "missing credential");
        using (var forged = await Send("forged-token")) Check(forged.StatusCode == HttpStatusCode.Unauthorized, "forged credential");
        using (var origin = await Send(token0, origin: "https://example.invalid")) Check((int)origin.StatusCode == 403, "cross-origin denied");
        using (var malformed = await Send(token0, "{")) Check((int)malformed.StatusCode == 400, "invalid JSON");
        var initial = await Snapshot(token0);
        Check(initial.Offers.Count == 3 && !initial.Committed, "authored opening offers");
        var first = Command(initial, 1);
        var repeatedField = JsonSerializer.Serialize(first, AuthoredContent.Json).TrimEnd('}') + ",\"Sequence\":1}";
        using (var duplicateJson = await Send(token0, repeatedField))
            Check((int)duplicateJson.StatusCode == 400, "duplicate envelope field rejected");
        Check((await Execute(token0, first with { MatchId = "other-match" })).Code == "MatchMismatch", "match binding");
        Check((await Execute(token0, first with { CommandKind = "SetWinner" })).Code == "CommandNotAllowed", "winner injection denied");
        var spoofed = JsonSerializer.Serialize(first, AuthoredContent.Json).TrimEnd('}') + ",\"Side\":1}";
        using (var response = await Send(token0, spoofed)) Check((int)response.StatusCode == 400, "identity field rejected");
        Check((await Execute(token0, first with { ContentVersion = "obsolete" })).Code == "ContentMismatch", "content binding");
        Check((await Execute(token0, Command(initial, 2) with { RoundId = "2" })).Code == "RoundMismatch", "round binding");
        Check((await Execute(token0, Command(initial, 3, "{\"Token\":1,\"OfferIndex\":0,\"Side\":1}"))).Code == "MalformedPayload", "payload allowlist");
        Check(!(await Execute(token0, Command(initial, 4, Payload(-1)))).Accepted, "stale choice token");
        Check(!(await Execute(token0, Command(initial, 5, Payload(initial.ChoiceToken, 99)))).Accepted, "illegal offer");
        var accepted = Command(initial, 6);
        Check((await Execute(token0, accepted)).Accepted, "legal choice");
        var duplicates = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => Execute(token0, accepted)));
        Check(duplicates.All(reply => reply.Accepted), "parallel retry returns stored acknowledgement");
        Check((await Execute(token0, accepted with { Payload = Payload(initial.ChoiceToken, 1) })).Code == "OperationConflict", "operation payload conflict");
        Check((await Execute(token0, Command(initial, 6))).Code == "UnexpectedSequence", "old sequence new operation");
        Check((await Snapshot(token0)).Committed && !(await Snapshot(token1)).Committed, "choice changes own side only");
        Check((await Snapshot(token0)).CommittedChoice?.OfferId == initial.Offers[0].Id,
            "reconnect snapshot retains own committed choice");
        Check((await Execute(token1, Command(await Snapshot(token1), 1))).Accepted, "opponent own choice");
        var next = await Snapshot(token0);
        Check(next.ChoiceToken != initial.ChoiceToken, "both choices advance domain");
        Check((await Execute(token0, accepted)).Accepted && (await Snapshot(token0)).ChoiceToken == next.ChoiceToken,
            "retry after phase advance does not mutate match");
        long sequence0 = 6, sequence1 = 1;
        while ((await Snapshot(token0)).Phase == nameof(MatchPhase.Draft))
        {
            var state0 = await Snapshot(token0);
            if (!state0.Committed && state0.Offers.Count > 0)
                Check((await Execute(token0, Command(state0, ++sequence0))).Accepted, "draft side 0");
            var state1 = await Snapshot(token1);
            if (state1.Phase == nameof(MatchPhase.Draft) && !state1.Committed && state1.Offers.Count > 0)
                Check((await Execute(token1, Command(state1, ++sequence1))).Accepted, "draft side 1");
        }
        Check((await Snapshot(token0)).Phase == nameof(MatchPhase.Battle), "real draft reaches battle");
        clock.Advance(TimeSpan.FromSeconds(0.5));
        await WaitForBackground(token0);
        var battle0 = await Snapshot(token0);
        var battle1 = await Snapshot(token1);
        Check(battle0.SimulationTick > 0 && battle0.Entities.Count > 0 &&
            battle0.Entities.Any(entity => entity.MaxHp > 0 && entity.Position.LengthSquared > 0),
            "background battle + immutable field wire roundtrip");
        Check(JsonSerializer.Serialize(battle0.Entities, AuthoredContent.Json) ==
            JsonSerializer.Serialize(battle1.Entities, AuthoredContent.Json), "two HTTP clients receive same battlefield");
        Check(battle0.DeadlineAt is null, "combat has no gameplay timeout");
        var revokedToken = token0;
        token0 = Reconnect("qa-player-0", "0", token0);
        using (var revoked = await Send(revokedToken)) Check((int)revoked.StatusCode == 401, "revoked session rejected");
        var rejoinedBattle = await Snapshot(token0);
        Check(rejoinedBattle.SimulationTick == battle0.SimulationTick && rejoinedBattle.ResyncRequired &&
            rejoinedBattle.Events.Count == 0 && rejoinedBattle.Entities.Count == battle0.Entities.Count,
            "new session resumes current battle without past animations");
        endpoint.RestartForVerification();
        var restoredBattle = await Snapshot(token0);
        Check(JsonSerializer.Serialize(restoredBattle, AuthoredContent.Json) ==
            JsonSerializer.Serialize(rejoinedBattle, AuthoredContent.Json), "HTTP battlefield restored from SQLite replay");
        Check((await Execute(token0, accepted)).Accepted && (await Snapshot(token0)).Revision == restoredBattle.Revision,
            "durable acknowledgement survives server restart and new session");

        clock.Advance(TimeSpan.FromMinutes(4));
        var offlineState = await WaitForBackground(token0);
        Check(offlineState.Results.Count >= 2 && offlineState.Round >= 3, "multiple real rounds with no player requests");
        token1 = Reconnect("qa-player-1", "1", token1);
        var rejoinedRounds = await Snapshot(token1);
        Check(rejoinedRounds.Results.Count == offlineState.Results.Count && rejoinedRounds.ResyncRequired &&
            rejoinedRounds.Events.Count == 0, "return after multiple offline rounds receives current results");
        Check(!(await Execute(token1, Command(initial, 1))).Accepted, "old round choice cannot mutate resumed match");

        clock.Advance(TimeSpan.FromHours(1));
        token0 = Reconnect("qa-player-0", "0", token0);
        var finalBackground = await WaitForBackground(token0);
        var finalHttp = await Snapshot(token0);
        Check(finalHttp.Phase == nameof(MatchPhase.MatchResult) &&
            Math.Max(finalHttp.Wins0, finalHttp.Wins1) == content.MatchRules.WinsRequired,
            "background server completes full match while clients absent");
        Check(finalHttp.Results.Count == finalHttp.Wins0 + finalHttp.Wins1 && finalHttp.Revision == finalBackground.Revision &&
            finalHttp.DeadlineAt is null, "final results committed once and retained");
        clock.Advance(TimeSpan.FromSeconds(1));
        await WaitForBackground(token0);
        Check((await Snapshot(token0)).Revision == finalHttp.Revision, "final match stays stable");
        endpoint.RestartForVerification();
        var restoredFinal = await Snapshot(token0);
        Check(restoredFinal.Phase == nameof(MatchPhase.MatchResult) && restoredFinal.Wins0 == finalHttp.Wins0 &&
            restoredFinal.Wins1 == finalHttp.Wins1 && restoredFinal.Revision == finalHttp.Revision,
            "HTTP final score survives SQLite reopen");
        using (var oversized = await Send(token0, new string('x', settings.MaxRequestBodyBytes + 1)))
            Check((int)oversized.StatusCode == 413, "request body limit");
        var limited = false;
        for (var request = 0; request <= settings.RequestsPerWindow; request++)
        {
            using var response = await Send("forged-token");
            if ((int)response.StatusCode == 429) { limited = true; break; }
        }
        Check(limited, "unauthenticated traffic hits bounded rate limiter");

        var domain = MatchDomainValidation.Run();
        Check(domain.StartsWith("MatchDomainValidation passed ", StringComparison.Ordinal), "existing domain regression: " + domain);
        var combat0 = RunFullMatch(content, settings.MaxSimulationTicksPerRound);
        var combat1 = RunFullMatch(content, settings.MaxSimulationTicksPerRound);
        Check(combat0 == combat1, "repeatable full server simulation");
        Console.WriteLine($"PASS local HTTP: {checks} checks; security + authored draft + background scheduler/reconnect; content={version}");
        Console.WriteLine($"PASS background server match: rounds={finalHttp.Results.Count}, score={finalHttp.Wins0}:{finalHttp.Wins1}; no client requests during offline advancement");
        Console.WriteLine(domain);
        Console.WriteLine("PASS headless full match twice: " + combat0);
    }

    private static string RunFullMatch(AuthoredContent content, int tickGuard)
    {
        var match = content.CreateMatch();
        var trace = new StringBuilder();
        for (var phaseGuard = 0; phaseGuard < 100; phaseGuard++)
        {
            switch (match.Phase)
            {
                case MatchPhase.Draft:
                    for (var side = 0; side < 2 && match.Phase == MatchPhase.Draft; side++)
                        if (!match.HasCommitted(side) && match.GetOffers(side).Count > 0 &&
                            !match.TryChoose(side, match.ChoiceToken, 0, out _))
                            throw new InvalidOperationException("QA legal choice rejected.");
                    break;
                case MatchPhase.Battle:
                    using (var simulation = new BattleSimulation(content.CreateDefinitions(), content.BattleRules, match.CreateScenario()))
                    {
                        var ticks = 0;
                        while (simulation.Outcome == BattleOutcome.Running && ticks < tickGuard)
                        { simulation.Step(); ticks++; }
                        // QA safety guard is an error, never a combat timeout or a synthetic winner.
                        if (simulation.Outcome == BattleOutcome.Running) throw new InvalidOperationException("QA simulation guard exceeded.");
                        trace.Append($"round={match.RoundNumber},ticks={ticks},outcome={simulation.Outcome};");
                        match.ResolveBattle(simulation.Outcome);
                    }
                    break;
                case MatchPhase.RoundResult: match.Continue(); break;
                case MatchPhase.MatchResult: return trace.Append($"score={match.Wins(0)}:{match.Wins(1)}").ToString();
                default: throw new InvalidOperationException("QA match requires review: " + match.Phase);
            }
        }
        throw new InvalidOperationException("QA phase guard exceeded.");
    }
}

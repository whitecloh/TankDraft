using System.Diagnostics;
using System.Text.Json;
using TankDraft.RemoteHost;

namespace TankDraft.ServerManager;

/// <summary>Only this owner starts/stops the server. HTTP callers cannot supply commands, paths or environment.</summary>
public sealed class ServerSupervisor(ManagerOptions options) : IAsyncDisposable
{
    readonly SemaphoreSlim gate = new(1, 1);
    readonly Queue<ManagerLog> logs = new();
    LocalAuthority? authority;
    Process? gateway;
    string? runtimeDirectory;
    string state = "Stopped";
    DateTimeOffset? started;
    int starts, unexpectedExits;
    bool stopping;
    long logSequence;
    long sampledAt = Stopwatch.GetTimestamp();
    double sampledCpuSeconds;
    GatewayReadinessWatchdog? readinessWatchdog;
    public async Task StartAsync(bool authorityOnly, CancellationToken token)
    {
        await gate.WaitAsync(token);
        var ownedAttempt = false;
        try
        {
            if (authority is not null) throw new InvalidOperationException("already_running");
            if (!authorityOnly && !File.Exists(options.GatewayExecutable)) throw new InvalidOperationException("gateway_build_missing");
            // Fusion credentials are supplied privately after provider auth setup, never via the admin UI.
            var authPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex-secrets", "TankDraft", "fusion-server-auth.json");
            if (!authorityOnly && !File.Exists(Path.Combine(Path.GetDirectoryName(authPath)!, "fusion-server-identity.txt"))) throw new InvalidOperationException("fusion_auth_not_configured");
            ownedAttempt = true;
            state = "Starting"; Log("Starting", authorityOnly ? "Запуск локального ядра (без Photon)." : "Запуск ядра и Fusion.");
            if (!authorityOnly) await PhotonServerAuthentication.RefreshAsync(Path.GetDirectoryName(authPath)!, authPath, token);
            authority = await LocalAuthority.StartAsync(options.PlayFabSecretPath, options.AllowlistedAccounts.ToHashSet(StringComparer.Ordinal), token, allowPhotonQa: options.AllowPlaintextQa, metaSettingsPath: options.MetaSettingsPath, settlementSettingsPath: options.SettlementSettingsPath);
            if (options.AllowPlaintextQa) Log("PlaintextQa", options.SettlementSettingsPath is null
                ? "Закрытый QA без шифрования игрового канала; награды выключены, токены остаются на шлюзе."
                : "Закрытый QA: тестовая награда 1 CO, максимум 10 выдач на аккаунт. Покупки выключены; токены остаются на шлюзе.");
            started = DateTimeOffset.UtcNow; starts++;
            readinessWatchdog = new GatewayReadinessWatchdog((double)Stopwatch.GetTimestamp() / Stopwatch.Frequency);
            if (!authorityOnly)
            {
                var privateRoot = Path.GetDirectoryName(authPath)!;
                runtimeDirectory = Path.Combine(privateRoot, "fusion-run-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(runtimeDirectory);
                var runtimePath = Path.Combine(runtimeDirectory, "gateway.json");
                await File.WriteAllTextAsync(runtimePath, JsonSerializer.Serialize(new
                {
                    Role = "Server", Endpoint = authority.Endpoint, authority.GatewayKey, AuthPath = authPath, options.AllowlistedAccounts,
                    StatusPath = Path.Combine(runtimeDirectory, "status.json"), SessionName = "td-qa-" + authority.InstanceId,
                    LifetimeSeconds = 1800, options.AllowPlaintextQa
                }), token);
                Directory.CreateDirectory(options.LogDirectory);
                var start = new ProcessStartInfo(options.GatewayExecutable) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(options.GatewayExecutable)! };
                start.ArgumentList.Add("-batchmode"); start.ArgumentList.Add("-nographics");
                start.ArgumentList.Add("-logFile"); start.ArgumentList.Add(Path.Combine(options.LogDirectory, "gateway.log"));
                start.Environment["TANKDRAFT_FUSION_RUNTIME_PATH"] = runtimePath;
                // Do not inherit server-only credentials into the Unity gateway.
                foreach (var name in start.Environment.Keys.Where(x => x.StartsWith("TANKDRAFT_", StringComparison.Ordinal) && x != "TANKDRAFT_FUSION_RUNTIME_PATH").ToArray()) start.Environment.Remove(name);
                gateway = Process.Start(start) ?? throw new InvalidOperationException("gateway_start_failed");
                state = "Connecting";
            }
            else state = "AuthorityOnly";
            Log(state, authorityOnly ? "Ядро готово. Внешних подключений нет." : "Ожидаем готовность Fusion.");
        }
        catch { if (ownedAttempt) { await CleanupAsync(); state = "Failed"; Log("StartFailed", "Запуск не выполнен. Проверьте конфигурацию и наличие сборки."); } throw; }
        finally { gate.Release(); }
    }
    public async Task StopAsync(bool force)
    {
        await gate.WaitAsync();
        try
        {
            if (authority is null) return;
            authority.BeginDrain();
            if (!force && !authority.CanFinishDrain)
            { state = "Draining"; Log("Drain", "Очередь закрыта. Ждём завершения матчей и начатых тестовых выдач."); return; }
            stopping = true;
            await CleanupAsync(); state = "Stopped";
            Log("Stopped", force ? "Принудительная остановка; незавершённые матчи аннулированы." : "Сервер остановлен.");
        }
        finally { stopping = false; gate.Release(); }
    }
    public async Task<ManagerStatus> StatusAsync()
    {
        await gate.WaitAsync();
        try
        {
            if (authority is not null)
            {
                if (!authority.IsRunning || gateway is { HasExited: true })
                { unexpectedExits++; await CleanupAsync(); state = "Failed"; Log("ProcessEnded", "Сервер завершился или достиг лимита времени. Автоперезапуск выключен."); }
                else if (state == "Draining" && authority.CanFinishDrain)
                { await CleanupAsync(); state = "Stopped"; Log("Drained", "Матчи завершены, сервер остановлен."); }
                else if (gateway is not null && runtimeDirectory is not null)
                {
                    var statusPath = Path.Combine(runtimeDirectory, "status.json");
                    var healthy = false;
                    try
                    {
                        var info = new FileInfo(statusPath);
                        if (info.Exists && info.Length < 4096)
                        {
                            using var json = JsonDocument.Parse(await File.ReadAllTextAsync(statusPath));
                            healthy = DateTime.UtcNow - info.LastWriteTimeUtc < TimeSpan.FromSeconds(5) && json.RootElement.GetProperty("Ready").GetBoolean();
                        }
                    }
                    catch (Exception e) when (e is IOException or JsonException or KeyNotFoundException) { healthy = false; }
                    if (state != "Draining") state = healthy ? "Ready" : "Connecting";
                    if (readinessWatchdog?.TimedOut(healthy, (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency) == true)
                    { await CleanupAsync(); state = "Failed"; Log("ReadinessTimeout", "Fusion устойчиво недоступен. Сервер остановлен."); }
                }
            }
            var metrics = authority?.Metrics;
            long gatewayBytes = 0;
            using var self = Process.GetCurrentProcess();
            var cpuSeconds = self.TotalProcessorTime.TotalSeconds;
            try { if (gateway is { HasExited: false }) { gateway.Refresh(); gatewayBytes = gateway.WorkingSet64; cpuSeconds += gateway.TotalProcessorTime.TotalSeconds; } } catch (InvalidOperationException) { }
            var sampleNow = Stopwatch.GetTimestamp();
            var elapsed = Stopwatch.GetElapsedTime(sampledAt, sampleNow).TotalSeconds;
            var cpuPercent = elapsed > .001 ? Math.Clamp((cpuSeconds - sampledCpuSeconds) / elapsed / Environment.ProcessorCount * 100, 0, 100) : 0;
            sampledAt = sampleNow; sampledCpuSeconds = cpuSeconds;
            return new(state, started, authority?.InstanceId, metrics, self.WorkingSet64 / 1048576,
                gatewayBytes / 1048576, Math.Round(cpuPercent, 1), starts, unexpectedExits, File.Exists(options.GatewayExecutable), stopping, logs.ToArray()) { PlaintextQa = options.AllowPlaintextQa };
        }
        finally { gate.Release(); }
    }
    void Log(string code, string message)
    {
        var entry = new ManagerLog(++logSequence, DateTimeOffset.UtcNow, code, message);
        logs.Enqueue(entry); while (logs.Count > 200) logs.Dequeue();
        // Operator log contains only controlled messages: never raw exceptions, tickets or HTTP payloads.
        try
        {
            Directory.CreateDirectory(options.LogDirectory);
            var path = Path.Combine(options.LogDirectory, "manager.jsonl");
            if (new FileInfo(path) is { Exists: true, Length: > 1048576 }) File.Move(path, path + ".previous", true);
            File.AppendAllText(path, JsonSerializer.Serialize(entry) + Environment.NewLine);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }
    async Task CleanupAsync()
    {
        if (gateway is not null)
        {
            try
            {
                if (!gateway.HasExited)
                {
                    if (runtimeDirectory is not null) await File.WriteAllTextAsync(Path.Combine(runtimeDirectory, "stop"), "stop");
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    try { await gateway.WaitForExitAsync(timeout.Token); }
                    catch (OperationCanceledException) { gateway.Kill(entireProcessTree: true); await gateway.WaitForExitAsync(); }
                }
            }
            finally { gateway.Dispose(); gateway = null; }
        }
        if (authority is not null) { await authority.DisposeAsync(); authority = null; }
        if (runtimeDirectory is not null)
        {
            // Explicitly created private files only; no recursive delete and no user-selected directory.
            foreach (var name in new[] { "gateway.json", "status.json", "status.json.tmp", "stop" })
                try { File.Delete(Path.Combine(runtimeDirectory, name)); } catch (IOException) { }
            try { Directory.Delete(runtimeDirectory, false); } catch (IOException) { }
            runtimeDirectory = null;
        }
    }
    public async ValueTask DisposeAsync() { await StopAsync(true); gate.Dispose(); }
}
public sealed record ManagerLog(long Sequence, DateTimeOffset At, string Code, string Message);
public sealed record ManagerStatus(string State, DateTimeOffset? Started, string? InstanceId, AuthorityMetrics? Authority,
    long ManagerMemoryMb, long GatewayMemoryMb, double CpuPercent, int Starts, int UnexpectedExits, bool GatewayBuildExists, bool Stopping, ManagerLog[] Logs)
{ public bool PlaintextQa { get; init; } }

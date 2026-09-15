using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TankDraft.Server.Security;

namespace TankDraft.LocalHost;

// USB-only local QA bridge.  It intentionally has no LAN listener, cloud endpoint,
// device settings mutation, package installation, or persisted credentials on the PC.
internal static partial class LocalTlsHost
{
    private const string AndroidPackage = "com.tankdraft.localqa";
    private const string AndroidBootstrap = "files/td-bootstrap.json";
    private static readonly TimeSpan AndroidCommandTimeout = TimeSpan.FromSeconds(15);

    public static async Task ServeAndroidAsync(string serial, bool automated)
    {
        if (string.IsNullOrWhiteSpace(serial) || serial.Length > 128)
            throw new InvalidOperationException("ADB serial is required.");

        var root = FindRepositoryRoot();
        var exe = Path.Combine(root, "Logs", "BackendClient", "Build", "TankDraftServerClient.exe");
        if (!File.Exists(exe))
            throw new InvalidOperationException("Build ServerMatch through Unity MCP first (Tools/Backend/QueueServerClientBuild.cs).");

        var adb = Path.Combine(root, "..", "..", "UNITY", "6000.3.10f1", "Editor", "Data", "PlaybackEngines", "AndroidPlayer", "SDK", "platform-tools", "adb.exe");
        adb = Path.GetFullPath(adb);
        if (!File.Exists(adb)) throw new InvalidOperationException("Expected Unity Android adb.exe is missing.");

        var device = new AndroidAdb(adb, serial);
        await device.RequireAuthorizedAsync();
        var runId = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        var run = Path.Combine(root, "Logs", "BackendClient", "android-" + runId);
        Directory.CreateDirectory(run);
        var lifecyclePath = Path.Combine(run, "android-lifecycle.jsonl");

        var settings = LocalTlsSettings.Load();
        var (content, version) = AuthoredContent.Load();
        var families = new LocalAccessFamilies(2);
        var a = families.Create("qa-player-0", LocalMatchEndpoint.MatchId, "0", TimeSpan.FromSeconds(settings.GrantTtlSeconds));
        var b = families.Create("qa-player-1", LocalMatchEndpoint.MatchId, "1", TimeSpan.FromSeconds(settings.GrantTtlSeconds));
        using var endpoint = new LocalMatchEndpoint(content, version, HostSettings.Load(), TimeProvider.System);
        using var certificate = CreateCertificate(out var pin);
        await using var app = await StartAsync(settings, endpoint, families, version, certificate, CancellationToken.None);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(850));
        Console.CancelKeyPress += Cancel;
        Process? windows = null;
        var reverseOwned = false;
        var completed = false;
        var phonePids = new HashSet<int>();
        try
        {
            reverseOwned = await device.EnsureReverseAsync(18783);
            await RecordLifecycleAsync(lifecyclePath, "tunnel-open", null);
            await device.WriteBootstrapAsync(b.Value, pin, runId, automated);
            windows = StartUnity(exe, run, 0, a.Value, pin, auto: true, restarted: true);
            await device.StartAppAsync();
            phonePids.UnionWith(await device.GetAppPidsAsync());
            await RecordLifecycleAsync(lifecyclePath, "phone-start", null);

            if (automated)
            {
                var firstBattle = await WaitForPhoneEvidenceAsync(device, runId, "Battle", minimumRevision: -1, stop.Token);
                await RecordLifecycleAsync(lifecyclePath, "battle-before-background", firstBattle);
                await CollectPhoneRuntimeAsync(device, phonePids, run);
                await device.HomeAsync();
                await RecordLifecycleAsync(lifecyclePath, "phone-home", firstBattle);
                await Task.Delay(TimeSpan.FromSeconds(12), stop.Token);
                var beforeResume = CaptureServerEvidence(families, b, settings, endpoint);
                await device.WriteBootstrapAsync(b.Value, pin, runId, automated);
                await device.StartAppAsync();
                phonePids.UnionWith(await device.GetAppPidsAsync());
                var resumedBattle = await WaitForPhoneEvidenceAsync(device, runId, "Battle", beforeResume.Revision, stop.Token);
                await RecordLifecycleAsync(lifecyclePath, "battle-after-resume", resumedBattle, CaptureServerEvidence(families, b, settings, endpoint));

                // The host keeps the match running while this client is absent.  The new
                // physical access token must retain the family's stable command stream.
                await device.RemoveReverseAsync(18783);
                reverseOwned = false;
                await RecordLifecycleAsync(lifecyclePath, "tunnel-removed", resumedBattle, CaptureServerEvidence(families, b, settings, endpoint));
                await CollectPhoneRuntimeAsync(device, phonePids, run);
                await device.ForceStopAsync();
                await CollectPhoneRuntimeAsync(device, phonePids, run);
                await RecordLifecycleAsync(lifecyclePath, "phone-force-stop", resumedBattle);
                await Task.Delay(TimeSpan.FromSeconds(40), stop.Token);
                _ = families.InvalidateAccess(b.Value);
                var afterOffline = CaptureServerEvidence(families, b, settings, endpoint);
                reverseOwned = await device.EnsureReverseAsync(18783);
                await device.WriteBootstrapAsync(b.Value, pin, runId, automated);
                await device.StartAppAsync();
                phonePids.UnionWith(await device.GetAppPidsAsync());
                await RecordLifecycleAsync(lifecyclePath, "phone-restart-after-offline", null, afterOffline);
                var result = await WaitForPhoneEvidenceAsync(device, runId, "MatchResult", resumedBattle.Revision, stop.Token);
                await WaitForPhoneFileAsync(device, runId, "result-1.png", stop.Token);
                await RecordLifecycleAsync(lifecyclePath, "match-result", result);
                await device.PullQaFilesAsync(runId, run);

                var limit = Stopwatch.StartNew();
                while (!windows.HasExited && limit.Elapsed < TimeSpan.FromSeconds(90))
                    await Task.Delay(250, stop.Token);
                if (!windows.HasExited || windows.ExitCode != 0)
                    throw new InvalidOperationException("Windows local QA client did not complete successfully.");
                completed = true;
            }
            else
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, stop.Token);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            Console.CancelKeyPress -= Cancel;
            try { await CollectPhoneRuntimeAsync(device, phonePids, run); } catch (Exception) { }
            try { await device.PullQaFilesAsync(runId, run); } catch (Exception) { }
            try { if (automated) await device.ForceStopAsync(); } catch (Exception) { }
            try { await device.DeleteBootstrapAsync(); } catch (Exception) { }
            if (reverseOwned) { try { await device.RemoveReverseAsync(18783); } catch (Exception) { } }
            var windowsExit = windows is not null && windows.HasExited ? windows.ExitCode : (int?)null;
            try { if (windows is not null && !windows.HasExited) windows.Kill(true); } catch (InvalidOperationException) { }
            windows?.Dispose();
            endpoint.Flush(); families.Revoke(a); families.Revoke(b);
            await app.StopAsync();
            await RecordLifecycleAsync(lifecyclePath, completed ? "completed" : "stopped", null);
            await File.WriteAllTextAsync(Path.Combine(run, "launcher-result.json"), JsonSerializer.Serialize(new { Completed = completed, WindowsExit = windowsExit }) + Environment.NewLine, new UTF8Encoding(false));
            Console.WriteLine("Local Android TLS run: " + run);
        }
        if (automated && !completed) throw new TimeoutException("Android automated match did not complete before the local run limit.");
        void Cancel(object? _, ConsoleCancelEventArgs e) { e.Cancel = true; stop.Cancel(); }
    }

    private static async Task<PhoneEvidence> WaitForPhoneEvidenceAsync(AndroidAdb device, string runId, string phase, long minimumRevision, CancellationToken token)
    {
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromSeconds(150))
        {
            token.ThrowIfCancellationRequested();
            if (await device.QaFileExistsAsync(runId, "failure-1.txt"))
                throw new InvalidOperationException("Phone client reported a local QA failure.");
            var evidence = await device.ReadQaTextAsync(runId, "presentation-1.jsonl", allowMissing: true);
            var latest = ParseLatestEvidence(evidence, phase);
            if (latest is not null && latest.Revision > minimumRevision) return latest;
            await Task.Delay(1000, token);
        }
        throw new TimeoutException("Phone did not report a new " + phase + " frame in its private QA evidence.");
    }

    private static async Task WaitForPhoneFileAsync(AndroidAdb device, string runId, string name, CancellationToken token)
    {
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromSeconds(30))
        {
            token.ThrowIfCancellationRequested();
            if (await device.QaFileExistsAsync(runId, name)) return;
            await Task.Delay(500, token);
        }
        throw new TimeoutException("Phone did not capture " + name + ".");
    }

    private static PhoneEvidence? ParseLatestEvidence(string jsonl, string phase)
    {
        PhoneEvidence? latest = null;
        foreach (var line in jsonl.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (!root.TryGetProperty("Phase", out var p) || p.GetString() != phase || !root.TryGetProperty("Revision", out var revision) || !revision.TryGetInt64(out var value)) continue;
                if (latest is null || value > latest.Revision)
                    latest = new PhoneEvidence(value, root.TryGetProperty("Round", out var round) ? round.GetInt32() : 0, phase);
            }
            catch (JsonException) { }
        }
        return latest;
    }

    private static ServerEvidence CaptureServerEvidence(LocalAccessFamilies families, LocalGrant grant, LocalTlsSettings settings, LocalMatchEndpoint endpoint)
    {
        var issue = families.Issue(grant.Value, TimeSpan.FromSeconds(settings.AccessTtlSeconds), TimeSpan.FromSeconds(settings.RefreshMarginSeconds))
                    ?? throw new InvalidOperationException("Could not obtain local QA access for authoritative snapshot.");
        return families.UseAccess(issue.AccessToken, access =>
        {
            var snapshot = endpoint.Capture(access.Caller);
            return new ServerEvidence(snapshot.Revision, snapshot.Round, snapshot.Phase);
        });
    }

    private static Task RecordLifecycleAsync(string path, string action, PhoneEvidence? evidence, ServerEvidence? server = null)
    {
        var line = JsonSerializer.Serialize(new { Utc = DateTimeOffset.UtcNow, Action = action, Revision = evidence?.Revision, Round = evidence?.Round, Phase = evidence?.Phase,
            ServerRevision = server?.Revision, ServerRound = server?.Round, ServerPhase = server?.Phase });
        return File.AppendAllTextAsync(path, line + Environment.NewLine, new UTF8Encoding(false));
    }

    private sealed record PhoneEvidence(long Revision, int Round, string Phase);
    private sealed record ServerEvidence(long Revision, int Round, string Phase);

    private static async Task CollectPhoneRuntimeAsync(AndroidAdb device, IEnumerable<int> pids, string run)
    {
        foreach (var pid in pids) await device.CollectPidLogcatAsync(pid, Path.Combine(run, "phone-runtime-" + pid + ".log"));
    }

    internal sealed class AndroidAdb(string executable, string serial)
    {
        public async Task RequireAuthorizedAsync()
        {
            var devices = await RunAsync(["devices"]);
            if (!devices.StandardOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                    .Any(x => string.Equals(x.Trim(), serial + "\tdevice", StringComparison.Ordinal)))
                throw new InvalidOperationException("ADB device is absent or not authorized: " + serial);
        }

        public async Task<bool> EnsureReverseAsync(int port)
        {
            var existing = await RunAsync(["-s", serial, "reverse", "--list"]);
            if (existing.ExitCode != 0) EnsureSuccess(existing);
            if (existing.StandardOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Any(x => x.Contains("tcp:" + port, StringComparison.Ordinal)))
                throw new InvalidOperationException("ADB reverse port 18783 is already owned by another mapping.");
            await RequireSuccessAsync(["-s", serial, "reverse", "tcp:" + port, "tcp:" + port]);
            return true;
        }

        public Task RemoveReverseAsync(int port) => RequireSuccessAsync(["-s", serial, "reverse", "--remove", "tcp:" + port]);
        public Task HomeAsync() => RequireSuccessAsync(["-s", serial, "shell", "am", "start", "-a", "android.intent.action.MAIN", "-c", "android.intent.category.HOME"]);
        public Task ForceStopAsync() => RequireSuccessAsync(["-s", serial, "shell", "am", "force-stop", AndroidPackage]);
        public Task StartAppAsync() => RequireSuccessAsync(["-s", serial, "shell", "monkey", "-p", AndroidPackage, "-c", "android.intent.category.LAUNCHER", "1"]);

        public async Task<IReadOnlyList<int>> GetAppPidsAsync()
        {
            var result = await RunAsync(["-s", serial, "shell", "pidof", AndroidPackage]);
            for (var attempt = 0; result.ExitCode != 0 && attempt < 20; attempt++)
            {
                await Task.Delay(250);
                result = await RunAsync(["-s", serial, "shell", "pidof", AndroidPackage]);
            }
            if (result.ExitCode != 0) return [];
            var pids = new List<int>();
            foreach (var raw in result.StandardOutput.Split([' ', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
                if (int.TryParse(raw, out var pid) && pid > 0) pids.Add(pid);
            return pids;
        }

        public async Task CollectPidLogcatAsync(int pid, string destination)
        {
            if (pid <= 0) return;
            var psi = CreateInfo(["-s", serial, "logcat", "-d", "--pid=" + pid, "-v", "threadtime"]);
            using var process = Process.Start(psi) ?? throw new InvalidOperationException("Could not start adb.");
            process.StandardInput.Close();
            await using var output = new FileStream(destination, FileMode.Append, FileAccess.Write, FileShare.Read);
            var copy = process.StandardOutput.BaseStream.CopyToAsync(output);
            var error = process.StandardError.ReadToEndAsync();
            try { await Task.WhenAll(copy, error, process.WaitForExitAsync().WaitAsync(AndroidCommandTimeout)); }
            catch (TimeoutException)
            {
                try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { }
                await Task.WhenAll(copy, error);
                throw;
            }
            if (process.ExitCode != 0) throw new InvalidOperationException("ADB logcat collection failed: " + Trim(await error));
        }

        public async Task WriteBootstrapAsync(string grant, string pin, string runId, bool automated, bool queue = false)
        {
            var bootstrap = queue
                ? JsonSerializer.Serialize(new { Grant = grant, Pin = pin, RunId = runId, Auto = automated, Queue = true })
                : JsonSerializer.Serialize(new { Grant = grant, Pin = pin, RunId = runId, Auto = automated });
            // exec-out is output-only; shell -T forwards stdin without a PTY or terminal echo.
            await RequireSuccessAsync(["-s", serial, "shell", "-T", "run-as", AndroidPackage, "sh", "-c", "'mkdir -p files && cat > " + AndroidBootstrap + "'"], bootstrap);
            await RequireSuccessAsync(["-s", serial, "shell", "run-as", AndroidPackage, "test", "-s", AndroidBootstrap]);
        }

        public Task DeleteBootstrapAsync() => RequireSuccessAsync(["-s", serial, "shell", "run-as", AndroidPackage, "rm", "-f", AndroidBootstrap]);

        public async Task<string> ReadQaTextAsync(string runId, string file, bool allowMissing)
        {
            var result = await RunAsync(["-s", serial, "shell", "run-as", AndroidPackage, "cat", "files/qa/" + runId + "/" + file]);
            if (result.ExitCode != 0 && allowMissing) return string.Empty;
            EnsureSuccess(result); return result.StandardOutput;
        }

        public async Task<bool> QaFileExistsAsync(string runId, string file)
        {
            var result = await RunAsync(["-s", serial, "shell", "run-as", AndroidPackage, "test", "-s", "files/qa/" + runId + "/" + file]);
            return result.ExitCode == 0;
        }

        public async Task PullQaFilesAsync(string runId, string outputDirectory)
        {
            var listing = await RunAsync(["-s", serial, "shell", "run-as", AndroidPackage, "find", "files/qa/" + runId, "-type", "f"]);
            EnsureSuccess(listing);
            foreach (var remote in listing.StandardOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                var prefix = "files/qa/" + runId + "/";
                if (!remote.StartsWith(prefix, StringComparison.Ordinal) || remote.Contains("..", StringComparison.Ordinal)) continue;
                var relative = remote[prefix.Length..].Replace('/', Path.DirectorySeparatorChar);
                var local = Path.GetFullPath(Path.Combine(outputDirectory, "phone", relative));
                var root = Path.GetFullPath(Path.Combine(outputDirectory, "phone")) + Path.DirectorySeparatorChar;
                if (!local.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Invalid private QA path.");
                Directory.CreateDirectory(Path.GetDirectoryName(local)!);
                await PullBinaryAsync(remote, local);
            }
        }

        private async Task PullBinaryAsync(string remote, string local)
        {
            var psi = CreateInfo(["-s", serial, "exec-out", "run-as", AndroidPackage, "cat", remote]);
            using var process = Process.Start(psi) ?? throw new InvalidOperationException("Could not start adb.");
            process.StandardInput.Close();
            await using var destination = File.Create(local);
            var copy = process.StandardOutput.BaseStream.CopyToAsync(destination);
            var error = process.StandardError.ReadToEndAsync();
            try { await Task.WhenAll(copy, error, process.WaitForExitAsync().WaitAsync(AndroidCommandTimeout)); }
            catch (TimeoutException)
            {
                try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { }
                await Task.WhenAll(copy, error);
                throw;
            }
            if (process.ExitCode != 0) throw new InvalidOperationException("ADB private-file copy failed: " + Trim((await error)));
        }

        private async Task RequireSuccessAsync(IReadOnlyList<string> arguments, string? standardInput = null)
        {
            var result = await RunAsync(arguments, standardInput); EnsureSuccess(result);
        }

        private async Task<AdbResult> RunAsync(IReadOnlyList<string> arguments, string? standardInput = null)
        {
            var psi = CreateInfo(arguments);
            using var process = Process.Start(psi) ?? throw new InvalidOperationException("Could not start adb.");
            var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            try
            {
                if (standardInput is not null) { await process.StandardInput.WriteAsync(standardInput); await process.StandardInput.FlushAsync(); }
                process.StandardInput.Close();
                await process.WaitForExitAsync().WaitAsync(AndroidCommandTimeout);
            }
            catch (TimeoutException)
            {
                try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { }
                await Task.WhenAll(stdout, stderr);
                throw;
            }
            return new AdbResult(process.ExitCode, await stdout, await stderr);
        }

        private ProcessStartInfo CreateInfo(IReadOnlyList<string> arguments)
        {
            var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true, StandardInputEncoding = new UTF8Encoding(false) };
            foreach (var argument in arguments) info.ArgumentList.Add(argument);
            return info;
        }
        private static void EnsureSuccess(AdbResult result) { if (result.ExitCode != 0) throw new InvalidOperationException("ADB command failed: " + Trim(result.StandardError)); }
        private static string Trim(string value) => value.Replace('\r', ' ').Replace('\n', ' ').Trim()[..Math.Min(240, value.Replace('\r', ' ').Replace('\n', ' ').Trim().Length)];
        private sealed record AdbResult(int ExitCode, string StandardOutput, string StandardError);
    }
}

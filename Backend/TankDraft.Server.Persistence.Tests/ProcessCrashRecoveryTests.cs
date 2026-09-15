using System.Diagnostics;
using TankDraft.Contracts.Battle;
using TankDraft.Server.Persistence;
using TankDraft.Server.Security;
using Xunit;

namespace TankDraft.Server.Persistence.Tests;

public sealed class ProcessCrashRecoveryTests
{
    [Fact]
    public void Killed_process_after_active_battle_commit_recovers_the_exact_effect_state()
    {
        using var fixture = new PersistenceFixture();
        var marker = Path.Combine(Path.GetDirectoryName(fixture.DatabasePath)!, "active.ready");
        using var process = StartProbe(fixture.DatabasePath, marker, "ActiveBattleAfterCommit");
        string[] lines;
        try
        {
            WaitForMarker(process, marker);
            lines = File.ReadAllLines(marker);
            Assert.Equal("READY", lines[0]);
            Assert.True(lines.Length >= 2 && lines[1].Length == 64, "Probe did not persist its state hash marker.");
        }
        finally { StopProbe(process); }
        using var recovered = fixture.Open();
        var caller = fixture.Caller(0, TimeSpan.FromHours(1));
        var state = recovered.Capture(caller);
        Assert.Equal("Battle", state.Phase);
        Assert.Contains(state.Entities, entity => entity.Kind is BattleEntityKind.Projectile or BattleEntityKind.Zone);
        Assert.Equal(lines[1], recovered.GetStateHash());
    }

    [Theory]
    [InlineData("BeforeCommit", false)]
    [InlineData("AfterCommit", true)]
    [InlineData("AfterApplyBeforeCommit", false)]
    public void Killed_process_recovers_only_committed_command(string crashPoint, bool wasCommitted)
    {
        using var fixture = new PersistenceFixture();
        CommandEnvelope command;
        using (var initial = fixture.Open())
        {
            command = fixture.Choose(initial, fixture.Caller(0), "probe-operation");
        }
        var marker = Path.Combine(Path.GetDirectoryName(fixture.DatabasePath)!, "probe.ready");
        using var process = StartProbe(fixture.DatabasePath, marker, crashPoint);
        try { WaitForMarker(process, marker); }
        finally { StopProbe(process); }

        using var recovered = fixture.Open();
        Assert.Equal(wasCommitted ? 1 : 0, recovered.CommittedSequence);
        var reply = recovered.Execute(fixture.Caller(0), command);
        Assert.Equal("Accepted", reply.Code);
        Assert.Equal(1, recovered.CommittedSequence);
    }

    private static Process StartProbe(string database, string marker, string crashPoint)
    {
        var root = PersistenceFixture.RepositoryRoot();
        var dotnet = Path.Combine(root, "Logs", "BackendSdk", "dotnet.exe");
        var probe = Path.Combine(root, "Backend", "TankDraft.Persistence.Probe", "bin", "Debug", "net10.0", "TankDraft.Persistence.Probe.dll");
        Assert.True(File.Exists(dotnet), "Pinned local .NET SDK is required for the process crash probe.");
        Assert.True(File.Exists(probe), "Probe build output is required before running persistence tests.");
        var start = new ProcessStartInfo(dotnet)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = root
        };
        start.ArgumentList.Add(probe);
        start.ArgumentList.Add(database);
        start.ArgumentList.Add(marker);
        start.ArgumentList.Add(crashPoint);
        return Process.Start(start) ?? throw new InvalidOperationException("Could not start persistence probe.");
    }

    private static void WaitForMarker(Process process, string marker)
    {
        var deadline = Stopwatch.GetTimestamp() + 10 * Stopwatch.Frequency;
        while (!File.Exists(marker) && !process.HasExited && Stopwatch.GetTimestamp() < deadline)
            Thread.Sleep(20);
        if (!File.Exists(marker))
        {
            var stdout = process.HasExited ? process.StandardOutput.ReadToEnd() : string.Empty;
            var stderr = process.HasExited ? process.StandardError.ReadToEnd() : string.Empty;
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw new Xunit.Sdk.XunitException($"Probe did not reach {marker}. stdout={stdout} stderr={stderr}");
        }
    }

    private static void StopProbe(Process process)
    {
        if (!process.HasExited) process.Kill(entireProcessTree: true);
        Assert.True(process.WaitForExit(10_000), "Probe did not stop after Process.Kill.");
    }
}

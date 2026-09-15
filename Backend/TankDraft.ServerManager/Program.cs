using TankDraft.ServerManager;

try
{
    var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex-secrets", "TankDraft", "fusion-server-manager.json");
    var options = ManagerOptions.Load(path);
    await using var supervisor = new ServerSupervisor(options);
    await using var app = ManagerWebHost.Create(supervisor);
    using var stop = new CancellationTokenSource();
    app.Lifetime.ApplicationStopping.Register(stop.Cancel);
    var run = app.RunAsync();
    Console.WriteLine("TankDraft Server Manager: http://127.0.0.1:18878/");
    // Health supervision continues with the browser closed; no dependence on HTTP polling.
    var monitor = Task.Run(async () =>
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try { while (await timer.WaitForNextTickAsync(stop.Token)) await supervisor.StatusAsync(); }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
    });
    await run; stop.Cancel(); await monitor;
}
catch { Console.Error.WriteLine("SERVER_MANAGER_FAILED: check private configuration, port and build. No credentials logged."); Environment.ExitCode = 1; }

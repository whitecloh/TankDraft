namespace TankDraft.LocalHost;

// Timer owns progress even when both players stop making requests.
internal sealed class MatchPumpService(ILocalMatchRouter endpoint, TimeSpan pollInterval) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(pollInterval);
        long lastSlowLog = 0;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var started = System.Diagnostics.Stopwatch.GetTimestamp();
                endpoint.Pump();
                var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(started);
                if (elapsed.TotalMilliseconds >= 250 && (lastSlowLog == 0 || System.Diagnostics.Stopwatch.GetElapsedTime(lastSlowLog).TotalSeconds >= 5))
                {
                    lastSlowLog = System.Diagnostics.Stopwatch.GetTimestamp();
                    Console.WriteLine($"Local scheduler delay: {elapsed.TotalMilliseconds:F0} ms at {DateTimeOffset.UtcNow:O}");
                }
                if (!await timer.WaitForNextTickAsync(stoppingToken)) break;
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace TankDraft.LocalHost;

// A raw loopback relay for QA. It neither interprets TLS nor persists any payload bytes.
internal sealed class LocalFaultProxy : IAsyncDisposable
{
    private const int ListenPort = 18783;
    private const int TargetPort = 18784;
    private static readonly TimeSpan ResetAt = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan StallStart = TimeSpan.FromSeconds(25);
    private static readonly TimeSpan StallEnd = TimeSpan.FromSeconds(33);

    private readonly LocalFaultSettings _settings;
    private readonly string _runDirectory;
    private readonly TcpListener _listener;
    private readonly SemaphoreSlim _slots;
    private readonly CancellationTokenSource _stop = new();
    private readonly Stopwatch _elapsed = Stopwatch.StartNew();
    private readonly ConcurrentDictionary<long, RelayConnection> _connections = new();
    private readonly Task _acceptTask;
    private readonly Task _schedulerTask;
    private readonly object _disposeLock = new();
    private Task? _disposeTask;
    private long _nextConnectionId;
    private long _accepted;
    private long _rejected;
    private long _active;
    private long _maxActive;
    private long _clientToServerBytes;
    private long _serverToClientBytes;
    private long _delayedChunks;
    private long _resetEvents;
    private long _resetConnections;
    private long _stallEvents;
    private long _stalledChunks;
    private long _stalledConnections;
    private long _failures;
    private int _delayMin = int.MaxValue;
    private int _delayMax;
    private int _faultsCompleted;

    public LocalFaultProxy(LocalFaultSettings settings, string runDirectory)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        if (string.IsNullOrWhiteSpace(runDirectory)) throw new ArgumentException("Run directory is required.", nameof(runDirectory));
        _runDirectory = Path.GetFullPath(runDirectory);
        Directory.CreateDirectory(_runDirectory);
        _slots = new SemaphoreSlim(settings.MaxConnections, settings.MaxConnections);
        _listener = new TcpListener(IPAddress.Loopback, ListenPort);

        try
        {
            _listener.Start(settings.MaxConnections);
            _acceptTask = AcceptLoopAsync();
            _schedulerTask = ScheduleFaultsAsync();
        }
        catch
        {
            _listener.Stop();
            _slots.Dispose();
            _stop.Dispose();
            throw;
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_disposeLock)
            _disposeTask ??= DisposeCoreAsync();
        return new ValueTask(_disposeTask);
    }

    private async Task AcceptLoopAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                TcpClient client;
                try { client = await _listener.AcceptTcpClientAsync(_stop.Token); }
                catch (OperationCanceledException) when (_stop.IsCancellationRequested) { break; }
                catch (ObjectDisposedException) when (_stop.IsCancellationRequested) { break; }

                Interlocked.Increment(ref _accepted);
                if (!_slots.Wait(0))
                {
                    Interlocked.Increment(ref _rejected);
                    client.Dispose();
                    continue;
                }

                var id = Interlocked.Increment(ref _nextConnectionId);
                var connection = new RelayConnection(id, client, _stop.Token, _settings, this);
                if (!_connections.TryAdd(id, connection))
                {
                    _slots.Release();
                    connection.Dispose();
                    continue;
                }
                var active = Interlocked.Increment(ref _active);
                UpdateMaximum(ref _maxActive, active);
                connection.Completion = RunConnectionAsync(connection);
            }
        }
        catch (SocketException) when (_stop.IsCancellationRequested) { }
        catch (Exception) when (!_stop.IsCancellationRequested) { Interlocked.Increment(ref _failures); }
    }

    private async Task RunConnectionAsync(RelayConnection connection)
    {
        try { await connection.RunAsync(); }
        // Every cancellation originates in this connection's bounded connect/read/write lifetime.
        catch (OperationCanceledException) { }
        catch (SocketException) { }
        catch (IOException) { }
        catch (Exception) { Interlocked.Increment(ref _failures); }
        finally
        {
            connection.Dispose();
            Interlocked.Decrement(ref _active);
            _slots.Release();
            _connections.TryRemove(KeyValuePair.Create(connection.Id, connection));
        }
    }

    private async Task ScheduleFaultsAsync()
    {
        try
        {
            await DelayUntilAsync(ResetAt, _stop.Token);
            if (_stop.IsCancellationRequested) return;
            var reset = _connections.Values.ToArray();
            Interlocked.Increment(ref _resetEvents);
            foreach (var connection in reset)
                if (connection.Stop()) Interlocked.Increment(ref _resetConnections);

            await DelayUntilAsync(StallStart, _stop.Token);
            if (_stop.IsCancellationRequested) return;
            Interlocked.Increment(ref _stallEvents);
            await DelayUntilAsync(StallEnd, _stop.Token);
            if (!_stop.IsCancellationRequested) Volatile.Write(ref _faultsCompleted, 1);
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (Exception) when (!_stop.IsCancellationRequested) { Interlocked.Increment(ref _failures); }
    }

    private async Task DelayUntilAsync(TimeSpan target, CancellationToken cancellationToken)
    {
        var remaining = target - _elapsed.Elapsed;
        if (remaining > TimeSpan.Zero) await Task.Delay(remaining, cancellationToken);
    }

    private async Task WaitForForwardPermissionAsync(CancellationToken cancellationToken, Action stalled)
    {
        var recordedStall = false;
        while (true)
        {
            var elapsed = _elapsed.Elapsed;
            if (elapsed < StallStart || elapsed >= StallEnd) return;
            if (!recordedStall)
            {
                recordedStall = true;
                stalled();
            }
            await Task.Delay(StallEnd - elapsed, cancellationToken);
        }
    }

    private int NextDelay(ref uint state)
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        var jitter = _settings.JitterMs == 0 ? 0 : (int)(state % (uint)(_settings.JitterMs + 1));
        return _settings.BaseDelayMs + jitter;
    }

    private void RecordDelay(int delay)
    {
        Interlocked.Increment(ref _delayedChunks);
        UpdateMinimum(ref _delayMin, delay);
        UpdateMaximum(ref _delayMax, delay);
    }

    private static void UpdateMinimum(ref int location, int candidate)
    {
        while (true)
        {
            var current = Volatile.Read(ref location);
            if (candidate >= current || Interlocked.CompareExchange(ref location, candidate, current) == current) return;
        }
    }

    private static void UpdateMaximum(ref long location, long candidate)
    {
        while (true)
        {
            var current = Volatile.Read(ref location);
            if (candidate <= current || Interlocked.CompareExchange(ref location, candidate, current) == current) return;
        }
    }

    private static void UpdateMaximum(ref int location, int candidate)
    {
        while (true)
        {
            var current = Volatile.Read(ref location);
            if (candidate <= current || Interlocked.CompareExchange(ref location, candidate, current) == current) return;
        }
    }

    private async Task DisposeCoreAsync()
    {
        try
        {
            _stop.Cancel();
            _listener.Stop();
            foreach (var connection in _connections.Values) connection.Stop();
            await AwaitOwnedAsync(_acceptTask);
            await AwaitOwnedAsync(_schedulerTask);
            while (!_connections.IsEmpty)
            {
                var completions = _connections.Values.Select(connection => connection.Completion ?? Task.CompletedTask).ToArray();
                if (completions.Length == 0) break;
                await Task.WhenAll(completions);
            }
        }
        finally
        {
            try { WriteStatistics(); }
            finally
            {
                _listener.Dispose();
                _slots.Dispose();
                _stop.Dispose();
            }
        }
    }

    private async Task AwaitOwnedAsync(Task task)
    {
        try { await task; }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (SocketException) { }
        catch (Exception) { Interlocked.Increment(ref _failures); }
    }

    private void WriteStatistics()
    {
        var minimum = Volatile.Read(ref _delayMin);
        var report = new
        {
            profile = _settings.Profile,
            seed = _settings.Seed,
            elapsedMilliseconds = (long)_elapsed.Elapsed.TotalMilliseconds,
            accepted = Interlocked.Read(ref _accepted),
            rejected = Interlocked.Read(ref _rejected),
            maxActive = Interlocked.Read(ref _maxActive),
            clientToServerBytes = Interlocked.Read(ref _clientToServerBytes),
            serverToClientBytes = Interlocked.Read(ref _serverToClientBytes),
            delayedChunks = Interlocked.Read(ref _delayedChunks),
            delayMinMilliseconds = minimum == int.MaxValue ? 0 : minimum,
            delayMaxMilliseconds = Volatile.Read(ref _delayMax),
            resetEvents = Interlocked.Read(ref _resetEvents),
            resetConnections = Interlocked.Read(ref _resetConnections),
            stallEvents = Interlocked.Read(ref _stallEvents),
            stalledChunks = Interlocked.Read(ref _stalledChunks),
            stalledConnections = Interlocked.Read(ref _stalledConnections),
            faultsCompleted = Volatile.Read(ref _faultsCompleted) != 0,
            failures = Interlocked.Read(ref _failures),
            activeAtStop = Interlocked.Read(ref _active)
        };
        File.WriteAllText(Path.Combine(_runDirectory, "network-faults.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }

    private sealed class RelayConnection : IDisposable
    {
        private readonly TcpClient _client;
        private readonly CancellationTokenSource _stop;
        private readonly LocalFaultSettings _settings;
        private readonly LocalFaultProxy _owner;
        private TcpClient? _server;
        private int _disposed;
        private int _stopped;
        private int _stalled;

        public RelayConnection(long id, TcpClient client, CancellationToken parent, LocalFaultSettings settings, LocalFaultProxy owner)
        {
            Id = id;
            _client = client;
            _stop = CancellationTokenSource.CreateLinkedTokenSource(parent);
            _settings = settings;
            _owner = owner;
        }

        public long Id { get; }
        public Task? Completion { get; set; }
        public bool IsStopping => _stop.IsCancellationRequested || Volatile.Read(ref _stopped) != 0;

        public async Task RunAsync()
        {
            _client.NoDelay = true;
            _server = new TcpClient(AddressFamily.InterNetwork) { NoDelay = true };
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            await _server.ConnectAsync(IPAddress.Loopback, TargetPort, timeout.Token);
            var clientStream = _client.GetStream();
            var serverStream = _server.GetStream();
            var c2s = CopyAsync(clientStream, serverStream, true, _stop.Token);
            var s2c = CopyAsync(serverStream, clientStream, false, _stop.Token);
            await Task.WhenAny(c2s, s2c);
            Stop();
            try { await Task.WhenAll(c2s, s2c); }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
            catch (IOException) { }
            catch (SocketException) { }
        }

        private async Task CopyAsync(NetworkStream source, NetworkStream destination, bool clientToServer, CancellationToken cancellationToken)
        {
            var buffer = new byte[_settings.BufferBytes];
            var state = (uint)(_settings.Seed ^ (int)Id ^ (clientToServer ? unchecked((int)0x9E3779B9) : unchecked((int)0x85EBCA6B)));
            if (state == 0) state = 1;
            while (true)
            {
                var count = await source.ReadAsync(buffer.AsMemory(), cancellationToken);
                if (count == 0) return;
                await _owner.WaitForForwardPermissionAsync(cancellationToken, RecordStall);
                var delay = _owner.NextDelay(ref state);
                _owner.RecordDelay(delay);
                await Task.Delay(TimeSpan.FromMilliseconds(delay), cancellationToken);
                await _owner.WaitForForwardPermissionAsync(cancellationToken, RecordStall);
                await destination.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                if (clientToServer) Interlocked.Add(ref _owner._clientToServerBytes, count);
                else Interlocked.Add(ref _owner._serverToClientBytes, count);
            }
        }

        public bool Stop()
        {
            var first = Interlocked.Exchange(ref _stopped, 1) == 0;
            if (!_stop.IsCancellationRequested) try { _stop.Cancel(); }
            catch (ObjectDisposedException) { }
            CloseSockets();
            return first;
        }

        private void RecordStall()
        {
            Interlocked.Increment(ref _owner._stalledChunks);
            if (Interlocked.Exchange(ref _stalled, 1) == 0) Interlocked.Increment(ref _owner._stalledConnections);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _ = Stop();
            _client.Dispose();
            _server?.Dispose();
            _stop.Dispose();
        }

        private void CloseSockets()
        {
            Shutdown(_client);
            Shutdown(_server);
        }

        private static void Shutdown(TcpClient? client)
        {
            try
            {
                var socket = client?.Client;
                if (socket is not null) socket.Shutdown(SocketShutdown.Both);
            }
            catch (SocketException) { }
            catch (ObjectDisposedException) { }
        }
    }
}

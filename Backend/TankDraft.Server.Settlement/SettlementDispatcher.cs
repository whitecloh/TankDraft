namespace TankDraft.Server.Settlement;

public sealed class SettlementDispatcher : IDisposable
{
    private readonly SqliteSettlementStore _store;
    private readonly IRewardProvider _provider;
    private readonly SemaphoreSlim _singleFlight = new(1, 1);
    private bool _disposed;
    public SettlementDispatcher(SqliteSettlementStore store, IRewardProvider provider) { _store = store ?? throw new ArgumentNullException(nameof(store)); _provider = provider ?? throw new ArgumentNullException(nameof(provider)); }
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed(); if (!await _singleFlight.WaitAsync(0).ConfigureAwait(false)) return 0;
        try
        {
            var applied = 0;
            foreach (var receipt in _store.ReadPending())
            {
                if (cancellationToken.IsCancellationRequested) break;
                if (!_store.TryBegin(receipt.Grant)) continue;
                try
                {
                    await _provider.GrantAsync(receipt.Grant, cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    _store.MarkNeedsReview(receipt.Grant, "provider_uncertain");
                    continue;
                }

                _store.MarkApplied(receipt.Grant);
                applied++;
            }
            return applied;
        }
        finally { _singleFlight.Release(); }
    }
    public void Dispose() { if (_disposed) return; _disposed = true; _singleFlight.Dispose(); }
    private void ThrowIfDisposed() { if (_disposed) throw new ObjectDisposedException(nameof(SettlementDispatcher)); }
}

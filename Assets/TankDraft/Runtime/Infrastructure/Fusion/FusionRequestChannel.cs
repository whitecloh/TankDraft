#nullable disable
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace TankDraft.Infrastructure.FusionTransport
{
    /// <summary>One bounded request per authenticated connection. Never reuse for another runner.</summary>
    public sealed class FusionRequestChannel : IDisposable
    {
        readonly object sync = new object();
        readonly Action<int, byte[]> send;
        readonly Func<bool> secureConnection;
        readonly TimeSpan timeout;
        readonly bool plaintextQa;
        TaskCompletionSource<byte[]> pending;
        int sequence;
        bool closed;
        public FusionRequestChannel(Action<int, byte[]> send, Func<bool> secureConnection, TimeSpan timeout, bool allowPlaintextQa = false)
        {
            this.send = send ?? throw new ArgumentNullException(nameof(send));
            this.secureConnection = secureConnection ?? throw new ArgumentNullException(nameof(secureConnection));
            if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromSeconds(30)) throw new ArgumentOutOfRangeException(nameof(timeout));
            this.timeout = timeout;
            plaintextQa = allowPlaintextQa;
        }
        public async Task<byte[]> ExchangeAsync(byte[] request, CancellationToken token)
        {
            if (request == null || request.Length == 0 || request.Length > 8192) throw new InvalidDataException();
            token.ThrowIfCancellationRequested();
            if (plaintextQa) FusionQaProtocol.ReadRequest(request);
            TaskCompletionSource<byte[]> completion;
            int id;
            lock (sync)
            {
                if (closed) throw new ObjectDisposedException(nameof(FusionRequestChannel));
                // SDK adapter must derive this from verified transport state, never a client config flag.
                if (!plaintextQa && !secureConnection()) throw new InvalidOperationException("fusion_secure_transport_required");
                if (pending != null) throw new InvalidOperationException("fusion_request_pending");
                if (sequence == int.MaxValue) throw new InvalidOperationException("fusion_connection_exhausted");
                id = ++sequence;
                pending = completion = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            }
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                deadline.CancelAfter(timeout);
                using (deadline.Token.Register(() => Cancel(completion)))
                {
                    try
                    {
                        lock (sync)
                        {
                            if (ReferenceEquals(pending, completion))
                            {
                                if (!plaintextQa && !secureConnection()) throw new InvalidOperationException("fusion_secure_transport_required");
                                send(id, (byte[])request.Clone());
                            }
                        }
                        return await completion.Task.ConfigureAwait(false);
                    }
                    finally { lock (sync) { if (ReferenceEquals(pending, completion)) pending = null; } }
                }
            }
        }
        // Late callbacks must target their original channel instance, which is disposed on disconnect.
        public bool CanSend(int id) { lock (sync) return !closed && pending != null && sequence == id; }
        public bool Receive(int id, byte[] response)
        {
            lock (sync)
            {
                if (closed || pending == null || id != sequence) return false;
                var completion = pending; pending = null;
                if (!plaintextQa && !secureConnection()) completion.TrySetException(new InvalidOperationException("fusion_secure_transport_required"));
                else if (response == null || response.Length == 0 || response.Length > 1048704) completion.TrySetException(new InvalidDataException());
                else
                {
                    try { if (plaintextQa) FusionQaProtocol.ReadResponse(response); completion.TrySetResult((byte[])response.Clone()); }
                    catch { completion.TrySetException(new InvalidDataException("fusion_qa_response_rejected")); }
                }
                return true;
            }
        }
        void Cancel(TaskCompletionSource<byte[]> completion)
        { lock (sync) { if (ReferenceEquals(pending, completion)) { pending = null; completion.TrySetCanceled(); } } }
        public void Dispose()
        { lock (sync) { closed = true; var completion = pending; pending = null; completion?.TrySetException(new IOException("fusion_connection_closed")); } }
    }
}

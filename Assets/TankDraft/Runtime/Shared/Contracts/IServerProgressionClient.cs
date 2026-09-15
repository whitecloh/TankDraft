using System;
using System.Threading;
using System.Threading.Tasks;

namespace TankDraft.Contracts
{
    public sealed class ProgressionRejectedException : Exception
    {
        public string Code { get; }
        public ProgressionRejectedException(string code) : base(code) { Code = code; }
    }
    public interface IServerProgressionClient
    {
        Task<ProgressionSnapshot> GetAsync(CancellationToken token);
        Task<ProgressionSnapshot> ExecuteAsync(string kind, string targetId, Guid operationId, long expectedSequence, CancellationToken token);
    }
}

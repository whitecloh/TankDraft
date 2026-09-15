using System;
using System.Threading;
using System.Threading.Tasks;

namespace TankDraft.Contracts
{
    /// <summary>
    /// Trusted profile boundary. Implementations return null only when the
    /// connected authoritative server explicitly enables legacy local QA mode.
    /// </summary>
    public interface IServerProfileClient
    {
        Task<ProfileSnapshot> GetAsync(CancellationToken token);
        Task<ProfileSnapshot> SaveLoadoutAsync(string[] units, string[] orders, CancellationToken token);
    }

    /// <summary>
    /// The server has supplied a newer profile after a definitive optimistic
    /// concurrency rejection. Callers must replace their view before creating
    /// another player intent; they must never replay an old candidate at the
    /// newer version automatically.
    /// </summary>
    public sealed class ServerProfileRefreshRequiredException : Exception
    {
        public ProfileSnapshot Snapshot { get; }
        public ServerProfileRefreshRequiredException(ProfileSnapshot snapshot)
            : base("server_profile_refresh_required")
        {
            Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        }
    }
}

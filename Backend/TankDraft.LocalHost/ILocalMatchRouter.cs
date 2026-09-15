using TankDraft.Server.Match;
using TankDraft.Server.Security;

namespace TankDraft.LocalHost;

public interface ILocalMatchRouter
{
    bool Pump();
    ServerMatchSnapshot Capture(CallerIdentity caller, long? cursor = null);
    CommandReply Execute(CallerIdentity caller, CommandEnvelope command);
    long NextCommandSequence(CallerIdentity caller);
}

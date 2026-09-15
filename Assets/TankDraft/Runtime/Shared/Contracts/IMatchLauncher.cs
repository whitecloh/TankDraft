namespace TankDraft.Contracts
{
    public interface IMatchLauncher
    {
        bool TryLaunch(ProfileSnapshot profile, out string reason);
    }

    // Presentation can defer background refresh while queue/navigation owns the menu.
    public interface IMenuMatchState
    {
        bool IsMenuIdle { get; }
    }
}

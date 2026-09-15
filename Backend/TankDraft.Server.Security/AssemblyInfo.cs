using System.Runtime.CompilerServices;

// Only the trusted replay adapter reconstructs previously authenticated command identities.
[assembly: InternalsVisibleTo("TankDraft.Server.Persistence")]
[assembly: InternalsVisibleTo("TankDraft.Server.Admission")]

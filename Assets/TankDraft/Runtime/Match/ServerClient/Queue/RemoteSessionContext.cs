using System;
using System.IO;

namespace TankDraft.Match.ServerClient
{
    // Created only by the main-thread PlayFab bootstrap. No credential is persisted to disk or environment.
    public sealed class RemoteSessionContext : IMatchCredentialsFactory
    {
        public static RemoteSessionContext Current { get; private set; }
        public static string BootstrapError { get; private set; }
        public static bool IsRequested => Current != null || !string.IsNullOrEmpty(BootstrapError);
        public readonly IPlayFabSessionSource Tickets;
        public readonly Uri BaseUri;
        public readonly string ContentVersion, RunDirectory;
        public string LobbyToken, PendingOperation, PendingJoinOperation, InstanceId, MatchId, PendingLeaveMatchId;
        public long LobbyValidUntil;
        public int LobbyExpiresSeconds, Side;
        public RemoteSessionContext(IPlayFabSessionSource tickets, Uri baseUri, string contentVersion, string runDirectory)
        {
            if (tickets == null || baseUri == null || baseUri.Scheme != Uri.UriSchemeHttps || string.IsNullOrEmpty(baseUri.DnsSafeHost) ||
                baseUri.HostNameType != UriHostNameType.Dns || !string.IsNullOrEmpty(baseUri.UserInfo) || baseUri.AbsolutePath != "/" ||
                !string.IsNullOrEmpty(baseUri.Query) || !string.IsNullOrEmpty(baseUri.Fragment) || string.IsNullOrEmpty(contentVersion) || string.IsNullOrEmpty(runDirectory)) throw new InvalidOperationException("Invalid remote session context.");
            Tickets = tickets; BaseUri = new UriBuilder(baseUri) { Path = "/", Query = "", Fragment = "" }.Uri; ContentVersion = contentVersion; RunDirectory = Path.GetFullPath(runDirectory);
        }
        public static void SetCurrent(RemoteSessionContext value) { if (value == null) throw new ArgumentNullException(nameof(value)); Current = value; BootstrapError = null; }
        public static void SetBootstrapError(string value) { Current = null; BootstrapError = string.IsNullOrWhiteSpace(value) ? "Настройки удалённого теста недоступны." : value; }
        public static void Reset() { Current = null; BootstrapError = null; }
        public IMatchCredentials Create()
        {
            if (string.IsNullOrEmpty(MatchId) || Side < 0 || Side > 1) throw new InvalidOperationException("Remote match assignment missing.");
            return new PlayFabMatchCredentials(new UriBuilder(BaseUri) { Scheme = "wss", Port = BaseUri.IsDefaultPort ? -1 : BaseUri.Port, Path = "/v1/socket" }.Uri, MatchId, Side, ContentVersion, RunDirectory, Tickets);
        }
    }
}

// Execute through Unity MCP. The persistent editor assembly survives build/domain reloads.
public static class TankDraftServerClientBuild
{
    public static string Queue() => TankDraft.Match.ServerClient.Editor.ServerClientBuild.Queue();
}
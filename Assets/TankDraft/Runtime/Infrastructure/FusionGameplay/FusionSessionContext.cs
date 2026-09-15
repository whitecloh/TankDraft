using System;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TankDraft.Match.ServerClient;

namespace TankDraft.Infrastructure.FusionGameplay
{
    public sealed class FusionSessionContext : IMatchCredentialsFactory, IMatchSessionNavigation, IMatchOpponentInfo
    {
        public FusionQaRequests Requests { get; private set; }
        public string InstanceId { get; private set; }
        public string ContentVersion { get; private set; }
        public string RunDirectory { get; private set; }
        public string ProgressionJournalScope => resume?.ProgressionJournalScope;
        Func<string, Task> loadScene;
        public string PendingLeave { get; private set; }
        public bool IsBot { get; private set; }
        string matchId, matchDirectory;
        FusionResumeStorage resume;
        int side;
        public void Initialize(FusionQaRequests requests, string instanceId, string version, string directory, Func<string, Task> sceneLoader, FusionResumeStorage resumeStorage)
        {
            if (Requests != null) throw new InvalidOperationException("Session already initialized.");
            resume = resumeStorage ?? throw new ArgumentNullException(nameof(resumeStorage));
            Requests = requests; InstanceId = instanceId; ContentVersion = version; RunDirectory = directory; loadScene = sceneLoader;
        }
        public Task LoadMenuAsync(string scene) => loadScene(scene);
        public Task LoadMatchAsync(string scene) => loadScene(scene);
        public void Assign(JObject assignment)
        {
            if (assignment.Value<string>("State") != "Matched" || assignment.Value<string>("InstanceId") != InstanceId ||
                assignment["Side"]?.Type != JTokenType.Integer) throw new InvalidDataException("Unverified assignment.");
            var key = resume.MatchKey(assignment.Value<string>("MatchId"), assignment.Value<int>("Side"));
            if (matchId == assignment.Value<string>("MatchId") && matchDirectory != null) return;
            matchId = assignment.Value<string>("MatchId"); side = assignment.Value<int>("Side");
            IsBot = assignment.Value<string>("OpponentKind") == "Bot";
            // An opaque local directory never trusts a server identifier as a filesystem path.
            matchDirectory = Path.Combine(RunDirectory, "match-" + key);
            Directory.CreateDirectory(matchDirectory);
            File.WriteAllText(Path.Combine(matchDirectory, "assignment.json"), assignment.ToString(Formatting.Indented));
            File.AppendAllText(Path.Combine(RunDirectory, "assignments.jsonl"), assignment.ToString(Formatting.None) + Environment.NewLine);
        }
        public IMatchCredentials Create()
        {
            if (matchId == null || PendingLeave != null) throw new InvalidOperationException("No playable assignment.");
            return new FusionMatchCredentials(Requests, matchId, side, ContentVersion, matchDirectory, resume.JournalPath(matchId, side));
        }
        public void PrepareReturn(string completedMatchId)
        {
            if (matchId != completedMatchId) throw new InvalidOperationException("Match changed.");
            PendingLeave = matchId;
        }
        public void ConfirmLeave() { PendingLeave = null; matchId = null; matchDirectory = null; }
    }
}

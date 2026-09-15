using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TankDraft.Infrastructure.FusionTransport;
using TankDraft.Match.ServerClient;
using UnityEngine;
using UnityEngine.SceneManagement;
using VContainer;
using VContainer.Unity;

namespace TankDraft.Infrastructure.FusionGameplay
{
    // Authored QA entry scene. Reuses ServerMatch and its existing presentation/session implementation.
    public sealed class FusionGameClientFlow : MonoBehaviour
    {
        [SerializeField] FusionDedicatedBootstrap connection;
        [SerializeField] ServerClientSettings settings;
        [SerializeField] string menuScene = "MainMenu";
        readonly CancellationTokenSource stop = new CancellationTokenSource();
        string logs;
        string stage = "validate";
        FusionSessionContext context;
        [Inject] public void Construct(FusionSessionContext value) { context = value; }
        async void Start()
        {
            try
            {
                if (!connection || !settings) throw new InvalidOperationException();
                settings.Validate(); DontDestroyOnLoad(gameObject);
                using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(stop.Token))
                {
                    deadline.CancelAfter(TimeSpan.FromSeconds(60));
                    stage = "runner";
                    while (connection.QaClient == null) await Task.Delay(50, deadline.Token);
                    logs = connection.QaPresentationDirectory;
                    if (string.IsNullOrEmpty(logs)) logs = Path.Combine(Application.persistentDataPath, "FusionQa", Guid.NewGuid().ToString("N"));
                    if (!Path.IsPathRooted(logs)) throw new InvalidDataException();
                    Directory.CreateDirectory(logs);
                    stage = "scope";
                    var stateRoot = Environment.GetEnvironmentVariable("TD_FUSION_STATE_DIRECTORY") ?? Path.Combine(Application.persistentDataPath, "FusionResume");
                    var resume = new FusionResumeStorage(stateRoot, connection.QaAccountId, connection.QaInstanceId, settings.ContentVersion);
                    context.Initialize(new FusionQaRequests(connection.QaClient), connection.QaInstanceId, settings.ContentVersion, logs, LoadScene, resume);
                    stage = "menu";
                    await LoadScene(menuScene);
                }
            }
            catch (OperationCanceledException) { if (!stop.IsCancellationRequested) Debug.LogError("FUSION_QA_GAME_FLOW_TIMEOUT"); }
            catch (Exception error) { Debug.LogError("FUSION_QA_GAME_FLOW_FAILED stage=" + stage + " type=" + error.GetType().Name); }
        }
        async Task LoadScene(string scene)
        {
            using (LifetimeScope.EnqueueParent(GetComponent<FusionClientLifetimeScope>()))
            {
                var load = SceneManager.LoadSceneAsync(scene);
                while (!load.isDone) await Task.Yield();
            }
        }
        void OnDestroy() { stop.Cancel(); }
    }
}

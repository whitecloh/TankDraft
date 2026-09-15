using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TankDraft.Match.ServerClient;
using UnityEngine;

namespace TankDraft.Identity.PlayFab
{
    // Private, provisioned test identity. Never included in assets or player builds.
    // Production device identity and server discovery are separate from this closed-test entry point.
    public static class RemotePlayFabBootstrap
    {
        static UnityPlayFabSessionSource _identity;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset()
        {
            Application.quitting -= Shutdown;
            Shutdown();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Initialize()
        {
#if UNITY_ANDROID && TANKDRAFT_REMOTE_ANDROID_QA
            RemoteSessionContext.SetBootstrapError("Удалённый тест ещё не настроен. Ожидается адрес тестового сервера.");
            Environment.SetEnvironmentVariable("TD_REMOTE_AUTO_QUEUE", null);
            if (!Debug.isDebugBuild) return;
            try
            {
                using (var unity = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = unity.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var files = activity.Call<AndroidJavaObject>("getNoBackupFilesDir"))
                using (var intent = activity.Call<AndroidJavaObject>("getIntent"))
                {
                    var autoQueue = intent != null && intent.Call<bool>("getBooleanExtra", "tankdraft.remoteAutoQueue", false);
                    var privateRoot = files.Call<string>("getAbsolutePath");
                    var path = Path.Combine(privateRoot, "td-remote-bootstrap.json");
                    if (!File.Exists(path)) return;
                    Load(path, Path.Combine(privateRoot, "remote-evidence"));
                    Environment.SetEnvironmentVariable("TD_REMOTE_AUTO_QUEUE", autoQueue ? "1" : null);
                }
            }
            catch { Reject(); }
#else
            var path = Environment.GetEnvironmentVariable("TD_REMOTE_BOOTSTRAP_PATH");
            if (string.IsNullOrEmpty(path)) return;
            RemoteSessionContext.SetBootstrapError("Настройки удалённого теста недоступны.");
            try
            {
                if (Application.platform != RuntimePlatform.WindowsPlayer && Application.platform != RuntimePlatform.WindowsEditor)
                    throw new InvalidDataException();
                var privateRoot = Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex-secrets", "TankDraft")) + Path.DirectorySeparatorChar;
                var fullPath = Path.GetFullPath(path);
                if (!Path.IsPathRooted(path) || !fullPath.StartsWith(privateRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException();
                Load(fullPath, null);
            }
            catch { Reject(); }
#endif
        }

        static void Load(string fullPath, string androidRunDirectory)
        {
                var file = new FileInfo(fullPath);
                if (!file.Exists || file.Length < 16 || file.Length > 4096 || (file.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException();
                JObject config;
                using (var reader = new JsonTextReader(new StringReader(File.ReadAllText(fullPath))) { MaxDepth = 3, DateParseHandling = DateParseHandling.None })
                {
                    config = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                    if (reader.Read()) throw new InvalidDataException();
                }
                var names = androidRunDirectory == null
                    ? new[] { "TitleId", "CustomId", "BaseUri", "ContentVersion", "RunDirectory" }
                    : new[] { "TitleId", "CustomId", "BaseUri", "ContentVersion" };
                if (config.Count != names.Length || config.Properties().Any(p => !names.Contains(p.Name) || p.Value.Type != JTokenType.String)) throw new InvalidDataException();
                if (config.Value<string>("TitleId") != "B16D9") throw new InvalidDataException();
                if (!System.Text.RegularExpressions.Regex.IsMatch(config.Value<string>("ContentVersion"), "^[a-f0-9]{64}$") ||
                    !System.Text.RegularExpressions.Regex.IsMatch(config.Value<string>("CustomId"), "^tankdraft-r1-[a-f0-9]{64}$")) throw new InvalidDataException();
                var endpoint = new Uri(config.Value<string>("BaseUri"));
                if (androidRunDirectory != null && (endpoint.Scheme != "https" || endpoint.Port < 1 ||
                    !System.Text.RegularExpressions.Regex.IsMatch(endpoint.DnsSafeHost, "^[a-f0-9]{12,64}\\.pr\\.edgegap\\.net$") ||
                    endpoint.AbsolutePath != "/" || endpoint.UserInfo.Length != 0 || endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0))
                    throw new InvalidDataException();
                var runDirectory = androidRunDirectory ?? config.Value<string>("RunDirectory");
                if (androidRunDirectory != null) Directory.CreateDirectory(runDirectory);
                _identity = new UnityPlayFabSessionSource("B16D9", config.Value<string>("CustomId"));
                RemoteSessionContext.SetCurrent(new RemoteSessionContext(_identity, endpoint, config.Value<string>("ContentVersion"), runDirectory));
                Application.quitting += Shutdown;
        }

        static void Reject()
        {
                _identity?.Dispose(); _identity = null;
                // No exception text: file paths, parsed identity values and provider responses are private.
                Debug.LogError("Remote test bootstrap rejected; local matchmaking is disabled for this launch.");
        }

        static void Shutdown()
        {
            _identity?.Dispose(); _identity = null;
            RemoteSessionContext.Reset();
        }
    }
}

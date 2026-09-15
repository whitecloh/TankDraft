#if UNITY_ANDROID && !UNITY_EDITOR && DEVELOPMENT_BUILD && TANKDRAFT_LOCAL_QA
using System;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace TankDraft.Match.ServerClient
{
    /// <summary>Loads one private, launcher-provisioned local QA grant before any scene code runs.</summary>
    public static class LocalAndroidQaBootstrap
    {
        const int BootstrapBytes = 4096;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Initialize()
        {
            Application.targetFrameRate = 60;
            try
            {
                var files = AndroidFilesDirectory();
                var bootstrap = Path.Combine(files, "td-bootstrap.json");
                var json = ReadBootstrap(bootstrap);
                var value = ServerWire.Parse(json);
                var grant = RequiredString(value, "Grant", 43);
                var pin = RequiredString(value, "Pin", 64);
                var runId = RequiredString(value, "RunId", 32);
                var queue = value["Queue"];
                if ((value.Count != 4 && value.Count != 5) || !IsBase64Url(grant) || !IsHex(pin) || !IsHex(runId) ||
                    value["Auto"] == null || value["Auto"].Type != JTokenType.Boolean)
                    throw new InvalidDataException("Invalid local QA bootstrap.");
                if ((value.Count == 5 && queue == null) || queue != null && queue.Type != JTokenType.Boolean)
                    throw new InvalidDataException("Invalid local QA bootstrap.");

                // A private queue credential survives app restarts for this bounded local run.
                // The owning launcher removes it when the server stops. Legacy one-match grants remain one-shot.
                if (queue == null || !queue.Value<bool>()) File.Delete(bootstrap);
                var run = Path.Combine(files, "qa", runId);
                Directory.CreateDirectory(run);
                Environment.SetEnvironmentVariable("TD_LOCAL_ENDPOINT", "wss://127.0.0.1:18783/v1/socket");
                Environment.SetEnvironmentVariable("TD_LOCAL_GRANT", grant);
                Environment.SetEnvironmentVariable("TD_LOCAL_TLS_PIN", pin);
                Environment.SetEnvironmentVariable("TD_LOCAL_SIDE", "1");
                Environment.SetEnvironmentVariable("TD_LOCAL_RUN", run);
                Environment.SetEnvironmentVariable("TD_LOCAL_AUTO", value.Value<bool>("Auto") ? "1" : "0");
                Environment.SetEnvironmentVariable("TD_LOCAL_ANDROID", "1");
                Environment.SetEnvironmentVariable("TD_LOCAL_RESTARTED", "1");
                if (queue != null && queue.Value<bool>())
                {
                    Environment.SetEnvironmentVariable("TD_QUEUE_MODE", "1");
                    Environment.SetEnvironmentVariable("TD_QUEUE_GRANT", grant);
                    Environment.SetEnvironmentVariable("TD_QUEUE_RUN", run);
                    Environment.SetEnvironmentVariable("TD_QUEUE_AUTOJOIN", value.Value<bool>("Auto") ? "1" : "0");
                    Environment.SetEnvironmentVariable("TD_QUEUE_AUTO_REMAINING", "2");
                }
            }
            catch
            {
                Debug.LogError("Local Android QA bootstrap unavailable.");
            }
        }

        static string AndroidFilesDirectory()
        {
            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var files = activity.Call<AndroidJavaObject>("getFilesDir"))
                return files.Call<string>("getAbsolutePath");
        }

        static string ReadBootstrap(string path)
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length < 2 || info.Length > BootstrapBytes) throw new InvalidDataException("Invalid local QA bootstrap.");
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length < 2 || bytes.Length > BootstrapBytes) throw new InvalidDataException("Invalid local QA bootstrap.");
            return new UTF8Encoding(false, true).GetString(bytes);
        }

        static string RequiredString(JObject value, string name, int length)
        {
            var token = value[name];
            if (token == null || token.Type != JTokenType.String) throw new InvalidDataException("Invalid local QA bootstrap.");
            var text = token.Value<string>();
            if (text == null || text.Length != length) throw new InvalidDataException("Invalid local QA bootstrap.");
            return text;
        }

        static bool IsBase64Url(string value)
        {
            foreach (var c in value)
                if (!(c >= 'A' && c <= 'Z') && !(c >= 'a' && c <= 'z') && !(c >= '0' && c <= '9') && c != '-' && c != '_') return false;
            return true;
        }

        static bool IsHex(string value)
        {
            foreach (var c in value)
                if (!Uri.IsHexDigit(c)) return false;
            return true;
        }
    }
}
#endif

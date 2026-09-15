using System;
using System.IO;
using System.Linq;
using Fusion;
using Fusion.Editor;
using Fusion.Photon.Realtime;
using UnityEditor;
using UnityEngine;

namespace TankDraft.Editor.FusionSetup
{
    /// <summary>Local SDK configuration only. Never starts a runner or changes a cloud account.</summary>
    public static class FusionProjectSetup
    {
        const string AppId = "92d5f593-3b30-4493-8168-ca40ae0e55b0";
        const string AppVersion = "tankdraft-fusion-qa-v1";

        [MenuItem("TankDraft/Networking/Fusion/Configure Local SDK")]
        public static void Configure()
        {
            var settings = PhotonAppSettings.Global;
            settings.AppSettings.AppIdFusion = AppId;
            settings.AppSettings.AppVersion = AppVersion;
            settings.AppSettings.FixedRegion = "eu";
            EditorUtility.SetDirty(settings);
            var config = NetworkProjectConfig.Global;
            config.Simulation.PlayerCount = 2;
            // Approved temporary closed QA; encrypted deployment remains a separate gate.
            config.EncryptionConfig.EnableEncryption = false;
            // Dedicated process per initial diagnostic session. Multi-match hosting is a later measured decision.
            config.PeerMode = NetworkProjectConfig.PeerModes.Single;
            config.HostMigration.EnableAutoUpdate = false;
            NetworkProjectConfigUtilities.SaveGlobalConfig(config);
            AssetDatabase.SaveAssets();
        }

        [MenuItem("TankDraft/Networking/Fusion/Validate Local SDK")]
        public static void ValidateMenu() => Debug.Log(Validate());

        public static string Validate()
        {
            if (typeof(NetworkRunner).Assembly.GetName().Version != new Version(2, 1, 2, 0))
                throw new InvalidOperationException("Expected Fusion SDK 2.1.2; review migration before changing it.");
            if (EditorSettings.serializationMode != SerializationMode.ForceText)
                throw new InvalidOperationException("Fusion requires Force Text serialization.");
            var app = PhotonAppSettings.Global.AppSettings;
            if (app.AppIdFusion != AppId || app.AppVersion != AppVersion || app.FixedRegion != "eu")
                throw new InvalidOperationException("Fusion application configuration differs from the approved QA app.");
            var config = NetworkProjectConfig.Global;
            if (config.EncryptionConfig.EnableEncryption || config.Simulation.PlayerCount != 2)
                throw new InvalidOperationException("Current closed QA requires explicit plaintext configuration and two players.");
            if (config.HostMigration.EnableAutoUpdate)
                throw new InvalidOperationException("Dedicated authority must not enable client host migration.");
            if (Directory.Exists("Assets/Photon/PhotonUnityNetworking"))
                throw new InvalidOperationException("Retired PUN SDK is still installed.");
            if (AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "PhotonUnityNetworking"))
                throw new InvalidOperationException("Retired PUN assembly is loaded.");
            ValidateRunnerEncryptionPolicy();
            return "PASS Fusion SDK 2.1.2, application, EU, 1vs1 configuration, explicit closed plaintext QA policy, no PUN. " +
                   "Local setup only: cloud authentication, game adapter and remote match are not validated.";
        }

        private static void ValidateRunnerEncryptionPolicy()
        {
            var validate = typeof(TankDraft.Infrastructure.FusionTransport.FusionDedicatedBootstrap).GetMethod(
                "ValidateEncryptionPolicy", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            if (validate == null) throw new InvalidOperationException("Fusion runtime encryption policy missing.");
            validate.Invoke(null, new object[] { true, false });
            validate.Invoke(null, new object[] { false, true });
            foreach (bool enabled in new[] { false, true })
            {
                bool rejected = false;
                try { validate.Invoke(null, new object[] { enabled, enabled }); }
                catch (System.Reflection.TargetInvocationException error) when (error.InnerException is InvalidOperationException) { rejected = true; }
                if (!rejected) throw new InvalidOperationException("Mismatched runtime/encryption configuration must be rejected before connecting.");
            }
        }
    }
}

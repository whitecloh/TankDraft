using System;

namespace SLVR.UIMotion
{
    /// <summary>In-memory settings provider with no PlayerPrefs or project-service dependency.</summary>
    public sealed class UiMotionSettingsProvider : IUiMotionSettingsProvider
    {
        private UiMotionSettingsAsset asset;
        private UiMotionSettingsSnapshot? runtimeOverride;

        public UiMotionSettingsProvider(UiMotionSettingsAsset asset = null)
        {
            this.asset = asset;
        }

        public UiMotionSettingsSnapshot Current => runtimeOverride
            ?? (asset != null ? asset.CreateSnapshot() : UiMotionDefaults.Settings);

        public event Action Changed;

        public void SetAsset(UiMotionSettingsAsset value)
        {
            if (ReferenceEquals(asset, value))
            {
                return;
            }

            asset = value;
            Changed?.Invoke();
        }

        public void SetRuntimeOverride(UiMotionSettingsSnapshot value)
        {
            runtimeOverride = value;
            Changed?.Invoke();
        }

        public void ClearRuntimeOverride()
        {
            if (!runtimeOverride.HasValue)
            {
                return;
            }

            runtimeOverride = null;
            Changed?.Invoke();
        }
    }
}

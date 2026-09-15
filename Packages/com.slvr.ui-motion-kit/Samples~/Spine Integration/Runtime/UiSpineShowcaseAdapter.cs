using global::Spine.Unity;
using UnityEngine;

namespace SLVR.UIMotion.Spine
{
    /// <summary>Optional Spine adapter. Import this sample only in projects that contain spine-unity.</summary>
    [DisallowMultipleComponent]
    public sealed class UiSpineShowcaseAdapter : MonoBehaviour, IUiShowcaseAdapter
    {
        [SerializeField] private SkeletonGraphic skeleton;
        [SerializeField] private string entranceAnimation = "entrance";
        [SerializeField] private string idleAnimation = "idle";
        [SerializeField] private string selectionAnimation = "select";
        [SerializeField, Min(0)] private int trackIndex;

        public void PlayEntrance() => Play(entranceAnimation, false);
        public void PlayIdle() => Play(idleAnimation, true);
        public void PlaySelectionResponse() => Play(selectionAnimation, false);

        public void PauseIdle()
        {
            if (skeleton != null) skeleton.timeScale = 0f;
        }

        public void Stop()
        {
            if (skeleton == null || skeleton.AnimationState == null) return;
            skeleton.AnimationState.ClearTrack(trackIndex);
            skeleton.timeScale = 0f;
        }

        private void Play(string animationName, bool loop)
        {
            if (skeleton == null || skeleton.AnimationState == null || string.IsNullOrWhiteSpace(animationName)) return;
            if (skeleton.SkeletonDataAsset == null || skeleton.SkeletonDataAsset.GetSkeletonData(false)?.FindAnimation(animationName) == null)
            {
                Debug.LogWarning($"SLVR UI Motion: Spine animation '{animationName}' was not found.", this);
                return;
            }

            skeleton.timeScale = 1f;
            skeleton.AnimationState.SetAnimation(trackIndex, animationName, loop);
        }
    }
}

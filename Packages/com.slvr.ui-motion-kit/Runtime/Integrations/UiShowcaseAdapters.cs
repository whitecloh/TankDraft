using UnityEngine;

namespace SLVR.UIMotion
{
    /// <summary>Host-neutral contract for the single focal animated object on a screen.</summary>
    public interface IUiShowcaseAdapter
    {
        void PlayEntrance();
        void PlayIdle();
        void PauseIdle();
        void PlaySelectionResponse();
        void Stop();
    }

    /// <summary>Optional Animator bridge for character or focal artwork, never ordinary UI transforms.</summary>
    [DisallowMultipleComponent]
    public sealed class UiAnimatorShowcaseAdapter : MonoBehaviour, IUiShowcaseAdapter
    {
        [SerializeField] private Animator animator;
        [SerializeField] private string entranceState = "Entrance";
        [SerializeField] private string idleState = "Idle";
        [SerializeField] private string selectionState = "Select";
        [SerializeField, Min(0)] private int layer;
        [SerializeField, Min(0f)] private float crossFadeDuration = 0.08f;

        public void Configure(Animator targetAnimator, string entrance, string idle, string selection)
        {
            animator = targetAnimator;
            entranceState = entrance;
            idleState = idle;
            selectionState = selection;
        }

        public void PlayEntrance() => Play(entranceState, 0f);
        public void PlayIdle() => Play(idleState, crossFadeDuration);
        public void PlaySelectionResponse() => Play(selectionState, crossFadeDuration);

        public void PauseIdle()
        {
            if (animator != null) animator.speed = 0f;
        }

        public void Stop()
        {
            if (animator == null) return;
            animator.speed = 0f;
            animator.Rebind();
        }

        private void Play(string stateName, float fade)
        {
            if (animator == null || string.IsNullOrWhiteSpace(stateName)) return;
            animator.speed = 1f;
            int stateHash = Animator.StringToHash(stateName);
            if (fade > 0f) animator.CrossFadeInFixedTime(stateHash, fade, layer);
            else animator.Play(stateHash, layer, 0f);
        }
    }
}

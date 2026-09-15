using DG.Tweening;
using TMPro;
using UnityEngine;

namespace SLVR.UIMotion
{
    /// <summary>
    /// Calendar-style text replacement for compact counters: the old label rolls up while a
    /// prepared duplicate rolls in from below. The duplicate is allocated once per component.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(TMP_Text))]
    public sealed class UiCalendarTextRoller : UiMotionElement
    {
        [SerializeField] private TMP_Text target;
        [SerializeField, Min(0f)] private float rollOffset = 44f;

        private TMP_Text incoming;
        private CanvasGroup targetGroup;
        private CanvasGroup incomingGroup;
        private Vector2 shownPosition;
        private bool initialized;

        private void OnDisable()
        {
            Lifecycle.Stop(UiMotionChannel.Value);
            RestoreShownState();
        }

        public void Configure(TMP_Text value, float offset = 44f)
        {
            target = value != null ? value : GetComponent<TMP_Text>();
            rollOffset = Mathf.Max(0f, offset);
            initialized = false;
            Initialize();
        }

        public void SetImmediate(string value)
        {
            Initialize();
            Lifecycle.Stop(UiMotionChannel.Value);
            if (target != null) target.text = value ?? string.Empty;
            RestoreShownState();
        }

        public void Play(string value)
        {
            Initialize();
            if (target == null || incoming == null)
            {
                return;
            }

            string next = value ?? string.Empty;
            if (string.Equals(target.text, next, System.StringComparison.Ordinal))
            {
                SetImmediate(next);
                return;
            }

            Lifecycle.Stop(UiMotionChannel.Value);
            RestoreShownState();
            incoming.text = next;
            incoming.rectTransform.anchoredPosition = shownPosition - Vector2.up * rollOffset;
            incomingGroup.alpha = 0f;

            float duration = ResolveDuration(UiMotionTiming.Normal);
            if (duration <= 0f || Settings.ResolveEffect(UiMotionEffectKind.Translation) != UiMotionEffectKind.Translation)
            {
                SetImmediate(next);
                return;
            }

            UiMotionTweenPolicy policy = ResolvePolicy(UiMotionEase.OutCubic);
            Sequence sequence = UiMotionTweenFactory.Sequence(this, policy);
            sequence.Join(UiMotionTweenFactory.Vector3(
                this,
                () => target.rectTransform.anchoredPosition,
                current => target.rectTransform.anchoredPosition = current,
                shownPosition + Vector2.up * rollOffset,
                duration,
                policy));
            sequence.Join(UiMotionTweenFactory.Float(
                this,
                () => targetGroup.alpha,
                current => targetGroup.alpha = current,
                0f,
                duration * 0.78f,
                policy));
            sequence.Join(UiMotionTweenFactory.Vector3(
                this,
                () => incoming.rectTransform.anchoredPosition,
                current => incoming.rectTransform.anchoredPosition = current,
                shownPosition,
                duration,
                policy));
            sequence.Join(UiMotionTweenFactory.Float(
                this,
                () => incomingGroup.alpha,
                current => incomingGroup.alpha = current,
                1f,
                duration,
                policy));
            sequence.OnComplete(() =>
            {
                target.text = next;
                RestoreShownState();
            });
            Lifecycle.Play(UiMotionChannel.Value, sequence);
        }

        private void Initialize()
        {
            if (initialized)
            {
                return;
            }

            if (target == null)
            {
                target = GetComponent<TMP_Text>();
            }

            if (target == null)
            {
                return;
            }

            shownPosition = target.rectTransform.anchoredPosition;
            targetGroup = EnsureCanvasGroup(target.gameObject);
            if (incoming == null)
            {
                incoming = CreateIncoming(target);
            }
            incomingGroup = incoming != null ? EnsureCanvasGroup(incoming.gameObject) : null;
            RestoreShownState();
            initialized = incoming != null && targetGroup != null && incomingGroup != null;
        }

        private static TMP_Text CreateIncoming(TMP_Text source)
        {
            TMP_Text clone = Object.Instantiate(source, source.transform.parent);
            clone.name = source.name + "_CalendarIncoming";
            clone.raycastTarget = false;
            Behaviour[] behaviours = clone.GetComponents<Behaviour>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] != clone)
                {
                    behaviours[i].enabled = false;
                }
            }

            return clone;
        }

        private void RestoreShownState()
        {
            if (target != null)
            {
                target.rectTransform.anchoredPosition = shownPosition;
            }

            if (targetGroup != null)
            {
                targetGroup.alpha = 1f;
            }

            if (incoming != null)
            {
                incoming.rectTransform.anchoredPosition = shownPosition;
            }

            if (incomingGroup != null)
            {
                incomingGroup.alpha = 0f;
            }
        }

        private static CanvasGroup EnsureCanvasGroup(GameObject owner)
        {
            CanvasGroup group = owner.GetComponent<CanvasGroup>();
            return group != null ? group : owner.AddComponent<CanvasGroup>();
        }
    }
}

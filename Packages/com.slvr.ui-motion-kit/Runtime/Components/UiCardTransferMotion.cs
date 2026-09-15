using System;
using DG.Tweening;
using UnityEngine;

namespace SLVR.UIMotion
{
    /// <summary>
    /// Moves one caller-owned card proxy between UI endpoints without reparenting either live UI item.
    /// The destination can be followed while it moves (for example while its ScrollRect is dragged).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UiCardTransferMotion : UiMotionElement
    {
        [SerializeField, Min(0.01f)] private float duration = 0.34f;
        [SerializeField, Min(0f)] private float arcHeight = 120f;
        [SerializeField, Range(1f, 1.25f)] private float arrivalOvershoot = 1.08f;
        [SerializeField, Range(0.5f, 0.95f)] private float settleStart = 0.78f;

        private RectTransform proxy;
        private RectTransform liveTarget;
        private Vector3 startPosition;
        private Vector3 cachedEndPosition;
        private Vector3 startScale;
        private Vector3 endScale;
        private float progress;
        private Action completed;
        private bool playing;
        private bool scaleEnabled;

        public bool IsPlaying => playing;
        public float Duration => duration;
        public RectTransform Proxy => proxy;

        public void Configure(
            float transferDuration = 0.34f,
            float transferArcHeight = 120f,
            float transferArrivalOvershoot = 1.08f,
            float transferSettleStart = 0.78f)
        {
            duration = Mathf.Max(0.01f, transferDuration);
            arcHeight = Mathf.Max(0f, transferArcHeight);
            arrivalOvershoot = Mathf.Clamp(transferArrivalOvershoot, 1f, 1.25f);
            settleStart = Mathf.Clamp(transferSettleStart, 0.5f, 0.95f);
        }

        public void Play(
            RectTransform transferProxy,
            Vector3 capturedStartPosition,
            Vector3 capturedStartScale,
            RectTransform target,
            Vector3 targetScale,
            Action onCompleted = null)
        {
            Cancel(false);
            if (transferProxy == null || target == null)
            {
                onCompleted?.Invoke();
                return;
            }

            proxy = transferProxy;
            liveTarget = target;
            startPosition = capturedStartPosition;
            cachedEndPosition = target.position;
            startScale = capturedStartScale;
            endScale = targetScale;
            completed = onCompleted;
            progress = 0f;
            playing = true;

            proxy.position = startPosition;
            proxy.localScale = startScale;

            UiMotionSettingsSnapshot settings = Settings;
            scaleEnabled = settings.ResolveEffect(UiMotionEffectKind.Scale) == UiMotionEffectKind.Scale;
            float resolvedDuration = UiMotionDefaults.ScaleDuration(duration, settings.Intensity);
            bool translationEnabled =
                settings.ResolveEffect(UiMotionEffectKind.Translation) == UiMotionEffectKind.Translation;
            if (resolvedDuration <= 0f || !translationEnabled)
            {
                CompleteImmediately();
                return;
            }

            UiMotionTweenPolicy policy = ResolvePolicy(settings, UiMotionEase.InOutCubic);
            Tweener tween = UiMotionTweenFactory.Float(
                this,
                () => progress,
                ApplyProgress,
                1f,
                resolvedDuration,
                policy);
            tween.OnComplete(CompleteInternal);
            Lifecycle.Play(UiMotionChannel.Custom0, tween);
        }

        /// <summary>Finishes at the latest valid target position and invokes the completion callback once.</summary>
        public void CompleteImmediately()
        {
            if (!playing)
            {
                return;
            }

            Lifecycle.Stop(UiMotionChannel.Custom0);
            ApplyProgress(1f);
            CompleteInternal();
        }

        /// <summary>
        /// Stops the flight. Hosts normally pass true when they need to restore ghosted source/target visuals.
        /// </summary>
        public void Cancel(bool invokeCompletion)
        {
            if (!playing)
            {
                return;
            }

            Lifecycle.Stop(UiMotionChannel.Custom0);
            Action callback = completed;
            ClearState();
            if (invokeCompletion)
            {
                callback?.Invoke();
            }
        }

        /// <summary>Returns a proxy-local scale matching the endpoint's current world-space size.</summary>
        public static Vector3 ResolveProxyScale(RectTransform transferProxy, RectTransform endpoint)
        {
            if (transferProxy == null || endpoint == null)
            {
                return Vector3.one;
            }

            return ResolveProxyScale(transferProxy, CaptureWorldSize(endpoint));
        }

        public static Vector3 ResolveProxyScale(RectTransform transferProxy, Vector2 worldSize)
        {
            if (transferProxy == null)
            {
                return Vector3.one;
            }

            RectTransform parent = transferProxy.parent as RectTransform;
            Vector3 parentScale = parent != null ? parent.lossyScale : Vector3.one;
            float proxyWidth = Mathf.Abs(transferProxy.rect.width * parentScale.x);
            float proxyHeight = Mathf.Abs(transferProxy.rect.height * parentScale.y);
            float widthScale = proxyWidth > 0.001f ? worldSize.x / proxyWidth : 1f;
            float heightScale = proxyHeight > 0.001f ? worldSize.y / proxyHeight : widthScale;
            float uniformScale = Mathf.Max(0.001f, Mathf.Min(widthScale, heightScale));
            return new Vector3(uniformScale, uniformScale, 1f);
        }

        public static Vector2 CaptureWorldSize(RectTransform endpoint)
        {
            if (endpoint == null)
            {
                return Vector2.zero;
            }

            Vector3 scale = endpoint.lossyScale;
            return new Vector2(
                Mathf.Abs(endpoint.rect.width * scale.x),
                Mathf.Abs(endpoint.rect.height * scale.y));
        }

        private void OnDisable()
        {
            Cancel(false);
        }

        private void ApplyProgress(float value)
        {
            if (!playing || proxy == null)
            {
                return;
            }

            progress = Mathf.Clamp01(value);
            if (liveTarget != null)
            {
                cachedEndPosition = liveTarget.position;
            }

            Vector3 control = Vector3.Lerp(startPosition, cachedEndPosition, 0.5f) + Vector3.up * arcHeight;
            proxy.position = Quadratic(startPosition, control, cachedEndPosition, progress);

            if (!scaleEnabled)
            {
                proxy.localScale = endScale;
                return;
            }

            Vector3 overshootScale = endScale * arrivalOvershoot;
            if (progress < settleStart)
            {
                float rise = Mathf.SmoothStep(0f, 1f, progress / settleStart);
                proxy.localScale = Vector3.LerpUnclamped(startScale, overshootScale, rise);
            }
            else
            {
                float settle = Mathf.SmoothStep(0f, 1f, (progress - settleStart) / (1f - settleStart));
                proxy.localScale = Vector3.LerpUnclamped(overshootScale, endScale, settle);
            }
        }

        private void CompleteInternal()
        {
            if (!playing)
            {
                return;
            }

            if (liveTarget != null)
            {
                cachedEndPosition = liveTarget.position;
            }

            if (proxy != null)
            {
                proxy.position = cachedEndPosition;
                proxy.localScale = endScale;
            }

            Action callback = completed;
            ClearState();
            callback?.Invoke();
        }

        private void ClearState()
        {
            playing = false;
            progress = 0f;
            completed = null;
            liveTarget = null;
            proxy = null;
        }

        private static Vector3 Quadratic(Vector3 start, Vector3 control, Vector3 end, float t)
        {
            float inverse = 1f - t;
            return inverse * inverse * start + 2f * inverse * t * control + t * t * end;
        }
    }
}

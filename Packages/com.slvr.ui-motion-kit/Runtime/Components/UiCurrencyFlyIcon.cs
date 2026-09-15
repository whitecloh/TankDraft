using System;
using DG.Tweening;
using UnityEngine;

namespace SLVR.UIMotion
{
    public enum UiCurrencyFlyPath
    {
        QuadraticBezier = 0,
        CubicBezier = 1,
    }

    /// <summary>One pooled currency icon. Its path endpoint is captured at launch for target-destroy safety.</summary>
    [DisallowMultipleComponent]
    public sealed class UiCurrencyFlyIcon : MonoBehaviour, IUiPoolable
    {
        private RectTransform rectTransform;
        private Tween tween;
        private float progress;

        public bool IsFlying => tween != null && tween.IsActive() && tween.IsPlaying();
        public RectTransform RectTransform => rectTransform != null
            ? rectTransform
            : rectTransform = transform as RectTransform;

        public void Begin(
            Component owner,
            Vector3 start,
            Vector3 controlA,
            Vector3 controlB,
            Vector3 end,
            UiCurrencyFlyPath path,
            float duration,
            float delay,
            UiMotionTweenPolicy policy,
            Action<UiCurrencyFlyIcon> completed)
        {
            Cancel();
            progress = 0f;
            transform.position = start;
            tween = UiMotionTweenFactory.Float(
                    owner,
                    () => progress,
                    value =>
                    {
                        progress = value;
                        transform.position = path == UiCurrencyFlyPath.CubicBezier
                            ? Cubic(start, controlA, controlB, end, value)
                            : Quadratic(start, controlA, end, value);
                    },
                    1f,
                    duration,
                    policy)
                .SetDelay(Mathf.Max(0f, delay));
            tween.OnComplete(() =>
            {
                tween = null;
                transform.position = end;
                completed?.Invoke(this);
            });
        }

        public void Cancel()
        {
            if (tween != null && tween.IsActive()) tween.Kill(false);
            tween = null;
            progress = 0f;
        }

        public void OnRentFromPool()
        {
            progress = 0f;
        }

        public void OnReturnToPool()
        {
            Cancel();
        }

        private void OnDisable()
        {
            Cancel();
        }

        private static Vector3 Quadratic(Vector3 a, Vector3 b, Vector3 c, float t)
        {
            float oneMinus = 1f - t;
            return oneMinus * oneMinus * a + 2f * oneMinus * t * b + t * t * c;
        }

        private static Vector3 Cubic(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float t)
        {
            float oneMinus = 1f - t;
            float oneMinus2 = oneMinus * oneMinus;
            float t2 = t * t;
            return oneMinus2 * oneMinus * a
                + 3f * oneMinus2 * t * b
                + 3f * oneMinus * t2 * c
                + t2 * t * d;
        }
    }
}

using System;
using DG.Tweening;
using DG.Tweening.Core;
using UnityEngine;

namespace SLVR.UIMotion
{
    /// <summary>Single entry point that applies the package's DOTween ownership policy.</summary>
    public static class UiMotionTweenFactory
    {
        public static Tweener Float(
            Component owner,
            DOGetter<float> getter,
            DOSetter<float> setter,
            float endValue,
            float duration,
            UiMotionTweenPolicy policy)
        {
            if (getter == null) throw new ArgumentNullException(nameof(getter));
            if (setter == null) throw new ArgumentNullException(nameof(setter));

            using (UiMotionProfiler.CreateTween.Auto())
            {
                return Configure(
                    DOTween.To(getter, setter, endValue, Mathf.Max(0f, duration)),
                    owner,
                    policy);
            }
        }

        public static Tweener Double(
            Component owner,
            DOGetter<double> getter,
            DOSetter<double> setter,
            double endValue,
            float duration,
            UiMotionTweenPolicy policy)
        {
            if (getter == null) throw new ArgumentNullException(nameof(getter));
            if (setter == null) throw new ArgumentNullException(nameof(setter));

            using (UiMotionProfiler.CreateTween.Auto())
            {
                return Configure(
                    DOTween.To(getter, setter, endValue, Mathf.Max(0f, duration)),
                    owner,
                    policy);
            }
        }

        public static Tweener Vector3(
            Component owner,
            DOGetter<Vector3> getter,
            DOSetter<Vector3> setter,
            Vector3 endValue,
            float duration,
            UiMotionTweenPolicy policy)
        {
            if (getter == null) throw new ArgumentNullException(nameof(getter));
            if (setter == null) throw new ArgumentNullException(nameof(setter));

            using (UiMotionProfiler.CreateTween.Auto())
            {
                return Configure(
                    DOTween.To(getter, setter, endValue, Mathf.Max(0f, duration)),
                    owner,
                    policy);
            }
        }

        public static Sequence Sequence(Component owner, UiMotionTweenPolicy policy)
        {
            using (UiMotionProfiler.CreateTween.Auto())
            {
                return Configure(DOTween.Sequence(), owner, policy);
            }
        }

        public static T Configure<T>(T tween, Component owner, UiMotionTweenPolicy policy)
            where T : Tween
        {
            if (tween == null) throw new ArgumentNullException(nameof(tween));
            if (owner == null) throw new ArgumentNullException(nameof(owner));

            tween
                .SetTarget(owner)
                .SetUpdate(UpdateType.Normal, policy.UseUnscaledTime)
                .SetEase(policy.Ease.ToDotweenEase())
                .SetRecyclable(policy.Recyclable)
                .SetAutoKill(true);

            if (policy.LinkToOwner)
            {
                tween.SetLink(owner.gameObject, LinkBehaviour.KillOnDestroy);
            }

            return tween;
        }
    }
}

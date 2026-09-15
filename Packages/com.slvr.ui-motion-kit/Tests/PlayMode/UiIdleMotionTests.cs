using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace SLVR.UIMotion.Tests
{
    public sealed class UiIdleMotionTests
    {
        [UnityTest]
        public IEnumerator EveryIdleMode_AdmitsWithoutComponentUpdate()
        {
            foreach (UiIdleMotionMode mode in System.Enum.GetValues(typeof(UiIdleMotionMode)))
            {
                var go = new GameObject("idle-" + mode, typeof(RectTransform), typeof(CanvasGroup));
                UiIdleMotion motion = go.AddComponent<UiIdleMotion>();
                motion.Configure(mode, (RectTransform)go.transform, go.GetComponent<CanvasGroup>());
                yield return null;

                Assert.That(motion.Mode, Is.EqualTo(mode));
                Assert.That(motion.IsAdmitted, Is.True, mode.ToString());
                Assert.That(motion.IsPlaying, Is.True, mode.ToString());
                Object.Destroy(go);
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator HiddenAndModalSuppressedIdle_HasNoLoopOrBudgetLease()
        {
            var go = new GameObject("idle", typeof(RectTransform));
            UiIdleMotion motion = go.AddComponent<UiIdleMotion>();
            motion.BudgetClass = UiMotionBudgetClass.Primary;
            yield return null;
            Assert.That(motion.IsAdmitted, Is.True);
            Assert.That(motion.IsPlaying, Is.True);

            motion.SetVisible(false);
            Assert.That(motion.IsAdmitted, Is.False);
            Assert.That(motion.IsPlaying, Is.False);
            Assert.That(UiIdleMotionRuntime.GetActive(UiMotionBudgetClass.Primary), Is.Zero);

            motion.SetVisible(true);
            Assert.That(motion.IsAdmitted, Is.True);
            System.IDisposable modal = UiIdleMotionRuntime.BeginModalSuppression();
            Assert.That(motion.IsAdmitted, Is.False);
            Assert.That(motion.IsPlaying, Is.False);
            modal.Dispose();
            Assert.That(motion.IsAdmitted, Is.True);

            Object.Destroy(go);
            yield return null;
            Assert.That(UiIdleMotionRuntime.GetActive(UiMotionBudgetClass.Primary), Is.Zero);
        }

        [UnityTest]
        public IEnumerator Wiggle_HideRestoresAuthoredRotation()
        {
            var go = new GameObject("wiggle", typeof(RectTransform));
            RectTransform target = (RectTransform)go.transform;
            target.localEulerAngles = new Vector3(0f, 0f, 4f);
            UiIdleMotion motion = go.AddComponent<UiIdleMotion>();
            motion.ConfigureWiggle(target, 1.5f, 1f, 0.25f);
            for (int i = 0; i < 12; i++)
            {
                yield return null;
                float offset = Mathf.Abs(Mathf.DeltaAngle(target.localEulerAngles.z, 4f));
                Assert.That(offset, Is.LessThanOrEqualTo(1.51f),
                    "Wiggle must stay inside its signed amplitude instead of interpolating through 360 degrees.");
            }

            Assert.That(motion.Mode, Is.EqualTo(UiIdleMotionMode.Wiggle));
            Assert.That(motion.CyclePause, Is.EqualTo(0.25f));
            Assert.That(motion.IsPlaying, Is.True);
            motion.SetVisible(false);
            Assert.That(Mathf.DeltaAngle(target.localEulerAngles.z, 4f), Is.EqualTo(0f).Within(0.01f));

            Object.Destroy(go);
            yield return null;
        }

        [UnityTest]
        public IEnumerator PrimaryBudget_RejectsSecondUntilFirstBecomesHidden()
        {
            var firstObject = new GameObject("first", typeof(RectTransform));
            var secondObject = new GameObject("second", typeof(RectTransform));
            UiIdleMotion first = firstObject.AddComponent<UiIdleMotion>();
            UiIdleMotion second = secondObject.AddComponent<UiIdleMotion>();
            first.BudgetClass = UiMotionBudgetClass.Primary;
            second.BudgetClass = UiMotionBudgetClass.Primary;
            yield return null;

            Assert.That(first.IsAdmitted, Is.True);
            Assert.That(second.IsAdmitted, Is.False);
            first.SetVisible(false);
            second.RefreshAdmission();
            Assert.That(second.IsAdmitted, Is.True);

            Object.Destroy(firstObject);
            Object.Destroy(secondObject);
            yield return null;
            Assert.That(UiIdleMotionRuntime.GetActive(UiMotionBudgetClass.Primary), Is.Zero);
        }

        [UnityTest]
        public IEnumerator UnbudgetedWiggle_PlaysWhenItsBudgetClassIsFull()
        {
            var ambientObject = new GameObject("ambient", typeof(RectTransform));
            var targetObject = new GameObject("interactive-target", typeof(RectTransform));
            UiIdleMotion ambient = ambientObject.AddComponent<UiIdleMotion>();
            UiIdleMotion target = targetObject.AddComponent<UiIdleMotion>();
            ambient.BudgetClass = UiMotionBudgetClass.Primary;
            target.BudgetClass = UiMotionBudgetClass.Primary;
            yield return null;

            Assert.That(ambient.IsAdmitted, Is.True);
            Assert.That(target.IsAdmitted, Is.False);

            target.ConfigureWiggle((RectTransform)targetObject.transform, useBudget: false);
            yield return null;

            Assert.That(target.UsesBudget, Is.False);
            Assert.That(target.IsAdmitted, Is.True);
            Assert.That(target.IsPlaying, Is.True);
            Assert.That(UiIdleMotionRuntime.GetActive(UiMotionBudgetClass.Primary), Is.EqualTo(1));

            Object.Destroy(ambientObject);
            Object.Destroy(targetObject);
            yield return null;
            Assert.That(UiIdleMotionRuntime.GetActive(UiMotionBudgetClass.Primary), Is.Zero);
        }

        [UnityTest]
        public IEnumerator SettingsChange_ReevaluatesReducedMotionAndIdleEffects()
        {
            var go = new GameObject("idle", typeof(RectTransform));
            UiIdleMotion motion = go.AddComponent<UiIdleMotion>();
            var provider = new UiMotionSettingsProvider();
            motion.SetSettingsProvider(provider);
            yield return null;
            Assert.That(motion.IsAdmitted, Is.True);

            provider.SetRuntimeOverride(CreateSettings(reducedMotion: true, idleEffects: true, intensity: 1f));
            Assert.That(motion.IsAdmitted, Is.False);

            provider.SetRuntimeOverride(CreateSettings(reducedMotion: false, idleEffects: false, intensity: 1f));
            Assert.That(motion.IsAdmitted, Is.False);

            provider.SetRuntimeOverride(CreateSettings(reducedMotion: false, idleEffects: true, intensity: 1f));
            Assert.That(motion.IsAdmitted, Is.True);

            Object.Destroy(go);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ModalBackdrop_AutomaticallySuppressesBackgroundIdle()
        {
            var idleObject = new GameObject("idle", typeof(RectTransform));
            UiIdleMotion idle = idleObject.AddComponent<UiIdleMotion>();
            var backdropObject = new GameObject(
                "backdrop",
                typeof(RectTransform),
                typeof(CanvasGroup),
                typeof(UiModalBackdrop));
            UiModalBackdrop backdrop = backdropObject.GetComponent<UiModalBackdrop>();
            yield return null;
            Assert.That(idle.IsAdmitted, Is.True);

            backdrop.ShowImmediate();
            Assert.That(UiIdleMotionRuntime.IsModalSuppressed, Is.True);
            Assert.That(idle.IsAdmitted, Is.False);
            backdrop.HideImmediate();
            Assert.That(UiIdleMotionRuntime.IsModalSuppressed, Is.False);
            Assert.That(idle.IsAdmitted, Is.True);

            Object.Destroy(idleObject);
            Object.Destroy(backdropObject);
            yield return null;
        }

        private static UiMotionSettingsSnapshot CreateSettings(bool reducedMotion, bool idleEffects, float intensity)
        {
            return new UiMotionSettingsSnapshot(
                null,
                null,
                intensity,
                reducedMotion,
                false,
                idleEffects,
                true,
                false);
        }
    }
}

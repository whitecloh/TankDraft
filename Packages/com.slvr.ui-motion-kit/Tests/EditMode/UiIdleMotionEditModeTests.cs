using NUnit.Framework;
using UnityEngine;

namespace SLVR.UIMotion.Tests
{
    public sealed class UiIdleMotionEditModeTests
    {
        [Test]
        public void DeterministicPhase_IsStableForEquivalentHierarchyPath()
        {
            float first = CreatePhase("Root", "Idle");
            float second = CreatePhase("Root", "Idle");
            float different = CreatePhase("Root", "DifferentIdle");

            Assert.That(second, Is.EqualTo(first));
            Assert.That(different, Is.Not.EqualTo(first));
            Assert.That(first, Is.InRange(0f, 1f));
        }

        [Test]
        public void ModalSuppression_IsNestedAndReleasesExactlyOnce()
        {
            Assert.That(UiIdleMotionRuntime.IsModalSuppressed, Is.False);
            System.IDisposable first = UiIdleMotionRuntime.BeginModalSuppression();
            System.IDisposable second = UiIdleMotionRuntime.BeginModalSuppression();
            Assert.That(UiIdleMotionRuntime.IsModalSuppressed, Is.True);

            first.Dispose();
            first.Dispose();
            Assert.That(UiIdleMotionRuntime.IsModalSuppressed, Is.True);
            second.Dispose();
            Assert.That(UiIdleMotionRuntime.IsModalSuppressed, Is.False);
        }

        private static float CreatePhase(string rootName, string childName)
        {
            var root = new GameObject(rootName, typeof(RectTransform));
            var child = new GameObject(childName, typeof(RectTransform));
            root.SetActive(false);
            child.transform.SetParent(root.transform, false);
            UiIdleMotion motion = child.AddComponent<UiIdleMotion>();
            float phase = motion.PhaseOffset01;
            Object.DestroyImmediate(root);
            return phase;
        }
    }
}

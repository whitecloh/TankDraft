using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace SLVR.UIMotion.Tests
{
    public sealed class UiPackageSampleTests
    {
        private const string SampleRoot = "Packages/com.slvr.ui-motion-kit/Samples~/UI Motion Gallery";

        [Test]
        public void GallerySampleContainsAllScopedPrefabs()
        {
            string[] expected =
            {
                "AnimatedButton_Basic", "AnimatedButton_CTA", "AnimatedIconButton", "NavigationItem", "TabButton",
                "NotificationBadge", "ModalRoot", "Tooltip", "ProgressBar_Juicy", "RewardPopup_Generic",
                "CurrencyFlyEmitter", "CardSelectionFrame", "ShimmerOverlay", "FocusRing", "ScreenDimmer",
            };

            foreach (string name in expected)
                Assert.That(File.Exists(Path.Combine(SampleRoot, "Prefabs", name + ".prefab")), Is.True, name);
            Assert.That(Directory.GetFiles(Path.Combine(SampleRoot, "Prefabs"), "*.prefab").Length, Is.EqualTo(expected.Length));
        }

        [Test]
        public void GallerySceneExistsAndIsNotInProductionBuildSettings()
        {
            string scene = SampleRoot + "/UI_Motion_Gallery.unity";
            Assert.That(File.Exists(scene), Is.True);
            Assert.That(EditorBuildSettings.scenes.Any(x => x.path.Contains("UI_Motion_Gallery")), Is.False);
        }

        [Test]
        public void PackageManifestPublishesGalleryAndOptionalSpineSamples()
        {
            string json = File.ReadAllText("Packages/com.slvr.ui-motion-kit/package.json");
            // Version is asserted as shape, not as a literal: this test is about the manifest
            // publishing the sample sets, and pinning an exact version made it fail on every bump.
            Assert.That(json, Does.Match("\"version\": \"\\d+\\.\\d+\\.\\d+\""));
            Assert.That(json, Does.Contain("Samples~/UI Motion Gallery"));
            Assert.That(json, Does.Contain("Samples~/Spine Integration"));
        }

        [Test]
        public void OptionalSpineAssemblyIsIsolatedOutsideCoreRuntime()
        {
            string spineAsmdef = "Packages/com.slvr.ui-motion-kit/Samples~/Spine Integration/Runtime/SLVR.UIMotion.Spine.asmdef";
            Assert.That(File.Exists(spineAsmdef), Is.True);
            Assert.That(File.ReadAllText(spineAsmdef), Does.Contain("spine-unity"));
            Assert.That(File.ReadAllText("Packages/com.slvr.ui-motion-kit/Runtime/SLVR.UIMotion.Runtime.asmdef"), Does.Not.Contain("spine-unity"));
        }
    }
}

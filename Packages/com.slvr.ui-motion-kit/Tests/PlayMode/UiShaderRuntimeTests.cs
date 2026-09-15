using System.Collections;
using System.IO;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace SLVR.UIMotion.Tests
{
    public sealed class UiShaderRuntimeTests
    {
        [UnityTest]
        public IEnumerator GraphicLifecycle_RestoresOriginalMaterialAndReleasesVariant()
        {
            int initialCount = UiShaderMaterialCache.CachedMaterialCount;
            var go = new GameObject("ShaderGraphic", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            Image image = go.GetComponent<Image>();
            var original = new Material(Shader.Find("UI/Default"));
            image.material = original;

            UiShaderEffectGraphic effect = go.AddComponent<UiShaderEffectGraphic>();
            Material active = effect.ActiveMaterial;
            Assert.That(active, Is.Not.Null);
            Assert.That(image.material, Is.SameAs(active));
            Assert.That(UiShaderMaterialCache.CachedMaterialCount, Is.EqualTo(initialCount + 1));

            effect.enabled = false;
            Assert.That(image.material, Is.SameAs(original));
            Assert.That(UiShaderMaterialCache.CachedMaterialCount, Is.EqualTo(initialCount));

            Object.Destroy(go);
            Object.Destroy(original);
            yield return null;
        }

        [UnityTest]
        public IEnumerator GpuAnimation_DoesNotCreatePerFrameMaterialVariants()
        {
            int initialCount = UiShaderMaterialCache.CachedMaterialCount;
            var go = new GameObject("AnimatedShaderGraphic", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            UiShaderEffectGraphic effect = go.AddComponent<UiShaderEffectGraphic>();
            Material active = effect.ActiveMaterial;
            int stableCount = UiShaderMaterialCache.CachedMaterialCount;

            for (int frame = 0; frame < 12; frame++)
            {
                yield return null;
                Assert.That(effect.ActiveMaterial, Is.SameAs(active));
                Assert.That(UiShaderMaterialCache.CachedMaterialCount, Is.EqualTo(stableCount));
            }

            Object.Destroy(go);
            yield return null;
            Assert.That(UiShaderMaterialCache.CachedMaterialCount, Is.EqualTo(initialCount));
        }

        [UnityTest]
        public IEnumerator RefreshMaterial_PreparesFirstVisibleMeshBeforePointerInput()
        {
            var canvasObject = new GameObject("Canvas", typeof(Canvas));
            canvasObject.SetActive(false);
            var imageObject = new GameObject(
                "RuntimeShine",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            imageObject.transform.SetParent(canvasObject.transform, false);
            Image image = imageObject.GetComponent<Image>();
            UiShaderEffectGraphic effect = imageObject.AddComponent<UiShaderEffectGraphic>();
            effect.SetTarget(image);
            effect.SetDescriptor(UiShaderMaterialDescriptor.ButtonShimmer);

            canvasObject.SetActive(true);
            effect.RefreshMaterial();
            Canvas.ForceUpdateCanvases();
            yield return null;

            Assert.That(effect.ActiveMaterial, Is.Not.Null);
            Assert.That(image.material, Is.SameAs(effect.ActiveMaterial));
            Assert.That(
                canvasObject.GetComponent<Canvas>().additionalShaderChannels & AdditionalCanvasShaderChannels.TexCoord1,
                Is.EqualTo(AdditionalCanvasShaderChannels.TexCoord1));
            Mesh mesh = imageObject.GetComponent<CanvasRenderer>().GetMesh();
            Assert.That(mesh.uv2, Is.Not.Empty, "The first visible mesh must contain shimmer UV1 data.");

            Object.Destroy(canvasObject);
        }

        [UnityTest]
        public IEnumerator MaskAndRectMask2D_ClipCustomShaderPixelsAndProduceSmokeCapture()
        {
            int initialCount = UiShaderMaterialCache.CachedMaterialCount;
            var renderTexture = new RenderTexture(256, 128, 24, RenderTextureFormat.ARGB32);
            var cameraObject = new GameObject("MaskSmokeCamera", typeof(Camera));
            Camera camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.targetTexture = renderTexture;
            camera.enabled = false;

            var canvasObject = new GameObject("MaskSmokeCanvas", typeof(RectTransform), typeof(Canvas));
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 1f;

            CreateMaskCase(canvasObject.transform, new Vector2(-64f, 0f), true, Color.red,
                UiShaderMaterialDescriptor.ShimmerDefault);
            CreateMaskCase(canvasObject.transform, new Vector2(64f, 0f), false, Color.green,
                UiShaderMaterialDescriptor.GradientFlowDefault);

            Canvas.ForceUpdateCanvases();
            yield return null;
            camera.Render();

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = renderTexture;
            var capture = new Texture2D(256, 128, TextureFormat.RGBA32, false);
            capture.ReadPixels(new Rect(0, 0, 256, 128), 0, 0);
            capture.Apply();
            RenderTexture.active = previous;

            Color maskInside = capture.GetPixel(80, 64);
            Color maskOutside = capture.GetPixel(120, 64);
            Color rectInside = capture.GetPixel(200, 64);
            Color rectOutside = capture.GetPixel(240, 64);
            Assert.That(maskInside.r, Is.GreaterThan(0.25f), "Stencil Mask did not render the custom shader inside.");
            Assert.That(maskOutside.maxColorComponent, Is.LessThan(0.08f), "Stencil Mask leaked outside its rect.");
            Assert.That(rectInside.g, Is.GreaterThan(0.15f), "RectMask2D did not render the custom shader inside.");
            Assert.That(rectOutside.maxColorComponent, Is.LessThan(0.08f), "RectMask2D leaked outside its rect.");

            string capturePath = Path.Combine(Application.temporaryCachePath, "SLVR_UI_MaskSmoke.png");
            File.WriteAllBytes(capturePath, capture.EncodeToPNG());
            Debug.Log($"[SLVR_MASK_SMOKE] capture={capturePath}");

            Object.Destroy(capture);
            Object.Destroy(canvasObject);
            Object.Destroy(cameraObject);
            renderTexture.Release();
            Object.Destroy(renderTexture);
            yield return null;
            Assert.That(UiShaderMaterialCache.CachedMaterialCount, Is.EqualTo(initialCount));
        }

        [UnityTest]
        public IEnumerator CanvasAndGcMarkers_ReportWarmShaderFrames()
        {
            var canvasObject = new GameObject("ProfileCanvas", typeof(RectTransform), typeof(Canvas));
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var imageObject = new GameObject("ProfileImage", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            imageObject.transform.SetParent(canvasObject.transform, false);
            imageObject.AddComponent<UiShaderEffectGraphic>();

            Canvas.ForceUpdateCanvases();
            for (int frame = 0; frame < 3; frame++) yield return null;

            using (var canvasBuild = ProfilerRecorder.StartNew(ProfilerCategory.Gui, "Canvas.BuildBatch", 32))
            using (var gcAlloc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC.Alloc", 32))
            {
                for (int frame = 0; frame < 12; frame++) yield return null;
                Debug.Log(
                    $"[SLVR_SHADER_PROFILE] canvasMarkerValid={canvasBuild.Valid} canvasLast={canvasBuild.LastValue} " +
                    $"gcMarkerValid={gcAlloc.Valid} gcLast={gcAlloc.LastValue} materials={UiShaderMaterialCache.CachedMaterialCount}");
            }

            Object.Destroy(canvasObject);
            yield return null;
        }

        private static void CreateMaskCase(
            Transform parent,
            Vector2 anchoredPosition,
            bool stencilMask,
            Color color,
            UiShaderMaterialDescriptor descriptor)
        {
            var maskObject = new GameObject(
                stencilMask ? "StencilMask" : "RectMask",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            maskObject.transform.SetParent(parent, false);
            RectTransform maskRect = maskObject.GetComponent<RectTransform>();
            maskRect.sizeDelta = new Vector2(80f, 80f);
            maskRect.anchoredPosition = anchoredPosition;
            Image maskImage = maskObject.GetComponent<Image>();
            maskImage.color = Color.white;
            if (stencilMask)
            {
                Mask mask = maskObject.AddComponent<Mask>();
                mask.showMaskGraphic = false;
            }
            else
            {
                maskObject.AddComponent<RectMask2D>();
                maskImage.enabled = false;
            }

            var childObject = new GameObject(
                "Effect",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            childObject.transform.SetParent(maskObject.transform, false);
            RectTransform childRect = childObject.GetComponent<RectTransform>();
            childRect.sizeDelta = new Vector2(140f, 60f);
            childRect.anchoredPosition = new Vector2(30f, 0f);
            childObject.GetComponent<Image>().color = color;
            UiShaderEffectGraphic effect = childObject.AddComponent<UiShaderEffectGraphic>();
            effect.SetDescriptor(descriptor);
        }
    }
}

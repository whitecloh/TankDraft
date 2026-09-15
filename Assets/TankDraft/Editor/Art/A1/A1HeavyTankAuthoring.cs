using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace TankDraft.Art.A1.Editor
{
    /// <summary>Local repeatable art import. Does not modify the production battle catalog.</summary>
    public static class A1HeavyTankAuthoring
    {
        public const string ModelFolder = "Assets/TankDraft/Art/A1/Tanks/HeavyTank/Models";
        public const string PrefabPath = "Assets/TankDraft/Prefabs/Battle/Units/A1/HeavyTank_A1.prefab";
        public const string ConfigPath = "Assets/TankDraft/Configs/Art/A1/HeavyTank_A1_Visual.asset";
        public const string MaterialPath = "Assets/TankDraft/Art/A1/Shared/Materials/A1_VertexInk.mat";
        public const string ScenePath = "Assets/TankDraft/Scenes/Art/A1_HeavyTank_Lookdev.unity";
        public const string EvidenceFolder = "ArtSource/A1/unit.heavy_tank/v002/renders/unity";

        [MenuItem("TankDraft/Art/A1/Import heavy tank tiers")]
        public static void Build()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play Mode before authoring.");
            EnsureFolder(ModelFolder);
            EnsureFolder(Path.GetDirectoryName(MaterialPath).Replace('\\','/'));
            EnsureFolder(Path.GetDirectoryName(PrefabPath).Replace('\\','/'));
            EnsureFolder(Path.GetDirectoryName(ConfigPath).Replace('\\','/'));
            EnsureFolder(Path.GetDirectoryName(ScenePath).Replace('\\','/'));
            var shader = Shader.Find("TankDraft/A1/VertexInk");
            if (shader == null || ShaderUtil.ShaderHasError(shader)) throw new InvalidOperationException("A1 shader missing or invalid.");
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, MaterialPath); }
            material.shader = shader;
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            var config = AssetDatabase.LoadAssetAtPath<A1TankVisualConfig>(ConfigPath);
            if (config == null) { config = ScriptableObject.CreateInstance<A1TankVisualConfig>(); AssetDatabase.CreateAsset(config, ConfigPath); }
            var root = new GameObject("HeavyTank_A1");
            try
            {
                var bindings = new A1TankVisual.TierBinding[3];
                for (int tier = 1; tier <= 3; tier++)
                {
                    string name = $"HeavyTank_A1_Tier{tier}.fbx";
                    string source = "ArtSource/A1/unit.heavy_tank/v002/export/" + name;
                    string destination = ModelFolder + "/" + name;
                    if (!File.Exists(source)) throw new FileNotFoundException(source);
                    File.Copy(source, destination, true);
                    AssetDatabase.ImportAsset(destination, ImportAssetOptions.ForceSynchronousImport);
                    var importer = (ModelImporter)AssetImporter.GetAtPath(destination);
                    importer.importAnimation = false;
                    importer.importCameras = false;
                    importer.importLights = false;
                    importer.importBlendShapes = false;
                    importer.materialImportMode = ModelImporterMaterialImportMode.None;
                    importer.meshCompression = ModelImporterMeshCompression.Off; // Preserve ink and exact mask.
                    importer.isReadable = false;
                    importer.importNormals = ModelImporterNormals.Import;
                    importer.importTangents = ModelImporterTangents.None;
                    importer.optimizeMeshPolygons = true;
                    importer.optimizeMeshVertices = true;
                    importer.SaveAndReimport();
                    var imported = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(destination));
                    PrefabUtility.UnpackPrefabInstance(imported,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
                    imported.transform.SetParent(root.transform, false);
                    // Rebuild rigid pivots in Unity space. FBX conversion rotations stay in the
                    // mesh children; animation pivots have identity rotations and +Z forward.
                    var sourceTurret = Find(imported.transform,"TurretRoot");
                    var sourceBarrel = Find(imported.transform,"BarrelRecoil");
                    var sourceMuzzle = Find(imported.transform,"MuzzleAnchor");
                    var sourceHit = Find(imported.transform,"HitAnchor");
                    var flatForward = Vector3.ProjectOnPlane(sourceMuzzle.position - sourceTurret.position,Vector3.up);
                    if (flatForward.sqrMagnitude < 0.01f) throw new InvalidOperationException("FBX forward is invalid.");
                    imported.transform.rotation = Quaternion.AngleAxis(Vector3.SignedAngle(flatForward,Vector3.forward,Vector3.up),Vector3.up) * imported.transform.rotation;
                    var group = new GameObject($"Tier{tier}");
                    group.transform.SetParent(root.transform,false);
                    var turret = Pivot("TurretRoot",sourceTurret.position,group.transform);
                    var barrel = Pivot("BarrelRecoil",sourceBarrel.position,turret);
                    var muzzle = Pivot("MuzzleAnchor",sourceMuzzle.position,barrel);
                    var hit = Pivot("HitAnchor",sourceHit.position,group.transform);
                    foreach (var mesh in imported.GetComponentsInChildren<MeshFilter>(true))
                    {
                        var parent = mesh.name.Contains("Barrel") ? barrel : mesh.name.Contains("Turret") ? turret : group.transform;
                        mesh.transform.SetParent(parent,true);
                        var renderer = mesh.GetComponent<MeshRenderer>();
                        renderer.sharedMaterials = new[] {material};
                        renderer.shadowCastingMode = ShadowCastingMode.Off;
                        renderer.receiveShadows = false;
                        renderer.lightProbeUsage = LightProbeUsage.Off;
                        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                    }
                    UnityEngine.Object.DestroyImmediate(imported);
                    bindings[tier-1] = new A1TankVisual.TierBinding {root=group,turret=turret,barrelRecoil=barrel,muzzle=muzzle,hit=hit,renderers=group.GetComponentsInChildren<Renderer>(true)};
                }
                var view = root.AddComponent<A1TankVisual>();
                view.Configure(config,bindings);
                view.ResetForPool();
                if (!view.Validate(out var error)) throw new InvalidOperationException(error);
                PrefabUtility.SaveAsPrefabAsset(root,PrefabPath);
                AssetDatabase.SaveAssets();
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [MenuItem("TankDraft/Art/A1/Render heavy tank contact sheets")]
        public static void Render()
        {
            Directory.CreateDirectory(EvidenceFolder);
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var instance = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
                SceneManager.MoveGameObjectToScene(instance,scene);
                var view = instance.GetComponent<A1TankVisual>();
                var cameraObject = new GameObject("A1_RenderCamera");
                SceneManager.MoveGameObjectToScene(cameraObject,scene);
                var camera = cameraObject.AddComponent<Camera>();
                camera.scene = scene;
                camera.orthographic = true;
                camera.orthographicSize = 1.85f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.95f,.94f,.89f,1);
                camera.nearClipPlane = .01f;
                camera.farClipPlane = 100;
                camera.enabled = false;
                for (int team=0;team<2;team++) for(int tier=1;tier<=3;tier++)
                {
                    view.SetEnemyTeam(team==1);
                    view.SetVisualTier(tier);
                    camera.transform.position = new Vector3(-5,6,7);
                    camera.transform.LookAt(new Vector3(0,.75f,0));
                    Capture(camera,$"{EvidenceFolder}/tier{tier}-{(team==0?"blue":"red")}.png",640,640);
                    camera.transform.position = new Vector3(0,8,0);
                    camera.transform.rotation = Quaternion.Euler(90,0,0);
                    Capture(camera,$"{EvidenceFolder}/tier{tier}-{(team==0?"blue":"red")}-top.png",640,640);
                }
            }
            finally {EditorSceneManager.ClosePreviewScene(scene);}
        }

        [MenuItem("TankDraft/Art/A1/Create heavy tank lookdev scene")]
        public static void CreateLookdevScene()
        {
            var previous = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
            try
            {
                for(int team=0;team<2;team++) for(int tier=1;tier<=3;tier++)
                {
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath),scene);
                    go.name=$"Tier{tier}_{(team==0?"Friendly":"Enemy")}";
                    go.transform.position = new Vector3((tier-2)*3.5f,0,team==0?2.1f:-2.1f);
                    var view=go.GetComponent<A1TankVisual>();
                    view.SetVisualTier(tier);view.SetEnemyTeam(team==1);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(view);
                    foreach(var tr in go.GetComponentsInChildren<Transform>(true)) PrefabUtility.RecordPrefabInstancePropertyModifications(tr.gameObject);
                }
                var cameraObject=new GameObject("A1_Camera");
                SceneManager.MoveGameObjectToScene(cameraObject,scene);
                var camera=cameraObject.AddComponent<Camera>();
                camera.orthographic=true;camera.orthographicSize=6.9f;camera.clearFlags=CameraClearFlags.SolidColor;
                camera.backgroundColor=new Color(.95f,.94f,.89f,1);camera.transform.position=new Vector3(0,12,12);camera.transform.LookAt(Vector3.zero);
                EditorSceneManager.SaveScene(scene,ScenePath);
            }
            finally {EditorSceneManager.CloseScene(scene,true);SceneManager.SetActiveScene(previous);}
        }

        public static void RenderComparison()
        {
            Directory.CreateDirectory(EvidenceFolder);
            var scene=EditorSceneManager.NewPreviewScene();
            try
            {
                var cameraObject=new GameObject("A1_ComparisonCamera");
                SceneManager.MoveGameObjectToScene(cameraObject,scene);
                var camera=cameraObject.AddComponent<Camera>();camera.scene=scene;camera.enabled=false;
                camera.orthographic=true;camera.orthographicSize=5.2f;
                camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.95f,.94f,.89f,1);
                camera.transform.position=new Vector3(0,12,13);camera.transform.LookAt(Vector3.zero);
                for(int team=0;team<2;team++) for(int tier=1;tier<=3;tier++)
                {
                    var go=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
                    SceneManager.MoveGameObjectToScene(go,scene);
                    go.transform.position=new Vector3((2-tier)*3.5f,0,team==0?-2.2f:2.2f);
                    go.transform.rotation=Quaternion.Euler(0,-22,0);
                    var view=go.GetComponent<A1TankVisual>();view.SetVisualTier(tier);view.SetEnemyTeam(team==1);
                    var labelObject=new GameObject("Label");SceneManager.MoveGameObjectToScene(labelObject,scene);
                    labelObject.transform.position=go.transform.position+new Vector3(0,0,1.9f);
                    labelObject.transform.rotation=camera.transform.rotation;
                    var text=labelObject.AddComponent<TextMesh>();text.text=$"LEVEL {tier}  /  {(team==0?"BLUE":"RED")}";
                    text.anchor=TextAnchor.MiddleCenter;text.alignment=TextAlignment.Center;
                    text.fontSize=48;text.characterSize=.058f;text.color=new Color(.07f,.07f,.06f);
                }
                Capture(camera,EvidenceFolder+"/comparison.png",1800,1200);
                camera.transform.position=new Vector3(0,15,0);camera.transform.rotation=Quaternion.Euler(90,180,0);
                foreach(var text in scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<TextMesh>()))text.transform.rotation=camera.transform.rotation;
                Capture(camera,EvidenceFolder+"/comparison-top.png",1800,1200);
            }
            finally{EditorSceneManager.ClosePreviewScene(scene);}
        }

        private static Transform Pivot(string name,Vector3 worldPosition,Transform parent)
        {var go=new GameObject(name);go.transform.position=worldPosition;go.transform.SetParent(parent,true);return go.transform;}
        private static Transform Find(Transform root,string name)
        {return root.GetComponentsInChildren<Transform>(true).First(t=>t.name==name || t.name.StartsWith(name+".",StringComparison.Ordinal));}
        private static void EnsureFolder(string path)
        {
            if(AssetDatabase.IsValidFolder(path))return;
            var parent=Path.GetDirectoryName(path).Replace('\\','/');EnsureFolder(parent);AssetDatabase.CreateFolder(parent,Path.GetFileName(path));
        }
        private static void Capture(Camera camera,string path,int width,int height)
        {
            var previous=RenderTexture.active;
            var target=RenderTexture.GetTemporary(width,height,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB,4);
            var image=new Texture2D(width,height,TextureFormat.RGBA32,false);
            try {camera.targetTexture=target;camera.Render();RenderTexture.active=target;image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());}
            finally {camera.targetTexture=null;RenderTexture.active=previous;RenderTexture.ReleaseTemporary(target);UnityEngine.Object.DestroyImmediate(image);}
        }
    }
}

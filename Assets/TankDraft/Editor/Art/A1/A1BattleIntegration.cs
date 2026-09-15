using System;
using System.IO;
using System.Linq;
using TankDraft.BattleContent;
using TankDraft.BattlePresentation;
using TankDraft.Contracts.Battle;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TankDraft.Art.A1.Editor
{
    public static class A1BattleIntegration
    {
        public const string OriginalPrefab="Assets/TankDraft/Prefabs/Battle/Units/Battle_unit_heavy_tank.prefab";
        public const string BattlePrefab="Assets/TankDraft/Prefabs/Battle/Units/A1/Battle_unit_heavy_tank_A1.prefab";
        public const string CatalogPath="Assets/TankDraft/Configs/Battle/Presentation/BattleViewCatalog.asset";
        private const string FlashMeshPath="Assets/TankDraft/Art/A1/Shared/Materials/A1_MuzzleFlash.asset";
        private const string FlashMaterialPath="Assets/TankDraft/Art/A1/Shared/Materials/A1_MuzzleFlash.mat";

        [MenuItem("TankDraft/Art/A1/Build battle heavy tank prefab")]
        public static void BuildCandidate()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Edit Mode required.");
            var root=PrefabUtility.LoadPrefabContents(OriginalPrefab);
            try
            {
                root.name="Battle_unit_heavy_tank_A1";
                var view=root.GetComponent<BattleEntityView>();
                var visual=view.VisualRoot;
                // Keep the existing prepared HP and defense HUD separate from the rotating art.
                foreach(var name in new[]{"HealthBarRoot","DefenseIndicators"})
                {
                    var hud=root.GetComponentsInChildren<Transform>(true).First(t=>t.name==name);
                    hud.SetParent(root.transform,true);
                    var p=hud.localPosition;p.z=-1.5f;hud.localPosition=p;
                }
                foreach(var child in visual.Cast<Transform>().ToArray())
                    if(child.name!="Shadow")UnityEngine.Object.DestroyImmediate(child.gameObject);
                var projection=new GameObject("ModelProjection").transform;
                projection.SetParent(visual,false);projection.localPosition=new Vector3(0,-.10f,-.45f);
                projection.localRotation=Quaternion.Euler(-65,0,0);projection.localScale=Vector3.one*.34f;
                var modelObject=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(A1HeavyTankAuthoring.PrefabPath),root.scene);
                modelObject.transform.SetParent(projection,false);
                var model=modelObject.GetComponent<A1TankVisual>();
                var adapter=visual.gameObject.AddComponent<BattleTankModelView>();
                var flash=new GameObject("MuzzleFlash");flash.transform.SetParent(visual,false);flash.transform.localScale=Vector3.one*.13f;
                flash.AddComponent<MeshFilter>().sharedMesh=FlashMesh();
                var renderer=flash.AddComponent<MeshRenderer>();renderer.sharedMaterial=FlashMaterial();
                renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;renderer.receiveShadows=false;
                flash.SetActive(false);
                var data=new SerializedObject(adapter);
                data.FindProperty("_model").objectReferenceValue=model;
                data.FindProperty("_projectionRoot").objectReferenceValue=projection;
                data.FindProperty("_muzzleFlash").objectReferenceValue=flash;
                data.ApplyModifiedPropertiesWithoutUndo();
                var binding=new SerializedObject(view);
                binding.FindProperty("_modelVisual").objectReferenceValue=adapter;
                binding.FindProperty("_turretRoot").objectReferenceValue=null;
                binding.FindProperty("_muzzleAnchor").objectReferenceValue=model.ActiveMuzzle;
                binding.FindProperty("_hitAnchor").objectReferenceValue=model.ActiveHit;
                binding.FindProperty("_teamRenderers").arraySize=0;
                binding.ApplyModifiedPropertiesWithoutUndo();
                view.ValidateFor(BattleEntityKind.Unit);
                PrefabUtility.SaveAsPrefabAsset(root,BattlePrefab);
                AssetDatabase.SaveAssets();
            }
            finally {PrefabUtility.UnloadPrefabContents(root);}
        }

        public static string ValidateCandidate()
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(BattlePrefab);
            if(!prefab)throw new InvalidOperationException("Build candidate first.");
            var root=UnityEngine.Object.Instantiate(prefab);
            try
            {
                var view=root.GetComponent<BattleEntityView>();view.ValidateFor(BattleEntityKind.Unit);
                var settings=AssetDatabase.LoadAssetAtPath<BattlePresentationSettings>("Assets/TankDraft/Configs/Battle/Presentation/BattlePresentation.asset");
                var entry=new BattleViewCatalogAsset.Entry {definitionId="unit.heavy_tank",kind=BattleEntityKind.Unit,prefab=prefab.GetComponent<BattleEntityView>(),visualScale=1,transformedVisualScale=1};
                view.Bind(11,settings.Side1Color);view.ConfigurePresentation(entry,settings);
                var checks=0;
                for(int tier=1;tier<=3;tier++)
                {
                    view.ModelVisual.Model.SetVisualTier(tier);
                    for(int direction=0;direction<8;direction++)
                    {
                        var radians=direction*Mathf.PI/4;
                        var facing=new BattleVec(Mathf.Sin(radians),Mathf.Cos(radians));
                        var state=new BattleEntityState(11,1,BattleEntityKind.Unit,"unit.heavy_tank",new BattleVec(.2f,.1f),new BattleVec(.1f,.1f),facing,70,100,.35f,0,0);
                        view.Render(state,1,10+direction);
                        var forward=view.ModelVisual.ActiveTurret.forward;
                        var screen=new Vector2(forward.x,forward.y).normalized;
                        if(Vector2.Dot(screen,new Vector2(facing.X,facing.Y))<.999f)throw new InvalidOperationException("Aim projection failed.");
                        if(view.MuzzleAnchor!=view.ModelVisual.Model.ActiveMuzzle)throw new InvalidOperationException("Stale muzzle tier anchor.");
                        checks++;
                    }
                }
                view.PlayShot(20);
                var pose=new BattleEntityState(11,1,BattleEntityKind.Unit,"unit.heavy_tank",new BattleVec(0,0),new BattleVec(0,0),new BattleVec(0,1),70,100,.35f,0,0);
                view.Render(pose,1,20.01f);
                if(!root.GetComponentsInChildren<Transform>(true).First(t=>t.name=="MuzzleFlash").gameObject.activeSelf)throw new InvalidOperationException("Shot did not show muzzle flash.");
                var recoiled=view.MuzzleAnchor.position;view.Render(pose,1,20.2f);
                if((view.MuzzleAnchor.position-recoiled).sqrMagnitude<.0001f)throw new InvalidOperationException("No recoil recovery.");
                view.Flash(21);view.Render(pose,1,21.01f);
                view.Clear();view.Bind(12,settings.Side0Color);view.ConfigurePresentation(entry,settings);view.Render(pose,1,22);
                if(view.ModelVisual.Model.VisualTier!=1)throw new InvalidOperationException("Pooled tier not reset.");
                var block=new MaterialPropertyBlock();view.ModelVisual.Model.GetComponentInChildren<MeshRenderer>().GetPropertyBlock(block);
                if(block.GetFloat("_HitFlash")!=0 || block.GetColor("_TeamColor")!=settings.Side0Color)throw new InvalidOperationException("Pooled flash or team not reset.");
                var text=$"PASS candidate: {checks} tier/direction aim cases; dynamic anchors; shot flash/recoil; hit/team/tier pool reset.";
                Directory.CreateDirectory("Logs/A1BattleIntegration");File.WriteAllText("Logs/A1BattleIntegration/candidate.txt",text);return text;
            }
            finally {UnityEngine.Object.DestroyImmediate(root);}
        }

        [MenuItem("TankDraft/Art/A1/Connect validated heavy tank to battle")]
        public static void Connect()
        {
            ValidateCandidate();
            var catalog=AssetDatabase.LoadAssetAtPath<BattleViewCatalogAsset>(CatalogPath);
            var previous=catalog.Get("unit.heavy_tank").prefab;
            var data=new SerializedObject(catalog);var entries=data.FindProperty("_entries");
            for(int i=0;i<entries.arraySize;i++)
            {
                var entry=entries.GetArrayElementAtIndex(i);
                if(entry.FindPropertyRelative("definitionId").stringValue!="unit.heavy_tank")continue;
                entry.FindPropertyRelative("prefab").objectReferenceValue=AssetDatabase.LoadAssetAtPath<GameObject>(BattlePrefab).GetComponent<BattleEntityView>();
                data.ApplyModifiedPropertiesWithoutUndo();
                try
                {
                    var definitions=AssetDatabase.LoadAssetAtPath<BattleScenarioCatalogAsset>("Assets/TankDraft/Configs/Battle/Scenarios/BattleScenarioCatalog.asset").CreateDefinitions();
                    catalog.Validate(definitions);EditorUtility.SetDirty(catalog);AssetDatabase.SaveAssets();return;
                }
                catch {entry.FindPropertyRelative("prefab").objectReferenceValue=previous;data.ApplyModifiedPropertiesWithoutUndo();throw;}
            }
            throw new InvalidOperationException("Heavy tank catalog entry not found.");
        }

        private static Mesh FlashMesh()
        {
            var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(FlashMeshPath);if(mesh)return mesh;
            mesh=new Mesh {name="A1_MuzzleFlash"};var vertices=new Vector3[17];var triangles=new int[48];
            for(int i=0;i<16;i++){float a=i*Mathf.PI/8;float r=i%2==0?1f:.4f;vertices[i+1]=new Vector3(Mathf.Cos(a)*r,Mathf.Sin(a)*r,0);triangles[i*3]=0;triangles[i*3+1]=(i+1)%16+1;triangles[i*3+2]=i+1;}
            mesh.vertices=vertices;mesh.triangles=triangles;mesh.RecalculateNormals();mesh.RecalculateBounds();AssetDatabase.CreateAsset(mesh,FlashMeshPath);return mesh;
        }
        private static Material FlashMaterial()
        {
            var material=AssetDatabase.LoadAssetAtPath<Material>(FlashMaterialPath);if(material)return material;
            material=new Material(Shader.Find("Unlit/Color")){name="A1_MuzzleFlash",color=new Color(1,.76f,.25f)};
            AssetDatabase.CreateAsset(material,FlashMaterialPath);return material;
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;

namespace TankDraft.Art.A1.Editor
{
    public static class A1HeavyTankValidation
    {
        [Serializable] public sealed class TierMetrics
        {
            public int tier, triangles, vertices, activeRenderers, submeshes, teamMaskVertices;
            public long editorMeshMemoryBytes;
            public Vector3 boundsSize, boundsMin, muzzle;
        }
        [Serializable] public sealed class Report
        {
            public string timestampUtc, unityVersion, graphicsApi, colorSpace, scope;
            public bool passed, instancingSupported, materialInstancing, readableDisabled;
            public int sharedMaterials, sampleUnits, sampleActiveRenderers, sampleTriangles;
            public TierMetrics[] tiers;
            public string[] checks;
        }

        [MenuItem("TankDraft/Art/A1/Validate heavy tank assets")]
        public static void ValidateMenu() { Debug.Log(Run()); }

        public static string Run()
        {
            var checks=new List<string>();
            var scene=EditorSceneManager.NewPreviewScene();
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(A1HeavyTankAuthoring.PrefabPath);
            Require(prefab!=null,"Prefab missing.");
            var report=new Report {timestampUtc=DateTime.UtcNow.ToString("o"),unityVersion=Application.unityVersion,
                graphicsApi=SystemInfo.graphicsDeviceType.ToString(),colorSpace=QualitySettings.activeColorSpace.ToString(),
                scope="Actual imported prefab and isolated Editor objects. No production battle or device FPS claim.",
                instancingSupported=SystemInfo.supportsInstancing,tiers=new TierMetrics[3],sampleUnits=96};
            try
            {
                var root=UnityEngine.Object.Instantiate(prefab);SceneManager.MoveGameObjectToScene(root,scene);
                var view=root.GetComponent<A1TankVisual>();
                Require(view.Validate(out var validation),validation);
                Require(view.Config.DefinitionId=="unit.heavy_tank","Stable visual ID changed.");
                var materials=root.GetComponentsInChildren<Renderer>(true).SelectMany(r=>r.sharedMaterials).Distinct().ToArray();
                Require(materials.Length==1 && materials[0]!=null,"Expected one shared material.");
                report.sharedMaterials=materials.Length;report.materialInstancing=materials[0].enableInstancing;
                Require(materials[0].passCount==1 && !ShaderUtil.ShaderHasError(materials[0].shader),"Ink shader must be one valid pass.");
                checks.Add("Prefab/config references, stable ID, one shared material, one shader pass");
                report.readableDisabled=true;
                for(int tier=1;tier<=3;tier++)
                {
                    view.SetVisualTier(tier);
                    var meshes=root.GetComponentsInChildren<MeshFilter>();
                    Require(meshes.Length==3,"Exactly three meshes must be active.");
                    var bounds=meshes[0].GetComponent<Renderer>().bounds;
                    var metric=new TierMetrics {tier=tier,activeRenderers=meshes.Length};
                    foreach(var filter in meshes)
                    {
                        var mesh=filter.sharedMesh;
                        metric.triangles+=mesh.triangles.Length/3;metric.vertices+=mesh.vertexCount;metric.submeshes+=mesh.subMeshCount;
                        metric.editorMeshMemoryBytes+=Profiler.GetRuntimeMemorySizeLong(mesh);
                        Require(mesh.subMeshCount==1,"Mesh has extra submeshes.");
                        var colors=mesh.colors;var vertices=mesh.vertices;
                        Require(colors.Length==mesh.vertexCount,"Vertex palette missing.");
                        for(int i=0;i<colors.Length;i++)
                        {
                            Require(colors[i].a<.01f || colors[i].a>.99f,"Mask must be binary.");
                            if(colors[i].a<.01f)
                            {
                                metric.teamMaskVertices++;
                                Require(filter.name.Contains("Turret") && filter.transform.TransformPoint(vertices[i]).y>1.4f,"Team mask leaked outside roof cap.");
                            }
                        }
                        bounds.Encapsulate(filter.GetComponent<Renderer>().bounds);
                        report.readableDisabled &= !((ModelImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(mesh))).isReadable;
                    }
                    Require(metric.teamMaskVertices>0,"No colored roof cap.");
                    Require(metric.triangles<1600,"Tier art budget exceeded.");
                    Require(bounds.min.y>-.08f && bounds.max.y>1.5f && bounds.max.y<2.2f,"Model upside down or wrong scale.");
                    Require(view.ActiveMuzzle.position.z>1.0f && Math.Abs(view.ActiveMuzzle.position.x)<.01f,"Muzzle must face +Z.");
                    metric.boundsMin=bounds.min;metric.boundsSize=bounds.size;metric.muzzle=view.ActiveMuzzle.position;
                    report.tiers[tier-1]=metric;
                    foreach(bool enemy in new[]{false,true})
                    {
                        view.SetEnemyTeam(enemy);
                        foreach(var renderer in root.GetComponentsInChildren<Renderer>(true))
                        {
                            var block=new MaterialPropertyBlock();renderer.GetPropertyBlock(block);
                            Require(block.GetColor("_TeamColor")== (enemy?view.Config.EnemyColor:view.Config.FriendlyColor),"Team color lost on inactive or active tier.");
                            Require(renderer.sharedMaterial==materials[0],"Material instance created.");
                        }
                    }
                }
                checks.Add("All three imported tiers: dimensions, +Z muzzle, mask restricted to roof, 3 submeshes, <1600 triangles");
                checks.Add("All six tier/team states: color on all bindings, only chosen tier active, shared material retained");
                view.SetVisualTier(1);view.SetTurretYaw(45);view.SetRecoil(.15f);view.SetVisualTier(3);
                Require(Quaternion.Angle(view.ActiveTurret.localRotation,Quaternion.Euler(0,45,0))<.01f,"Tier switch lost aim.");
                var before=view.ActiveMuzzle.position;view.SetRecoil(0);var direction=view.ActiveTurret.forward;
                Require(Vector3.Dot(view.ActiveMuzzle.position-before,direction)>.149f,"Recoil direction invalid.");
                view.ResetForPool();Require(view.VisualTier==1 && !view.IsEnemyTeam,"Pool reset failed.");
                checks.Add("Aim persists through upgrade; muzzle follows turret and positive recoil retracts; pool reset restores defaults");
                for(int i=0;i<report.sampleUnits;i++)
                {
                    var sample=UnityEngine.Object.Instantiate(prefab);SceneManager.MoveGameObjectToScene(sample,scene);
                    var item=sample.GetComponent<A1TankVisual>();item.SetVisualTier(3);item.SetEnemyTeam(i%2==0);
                    report.sampleActiveRenderers+=sample.GetComponentsInChildren<Renderer>().Length;
                    report.sampleTriangles+=sample.GetComponentsInChildren<MeshFilter>().Sum(f=>f.sharedMesh.triangles.Length/3);
                    Require(sample.GetComponentsInChildren<Renderer>(true).All(r=>r.sharedMaterial==materials[0]),"Crowd duplicated shared material.");
                }
                checks.Add("96 instantiated highest-tier units share meshes/material; only 288 mesh renderers active (not an FPS benchmark)");
                report.checks=checks.ToArray();report.passed=true;
                Directory.CreateDirectory("ArtSource/A1/unit.heavy_tank/v002/metrics");
                var json=JsonUtility.ToJson(report,true);
                File.WriteAllText("ArtSource/A1/unit.heavy_tank/v002/metrics/unity-validation.json",json);
                return json;
            }
            finally {EditorSceneManager.ClosePreviewScene(scene);}
        }
        private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    }
}

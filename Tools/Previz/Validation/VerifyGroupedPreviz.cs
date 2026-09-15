using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;
public static class VerifyGroupedPreviz {
 [Serializable] public class E { public string id,parentId; public float x,y,width,height; }
 [Serializable] public class G { public string id,type,alignment; public float x,y,width,height,cellWidth,cellHeight,spacingX,spacingY; public int paddingLeft,paddingRight,paddingTop,paddingBottom,columns; public string[] children; }
 [Serializable] public class V { public float width,height; }
 [Serializable] public class L { public E[] elements; public G[] groups; public V viewport; }
 static Type Importer { get { return Type.GetType("TankDraft.Editor.Previz.PrevizImporter, TankDraft.Previz.Editor",true); } }
 static object Parse(string json) { return Importer.GetMethod("ParseAndValidate").Invoke(null,new object[]{json}); }
 static T Copy<T>(object source) where T:new() {var result=new T();foreach(var f in typeof(T).GetFields()){var sf=source.GetType().GetField(f.Name);if(sf!=null)f.SetValue(result,sf.GetValue(source));}return result;}
 static L Read(string json){var data=Parse(json);return new L { viewport=Copy<V>(data.GetType().GetField("viewport").GetValue(data)), elements=((Array)data.GetType().GetField("elements").GetValue(data)).Cast<object>().Select(Copy<E>).ToArray(),groups=((Array)data.GetType().GetField("groups").GetValue(data)).Cast<object>().Select(Copy<G>).ToArray()};}
 static void Import(string json,string path) { if(!File.Exists(path))Importer.GetMethod("Import").Invoke(null,new object[]{json,path}); }
 static void Assert(bool good,string message) { if(!good)throw new Exception(message); }
 static RectTransform Find(GameObject root,string id) { return root.GetComponentsInChildren<RectTransform>(true).Single(r=>r.name==id); }
 static void Rebuild(GameObject root,L layout) {
  var r=root.GetComponent<RectTransform>();root.GetComponent<Canvas>().renderMode=RenderMode.WorldSpace;
  r.sizeDelta=new Vector2(layout.viewport.width,layout.viewport.height);r.position=Vector3.zero;r.localScale=Vector3.one;
  Canvas.ForceUpdateCanvases();LayoutRebuilder.ForceRebuildLayoutImmediate(r);
  foreach(var g in layout.groups??new G[0])LayoutRebuilder.ForceRebuildLayoutImmediate(Find(root,g.id));
 }
 static int Check(GameObject root,L layout,bool checkTemplate=true) {
  Rebuild(root,layout);var rootRect=root.GetComponent<RectTransform>();int checkedCount=0;
  foreach(var e in layout.elements){var r=Find(root,e.id);var c=new Vector3[4];r.GetWorldCorners(c);var top=rootRect.InverseTransformPoint(c[1]);var actual=new Vector4(top.x-rootRect.rect.xMin,rootRect.rect.yMax-top.y,r.rect.width,r.rect.height);var expected=new Vector4(e.x,e.y,e.width,e.height);
   Assert(Vector4.Distance(actual,expected)<.15f,e.id+" actual="+actual+" expected="+expected);checkedCount++;
   if(!string.IsNullOrEmpty(e.parentId))Assert(r.parent.name==e.parentId,"Parent mismatch "+e.id);
  }
  foreach(var g in layout.groups??new G[0]){
   var gr=Find(root,g.id);Assert(gr.childCount==g.children.Length,"Group count "+g.id);
   string firstPath=null;
   foreach(var id in g.children){var r=Find(root,id);Assert(r.parent==gr,"Group parent "+id);Assert(r.GetComponent<LayoutElement>()!=null,"Missing LayoutElement "+id);
    if(checkTemplate){Assert(PrefabUtility.IsPartOfPrefabInstance(r.gameObject),"Missing nested prefab "+id);var source=PrefabUtility.GetCorrespondingObjectFromSource(r.gameObject);var path=AssetDatabase.GetAssetPath(source);if(firstPath==null)firstPath=path;Assert(path==firstPath,"Not shared template "+g.id);}
   }
  }
  return checkedCount;
 }
 public static string Run(){
  Assert(Application.dataPath.Replace('\\','/')=="U:/UNITY_PROJECTS/TankDraft/Assets","Wrong project");Assert(!EditorApplication.isCompiling&&!EditorUtility.scriptCompilationFailed,"Compilation not ready");
  var active=SceneManager.GetActiveScene();var dirty=active.isDirty;int parsed=0;
  foreach(var file in Directory.GetFiles("Logs/TankDraftSetup/reference-layouts","*.json").Concat(Directory.GetFiles("Logs/TankDraftSetup/group-cases","*.json"))){Parse(File.ReadAllText(file));parsed++;}
  Parse(File.ReadAllText("Tools/Previz/Samples/draft-layout-v1.json"));parsed++;
  const string mainPath="Assets/TankDraft/Art/UI/Previz/Arena_Grouped_Previz.prefab";
  var json=File.ReadAllText("Logs/TankDraftSetup/reference-layouts/arena-576x1280.json");Import(json,mainPath);var layout=Read(json);
  var scene=EditorSceneManager.NewPreviewScene();int geometries=0;int variants=0;
  try{
   var view=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(mainPath),scene);geometries+=Check(view,layout);UnityEngine.Object.DestroyImmediate(view);
  const string smokePath="Assets/TankDraft/Art/UI/Previz/GroupValidation/Hierarchy_Smoke.prefab";
   var smokeJson=File.ReadAllText("Logs/TankDraftSetup/group-cases/horizontal-UpperLeft.json");Import(smokeJson,smokePath);
   foreach(var file in Directory.GetFiles("Logs/TankDraftSetup/group-cases","*.json")){
    var caseJson=File.ReadAllText(file);var data=Parse(caseJson);var spec=Read(caseJson);view=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(smokePath),scene);
    var groups=(Array)data.GetType().GetField("groups").GetValue(data);
    for(int i=0;i<spec.groups.Length;i++){
     var g=spec.groups[i];var rect=Find(view,g.id);UnityEngine.Object.DestroyImmediate(rect.GetComponent<LayoutGroup>());rect.anchoredPosition=new Vector2(g.x,-g.y);rect.sizeDelta=new Vector2(g.width,g.height);
     Importer.GetMethod("ConfigureLayoutGroup",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{rect,groups.GetValue(i)});
     foreach(var id in g.children){var le=Find(view,id).GetComponent<LayoutElement>();le.preferredWidth=le.minWidth=g.cellWidth;le.preferredHeight=le.minHeight=g.cellHeight;}
    }
    geometries+=Check(view,spec);variants++;UnityEngine.Object.DestroyImmediate(view);
   }
  }finally{EditorSceneManager.ClosePreviewScene(scene);}
  Assert(SceneManager.GetActiveScene()==active&&active.isDirty==dirty,"Active scene changed");
  var summary="Parsed="+parsed+"; actual Unity layouts="+(variants+1)+"; verified rectangles="+geometries+"; shared nested prefabs=true; activeSceneUnchanged=true; compilationFailed="+EditorUtility.scriptCompilationFailed;
  File.WriteAllText("Logs/TankDraftSetup/group-verification.txt",summary);return summary;
 }
}

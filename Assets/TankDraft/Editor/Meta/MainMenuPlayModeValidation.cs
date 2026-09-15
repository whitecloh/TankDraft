using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using TMPro;
using VContainer;
using Vareiko.Foundation.UI;
using TankDraft.Bootstrap;
using TankDraft.Infrastructure;
using TankDraft.UI;

namespace TankDraft.Editor.UI
{
    [InitializeOnLoad]
    public static class MainMenuPlayModeValidation
    {
        const string Key="TankDraft.MainMenuRuntimeQA";
        const string ScenePath="Assets/TankDraft/Scenes/Diagnostics/QA/MainMenuRuntimeQA.unity";
        const string Evidence="Logs/TankDraftSetup/RuntimeQA/";
        static double nextTick;
        static bool captureRequested;
        static Action afterCapture;
        static MainMenuPlayModeValidation(){EditorApplication.update+=Tick;}
        static void Check(bool test,string message)
        { if(!test)throw new Exception(message);SessionState.SetInt(Key+"checks",SessionState.GetInt(Key+"checks",0)+1); }
        static T Named<T>(Component root,string name) where T:Component => root.GetComponentsInChildren<T>(true).Single(v=>v.name==name);
        static void Click(Component root,string name)
        {
            if(captureRequested){afterCapture=()=>Click(root,name);return;}
            var button=Named<UIButtonView>(root,name);
            Check(button.gameObject.activeInHierarchy && button.Interactable,"Inactive button: "+name);
            button.Button.onClick.Invoke();
        }
        static void ClickCard(UICatalogPanel panel,string title)
        {
            if(captureRequested){afterCapture=()=>ClickCard(panel,title);return;}
            var card=panel.GetComponentsInChildren<UICatalogCardView>().Single(c=>c.GetComponentsInChildren<TMP_Text>().Any(t=>t.name=="Title_Text" && t.text==title));
            card.GetComponent<UIButtonView>().Button.onClick.Invoke();
        }
        public static string Begin()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode || EditorUtility.scriptCompilationFailed)throw new Exception("Idle compiled Editor required.");
            if(SceneManager.GetActiveScene().isDirty)throw new Exception("Current scene is dirty; validation cannot replace it.");
            Directory.CreateDirectory(Evidence);SetResolution(576,1280);
            var scene=EditorSceneManager.OpenScene(MainMenuRuntimeAuthoring.ScenePath);
            var settings=ScriptableObject.CreateInstance<LocalProfileSettings>();
            var so=new SerializedObject(settings);so.FindProperty("_fileName").stringValue="qa-mainmenu-"+Guid.NewGuid().ToString("N")+".json";so.ApplyModifiedPropertiesWithoutUndo();
            const string settingsPath="Assets/TankDraft/Configs/Diagnostics/QA/RuntimeProfileSettings.asset";
            Directory.CreateDirectory(Path.GetDirectoryName(settingsPath));
            var existing=AssetDatabase.LoadAssetAtPath<LocalProfileSettings>(settingsPath);
            if(existing){EditorUtility.CopySerialized(settings,existing);UnityEngine.Object.DestroyImmediate(settings);settings=existing;}else AssetDatabase.CreateAsset(settings,settingsPath);
            var scope=UnityEngine.Object.FindFirstObjectByType<MainMenuLifetimeScope>();var scopeData=new SerializedObject(scope);scopeData.FindProperty("_profileSettings").objectReferenceValue=settings;scopeData.ApplyModifiedPropertiesWithoutUndo();
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));EditorSceneManager.SaveScene(scene,ScenePath);AssetDatabase.SaveAssets();
            SessionState.SetString(Key+"profilePath",settings.GetPath());SessionState.SetBool(Key,true);SessionState.SetInt(Key+"phase",0);SessionState.SetInt(Key+"checks",0);SessionState.SetString(Key+"started",DateTime.UtcNow.ToString("O"));
            File.WriteAllText(Evidence+"status.txt","RUNNING");EditorApplication.isPlaying=true;
            return "Play Mode QA started; temporary profile is isolated from the player profile.";
        }
        static void Capture(string name){captureRequested=true;ScreenCapture.CaptureScreenshot(Path.GetFullPath(Evidence+name+".png"));}
        static void Tick()
        {
            if(!SessionState.GetBool(Key,false) || EditorApplication.isCompiling || EditorApplication.timeSinceStartup<nextTick)return;
            nextTick=EditorApplication.timeSinceStartup+.35;
            try
            {
                if(DateTime.UtcNow-DateTime.Parse(SessionState.GetString(Key+"started",""))>TimeSpan.FromMinutes(3))throw new Exception("Runtime QA timeout");
                if(captureRequested){captureRequested=false;var action=afterCapture;afterCapture=null;action?.Invoke();return;}
                int phase=SessionState.GetInt(Key+"phase",0);
                if(!EditorApplication.isPlaying)
                {
                    if(phase==12){EditorSceneManager.OpenScene(ScenePath);SessionState.SetInt(Key+"phase",13);EditorApplication.isPlaying=true;}
                    else if(phase==18){Finish();}
                    return;
                }
                var scope=UnityEngine.Object.FindFirstObjectByType<MainMenuLifetimeScope>();if(!scope || scope.Container==null)return;
                var startup=scope.Container.Resolve<MainMenuStartup>();if(startup.Failure!=null)throw new Exception("Startup failed",startup.Failure);if(!startup.IsReady)return;
                var view=UnityEngine.Object.FindFirstObjectByType<UIMainMenuRuntimeRoot>();
                switch(phase)
                {
                    case 0:
                        Check(EventSystem.current!=null && EventSystem.current.currentInputModule!=null,"Input system missing");
                        Check(view.Screen.IsVisible && view.Arena.IsVisible && !view.Collection.IsVisible,"Cold startup visibility");
                        Check(startup.Profile.Current.UnitIds[0]=="unit.mines","QA profile must start from seed");
                        Capture("01-main");break;
                    case 1: Click(view.Hud,"Collection_Button");break;
                    case 2:
                        Check(view.Collection.IsVisible && !view.Arena.IsVisible,"Army navigation failed");
                        Check(view.Collection.GetComponentInChildren<UICatalogPanel>().GetComponentsInChildren<UICatalogCardView>().Length==12,"Catalog count");
                        Capture("02-army");ClickCard(view.Collection.GetComponentInChildren<UICatalogPanel>(),"Средний танк");break;
                    case 3:
                        Check(view.Details.IsVisible,"Card did not open");Capture("03-card");Click(view.Details,"Equip_Button");break;
                    case 4:
                        var slots=view.Details.GetComponentInChildren<UILoadoutPanel>();Check(slots!=null,"Replacement slots hidden");
                        Click(slots,"LoadoutSlot_01");break;
                    case 5:
                        Check(startup.Profile.Current.UnitIds[0]=="unit.medium_tank","Unit was not equipped");
                        Check(!view.Details.IsVisible,"Details remain after successful equip");
                        Check(File.Exists(SessionState.GetString(Key+"profilePath","")),"Profile not saved");
                        Click(view.Collection,"Orders_Button");break;
                    case 6:
                        Check(view.Collection.GetComponentInChildren<UILoadoutPanel>().GetComponentsInChildren<UICatalogCardView>().Length==3,"Order slot count");
                        Check(Named<UIButtonView>(view.Collection.GetComponentInChildren<UILoadoutPanel>(),"LoadoutSlot_02").Interactable==false,"Locked order slot accepts input");
                        Capture("04-orders");ClickCard(view.Collection.GetComponentInChildren<UICatalogPanel>(),"Ремонтная группа");break;
                    case 7: Click(view.Details,"Equip_Button");break;
                    case 8: Click(view.Details.GetComponentInChildren<UILoadoutPanel>(),"LoadoutSlot_01");break;
                    case 9:
                        Check(startup.Profile.Current.OrderIds[0]=="order.repair_team","Order was not equipped");
                        var count=view.Collection.GetComponentInChildren<UICatalogPanel>().transform.childCount;
                        for(int i=0;i<5;i++){startup.Presenter.OpenCollection(false);startup.Presenter.OpenCollection(true);}
                        Check(view.Collection.GetComponentInChildren<UICatalogPanel>().transform.childCount==count,"Pool grows on rebind");
                        Click(view.Hud,"Battle_Button");break;
                    case 10:
                        Check(view.Arena.IsVisible && !view.Collection.IsVisible,"Back to main failed");
                        Click(view.Arena,"StartBattle_Button");Check(view.Message.IsVisible,"Unavailable battle has no feedback");Click(view.Message,"Close_Button");break;
                    case 11:
                        SessionState.SetInt(Key+"phase",12);EditorApplication.isPlaying=false;return;
                    case 12:return;
                    case 13:
                        Check(startup.Profile.Current.UnitIds[0]=="unit.medium_tank" && startup.Profile.Current.OrderIds[0]=="order.repair_team","Loadout lost after full Play Mode restart");
                        SetResolution(576,1024);break;
                    case 14:
                        Check(UnityEngine.Screen.width==576 && UnityEngine.Screen.height==1024,"9:16 target resolution");
                        var battle=Named<UIButtonView>(view.Arena,"StartBattle_Button").GetComponent<RectTransform>();var nav=Named<UIButtonView>(view.Hud,"Battle_Button").GetComponent<RectTransform>();
                        var a=new Vector3[4];var n=new Vector3[4];battle.GetWorldCorners(a);nav.GetWorldCorners(n);Check(a[0].y>=n[1].y,"Battle overlaps bottom navigation at 9:16");Capture("05-main-9x16");afterCapture=()=>startup.Presenter.OpenCollection(false);break;
                    case 15:
                        var scroll=view.Collection.GetComponentInChildren<UnityEngine.UI.ScrollRect>();Check(scroll.content.rect.height>scroll.viewport.rect.height,"Collection cannot scroll at shorter aspect");scroll.verticalNormalizedPosition=0;Capture("06-army-scroll-9x16");break;
                    case 16:
                        var safe=view.GetComponentInChildren<UiSafeArea>();safe.Apply(new Rect(0,40,576,944),new Vector2Int(576,1024));
                        Check(Mathf.Abs(safe.GetComponent<RectTransform>().anchorMin.y-40f/1024)<.001f,"Safe area bottom anchor");
                        Check(Mathf.Abs(safe.GetComponent<RectTransform>().anchorMax.y-984f/1024)<.001f,"Safe area top anchor");break;
                    case 17: SessionState.SetInt(Key+"phase",18);EditorApplication.isPlaying=false;return;
                    default:throw new Exception("Unknown QA phase "+phase);
                }
                SessionState.SetInt(Key+"phase",phase+1);
            }
            catch(Exception exception)
            {
                File.WriteAllText(Evidence+"status.txt","FAIL phase "+SessionState.GetInt(Key+"phase",0)+"\n"+exception);
                SessionState.SetBool(Key,false);EditorApplication.isPlaying=false;Debug.LogException(exception);
            }
        }
        static void Finish()
        {
            var count=SessionState.GetInt(Key+"checks",0);
            SessionState.SetBool(Key,false);SetResolution(576,1280);EditorSceneManager.OpenScene(MainMenuRuntimeAuthoring.ScenePath);
            File.WriteAllText(Evidence+"status.txt","PASS "+count+" Play Mode flow assertions; two cold starts; 576x1280 and 576x1024. Player profile untouched. QA profile: "+SessionState.GetString(Key+"profilePath",""));
        }
        static void SetResolution(int width,int height)
        {
            var assembly=typeof(UnityEditor.Editor).Assembly;var sizesType=assembly.GetType("UnityEditor.GameViewSizes");
            var singleton=typeof(ScriptableSingleton<>).MakeGenericType(sizesType);var sizes=singleton.GetProperty("instance",BindingFlags.Public|BindingFlags.Static).GetValue(null);
            var groupEnum=assembly.GetType("UnityEditor.GameViewSizeGroupType");var group=sizesType.GetMethod("GetGroup").Invoke(sizes,new[]{Enum.Parse(groupEnum,"Standalone")});var type=group.GetType();
            int builtin=(int)type.GetMethod("GetBuiltinCount").Invoke(group,null),custom=(int)type.GetMethod("GetCustomCount").Invoke(group,null),index=-1;
            for(int i=0;i<builtin+custom;i++){var size=type.GetMethod("GetGameViewSize").Invoke(group,new object[]{i});if((int)size.GetType().GetProperty("width").GetValue(size)==width && (int)size.GetType().GetProperty("height").GetValue(size)==height){index=i;break;}}
            if(index<0){var sizeType=assembly.GetType("UnityEditor.GameViewSize");var sizeEnum=assembly.GetType("UnityEditor.GameViewSizeType");var item=Activator.CreateInstance(sizeType,new object[]{Enum.Parse(sizeEnum,"FixedResolution"),width,height,"TankDraft "+width+"x"+height});type.GetMethod("AddCustomSize").Invoke(group,new[]{item});index=builtin+custom;}
            var gameType=assembly.GetType("UnityEditor.GameView");var window=EditorWindow.GetWindow(gameType);gameType.GetProperty("selectedSizeIndex",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).SetValue(window,index);window.Show();window.Repaint();
        }
    }
}

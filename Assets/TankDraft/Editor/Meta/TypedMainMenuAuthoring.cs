using System;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Vareiko.Foundation.UI;
using TankDraft.UI;

namespace TankDraft.Editor.UI
{
    // One-time editor authoring via MCP. The resulting standalone prefabs are the source of truth.
    public static class TypedMainMenuAuthoring
    {
        const string Root = "Assets/TankDraft/Prefabs/UI/";
        const string Menu = Root + "MainMenuScreen/";
        static Scene scene;
        static TMP_FontAsset font;
        static Sprite surface;
        static readonly Color Background = new Color32(23,33,43,255);
        static readonly Color Panel = new Color32(43,58,70,255);
        static readonly Color Accent = new Color32(193,159,100,255);

        static RectTransform New(string name, Transform parent, float w, float h)
        {
            var r = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            if (parent) r.SetParent(parent, false); else SceneManager.MoveGameObjectToScene(r.gameObject, scene);
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(.5f,.5f);
            r.sizeDelta = new Vector2(w,h); return r;
        }
        static void Stretch(RectTransform r)
        { r.anchorMin=Vector2.zero; r.anchorMax=Vector2.one; r.offsetMin=r.offsetMax=Vector2.zero; }
        static void At(RectTransform r,float x,float y,float w,float h)
        { r.anchorMin=r.anchorMax=r.pivot=new Vector2(0,1);r.anchoredPosition=new Vector2(x,-y);r.sizeDelta=new Vector2(w,h); }
        static void TopCenter(RectTransform r,float y,float w,float h)
        {r.anchorMin=r.anchorMax=r.pivot=new Vector2(.5f,1);r.anchoredPosition=new Vector2(0,-y);r.sizeDelta=new Vector2(w,h);}
        static Image Image(RectTransform r, Color color, bool raycast=false)
        {var i=r.gameObject.AddComponent<Image>();i.sprite=surface;i.type=UnityEngine.UI.Image.Type.Sliced;i.color=color;i.raycastTarget=raycast;return i;}
        static TMP_Text Text(string name,Transform parent,string value,float size=22)
        {var r=New(name,parent,0,0);Stretch(r);var t=r.gameObject.AddComponent<TextMeshProUGUI>();t.font=font;t.text=value;t.fontSize=size;t.alignment=TextAlignmentOptions.Center;t.color=Color.white;t.raycastTarget=false;t.textWrappingMode=TextWrappingModes.Normal;return t;}
        static void Ref(UnityEngine.Object target,string name,UnityEngine.Object value)
        {var so=new SerializedObject(target);var p=so.FindProperty(name);if(p==null)throw new Exception("Missing field "+target.GetType()+"."+name);p.objectReferenceValue=value;so.ApplyModifiedPropertiesWithoutUndo();}
        static void Refs(UnityEngine.Object target,string name,UnityEngine.Object[] values)
        {var so=new SerializedObject(target);var p=so.FindProperty(name);if(p==null)throw new Exception("Missing array "+name);p.arraySize=values.Length;for(int i=0;i<values.Length;i++)p.GetArrayElementAtIndex(i).objectReferenceValue=values[i];so.ApplyModifiedPropertiesWithoutUndo();}
        static T Role<T>(RectTransform r,string id,bool hide=false) where T:UIElement
        {var c=r.gameObject.AddComponent<T>();var so=new SerializedObject(c);so.FindProperty("_id").stringValue=id;so.FindProperty("_hideOnAwake").boolValue=hide;so.FindProperty("_canvasGroup").objectReferenceValue=r.GetComponent<CanvasGroup>();if(c is UIWindow)so.FindProperty("_isModal").boolValue=false;so.ApplyModifiedPropertiesWithoutUndo();return c;}
        static void Cell(RectTransform r,float w,float h)
        {var l=r.GetComponent<LayoutElement>()??r.gameObject.AddComponent<LayoutElement>();l.minWidth=l.preferredWidth=w;l.minHeight=l.preferredHeight=h;l.flexibleWidth=l.flexibleHeight=0;}
        static HorizontalLayoutGroup Row(RectTransform r,float spacing)
        {var g=r.gameObject.AddComponent<HorizontalLayoutGroup>();g.childAlignment=TextAnchor.MiddleCenter;g.spacing=spacing;g.childControlWidth=g.childControlHeight=true;g.childForceExpandWidth=g.childForceExpandHeight=false;return g;}
        static UIButtonView Button(string name,Transform parent,string caption,float w,float h,out TMP_Text text)
        {var r=New(name,parent,w,h);var visual=New("Background_Image",r,w,h);Stretch(visual);var image=Image(visual,Panel,true);var b=r.gameObject.AddComponent<Button>();b.targetGraphic=image;var view=Role<UIButtonView>(r,"");Ref(view,"_button",b);text=Text("Label_Text",r,caption,22);return view;}
        static GameObject Save(RectTransform r,string path)
        {if(File.Exists(path))throw new IOException("Refusing overwrite: "+path);Directory.CreateDirectory(Path.GetDirectoryName(path));PrefabUtility.SaveAsPrefabAsset(r.gameObject,path,out bool ok);if(!ok)throw new IOException(path);UnityEngine.Object.DestroyImmediate(r.gameObject);return AssetDatabase.LoadAssetAtPath<GameObject>(path);}
        static RectTransform Instance(GameObject prefab,Transform parent)
        {return ((GameObject)PrefabUtility.InstantiatePrefab(prefab,parent)).GetComponent<RectTransform>();}
        static TMP_FontAsset Font()
        {
            const string path="Assets/TankDraft/Art/UI/Fonts/TankDraftUI SDF.asset";
            var existing=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);if(existing)return existing;
            var source=AssetDatabase.LoadAssetAtPath<Font>("Assets/TextMesh Pro/Fonts/LiberationSans.ttf");if(!source)throw new Exception("TMP Essential Resources are required");
            var f=TMP_FontAsset.CreateFontAsset(source);f.name="TankDraftUI SDF";f.atlasPopulationMode=AtlasPopulationMode.Dynamic;
            f.TryAddCharacters("0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyzАБВГДЕЁЖЗИЙКЛМНОПРСТУФХЦЧШЩЪЫЬЭЮЯабвгдеёжзийклмнопрстуфхцчшщъыьэюя<>/.,:… —");
            Directory.CreateDirectory(Path.GetDirectoryName(path));AssetDatabase.CreateAsset(f,path);AssetDatabase.AddObjectToAsset(f.material,f);foreach(var a in f.atlasTextures)AssetDatabase.AddObjectToAsset(a,f);EditorUtility.SetDirty(f);AssetDatabase.SaveAssets();return f;
        }
        public static string Build()
        {
            if(UnityEngine.Application.dataPath.Replace('\\','/')!="U:/UNITY_PROJECTS/TankDraft/Assets")throw new Exception("Wrong project");
            if(File.Exists(Root+"UIRoot.prefab"))return "Typed main menu already exists; edit its standalone prefabs.";
            font=Font();surface=AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");if(!surface)throw new Exception("Built-in UI surface missing");
            scene=EditorSceneManager.NewPreviewScene();
            try
            {
                var counter=New("UICurrencyCounterView",null,120,48);Cell(counter,120,48);var cv=Role<UICurrencyCounterView>(counter,"");Image(counter,Panel);
                var icon=New("Icon_Image",counter,32,32);At(icon,8,8,32,32);var iconImage=icon.gameObject.AddComponent<Image>();iconImage.raycastTarget=false;iconImage.enabled=false;
                var amount=Text("Amount_Text",counter,"0",21);amount.rectTransform.offsetMin=new Vector2(44,0);amount.rectTransform.offsetMax=new Vector2(-8,0);Ref(cv,"_amountText",amount);Ref(cv,"_iconImage",iconImage);
                var counterAsset=Save(counter,Root+"Common/UICurrencyCounterView.prefab");

                var reward=New("UIRewardSlotView",null,81,87);Cell(reward,81,87);var rv=Role<UIRewardSlotView>(reward,"");
                var rb=Button("Claim_Button",reward,"",81,87,out var rewardCaption);Stretch(rb.GetComponent<RectTransform>());rewardCaption.name="Caption_Text";rewardCaption.fontSize=17;rewardCaption.rectTransform.offsetMax=new Vector2(0,-45);
                var rewardIcon=New("Icon_Image",reward,38,38);At(rewardIcon,21,7,38,38);var ri=rewardIcon.gameObject.AddComponent<Image>();ri.enabled=false;ri.raycastTarget=false;
                Ref(rv,"_captionText",rewardCaption);Ref(rv,"_iconImage",ri);Ref(rv,"_button",rb);var rewardAsset=Save(reward,Root+"Common/UIRewardSlotView.prefab");

                var nav=Button("UINavigationButton",null,"",107,163,out var navText);Cell(nav.GetComponent<RectTransform>(),107,163);var navAsset=Save(nav.GetComponent<RectTransform>(),Root+"Common/UINavigationButton.prefab");

                var currencies=New("UIMainMenuCurrenciesPanel",null,528,48);var cp=Role<UIMainMenuCurrenciesPanel>(currencies,"main_menu.hud.currencies");var cc=New("Currencies_Container",currencies,528,48);Stretch(cc);Row(cc,10);
                var counterViews=new UnityEngine.Object[4];string[] counterNames={"Energy_Counter","Gems_Counter","Coins_Counter","Mastery_Counter"};for(int i=0;i<4;i++){var inst=Instance(counterAsset,cc);inst.name=counterNames[i];counterViews[i]=inst.GetComponent<UICurrencyCounterView>();}Refs(cp,"_items",counterViews);
                var currencyPanelAsset=Save(currencies,Menu+"MainMenuHudWindow/UIMainMenuCurrenciesPanel.prefab");

                var rewards=New("UIMainMenuRewardsPanel",null,382,87);var rp=Role<UIMainMenuRewardsPanel>(rewards,"main_menu.arena.rewards");var rc=New("Rewards_Container",rewards,382,87);Stretch(rc);Row(rc,10);
                var rewardViews=new UnityEngine.Object[4];for(int i=0;i<4;i++){var inst=Instance(rewardAsset,rc);inst.name="RewardSlot_0"+(i+1);rewardViews[i]=inst.GetComponent<UIRewardSlotView>();}Refs(rp,"_items",rewardViews);
                var rewardsAsset=Save(rewards,Menu+"MainMenuArenaWindow/UIMainMenuRewardsPanel.prefab");

                var navigation=New("UIMainMenuNavigationPanel",null,576,163);var np=Role<UIMainMenuNavigationPanel>(navigation,"main_menu.hud.navigation");var nc=New("Navigation_Container",navigation,576,163);Stretch(nc);Row(nc,0);
                var buttons=new UnityEngine.Object[5];var labels=new UnityEngine.Object[5];string[] navNames={"Shop_Button","Collection_Button","Battle_Button","Events_Button","Rating_Button"};for(int i=0;i<5;i++){var inst=Instance(navAsset,nc);inst.name=navNames[i];buttons[i]=inst.GetComponent<UIButtonView>();labels[i]=inst.GetComponentInChildren<TMP_Text>();if(i==2){Cell(inst,148,163);inst.GetComponentInChildren<Image>().color=Accent;}}Refs(np,"_buttons",buttons);Refs(np,"_labels",labels);
                var navPanelAsset=Save(navigation,Menu+"MainMenuHudWindow/UIMainMenuNavigationPanel.prefab");

                var hud=New("UIMainMenuHudWindow",null,576,1280);var hv=Role<UIMainMenuHudWindow>(hud,"main_menu.hud");
                var currencyInst=Instance(currencyPanelAsset,hud);TopCenter(currencyInst,49,528,48);Ref(hv,"_currenciesPanel",currencyInst.GetComponent<UIMainMenuCurrenciesPanel>());
                var navInst=Instance(navPanelAsset,hud);navInst.anchorMin=new Vector2(0,0);navInst.anchorMax=new Vector2(1,0);navInst.pivot=new Vector2(.5f,0);navInst.anchoredPosition=Vector2.zero;navInst.sizeDelta=new Vector2(0,163);Ref(hv,"_navigationPanel",navInst.GetComponent<UIMainMenuNavigationPanel>());
                var profile=Button("Profile_Button",hud,"",313,124,out var profileText);At(profile.GetComponent<RectTransform>(),10,110,313,124);profileText.name="PlayerName_Text";Ref(hv,"_profileButton",profile);Ref(hv,"_profileNameText",profileText);
                var pass=Button("Pass_Button",hud,"",250,112,out var passText);var passRect=pass.GetComponent<RectTransform>();passRect.anchorMin=passRect.anchorMax=passRect.pivot=Vector2.one;passRect.anchoredPosition=new Vector2(0,-120);passRect.sizeDelta=new Vector2(250,112);passText.name="PassProgress_Text";Ref(hv,"_passButton",pass);Ref(hv,"_passProgressText",passText);
                var settings=Button("Settings_Button",hud,"…",66,68,out var settingsText);At(settings.GetComponent<RectTransform>(),487,264,66,68);Ref(hv,"_settingsButton",settings);
                var hudAsset=Save(hud,Menu+"MainMenuHudWindow/UIMainMenuHudWindow.prefab");

                var arena=New("UIMainMenuArenaWindow",null,576,1280);var av=Role<UIMainMenuArenaWindow>(arena,"main_menu.arena");
                var arenaVisual=New("Arena_Visual",arena,413,294);At(arenaVisual,81,430,413,294);Image(arenaVisual,Panel);
                var progress=New("Progress_Panel",arena,502,74);At(progress,38,718,502,74);Image(progress,Panel);var progressText=Text("Progress_Text",progress,"",24);Ref(av,"_progressText",progressText);
                var arenaTitle=Text("ArenaName_Text",arena,"",23);At(arenaTitle.rectTransform,129,793,317,44);Ref(av,"_arenaNameText",arenaTitle);
                var rewardInst=Instance(rewardsAsset,arena);TopCenter(rewardInst,866,382,87);Ref(av,"_rewardsPanel",rewardInst.GetComponent<UIMainMenuRewardsPanel>());
                var battle=Button("StartBattle_Button",arena,"",288,111,out var battleText);TopCenter(battle.GetComponent<RectTransform>(),964,288,111);battle.GetComponentInChildren<Image>().color=Accent;Ref(av,"_battleButton",battle);Ref(av,"_battleLabel",battleText);
                var summary=Button("LastResult_Button",arena,"",502,76,out var summaryText);At(summary.GetComponent<RectTransform>(),38,342,502,76);summaryText.fontSize=20;Ref(av,"_resultButton",summary);Ref(av,"_resultLabel",summaryText);
                var arenaAsset=Save(arena,Menu+"MainMenuArenaWindow/UIMainMenuArenaWindow.prefab");

                var screen=New("UIMainMenuScreen",null,576,1280);screen.gameObject.AddComponent<CanvasGroup>();var sv=Role<UIMainMenuScreen>(screen,"main_menu",true);
                var bg=New("Background_Image",screen,576,1280);Stretch(bg);Image(bg,Background);
                var arenaWindow=Instance(arenaAsset,screen);Stretch(arenaWindow);Ref(sv,"_arenaWindow",arenaWindow.GetComponent<UIMainMenuArenaWindow>());
                var hudWindow=Instance(hudAsset,screen);Stretch(hudWindow);Ref(sv,"_hudWindow",hudWindow.GetComponent<UIMainMenuHudWindow>());
                var refresh=screen.gameObject.AddComponent<UiLayoutRefresh>();Ref(refresh,"_root",screen);Refs(refresh,"_groups",screen.GetComponentsInChildren<LayoutGroup>(true));Ref(sv,"_layoutRefresh",refresh);
                var screenAsset=Save(screen,Menu+"UIMainMenuScreen.prefab");

                var root=New("UIRoot",null,576,1280);var canvas=root.gameObject.AddComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;var scaler=root.gameObject.AddComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(576,1280);scaler.matchWidthOrHeight=.5f;root.gameObject.AddComponent<GraphicRaycaster>();root.gameObject.AddComponent<UIRegistry>();var main=Instance(screenAsset,root);Stretch(main);Save(root,Root+"UIRoot.prefab");
                AssetDatabase.SaveAssets();return "Authored typed UI root, screen, 2 windows, 3 panels and 3 shared items; no scene changed.";
            }
            finally {EditorSceneManager.ClosePreviewScene(scene);}
        }
    }
}


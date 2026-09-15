using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;
using TMPro;
using Vareiko.Foundation.UI;
using TankDraft.UI;
using TankDraft.Content;
using TankDraft.Contracts;
using TankDraft.Bootstrap;
using TankDraft.Infrastructure;

namespace TankDraft.Editor.UI
{
    public static class MainMenuRuntimeAuthoring
    {
        const string UI = "Assets/TankDraft/Prefabs/UI/";
        const string Config = "Assets/TankDraft/Configs/Meta/";
        public const string ScenePath = "Assets/TankDraft/Scenes/Frontend/MainMenu.unity";
        static Scene scene;
        static TMP_FontAsset font;
        static Sprite surface;
        static readonly Color Panel = new Color32(43, 58, 70, 255), Accent = new Color32(193, 159, 100, 255);
        static RectTransform New(string name, Transform parent, float w, float h)
        {
            var r = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            if (parent)
                r.SetParent(parent, false);
            else
                SceneManager.MoveGameObjectToScene(r.gameObject, scene);
            r.sizeDelta = new Vector2(w, h);
            return r;
        }

        static void Stretch(RectTransform r)
        {
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = r.offsetMax = Vector2.zero;
        }

        static void At(RectTransform r, float x, float y, float w, float h)
        {
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(0, 1);
            r.anchoredPosition = new Vector2(x, -y);
            r.sizeDelta = new Vector2(w, h);
        }

        static void Top(RectTransform r, float y, float w, float h)
        {
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(.5f, 1);
            r.anchoredPosition = new Vector2(0, -y);
            r.sizeDelta = new Vector2(w, h);
        }

        static Image Image(RectTransform r, Color color, bool input = false)
        {
            var i = r.gameObject.AddComponent<Image>();
            i.sprite = surface;
            i.type = UnityEngine.UI.Image.Type.Sliced;
            i.color = color;
            i.raycastTarget = input;
            return i;
        }

        static TMP_Text Text(string name, Transform parent, float size = 22)
        {
            var r = New(name, parent, 0, 0);
            Stretch(r);
            var t = r.gameObject.AddComponent<TextMeshProUGUI>();
            t.font = font;
            t.fontSize = size;
            t.text = "";
            t.color = Color.white;
            t.alignment = TextAlignmentOptions.Center;
            t.raycastTarget = false;
            return t;
        }

        static void Set(UnityEngine.Object o, string field, object value)
        {
            var s = new SerializedObject(o);
            var p = s.FindProperty(field);
            if (p == null)
                throw new Exception(o.name + "." + field);
            if (value is string str)
                p.stringValue = str;
            else if (value is int n)
                p.intValue = n;
            else if (value is bool b)
                p.boolValue = b;
            else if (value is Color c)
                p.colorValue = c;
            else
                p.objectReferenceValue = value as UnityEngine.Object;
            s.ApplyModifiedPropertiesWithoutUndo();
        }

        static void Array(UnityEngine.Object o, string field, UnityEngine.Object[] values)
        {
            var s = new SerializedObject(o);
            var p = s.FindProperty(field);
            p.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
                p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            s.ApplyModifiedPropertiesWithoutUndo();
        }

        static void Strings(UnityEngine.Object o, string field, string[] values)
        {
            var s = new SerializedObject(o);
            var p = s.FindProperty(field);
            p.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
                p.GetArrayElementAtIndex(i).stringValue = values[i];
            s.ApplyModifiedPropertiesWithoutUndo();
        }

        static T Role<T>(RectTransform r, string id, bool hide = false, bool modal = false)
            where T : UIElement
        {
            var v = r.gameObject.AddComponent<T>();
            Set(v, "_id", id);
            Set(v, "_hideOnAwake", hide);
            if (v is UIWindow)
                Set(v, "_isModal", modal);
            return v;
        }

        static UIButtonView Button(string name, Transform p, float w, float h, out TMP_Text label)
        {
            var r = New(name, p, w, h);
            var image = Image(r, Panel, true);
            var b = r.gameObject.AddComponent<Button>();
            b.targetGraphic = image;
            var v = Role<UIButtonView>(r, "");
            Set(v, "_button", b);
            label = Text("Label_Text", r);
            return v;
        }

        static HorizontalLayoutGroup Row(RectTransform r)
        {
            var g = r.gameObject.AddComponent<HorizontalLayoutGroup>();
            g.spacing = 6;
            g.childAlignment = TextAnchor.MiddleCenter;
            g.childControlWidth = g.childControlHeight = true;
            g.childForceExpandWidth = g.childForceExpandHeight = false;
            return g;
        }

        static void Cell(RectTransform r, float w, float h)
        {
            var l = r.gameObject.AddComponent<LayoutElement>();
            l.minWidth = l.preferredWidth = w;
            l.minHeight = l.preferredHeight = h;
            l.flexibleWidth = l.flexibleHeight = 0;
        }

        static GameObject Save(RectTransform r, string path)
        {
            if (File.Exists(path))
                throw new IOException("Refusing overwrite: " + path);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var asset = PrefabUtility.SaveAsPrefabAsset(r.gameObject, path);
            UnityEngine.Object.DestroyImmediate(r.gameObject);
            return asset;
        }

        static T Asset<T>(string path)
            where T : ScriptableObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing)
                return existing;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var a = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(a, path);
            return a;
        }

        static RectTransform Instance(GameObject prefab, Transform parent)
        {
            return ((GameObject)PrefabUtility.InstantiatePrefab(prefab, parent)).GetComponent<RectTransform>();
        }

        public static string Build()
        {
            if (EditorApplication.isPlaying)
                throw new Exception("Edit Mode required.");
            if (File.Exists(ScenePath))
                return "Runtime scene already authored; edit source assets.";
            font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TankDraft/Art/UI/Fonts/TankDraftUI SDF.asset");
            surface = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            var catalog = CreateConfigs();
            scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var card = New("UICatalogCardView", null, 134, 199);
                Cell(card, 134, 199);
                Image(card, Panel, true);
                var b = card.gameObject.AddComponent<Button>();
                b.targetGraphic = card.GetComponent<Image>();
                var cb = Role<UIButtonView>(card, "");
                Set(cb, "_button", b);
                var cv = Role<UICatalogCardView>(card, "");
                Set(cv, "_button", cb);
                var selected = New("SelectedState_Image", card, 134, 6);
                At(selected, 0, 0, 134, 6);
                Image(selected, Accent);
                Set(cv, "_selectedState", selected.gameObject);
                var icon = New("Icon_Image", card, 90, 90);
                Top(icon, 17, 90, 90);
                var ci = icon.gameObject.AddComponent<Image>();
                ci.raycastTarget = false;
                ci.enabled = false;
                Set(cv, "_iconImage", ci);
                var title = Text("Title_Text", card, 20);
                At(title.rectTransform, 4, 105, 126, 58);
                Set(cv, "_titleText", title);
                var subtitle = Text("Subtitle_Text", card, 15);
                At(subtitle.rectTransform, 4, 166, 126, 30);
                Set(cv, "_subtitleText", subtitle);
                var cardAsset = Save(card, UI + "Common/UICatalogCardView.prefab");
                var slots = New("UILoadoutPanel", null, 576, 199);
                var lp = Role<UILoadoutPanel>(slots, "");
                var row = Row(slots);
                Set(lp, "_group", row);
                var items = new UnityEngine.Object[4];
                for (int i = 0; i < 4; i++)
                {
                    var r = Instance(cardAsset, slots);
                    r.name = "LoadoutSlot_0" + (i + 1);
                    items[i] = r.GetComponent<UICatalogCardView>();
                }

                Array(lp, "_items", items);
                var slotAsset = Save(slots, UI + "Common/UILoadoutPanel.prefab");
                var collection = New("UIMainMenuCollectionWindow", null, 576, 1280);
                var cw = Role<UIMainMenuCollectionWindow>(collection, "main_menu.collection", true);
                var tabs = New("CategoryTabs_Container", collection, 546, 62);
                Top(tabs, 126, 546, 62);
                Row(tabs);
                var units = Button("Units_Button", tabs, 270, 62, out var unitLabel);
                Cell(units.GetComponent<RectTransform>(), 270, 62);
                Set(cw, "_unitsButton", units);
                Set(cw, "_unitsLabel", unitLabel);
                var orders = Button("Orders_Button", tabs, 270, 62, out var orderLabel);
                Cell(orders.GetComponent<RectTransform>(), 270, 62);
                Set(cw, "_ordersButton", orders);
                Set(cw, "_ordersLabel", orderLabel);
                var loadout = Instance(slotAsset, collection);
                Top(loadout, 207, 576, 199);
                Set(loadout.GetComponent<UILoadoutPanel>(), "_id", "main_menu.collection.loadout");
                Set(cw, "_loadoutPanel", loadout.GetComponent<UILoadoutPanel>());
                var summary = Text("LoadoutSummary_Text", collection, 18);
                Top(summary.rectTransform, 425, 554, 30);
                Set(cw, "_summaryText", summary);
                var heading = Text("CollectionHeading_Text", collection, 28);
                Top(heading.rectTransform, 467, 554, 44);
                Set(cw, "_headingText", heading);
                var scroll = New("Collection_ScrollView", collection, 576, 585);
                Stretch(scroll);
                scroll.offsetMin = new Vector2(0, 170);
                scroll.offsetMax = new Vector2(0, -525);
                Image(scroll, Color.clear, true);
                scroll.gameObject.AddComponent<RectMask2D>();
                var sr = scroll.gameObject.AddComponent<ScrollRect>();
                sr.horizontal = false;
                sr.movementType = ScrollRect.MovementType.Clamped;
                sr.viewport = scroll;
                var content = New("Cards_Container", scroll, 576, 650);
                content.anchorMin = new Vector2(0, 1);
                content.anchorMax = Vector2.one;
                content.pivot = new Vector2(.5f, 1);
                content.sizeDelta = new Vector2(0, 650);
                var grid = content.gameObject.AddComponent<GridLayoutGroup>();
                grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                grid.constraintCount = 4;
                grid.cellSize = new Vector2(134, 199);
                grid.spacing = new Vector2(6, 14);
                grid.padding = new RectOffset(11, 11, 0, 12);
                grid.childAlignment = TextAnchor.UpperCenter;
                var template = Instance(cardAsset, content);
                template.name = "Card_Template";
                template.gameObject.SetActive(false);
                var cp = Role<UICatalogPanel>(content, "main_menu.collection.catalog");
                Set(cp, "_template", template.GetComponent<UICatalogCardView>());
                Set(cp, "_content", content);
                Set(cp, "_group", grid);
                sr.content = content;
                Set(cw, "_catalogPanel", cp);
                Set(cw, "_scroll", sr);
                var collectionAsset = Save(collection, UI + "MainMenuScreen/MainMenuCollectionWindow/UIMainMenuCollectionWindow.prefab");
                var detail = New("UICardDetailsWindow", null, 576, 1280);
                var dv = Role<UICardDetailsWindow>(detail, "main_menu.card_details", true, true);
                Image(detail, new Color(0, 0, 0, .82f), true);
                var body = New("Details_Panel", detail, 552, 810);
                Top(body, 265, 552, 810);
                Image(body, Panel);
                Set(dv, "_detailsContainer", body.gameObject);
                var dt = Text("Title_Text", body, 30);
                At(dt.rectTransform, 14, 26, 524, 70);
                Set(dv, "_titleText", dt);
                var di = New("Icon_Image", body, 130, 130);
                Top(di, 106, 130, 130);
                var dim = di.gameObject.AddComponent<Image>();
                dim.raycastTarget = false;
                dim.enabled = false;
                Set(dv, "_iconImage", dim);
                var desc = Text("Description_Text", body, 23);
                At(desc.rectTransform, 22, 250, 508, 175);
                Set(dv, "_descriptionText", desc);
                var action = Button("Equip_Button", body, 320, 72, out var al);
                Top(action.GetComponent<RectTransform>(), 444, 320, 72);
                action.GetComponent<Image>().color = Accent;
                Set(dv, "_actionButton", action);
                Set(dv, "_actionLabel", al);
                var st = Text("SlotHeading_Text", body, 21);
                Top(st.rectTransform, 530, 530, 36);
                Set(dv, "_slotHeading", st);
                var ds = Instance(slotAsset, body);
                Top(ds, 580, 548, 199);
                Set(ds.GetComponent<UILoadoutPanel>(), "_id", "main_menu.card_details.slots");
                Set(dv, "_slotsPanel", ds.GetComponent<UILoadoutPanel>());
                var close = Button("Close_Button", detail, 300, 65, out var cl);
                Top(close.GetComponent<RectTransform>(), 1115, 300, 65);
                Set(dv, "_closeButton", close);
                Set(dv, "_closeLabel", cl);
                var detailAsset = Save(detail, UI + "MainMenuScreen/CardDetailsWindow/UICardDetailsWindow.prefab");
                var message = New("UIMessageWindow", null, 576, 1280);
                var mv = Role<UIMessageWindow>(message, "main_menu.message", true, true);
                Image(message, new Color(0, 0, 0, .86f), true);
                var mp = New("Message_Panel", message, 536, 460);
                Top(mp, 380, 536, 460);
                Image(mp, Panel);
                var mt = Text("Title_Text", mp, 29);
                At(mt.rectTransform, 16, 25, 504, 60);
                Set(mv, "_titleText", mt);
                var mb = Text("Body_Text", mp, 23);
                At(mb.rectTransform, 20, 105, 496, 225);
                Set(mv, "_bodyText", mb);
                var mc = Button("Close_Button", mp, 280, 70, out var ml);
                Top(mc.GetComponent<RectTransform>(), 360, 280, 70);
                Set(mv, "_closeButton", mc);
                Set(mv, "_closeLabel", ml);
                var messageAsset = Save(message, UI + "MainMenuScreen/MessageWindow/UIMessageWindow.prefab");
                UpdateNavigation();
                var screen = PrefabUtility.LoadPrefabContents(UI + "MainMenuScreen/UIMainMenuScreen.prefab");
                try
                {
                    Stretch(Instance(collectionAsset, screen.transform));
                    PrefabUtility.SaveAsPrefabAsset(screen, UI + "MainMenuScreen/UIMainMenuScreen.prefab");
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(screen);
                }

                var root = PrefabUtility.LoadPrefabContents(UI + "UIRoot.prefab");
                try
                {
                    var main = root.GetComponentInChildren<UIMainMenuScreen>(true);
                    var safe = New("SafeArea_Container", root.transform, 576, 1280);
                    Stretch(safe);
                    var safec = safe.gameObject.AddComponent<UiSafeArea>();
                    Set(safec, "_target", safe);
                    main.transform.SetParent(safe, false);
                    Stretch(main.GetComponent<RectTransform>());
                    // Modal windows are last siblings above the HUD and collection.
                    var details = Instance(detailAsset, safe);
                    Stretch(details);
                    var msg = Instance(messageAsset, safe);
                    Stretch(msg);
                    var refs = root.AddComponent<UIMainMenuRuntimeRoot>();
                    Set(refs, "_screen", main);
                    Set(refs, "_hud", main.GetComponentInChildren<UIMainMenuHudWindow>(true));
                    Set(refs, "_arena", main.GetComponentInChildren<UIMainMenuArenaWindow>(true));
                    Set(refs, "_collection", main.GetComponentInChildren<UIMainMenuCollectionWindow>(true));
                    Set(refs, "_details", details.GetComponent<UICardDetailsWindow>());
                    Set(refs, "_message", msg.GetComponent<UIMessageWindow>());
                    root.GetComponent<CanvasScaler>().matchWidthOrHeight = 0;
                    PrefabUtility.SaveAsPrefabAsset(root, UI + "UIRoot.prefab");
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }

                AssetDatabase.SaveAssets();
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }

            CreateScene(catalog);
            return "Authored 24 collection definitions, startup configs, collection/details/message prefabs, safe area and MainMenu scene.";
        }

        static void UpdateNavigation()
        {
            var path = UI + "MainMenuScreen/MainMenuHudWindow/UIMainMenuNavigationPanel.prefab";
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var v = root.GetComponent<UIMainMenuNavigationPanel>();
                var buttons = root.GetComponentsInChildren<UIButtonView>();
                var images = new UnityEngine.Object[buttons.Length];
                for (int i = 0; i < buttons.Length; i++)
                    images[i] = buttons[i].GetComponentInChildren<Image>();
                Array(v, "_backgrounds", images);
                Set(v, "_normalColor", Panel);
                Set(v, "_selectedColor", Accent);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        static MetaCatalogAsset CreateConfigs()
        {
            string[] unitIds =
            {
                "mines",
                "heavy_tank",
                "tank_destroyer",
                "field_artillery",
                "medium_tank",
                "light_raider",
                "repair_vehicle",
                "rocket_artillery",
                "drone_carrier",
                "mobile_mortar",
                "anti_tank_obstacle",
                "stealth_vehicle"
            };
            string[] unitNames =
            {
                "Противотанковые мины",
                "Тяжёлый танк",
                "ПТ-САУ",
                "Полевая САУ",
                "Средний танк",
                "Лёгкий рейдер",
                "Ремонтная машина",
                "Ракетная установка",
                "Носитель дронов",
                "Самоходный миномёт",
                "Противотанковые ежи",
                "Стелс-машина"
            };
            int[] rows =
            {
                0,
                1,
                2,
                3,
                1,
                1,
                1,
                3,
                3,
                3,
                0,
                1
            };
            string[] roles =
            {
                "Преграда",
                "Танк",
                "ПТ-САУ",
                "Артиллерия"
            };
            string[] orderIds =
            {
                "reinforce_armor",
                "repair_team",
                "smoke_screen",
                "artillery_strike",
                "minefield",
                "drone_recon",
                "precision_strike",
                "reinforcements",
                "overdrive",
                "emp",
                "supply_drop",
                "barrage"
            };
            string[] orderNames =
            {
                "Усиление брони",
                "Ремонтная группа",
                "Дымовая завеса",
                "Артудар",
                "Минное поле",
                "Разведка дронами",
                "Точный удар",
                "Подкрепление",
                "Форсаж",
                "ЭМИ",
                "Снабжение",
                "Огневой вал"
            };
            var entries = new List<UnityEngine.Object>();
            var owned = new List<string>();
            for (int kind = 0; kind < 2; kind++)
                for (int i = 0; i < 12; i++)
                {
                    string id = (kind == 0 ? "unit." : "order.") + (kind == 0 ? unitIds[i] : orderIds[i]);
                    var entry = Asset<ContentEntryAsset>(Config + (kind == 0 ? "Units/" : "Orders/") + id + ".asset");
                    Set(entry, "_id", id);
                    Set(entry, "_kind", kind);
                    Set(entry, "_row", kind == 0 ? rows[i] : 0);
                    Set(entry, "_requiredArena", i < (kind == 0 ? 6 : 3) ? 1 : 2 + i / 3);
                    Set(entry, "_title", kind == 0 ? unitNames[i] : orderNames[i]);
                    Set(entry, "_roleLabel", kind == 0 ? roles[rows[i]] : "Приказ");
                    Set(entry, "_description", kind == 0 ? "Роль в армии: " + roles[rows[i]] + ". Техника размещается в своей начальной линии." : "Тактический приказ для выбранной армии.");
                    entries.Add(entry);
                    if (i < (kind == 0 ? 6 : 3))
                        owned.Add(id);
                }

            var rules = Asset<ArmyRulesAsset>(Config + "Rules/ArmyRules.asset");
            var seed = Asset<NewProfileAsset>(Config + "Profile/NewProfile.asset");
            Set(seed, "_playerName", "Командир");
            Set(seed, "_energy", 5);
            Set(seed, "_gems", 100);
            Set(seed, "_coins", 1250);
            Set(seed, "_mastery", 10);
            Strings(seed, "_ownedIds", owned.ToArray());
            Strings(seed, "_unitIds", new[] { "unit.mines", "unit.heavy_tank", "unit.tank_destroyer", "unit.field_artillery" });
            Strings(seed, "_orderIds", new[] { "order.reinforce_armor", "", "" });
            var texts = Asset<UiTextCatalog>(Config + "UI/RussianTexts.asset");
            var values = new Dictionary<UiTextId, string>
            {
                {
                    UiTextId.Units,
                    "Техника"
                },
                {
                    UiTextId.Orders,
                    "Приказы"
                },
                {
                    UiTextId.Collection,
                    "Коллекция"
                },
                {
                    UiTextId.UnitLoadout,
                    "Нажмите на карточку, чтобы изменить состав"
                },
                {
                    UiTextId.OrderLoadout,
                    "Приказы вашей армии"
                },
                {
                    UiTextId.EmptySlot,
                    "Пусто"
                },
                {
                    UiTextId.CommanderGate,
                    "Командир {0}"
                },
                {
                    UiTextId.ArenaGate,
                    "Арена {0}"
                },
                {
                    UiTextId.NotOwned,
                    "Нет в коллекции"
                },
                {
                    UiTextId.Selected,
                    "В армии"
                },
                {
                    UiTextId.Equip,
                    "В АРМИЮ"
                },
                {
                    UiTextId.ChooseSlot,
                    "Выберите слот для замены"
                },
                {
                    UiTextId.Close,
                    "Закрыть"
                },
                {
                    UiTextId.Back,
                    "Назад"
                },
                {
                    UiTextId.Saved,
                    "Состав сохранён"
                },
                {
                    UiTextId.SaveFailed,
                    "Не удалось сохранить. Повторите попытку."
                },
                {
                    UiTextId.AlreadyEquipped,
                    "Уже выбрано"
                },
                {
                    UiTextId.InvalidSelection,
                    "Этот выбор недоступен"
                },
                {
                    UiTextId.Shop,
                    "Магазин"
                },
                {
                    UiTextId.Army,
                    "Армия"
                },
                {
                    UiTextId.Battle,
                    "В БОЙ"
                },
                {
                    UiTextId.Events,
                    "События"
                },
                {
                    UiTextId.Rating,
                    "Рейтинг"
                },
                {
                    UiTextId.PassProgress,
                    "Боевой пропуск"
                },
                {
                    UiTextId.ArenaTitle,
                    "Полигон · Арена {0}"
                },
                {
                    UiTextId.ArenaProgress,
                    "{0} / {1}"
                },
                {
                    UiTextId.EmptyReward,
                    "Пусто"
                },
                {
                    UiTextId.FeaturePending,
                    "Раздел пока недоступен."
                },
                {
                    UiTextId.MatchPending,
                    "Бои пока недоступны. Соберите армию в разделе «Армия»."
                },
                {
                    UiTextId.ProfileTitle,
                    "Профиль"
                },
                {
                    UiTextId.ProfileBody,
                    "{0}\nУровень командира: {1}\nАрена: {2}"
                },
                {
                    UiTextId.SettingsTitle,
                    "Настройки"
                },
                {
                    UiTextId.SettingsBody,
                    "Настройки пока недоступны."
                },
                {
                    UiTextId.RewardTitle,
                    "Награды"
                },
                {
                    UiTextId.RewardPending,
                    "В этом слоте пока нет награды."
                },
                {
                    UiTextId.PassTitle,
                    "Боевой пропуск"
                },
                {
                    UiTextId.PassPending,
                    "Боевой пропуск пока недоступен."
                },
                {
                    UiTextId.StartupErrorTitle,
                    "Не удалось загрузить профиль"
                },
                {
                    UiTextId.StartupErrorBody,
                    "Сохранение не изменено. Перезапустите игру и повторите попытку."
                },
                {
                    UiTextId.SlotFormat,
                    "Слот {0}"
                },
                {
                    UiTextId.RoleFormat,
                    "Роль: {0}"
                },
                {
                    UiTextId.StartupLoading,
                    "Загрузка…"
                }
            };
            var so = new SerializedObject(texts);
            var array = so.FindProperty("_entries");
            array.arraySize = values.Count;
            int pos = 0;
            foreach (var pair in values)
            {
                var element = array.GetArrayElementAtIndex(pos++);
                element.FindPropertyRelative("Id").intValue = (int)pair.Key;
                element.FindPropertyRelative("Value").stringValue = pair.Value;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            var catalog = Asset<MetaCatalogAsset>(Config + "Catalogs/MetaCatalog.asset");
            AppendResultTexts(texts);
            Array(catalog, "_entries", entries.ToArray());
            Set(catalog, "_armyRules", rules);
            Set(catalog, "_newProfile", seed);
            Set(catalog, "_uiText", texts);
            catalog.CreateDefinitions();
            AssetDatabase.SaveAssets();
            return catalog;
        }

        public static string ApplyResultPresentation()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play before authoring.");
            var texts = AssetDatabase.LoadAssetAtPath<UiTextCatalog>(Config + "UI/RussianTexts.asset");
            if (!texts) throw new InvalidOperationException("Existing UI texts required.");
            AppendResultTexts(texts);
            string path = UI + "MainMenuScreen/MainMenuArenaWindow/UIMainMenuArenaWindow.prefab";
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var view = root.GetComponent<UIMainMenuArenaWindow>();
                var so = new SerializedObject(view);
                var battle = so.FindProperty("_battleButton").objectReferenceValue as UIButtonView;
                if (!battle) throw new InvalidOperationException("Existing authored battle button required.");
                var found = root.transform.Find("LastResult_Button");
                var summary = found ? found.gameObject : UnityEngine.Object.Instantiate(battle.gameObject, root.transform);
                summary.name = "LastResult_Button";
                At(summary.GetComponent<RectTransform>(), 38, 342, 502, 76);
                var label = summary.GetComponentInChildren<TMP_Text>(true);
                label.fontSize = 20;
                label.enableAutoSizing = false;
                label.alignment = TextAlignmentOptions.Center;
                label.text = string.Empty;
                var background = summary.GetComponent<UnityEngine.UI.Image>();
                if (background) background.color = Panel;
                so.FindProperty("_resultButton").objectReferenceValue = summary.GetComponent<UIButtonView>();
                so.FindProperty("_resultLabel").objectReferenceValue = label;
                so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            var catalog = AssetDatabase.LoadAssetAtPath<MetaCatalogAsset>(Config + "Catalogs/MetaCatalog.asset");
            var catalogObject = new SerializedObject(catalog);
            catalogObject.FindProperty("_resultRefreshMilliseconds").intValue = 2000;
            catalogObject.FindProperty("_resultRefreshAttempts").intValue = 3;
            catalogObject.ApplyModifiedPropertiesWithoutUndo();
            string messagePath = UI + "MainMenuScreen/MessageWindow/UIMessageWindow.prefab";
            var message = PrefabUtility.LoadPrefabContents(messagePath);
            try
            {
                var so = new SerializedObject(message.GetComponent<UIMessageWindow>());
                var body = so.FindProperty("_bodyText").objectReferenceValue as TMP_Text;
                body.enableAutoSizing = true; body.fontSizeMin = 18; body.fontSizeMax = 23;
                PrefabUtility.SaveAsPrefabAsset(message, messagePath);
            }
            finally { PrefabUtility.UnloadPrefabContents(message); }
            texts.Validate(); catalog.CreateDefinitions(); AssetDatabase.SaveAssets();
            return "Result summary prefab, profile text sizing and authored text/refresh configs updated.";
        }

        static void AppendResultTexts(UiTextCatalog texts)
        {
            var values = new Dictionary<UiTextId, string>
            {
                { UiTextId.RecentResultSummary, "{0} {1}:{2} · {3}\nНажмите, чтобы обновить результаты" },
                { UiTextId.RecentResultLine, "{0} {1}:{2} · {3}" },
                { UiTextId.RecentResultsTitle, "Последние бои" },
                { UiTextId.ResultWin, "Победа" }, { UiTextId.ResultLoss, "Поражение" },
                { UiTextId.ResultRewardPending, "Награда ожидается: {0}" },
                { UiTextId.ResultRewardApplied, "Получено монет: {0}" },
                { UiTextId.ResultRewardReview, "Награда на проверке" },
                { UiTextId.ResultRewardSkipped, "Лимит тестовых наград" },
                { UiTextId.ProfileRefreshFailed, "Не удалось обновить. Данные сохранены; откройте окно ещё раз." },
                { UiTextId.ProfileRefreshing, "Обновляем данные…" }, { UiTextId.Retry, "Повторить" }
            };
            var so = new SerializedObject(texts);
            var entries = so.FindProperty("_entries");
            foreach (var pair in values)
            {
                SerializedProperty entry = null;
                for (int i = 0; i < entries.arraySize; i++)
                    if (entries.GetArrayElementAtIndex(i).FindPropertyRelative("Id").intValue == (int)pair.Key)
                    { entry = entries.GetArrayElementAtIndex(i); break; }
                if (entry == null) { int index = entries.arraySize++; entry = entries.GetArrayElementAtIndex(index); }
                entry.FindPropertyRelative("Id").intValue = (int)pair.Key;
                entry.FindPropertyRelative("Value").stringValue = pair.Value;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void CreateScene(MetaCatalogAsset catalog)
        {
            var previous = SceneManager.GetActiveScene();
            var s = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                var root = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(UI + "UIRoot.prefab"), s);
                var events = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                SceneManager.MoveGameObjectToScene(events, s);
                events.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
                var go = new GameObject("MainMenuLifetimeScope");
                SceneManager.MoveGameObjectToScene(go, s);
                var scope = go.AddComponent<MainMenuLifetimeScope>();
                Set(scope, "_catalog", catalog);
                Set(scope, "_matchSettings", AssetDatabase.LoadAssetAtPath<ScriptableObject>("Assets/TankDraft/Configs/Match/MatchSettings.asset"));
                Set(scope, "_queueSettings", AssetDatabase.LoadAssetAtPath<ScriptableObject>("Assets/TankDraft/Configs/Match/Server/NetworkQueueSettings.asset"));
                Set(scope, "_uiRoot", root.GetComponent<UIMainMenuRuntimeRoot>());
                Set(scope, "_profileSettings", Asset<LocalProfileSettings>(Config + "Profile/LocalProfileSettings.asset"));
                Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
                EditorSceneManager.SaveScene(s, ScenePath);
                AssetDatabase.SaveAssets();
            }
            finally
            {
                SceneManager.SetActiveScene(previous);
                EditorSceneManager.CloseScene(s, true);
            }
        }
    }
}

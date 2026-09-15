using System;
using System.Reflection;
using TankDraft.Match.Presentation;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace TankDraft.Match.Editor
{
    public static class ReferenceWaitingAuthoring
    {
        const string Path = "Assets/TankDraft/Prefabs/UI/Match/UIMatchScreen.prefab";
        public static string Author()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("Edit mode required.");
            var root = PrefabUtility.LoadPrefabContents(Path);
            try
            {
                var window = root.GetComponentInChildren<UIMatchDraftWindow>(true);
                var so = new SerializedObject(window);
                var label = so.FindProperty("_waitingLabelText").objectReferenceValue as TMP_Text;
                if (!label)
                {
                    var source = so.FindProperty("_orderLabelText").objectReferenceValue as TMP_Text;
                    if (!source) throw new InvalidOperationException("Authored draft label source missing.");
                    label = UnityEngine.Object.Instantiate(source, window.transform, false);
                    label.name = "Waiting_Label";
                    var rect = label.rectTransform;
                    rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
                    rect.pivot = new Vector2(.5f, .5f);
                    rect.sizeDelta = new Vector2(640, 64);
                    rect.anchoredPosition = new Vector2(0, -205);
                    label.alignment = TextAlignmentOptions.Center;
                    label.raycastTarget = false;
                    label.text = string.Empty;
                    label.gameObject.SetActive(false);
                    so.FindProperty("_waitingLabelText").objectReferenceValue = label;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
                var layout = so.FindProperty("_cardsLayout").objectReferenceValue as UnityEngine.UI.HorizontalLayoutGroup;
                layout.childAlignment = TextAnchor.MiddleCenter;
                PrefabUtility.SaveAsPrefabAsset(root, Path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            return "PASS waiting label and centred shared card layout authored.";
        }

        public static string ValidateRuntime(UIMatchDraftWindow window)
        {
            if (!UnityEngine.Application.isPlaying) throw new InvalidOperationException("Play Mode required.");
            var field = typeof(UIMatchDraftWindow).GetField("_cards", BindingFlags.NonPublic | BindingFlags.Instance);
            var cards = (MatchCardView[])field.GetValue(window);
            int clicks = 0;
            window.Initialize(_ => clicks++, () => { });
            var model = new MatchViewModel { OfferPresentationKey = "waiting-A", DraftVisible = true,
                Cards = new[] { Card("First"), Card("Second"), Card("Third") } };
            window.Render(model);
            cards[0].Click();
            var waiting = new MatchViewModel { OfferPresentationKey = "waiting-A", WaitingForOpponent = true, WaitingLabel = "Ожидание соперника" };
            window.Render(waiting);
            window.Render(waiting);
            if (cards[0].gameObject.activeSelf || cards[2].gameObject.activeSelf || !cards[1].gameObject.activeSelf || cards[1].Interactable)
                throw new InvalidOperationException("Waiting must retain exactly one disabled card.");
            cards[1].Click();
            if (clicks != 1) throw new InvalidOperationException("Waiting card submitted again.");
            var label = (TMP_Text)typeof(MatchCardView).GetField("_titleText", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(cards[1]);
            if (label.text != "First") throw new InvalidOperationException("Wrong selected offer retained.");
            waiting.PresentationReset = true;
            window.Render(waiting);
            if (Array.Exists(cards, c => c.gameObject.activeSelf)) throw new InvalidOperationException("Recovery invented a missing selected offer.");
            waiting.PresentationReset = false;
            waiting.OfferPresentationKey = "waiting-B";
            window.Render(waiting);
            if (Array.Exists(cards, c => c.gameObject.activeSelf)) throw new InvalidOperationException("Old selection leaked into new offer.");
            window.Hide();
            return "PASS waiting: selected content retained, disabled, no duplicate submit; reset/new token forget old selection.";
        }
        static MatchCardModel Card(string title) => new MatchCardModel { Title = title, ActionLabel = "+1", Enabled = true };
    }
}

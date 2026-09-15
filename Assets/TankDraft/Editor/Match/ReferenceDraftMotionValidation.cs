using System;
using System.Reflection;
using TankDraft.Match.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace TankDraft.Match.Editor
{
    public static class ReferenceDraftMotionValidation
    {
        public static string ValidateAsset()
        {
            MatchPresentationTimings timings = AssetDatabase.LoadAssetAtPath<MatchPresentationTimings>(ReferenceDraftMotionAuthoring.TimingsPath);
            if (timings == null)
                throw new InvalidOperationException("Reference presentation timings asset is missing.");
            timings.Validate();

            GameObject cardPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ReferenceDraftMotionAuthoring.CardPrefabPath);
            MatchCardView card = cardPrefab == null ? null : cardPrefab.GetComponent<MatchCardView>();
            MatchCardMotion motion = card == null ? null : card.GetComponent<MatchCardMotion>();
            if (motion == null || motion.VisualRoot == null || motion.VisualRoot.parent != card.transform || motion.VisualRoot.GetComponent<CanvasGroup>() == null)
                throw new InvalidOperationException("Match card must animate an internal VisualRoot with a CanvasGroup.");
            if (card.GetComponent<LayoutElement>() == null)
                throw new InvalidOperationException("Match card slot lost its LayoutElement.");

            return "PASS reference draft motion asset: timings and internal visual root are authored.";
        }

        // Invoke only in Play Mode against a disposable QA window. It intentionally verifies
        // presentation behavior without waiting for the visual animation to complete.
        public static string ValidateRuntime(UIMatchDraftWindow window)
        {
            if (!UnityEngine.Application.isPlaying)
                throw new InvalidOperationException("Reference draft motion runtime validation requires Play Mode.");
            if (window == null)
                throw new ArgumentNullException(nameof(window));

            MatchCardView[] cards = GetCards(window);
            if (cards == null || cards.Length != 3)
                throw new InvalidOperationException("Runtime draft window must contain exactly three card views.");
            for (int index = 0; index < cards.Length; index++)
            {
                if (cards[index] == null || cards[index].GetComponent<MatchCardMotion>() == null)
                    throw new InvalidOperationException("Runtime draft card is missing MatchCardMotion.");
            }

            int chooseCount = 0;
            window.Initialize(_ => chooseCount++, null);
            MatchViewModel first = CreateModel("reference-draft-offer-A", true);
            window.Render(first);
            int[] entrances = new int[cards.Length];
            float[] widths = new float[cards.Length];
            for (int index = 0; index < cards.Length; index++)
            {
                MatchCardMotion motion = cards[index].GetComponent<MatchCardMotion>();
                if (!motion.IsEntrancePlaying || motion.EntranceRunId <= 0)
                    throw new InvalidOperationException("Draft entrance did not start for card " + index + ".");
                entrances[index] = motion.EntranceRunId;
                widths[index] = ((RectTransform)cards[index].transform).rect.width;
            }

            cards[0].Click();
            if (chooseCount != 1)
                throw new InvalidOperationException("Draft click must submit exactly once without waiting for feedback.");

            MatchViewModel rebind = CreateModel("reference-draft-offer-A", false);
            window.Render(rebind);
            for (int index = 0; index < cards.Length; index++)
            {
                MatchCardMotion motion = cards[index].GetComponent<MatchCardMotion>();
                if (motion.EntranceRunId != entrances[index])
                    throw new InvalidOperationException("Rebind restarted draft presentation for card " + index + ".");
                float width = ((RectTransform)cards[index].transform).rect.width;
                if (!Mathf.Approximately(width, widths[index]))
                    throw new InvalidOperationException("Presentation changed LayoutGroup slot width for card " + index + ".");
            }

            window.Hide();
            for (int index = 0; index < cards.Length; index++)
                if (!cards[index].GetComponent<MatchCardMotion>().IsAtRest)
                    throw new InvalidOperationException("Hide did not reset presentation for card " + index + ".");

            return "PASS draft motion runtime: entrance once, immediate click, rebind stable, layout unchanged, hide reset.";
        }

        private static MatchCardView[] GetCards(UIMatchDraftWindow window)
        {
            FieldInfo field = typeof(UIMatchDraftWindow).GetField("_cards", BindingFlags.Instance | BindingFlags.NonPublic);
            return field?.GetValue(window) as MatchCardView[];
        }

        private static MatchViewModel CreateModel(string key, bool enabled)
        {
            return new MatchViewModel
            {
                OfferPresentationKey = key,
                DraftVisible = true,
                OrderVisible = false,
                OrderEnabled = false,
                Cards = new[]
                {
                    new MatchCardModel { Title = "Minefield", ActionLabel = "+1", Description = "Front", Enabled = enabled },
                    new MatchCardModel { Title = "Tank", ActionLabel = "Upgrade", Description = "Mid", Enabled = enabled },
                    new MatchCardModel { Title = "Artillery", ActionLabel = "+2", Description = "Back", Enabled = enabled }
                }
            };
        }
    }
}

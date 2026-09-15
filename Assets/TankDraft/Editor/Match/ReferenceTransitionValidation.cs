using System;
using System.Linq;
using System.Reflection;
using TankDraft.Match.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace TankDraft.Match.Editor
{
    public static class ReferenceTransitionValidation
    {
        public static string ValidateAsset()
        {
            MatchPresentationTimings timings = AssetDatabase.LoadAssetAtPath<MatchPresentationTimings>(ReferenceDraftMotionAuthoring.TimingsPath);
            if (timings == null)
                throw new InvalidOperationException("Reference presentation timings asset is missing.");
            timings.Validate();

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ReferenceTransitionAuthoring.ScreenPrefabPath);
            UIMatchHudWindow hud = prefab == null ? null : prefab.GetComponentInChildren<UIMatchHudWindow>(true);
            if (hud == null)
                throw new InvalidOperationException("Match screen is missing an authored HUD.");
            hud.Validate();
            CheckRows(prefab.transform, "OwnHearts_Row");
            CheckRows(prefab.transform, "OpponentHearts_Row");
            UIMatchTransitionView transition = prefab.GetComponentInChildren<UIMatchTransitionView>(true);
            if (transition == null)
                throw new InvalidOperationException("Match HUD is missing the authored round transition overlay.");
            transition.Validate();
            return "PASS reference transition asset: four heart slots per side and timed transition overlay are authored.";
        }

        // Invoke only in Play Mode on a disposable QA HUD. The method proves visual projection
        // behavior without touching a match, server clock, or input callbacks.
        public static string ValidateRuntime(UIMatchHudWindow hud)
        {
            if (!UnityEngine.Application.isPlaying)
                throw new InvalidOperationException("Reference transition runtime validation requires Play Mode.");
            if (hud == null)
                throw new ArgumentNullException(nameof(hud));
            hud.Validate();
            UIMatchTransitionView transition = Get<UIMatchTransitionView>(hud, "_transitions");
            foreach (HorizontalLayoutGroup row in hud.GetComponentsInChildren<HorizontalLayoutGroup>(true))
                if (row.name == "OwnHearts_Row" || row.name == "OpponentHearts_Row")
                {
                    LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)row.transform);
                    foreach (RectTransform slot in row.transform)
                        if (Mathf.Abs(slot.rect.width - 32f) > .01f || Mathf.Abs(slot.rect.height - 32f) > .01f)
                            throw new InvalidOperationException("Heart LayoutGroup did not apply the authored 32px slot size.");
                }

            MatchViewModel first = Model("match-a", 0d, 0, 0, false);
            hud.Render(first);
            if (!string.Equals(transition.CurrentText, "РАУНД 1", StringComparison.Ordinal))
                throw new InvalidOperationException("Round banner was not projected for the first battle snapshot.");
            if (Get<float>(hud, "_ownPulseElapsed") >= 0f || Get<float>(hud, "_opponentPulseElapsed") >= 0f)
                throw new InvalidOperationException("Initial heart binding must not animate a loss.");

            hud.Render(Model("match-a", .8d, 0, 0, false));
            if (!transition.IsVisible || !string.Equals(transition.CurrentText, "РАУНД 1", StringComparison.Ordinal))
                throw new InvalidOperationException("Round banner did not remain visible during its first segment.");
            hud.Render(Model("match-a", 1.1d, 0, 0, false));
            if (!transition.IsVisible || !string.Equals(transition.CurrentText, "СРАЖАЙСЯ", StringComparison.Ordinal))
                throw new InvalidOperationException("Fight banner was not projected after the round segment.");

            hud.Render(Model("match-a", 1.1d, 0, 1, false));
            if (Get<float>(hud, "_ownPulseElapsed") < 0f)
                throw new InvalidOperationException("A newly lost own heart must pulse after the initial binding.");
            // Advance presentation pulse independently of server battle age: receiving a
            // late snapshot does not mean Unity frames elapsed between these calls.
            typeof(UIMatchHudWindow).GetField("_ownPulseElapsed", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(hud, .1f);
            hud.Render(Model("match-a", 3d, 0, 1, false));
            if (transition.IsVisible || Get<float>(hud, "_ownPulseElapsed") != .1f || Get<float>(hud, "_opponentPulseElapsed") >= 0f)
                throw new InvalidOperationException("Late battle age replayed a transition or restarted the existing heart pulse.");
            hud.Render(Model("match-a", 1.1d, 0, 1, false));
            if (transition.IsVisible || !string.IsNullOrEmpty(transition.CurrentText))
                throw new InvalidOperationException("A stale HUD frame replayed a completed transition.");
            hud.Render(Model("match-a", .5d, 0, 1, true));
            if (Get<float>(hud, "_ownPulseElapsed") >= 0f || Get<float>(hud, "_opponentPulseElapsed") >= 0f)
                throw new InvalidOperationException("Recovery must clear historical heart pulses.");
            hud.Render(Model("match-a", .5d, 0, 1, false));
            if (transition.IsVisible || !string.IsNullOrEmpty(transition.CurrentText))
                throw new InvalidOperationException("A recovered snapshot replayed a suppressed transition.");
            hud.Render(Model("match-a", .5d, 0, 1, false, 2));
            if (!transition.IsVisible || !string.Equals(transition.CurrentText, "РАУНД 2", StringComparison.Ordinal))
                throw new InvalidOperationException("A new round did not own a fresh transition identity.");
            return "PASS transition runtime: first binding stable, score loss pulses, completed/recovered frames do not replay, new round classifies.";
        }

        private static MatchViewModel Model(string matchId, double age, int ownWins, int opponentWins, bool reset, int round = 1)
        {
            return new MatchViewModel
            {
                MatchPresentationId = matchId,
                PhaseName = "Battle",
                RoundNumber = round,
                OwnWins = ownWins,
                OpponentWins = opponentWins,
                WinsRequired = 4,
                BattleElapsedSeconds = age,
                PresentationReset = reset,
                MenuVisible = true
            };
        }

        private static void CheckRows(Transform root, string name)
        {
            Transform row = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(value => value.name == name);
            if (row == null || row.GetComponent<HorizontalLayoutGroup>() == null || row.childCount != 4)
                throw new InvalidOperationException("Match HUD needs an authored four-slot HorizontalLayoutGroup: " + name + ".");
        }

        private static T Get<T>(object target, string fieldName)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
                throw new MissingFieldException(target.GetType().Name, fieldName);
            return (T)field.GetValue(target);
        }
    }
}

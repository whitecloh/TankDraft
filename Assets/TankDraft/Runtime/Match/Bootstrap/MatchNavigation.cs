using System;
using System.Collections.Generic;
using TankDraft.Contracts;
using TankDraft.Match.Content;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TankDraft.Match.Bootstrap
{
    // Transient immutable request belongs to navigation, never to a ScriptableObject or save file.
    public sealed class MatchNavigation : IMatchLauncher
    {
        private static ProfileSnapshot _pending;
        private readonly MatchSettingsAsset _settings;
        public MatchNavigation(MatchSettingsAsset settings)
        {
            _settings = settings;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            _pending = null;
        }

        public static ProfileSnapshot Consume()
        {
            var request = _pending;
            _pending = null;
            return request;
        }

        public static bool Supports(ProfileSnapshot profile, MatchSettingsAsset settings)
        {
            if (profile == null || profile.UnitIds.Count != 4)
                return false;
            var supported = new HashSet<string>();
            foreach (var unit in settings.CreateUnits())
                supported.Add(unit.Id);
            var unique = new HashSet<string>();
            foreach (var id in profile.UnitIds)
                if (!supported.Contains(id) || !unique.Add(id))
                    return false;
            foreach (var id in profile.OrderIds)
                if (!string.IsNullOrEmpty(id) && id != "order.reinforce_armor")
                    return false;
            return true;
        }

        public bool TryLaunch(ProfileSnapshot profile, out string reason)
        {
            if (!_settings || !Supports(profile, _settings))
            {
                reason = _settings ? _settings.UnsupportedDeck : "Match settings are missing.";
                return false;
            }

            if (!UnityEngine.Application.CanStreamedLevelBeLoaded(_settings.MatchSceneName))
            {
                reason = "Match scene is missing from build settings.";
                return false;
            }

            _pending = profile;
            try
            {
                SceneManager.LoadScene(_settings.MatchSceneName);
            }
            catch
            {
                _pending = null;
                throw;
            }

            reason = string.Empty;
            return true;
        }
    }
}

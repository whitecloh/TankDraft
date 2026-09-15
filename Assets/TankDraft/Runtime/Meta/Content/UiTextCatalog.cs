using System;
using System.Collections.Generic;
using UnityEngine;
namespace TankDraft.Content
{
    public enum UiTextId
    {
        Units, Orders, Collection, UnitLoadout, OrderLoadout, EmptySlot, CommanderGate, ArenaGate, NotOwned,
        Selected, Equip, ChooseSlot, Close, Back, Saved, SaveFailed, AlreadyEquipped, InvalidSelection,
        Shop, Army, Battle, Events, Rating, PassProgress, ArenaTitle, ArenaProgress, EmptyReward,
        FeaturePending, MatchPending, ProfileTitle, ProfileBody, SettingsTitle, SettingsBody,
        RewardTitle, RewardPending, PassTitle, PassPending, StartupErrorTitle, StartupErrorBody,
        SlotFormat, RoleFormat, StartupLoading,
        RecentResultSummary, RecentResultLine, RecentResultsTitle, ResultWin, ResultLoss,
        ResultRewardPending, ResultRewardApplied, ResultRewardReview, ResultRewardSkipped,
        ProfileRefreshFailed, ProfileRefreshing, Retry
    }
    [CreateAssetMenu(menuName = "TankDraft/UI/Text catalog")]
    public sealed class UiTextCatalog : ScriptableObject
    {
        [Serializable] public struct Entry { public UiTextId Id; [TextArea] public string Value; }
        [SerializeField] private Entry[] _entries;
        public string Get(UiTextId id)
        {
            foreach (var entry in _entries) if (entry.Id == id) return entry.Value;
            throw new InvalidOperationException("Missing UI text: " + id);
        }
        public string Format(UiTextId id, params object[] arguments) => string.Format(System.Globalization.CultureInfo.CurrentCulture, Get(id), arguments);
        public void Validate()
        {
            if (_entries == null) throw new InvalidOperationException("UI text catalog is empty.");
            var ids = new HashSet<UiTextId>();
            foreach (var entry in _entries)
                if (!ids.Add(entry.Id) || string.IsNullOrWhiteSpace(entry.Value)) throw new InvalidOperationException("Invalid UI text: " + entry.Id);
            foreach (UiTextId id in Enum.GetValues(typeof(UiTextId))) Get(id);
        }
    }
}

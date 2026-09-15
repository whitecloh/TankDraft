using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using TankDraft.Contracts;
using TankDraft.Application;
using TankDraft.Content;

namespace TankDraft.UI
{
    public sealed class MainMenuPresenter : IMainMenuUiCommands, IDisposable
    {
        private readonly ProfileService _profiles;
        private readonly IMatchLauncher _matchLauncher;
        private readonly MetaDefinitions _definitions;
        private readonly MetaCatalogAsset _content;
        private readonly UIMainMenuRuntimeRoot _view;
        private readonly ProgressionPresenter _progression;
        private readonly string _progressionJournalScope;
        private UiTextCatalog Text => _content.Text;

        private bool _collection, _orders, _initialized, _choosingSlot, _equipPending, _disposed, _resultShowing;
        private string _detailId, _notice;
        private bool _profileOpen, _refreshPending, _refreshLoopRunning;
        private string _profileNotice;
        private readonly CancellationTokenSource _stop = new CancellationTokenSource();
        public bool IsBusy => _equipPending || _refreshPending || _resultShowing || (_progression?.IsBusy ?? false);
        public MainMenuPresenter(ProfileService profiles, MetaDefinitions definitions, MetaCatalogAsset content, UIMainMenuRuntimeRoot view, IMatchLauncher matchLauncher = null, ProgressionPresenter progression = null, string progressionJournalScope = null)
        {
            _matchLauncher = matchLauncher;
            _profiles = profiles;
            _definitions = definitions;
            _content = content;
            _view = view;
            _progression = progression;
            _progressionJournalScope = progressionJournalScope;
        }

        public void Initialize()
        {
            if (_initialized)
                return;
            _view.Validate();
            _view.Screen.Initialize(this, OpenLatestResult);
            _view.Collection.Initialize(() => OpenCollection(false), () => OpenCollection(true), OpenCard);
            _view.Details.Initialize(ChooseReplacement, CloseCard, EquipInSlot, OpenDetailProgression);
            _profiles.Changed += OnProfileChanged;
            _initialized = true;
            _view.Message.Hide();
            _view.Details.Hide();
            Refresh();
            TryShowLatestResult();
            if (_profiles.IsServerBacked && _profiles.Current.RecentResults.Count > 0)
                _ = RefreshResultsAsync(_content.ResultRefreshAttempts, true);
        }

        private void OnProfileChanged(ProfileSnapshot profile)
        {
            Refresh();
            if (_profileOpen) BindProfile();
            TryShowLatestResult();
        }
        private bool Selected(string id)
        {
            var p = _profiles.Current;
            foreach (var value in p.UnitIds)
                if (value == id)
                    return true;
            foreach (var value in p.OrderIds)
                if (value == id)
                    return true;
            return false;
        }

        private string Availability(string id)
        {
            switch (_profiles.GetAvailability(id))
            {
                case EquipResult.ArenaLocked:
                    return Text.Format(UiTextId.ArenaGate, _definitions.Get(id).RequiredArena);
                case EquipResult.NotOwned:
                    return Text.Get(UiTextId.NotOwned);
                default:
                    return Selected(id) ? Text.Get(UiTextId.Selected) : _content.GetVisual(id).RoleLabel;
            }
        }

        private CardViewModel Card(string id)
        {
            var visual = _content.GetVisual(id);
            return new CardViewModel(id, visual.Title, Availability(id), visual.Icon, true, Selected(id));
        }

        private CardViewModel[] Loadout(bool slotsForReplacement = false)
        {
            var p = _profiles.Current;
            var kind = slotsForReplacement ? _definitions.Get(_detailId).Kind : (_orders ? ContentKind.Order : ContentKind.Unit);
            var ids = kind == ContentKind.Unit ? p.UnitIds : p.OrderIds;
            var result = new CardViewModel[ids.Count];
            for (int i = 0; i < ids.Count; i++)
            {
                bool unlocked = kind == ContentKind.Unit || _profiles.IsOrderSlotUnlocked(i);
                var visual = string.IsNullOrEmpty(ids[i]) ? null : _content.GetVisual(ids[i]);
                result[i] = new CardViewModel(slotsForReplacement ? i.ToString() : ids[i], visual ? visual.Title : Text.Get(UiTextId.EmptySlot), unlocked ? (slotsForReplacement ? Text.Format(UiTextId.SlotFormat, i + 1) : visual ? Availability(ids[i]) : Text.Get(UiTextId.EmptySlot)) : Text.Format(UiTextId.CommanderGate, _definitions.Rules.OrderUnlockLevels[i]), visual ? visual.Icon : null, unlocked && (slotsForReplacement || visual != null), !slotsForReplacement && visual != null);
            }

            return result;
        }

        private CollectionViewModel CollectionModel()
        {
            var cards = new List<CardViewModel>();
            var kind = _orders ? ContentKind.Order : ContentKind.Unit;
            foreach (var definition in _definitions.Entries)
                if (definition.Kind == kind)
                    cards.Add(Card(definition.Id));
            return new CollectionViewModel(Loadout(), cards.ToArray(), Text.Get(UiTextId.Units), Text.Get(UiTextId.Orders), Text.Get(UiTextId.Collection), _notice ?? Text.Get(_orders ? UiTextId.OrderLoadout : UiTextId.UnitLoadout), _orders);
        }

        private MainMenuViewModel MenuModel()
        {
            var p = _profiles.Current;
            int[] amounts =
            {
                p.Energy,
                p.Gems,
                p.Coins,
                p.Mastery
            };
            var counters = new CurrencyCounterViewModel[4];
            for (int i = 0; i < counters.Length; i++)
                counters[i] = new CurrencyCounterViewModel(amounts[i].ToString(), _content.CurrencyIcon(i));
            var rewards = new RewardSlotViewModel[4];
            for (int i = 0; i < rewards.Length; i++)
                rewards[i] = new RewardSlotViewModel(Text.Get(UiTextId.EmptyReward), null, true);
            string resultSummary = p.RecentResults.Count == 0 ? string.Empty : ResultText(p.RecentResults[0], UiTextId.RecentResultSummary);
            return new MainMenuViewModel(counters, rewards, p.PlayerName, Text.Get(UiTextId.PassProgress), Text.Format(UiTextId.ArenaTitle, p.ArenaLevel), Text.Format(UiTextId.ArenaProgress, p.ArenaProgress, _content.ArenaTarget), Text.Get(UiTextId.Battle), new[] { Text.Get(UiTextId.Shop), Text.Get(UiTextId.Army), Text.Get(UiTextId.Battle), Text.Get(UiTextId.Events), Text.Get(UiTextId.Rating) }, resultSummary);
        }

        public void Refresh()
        {
            _view.Screen.Bind(MenuModel());
            _view.Hud.SetCollectionMode(_collection);
            _view.Hud.SetSelectedTab(_collection ? 1 : 2);
            if (_collection)
            {
                _view.Arena.Hide();
                _view.Collection.Bind(CollectionModel());
            }
            else
            {
                _view.Collection.Hide();
                _view.Arena.Show();
            }
        }

        public void OpenCollection(bool orders)
        {
            _collection = true;
            _orders = orders;
            _notice = null;
            CloseCard();
            Refresh();
        }

        public void OpenCard(string id)
        {
            if (string.IsNullOrEmpty(id) || !_definitions.TryGet(id, out _))
                return;
            _detailId = id;
            _choosingSlot = false;
            BindDetails();
        }

        private void BindDetails(string error = null)
        {
            var visual = _content.GetVisual(_detailId);
            bool available = !_equipPending && _profiles.GetAvailability(_detailId) == EquipResult.Success && !Selected(_detailId);
            bool canProgression = _definitions.Get(_detailId).Kind == ContentKind.Unit && _progression != null && _progression.IsAvailable;
            _view.Details.Bind(new CardDetailsViewModel(visual.Title, visual.Description + "\n\n" + (error ?? Availability(_detailId)), Selected(_detailId) ? Text.Get(UiTextId.Selected) : Text.Get(UiTextId.Equip), Text.Get(UiTextId.Close), Text.Get(UiTextId.ChooseSlot), visual.Icon, available, _choosingSlot, Loadout(true), canProgression));
        }

        private void ChooseReplacement()
        {
            if (_detailId == null)
                return;
            _choosingSlot = true;
            BindDetails();
        }

        private async void EquipInSlot(int slot)
        {
            if (_equipPending || !_choosingSlot || _detailId == null)
                return;
            _equipPending = true;
            BindDetails();
            EquipResult result;
            try
            {
                result = await _profiles.EquipAsync(_detailId, slot, _stop.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            finally
            {
                _equipPending = false;
            }
            if (_disposed) return;
            if (result == EquipResult.Success)
            {
                _notice = Text.Get(UiTextId.Saved);
                CloseCard();
                Refresh();
            }
            else
                BindDetails(Text.Get(result == EquipResult.SaveFailed ? UiTextId.SaveFailed : result == EquipResult.AlreadyEquipped ? UiTextId.AlreadyEquipped : UiTextId.InvalidSelection));
        }

        private void CloseCard()
        {
            _view.Details.Hide();
            _detailId = null;
            _choosingSlot = false;
        }

        private void Message(UiTextId title, UiTextId body)
        {
            _profileOpen = false;
            _view.Message.Bind(Text.Get(title), Text.Get(body), Text.Get(UiTextId.Close), () => _view.Message.Hide());
        }

        public void StartBattle()
        {
            if (_equipPending || _refreshPending)
            {
                Message(UiTextId.Battle, UiTextId.MatchPending);
                return;
            }
            if (_matchLauncher == null)
            {
                Message(UiTextId.Battle, UiTextId.MatchPending);
                return;
            }

            _profileOpen = false;
            if (!_matchLauncher.TryLaunch(_profiles.Current, out var reason))
                _view.Message.Bind(Text.Get(UiTextId.Battle), reason, Text.Get(UiTextId.Close), () => _view.Message.Hide());
        }

        public void OpenProfile()
        {
            _profileOpen = true;
            _profileNotice = null;
            BindProfile();
            if (_profiles.IsServerBacked) _ = RefreshResultsAsync(1, false);
        }

        private void OpenDetailProgression()
        {
            if (_detailId != null) _progression?.OpenUnit(_detailId);
        }

        private void OpenLatestResult()
        {
            if (_profiles.Current.RecentResults.Count == 0 || _progression == null || _resultShowing || !PresentResult(_profiles.Current.RecentResults[0]))
                OpenProfile();
        }

        private void TryShowLatestResult()
        {
            if (_profiles.Current.RecentResults.Count == 0) return;
            RecentMatchResult latest = _profiles.Current.RecentResults[0];
            if (!latest.MasteryApplied || !HasParticipantRewards(latest) || !ProgressionPresenter.IsValidJournalScope(_progressionJournalScope) || UnityEngine.PlayerPrefs.GetString(ResultSeenKey(latest.ResultId), string.Empty) == latest.ResultId) return;
            PresentResult(latest);
        }

        private bool PresentResult(RecentMatchResult result)
        {
            if (_progression == null || _resultShowing) return false;
            _resultShowing = _progression.ShowResult(result, () => { MarkResultSeen(result.ResultId); _resultShowing = false; }, () => _resultShowing = false);
            return _resultShowing;
        }

        private void MarkResultSeen(string resultId)
        {
            if (!ProgressionPresenter.IsValidJournalScope(_progressionJournalScope)) return;
            UnityEngine.PlayerPrefs.SetString(ResultSeenKey(resultId), resultId);
            UnityEngine.PlayerPrefs.Save();
        }

        private static bool HasParticipantRewards(RecentMatchResult result) => result.ParticipantUnitIds != null && result.ParticipantUnitIds.Count == 4;
        private string ResultSeenKey(string resultId) => "TankDraft.Progression.ResultSeen." + _progressionJournalScope + "." + resultId;

        private string ResultText(RecentMatchResult result, UiTextId format)
        {
            UiTextId state = result.State == RewardDeliveryState.Applied ? UiTextId.ResultRewardApplied :
                result.State == RewardDeliveryState.NeedsReview ? UiTextId.ResultRewardReview :
                result.State == RewardDeliveryState.Skipped ? UiTextId.ResultRewardSkipped : UiTextId.ResultRewardPending;
            return Text.Format(format, Text.Get(result.Won ? UiTextId.ResultWin : UiTextId.ResultLoss),
                result.OwnWins, result.OpponentWins, Text.Format(state, result.Amount));
        }

        private void BindProfile()
        {
            var profile = _profiles.Current;
            var body = new StringBuilder(Text.Format(UiTextId.ProfileBody, profile.PlayerName, profile.CommanderLevel, profile.ArenaLevel));
            if (profile.RecentResults.Count > 0)
            {
                body.Append("\n\n").Append(Text.Get(UiTextId.RecentResultsTitle));
                for (int i = 0; i < Math.Min(3, profile.RecentResults.Count); i++)
                    body.Append('\n').Append(ResultText(profile.RecentResults[i], UiTextId.RecentResultLine));
            }
            if (!string.IsNullOrEmpty(_profileNotice)) body.Append("\n\n").Append(_profileNotice);
            _view.Message.Bind(Text.Get(UiTextId.ProfileTitle), body.ToString(), Text.Get(UiTextId.Close), () =>
            { _profileOpen = false; _view.Message.Hide(); });
        }

        private async Task RefreshResultsAsync(int attempts, bool delayFirst)
        {
            if (_refreshLoopRunning || _disposed || _equipPending) return;
            _refreshLoopRunning = true;
            // The UI only refreshes the provider projection. It never adds the displayed reward to the balance.
            try
            {
                for (int attempt = 0; attempt < attempts; attempt++)
                {
                    if (delayFirst || attempt > 0) await Task.Delay(_content.ResultRefreshMilliseconds, _stop.Token);
                    if (_disposed || _equipPending || (_matchLauncher is IMenuMatchState state && !state.IsMenuIdle)) return;
                    _refreshPending = true;
                    _profileNotice = Text.Get(UiTextId.ProfileRefreshing);
                    if (_profileOpen) BindProfile();
                    await _profiles.RefreshAsync(_stop.Token);
                    _profileNotice = null;
                    _refreshPending = false;
                    if (_profileOpen) BindProfile();
                    bool waiting = false;
                    foreach (var result in _profiles.Current.RecentResults)
                        waiting |= result.State == RewardDeliveryState.Pending || result.State == RewardDeliveryState.Sending;
                    if (!waiting) break;
                }
            }
            catch (OperationCanceledException) when (_disposed) { }
            catch (Exception)
            {
                _profileNotice = Text.Get(UiTextId.ProfileRefreshFailed);
                if (!_disposed && _profileOpen) BindProfile();
            }
            finally { _refreshPending = false; _refreshLoopRunning = false; }
        }
        public void OpenPass() => Message(UiTextId.PassTitle, UiTextId.PassPending);
        public void OpenSettings() => Message(UiTextId.SettingsTitle, UiTextId.SettingsBody);
        public void OpenReward(int index) => Message(UiTextId.RewardTitle, UiTextId.RewardPending);
        public void SelectTab(int index)
        {
            if (index == 1)
                OpenCollection(false);
            else if (index == 2)
            {
                _collection = false;
                CloseCard();
                Refresh();
            }
            else if (index == 0 && _progression != null && _progression.IsAvailable)
                _progression.OpenShop();
            else if (index >= 0 && index < 5)
                Message(index == 0 ? UiTextId.Shop : index == 3 ? UiTextId.Events : UiTextId.Rating, UiTextId.FeaturePending);
        }

        public void Dispose()
        {
            if (!_initialized)
                return;
            _disposed = true;
            _stop.Cancel();
            _profiles.Changed -= OnProfileChanged;
            _view.Screen.Release();
            _view.Collection.Release();
            _view.Details.Release();
            _view.Message.Release();
            _progression?.Dispose();
            _initialized = false;
        }
    }
}

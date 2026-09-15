using System;
using System.Collections.Generic;
using TankDraft.Application;
using TankDraft.BattlePresentation;
using TankDraft.Contracts;
using TankDraft.Contracts.Battle;
using TankDraft.Infrastructure;
using TankDraft.Match.Content;
using TankDraft.Match.Domain;
using TankDraft.Match.Presentation;
using TankDraft.Simulation;
using UnityEngine;
using UnityEngine.SceneManagement;
using VContainer.Unity;

namespace TankDraft.Match.Bootstrap
{
    public sealed class MatchSession : IStartable, ITickable, IDisposable
    {
        private readonly MatchSettingsAsset _settings;
        private readonly BattleWorldView _world;
        private readonly UIMatchScreen _screen;
        private readonly List<BattleEntityState> _states = new List<BattleEntityState>(512);
        private readonly List<BattleEvent> _events = new List<BattleEvent>(256);
        private BattleDefinitions _definitions;
        private BattleRules _battleRules;
        private MatchRules _matchRules;
        private BattleSimulation _simulation;
        private double _accumulator;
        private bool _disposed;
        private long _renderedChoice;
        private uint _botRandom;
        private string _notice;
        private readonly string _presentationId = Guid.NewGuid().ToString("N");
        private int _preparationRound = -1;
        private bool _hudReset = true;
        public MatchService Match { get; private set; }
        public BattleSimulation Simulation => _simulation;
        public ProfileSnapshot Loadout { get; private set; }
        public bool IsReady { get; private set; }
        public Exception Failure { get; private set; }
        public IReadOnlyList<BattleEntityState> States => _states;

        public MatchSession(MatchSettingsAsset settings, BattleWorldView world, UIMatchScreen screen)
        {
            _settings = settings;
            _world = world;
            _screen = screen;
        }

        public void Start()
        {
            try
            {
                _settings.Validate();
                _matchRules = _settings.CreateRules();
                _settings.BattlePresentation.Validate();
                _screen.Validate();
                _definitions = _settings.BattleCatalog.CreateDefinitions();
                _battleRules = _settings.BattleCatalog.CreateRules();
                _settings.Views.Validate(_definitions);
                Loadout = MatchNavigation.Consume();
                if (Loadout == null)
                {
                    var profiles = new ProfileService(_settings.MetaCatalog.CreateDefinitions(), _settings.MetaCatalog.CreateInitialProfile(), new JsonProfileRepository(_settings.ProfileSettings.GetPath()));
                    profiles.Initialize();
                    Loadout = profiles.Current;
                }

                if (!MatchNavigation.Supports(Loadout, _settings))
                    throw new InvalidOperationException(_settings.UnsupportedDeck);
                var units = _settings.CreateUnits();
                var playerDeck = new string[Loadout.UnitIds.Count];
                for (int i = 0; i < playerDeck.Length; i++)
                    playerDeck[i] = Loadout.UnitIds[i];
                // A roster is larger than a four-slot army. The local diagnostic bot
                // mirrors the validated loadout instead of equipping the whole catalog.
                var enemyDeck = (string[])playerDeck.Clone();
                string order = string.Empty;
                foreach (var id in Loadout.OrderIds)
                    if (!string.IsNullOrEmpty(id))
                    {
                        order = id;
                        break;
                    }

                _botRandom = unchecked((uint)_settings.Seed ^ 0x9e3779b9u);
                Match = new MatchService(_matchRules, units, playerDeck, enemyDeck, order, (uint)_settings.Seed);
                _world.Initialize(_settings.Views, _settings.BattlePresentation);
                _world.ConfigureBounds(_battleRules.HalfWidth, _battleRules.HalfHeight);
                _screen.Initialize(Choose, UseOrder, Continue, ReturnToMenu);
                IsReady = true;
                PreparePhase();
                _screen.Show();
            }
            catch (Exception exception)
            {
                Fail(exception);
            }
        }

        // The local opponent commits before the player acts. It only reads its own public offers.
        private void CommitBot()
        {
            while (Match.Phase == MatchPhase.Draft && (!Match.IsComeback || Match.BonusSide == 1) && !Match.HasCommitted(1))
            {
                var offers = Match.GetOffers(1);
                _botRandom ^= _botRandom << 13;
                _botRandom ^= _botRandom >> 17;
                _botRandom ^= _botRandom << 5;
                int index = (int)(_botRandom % (uint)offers.Count);
                if (!Match.TryChoose(1, Match.ChoiceToken, index, out var reason))
                    throw new InvalidOperationException(reason);
            }
        }

        private void PreparePhase()
        {
            CommitBot();
            _simulation?.Dispose();
            _simulation = null;
            _states.Clear();
            _events.Clear();
            _accumulator = 0;
            if (Match.Phase == MatchPhase.Draft || Match.Phase == MatchPhase.Battle)
            {
                _simulation = new BattleSimulation(_definitions, _battleRules, Match.CreateScenario(), preparation: Match.Phase == MatchPhase.Draft);
                _simulation.Capture(_states);

            }

            _world.SyncPreparation(_states, Time.unscaledTime, _preparationRound != Match.RoundNumber, Match.Phase == MatchPhase.Battle);
            _preparationRound = Match.RoundNumber;
            RenderHud();
        }

        public void Choose(int index)
        {
            if (!CanCommand())
                return;
            try
            {
                if (Match.TryChoose(0, _renderedChoice, index, out var reason))
                {
                    _notice = null;
                    PreparePhase();
                }
                else
                {
                    _notice = reason;
                    RenderHud();
                }
            }
            catch (Exception exception)
            {
                Fail(exception);
            }
        }

        public void UseOrder()
        {
            if (!CanCommand())
                return;
            try
            {
                if (Match.TryUseOrder(0, _renderedChoice, out var reason))
                {
                    _notice = null;
                    PreparePhase();
                }
                else
                {
                    _notice = reason;
                    RenderHud();
                }
            }
            catch (Exception exception)
            {
                Fail(exception);
            }
        }

        public void Continue()
        {
            if (!CanCommand() || Match.Phase != MatchPhase.RoundResult)
                return;
            try
            {
                Match.Continue();
                _notice = null;
                PreparePhase();
            }
            catch (Exception exception)
            {
                Fail(exception);
            }
        }

        public void ReturnToMenu()
        {
            if (_disposed)
                return;
            SceneManager.LoadScene(_settings.MenuSceneName);
        }

        private bool CanCommand() => IsReady && !_disposed && Failure == null;
        public void Tick()
        {
            if (!CanCommand())
                return;
            try
            {
                if (Match.Phase == MatchPhase.Battle)
                {
                    _accumulator += Time.unscaledDeltaTime;
                    int steps = 0;
                    while (_accumulator >= _battleRules.TickSeconds && steps++ < _settings.BattlePresentation.MaxTicksPerFrame && Match.Phase == MatchPhase.Battle)
                    {
                        _accumulator -= _battleRules.TickSeconds;
                        Advance();
                    }
                }

                float alpha = Match.Phase == MatchPhase.Battle ? Mathf.Clamp01((float)(_accumulator / _battleRules.TickSeconds)) : 1;
                _world.Sync(_states, alpha, Time.unscaledTime, Match.Phase == MatchPhase.Battle);
                _world.TickEffects(Time.unscaledTime);
            }
            catch (Exception exception)
            {
                Fail(exception);
            }
        }

        private void Advance()
        {
            Advance(Time.unscaledTime);
        }

        private void Advance(float presentationTime)
        {
            if (_simulation == null)
                throw new InvalidOperationException("Combat started without both armies.");
            _simulation.Step();
            _simulation.Capture(_states);
            _simulation.DrainEvents(_events);
            _world.Present(_events, presentationTime);
            if (_simulation.Outcome != BattleOutcome.Running)
            {
                Match.ResolveBattle(_simulation.Outcome);
                RenderHud();
            }
            else if (_simulation.Tick % 10 == 0)
                RenderHud();
        }

#if UNITY_EDITOR
        public void AdvanceForValidation(int ticks)
        {
            if (ticks < 0 || ticks > 30000) throw new ArgumentOutOfRangeException(nameof(ticks));
            if (!CanCommand()) throw new InvalidOperationException("Session is not ready.");
            // Accelerated validation must advance visual lifetimes too; wall time is frozen
            // during this call, otherwise an entire round accumulates in the effect pool.
            float now = Time.unscaledTime;
            for (int i = 0; i < ticks && Match.Phase == MatchPhase.Battle; i++)
            {
                now += _battleRules.TickSeconds;
                _world.TickEffects(now);
                Advance(now);
                _world.Sync(_states, 1, now);
            }
            // Return presentation to the real clock after the diagnostic time jump.
            _world.Clear();
            _world.Sync(_states, 1, Time.unscaledTime);
        }
#endif
        private int Count(int side)
        {
            int count = 0;
            foreach (var entry in Match.GetArmy(side))
                count += entry.Count;
            return count;
        }

        private void RenderHud()
        {
            _renderedChoice = Match.ChoiceToken;
            bool draft = Match.Phase == MatchPhase.Draft;
            bool playerChoosing = draft && (!Match.IsComeback || Match.BonusSide == 0) && !Match.HasCommitted(0);
            var cards = new List<MatchCardModel>();
            if (playerChoosing)
                foreach (var offer in Match.GetOffers(0))
                {
                    var visual = _settings.MetaCatalog.GetVisual(offer.UnitId);
                    int count = 0, level = 0;
                    foreach (var unit in Match.GetArmy(0))
                        if (unit.UnitId == offer.UnitId)
                        {
                            count = unit.Count;
                            level = unit.UpgradeLevel;
                        }

                    string action = offer.Kind == DraftActionKind.Add ? string.Format(_settings.AddFormat, offer.Amount) : offer.Kind == DraftActionKind.Double ? string.Format(_settings.DoubleFormat, count * 2) : string.Format(_settings.UpgradeFormat, level + 1);
                    cards.Add(new MatchCardModel { Title = visual.Title, ActionLabel = action, Description = visual.RoleLabel, Icon = visual.Icon, Enabled = true });
                }

            string status = _settings.Battle;
            switch (Match.Phase)
            {
                case MatchPhase.Draft:
                    status = Match.IsComeback ? _settings.Comeback : string.Format(_settings.DraftFormat, Match.ChoiceNumber, _matchRules.NormalChoices);
                    break;
                case MatchPhase.RoundResult:
                    status = Match.LastWinner == 0 ? _settings.RoundWin : _settings.RoundLoss;
                    break;
                case MatchPhase.MatchResult:
                    status = Match.LastWinner == 0 ? _settings.MatchWin : _settings.MatchLoss;
                    break;
                case MatchPhase.ReviewRequired:
                    status = _settings.ReviewRequired;
                    break;
            }

            _screen.Render(new MatchViewModel { MatchPresentationId = _presentationId, PhaseName = Match.Phase.ToString(), RoundNumber = Match.RoundNumber, OwnWins = Match.Wins(0), OpponentWins = Match.Wins(1), WinsRequired = _matchRules.WinsRequired, BattleElapsedSeconds = (_simulation?.Tick ?? 0) * (double)_battleRules.TickSeconds, PresentationReset = _hudReset, WaitingForOpponent = draft && !playerChoosing, WaitingLabel = _settings.Waiting, OfferPresentationKey = Match.ChoiceToken.ToString(System.Globalization.CultureInfo.InvariantCulture), Title = string.Format(_settings.RoundFormat, Match.RoundNumber) + "  ·  " + _settings.LocalBot, Score = string.Format(_settings.ScoreFormat, Match.Wins(0), Match.Wins(1), _matchRules.WinsRequired), Status = status, Army0 = draft ? (Count(0) == 0 ? _settings.NoArmy : string.Format(_settings.ArmyFormat, Count(0))) : string.Format(_settings.ArmyFormat, _simulation?.AliveSide0 ?? 0), Army1 = draft ? (Count(1) == 0 ? _settings.NoArmy : string.Format(_settings.ArmyFormat, Count(1))) : string.Format(_settings.ArmyFormat, _simulation?.AliveSide1 ?? 0), Hint = _notice ?? (playerChoosing ? _settings.OrderHint : draft ? _settings.Waiting : string.Empty), OrderLabel = Match.OrderCharges > 0 ? string.Format(_settings.OrderFormat, Match.OrderCharges, _matchRules.OrderArmorPercent) : _settings.NoOrder, NextLabel = _settings.Continue, MenuLabel = _settings.Menu, DraftVisible = playerChoosing, OrderVisible = playerChoosing, OrderEnabled = Match.CanUseOrder(0), NextVisible = Match.Phase == MatchPhase.RoundResult, Cards = cards.ToArray() });
            _hudReset = false;
        }

        private void Fail(Exception exception)
        {
            Failure = exception;
            IsReady = false;
            Debug.LogException(exception);
            if (_screen)
            {
                _screen.Initialize(null, null, null, ReturnToMenu);
                _screen.Render(new MatchViewModel { Title = _settings.LocalBot, Status = _settings.ReviewRequired, Hint = exception.Message, MenuLabel = _settings.Menu, Cards = Array.Empty<MatchCardModel>() });
                _screen.Show();
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            IsReady = false;
            if (_screen)
                _screen.Unbind();
            _simulation?.Dispose();
            _simulation = null;
            if (_world)
                _world.Dispose();
            _states.Clear();
            _events.Clear();
        }
    }
}

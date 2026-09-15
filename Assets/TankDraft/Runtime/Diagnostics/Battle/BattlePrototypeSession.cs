using System;
using System.Collections.Generic;
using TankDraft.BattleContent;
using TankDraft.BattlePresentation;
using TankDraft.Contracts.Battle;
using TankDraft.Simulation;
using UnityEngine;
using VContainer.Unity;

namespace TankDraft.BattleBootstrap
{
    public sealed class BattlePrototypeSession : IStartable, ITickable, IDisposable
    {
        private readonly BattleScenarioCatalogAsset _catalog;
        private readonly BattleViewCatalogAsset _views;
        private readonly BattlePresentationSettings _presentation;
        private readonly BattleWorldView _world;
        private readonly UIBattlePrototypeScreen _screen;
        private readonly List<BattleEntityState> _states = new List<BattleEntityState>(512);
        private readonly List<BattleEvent> _events = new List<BattleEvent>(256);
        private BattleDefinitions _definitions;
        private BattleRules _rules;
        private BattleScenarioDefinition _scenario;
        private BattleSimulation _simulation;
        private int _scenarioIndex;
        private long _nextCommand, _lastHudTick = -1;
        private double _accumulator;
        private bool _started, _paused, _disposed, _hudDirty = true;
        public BattleSimulation Simulation => _simulation;
        public bool IsReady { get; private set; }
        public bool IsPaused => _paused;
        public Exception Failure { get; private set; }
        public IReadOnlyList<BattleEntityState> States => _states;

        public BattlePrototypeSession(BattleScenarioCatalogAsset catalog, BattleViewCatalogAsset views, BattlePresentationSettings presentation, BattleWorldView world, UIBattlePrototypeScreen screen)
        {
            _catalog = catalog;
            _views = views;
            _presentation = presentation;
            _world = world;
            _screen = screen;
        }

        public void Start()
        {
            try
            {
                _catalog.Validate();
                _presentation.Validate();
                _screen.Validate();
                _definitions = _catalog.CreateDefinitions();
                _rules = _catalog.CreateRules();
                _views.Validate(_definitions);
                _world.Initialize(_views, _presentation);
                _world.ConfigureBounds(_rules.HalfWidth, _rules.HalfHeight);
                _screen.Initialize(StartBattle, TogglePause, StepOnce, Reset, NextScenario, QueueDebugZone, _presentation);
                Reset();
                if (Failure != null)
                    return;
                _screen.Show();
                IsReady = true;
            }
            catch (Exception e)
            {
                Fail(e);
            }
        }

        public void StartBattle()
        {
            if (!CanRun())
                return;
            _started = true;
            _paused = false;
            _hudDirty = true;
        }

        public void TogglePause()
        {
            if (!_started || !CanRun())
                return;
            _paused = !_paused;
            _hudDirty = true;
        }

        public void StepOnce()
        {
            if (!CanRun())
                return;
            _started = true;
            _paused = true;
            _accumulator = 0;
            try
            {
                Advance();
                Render(1);
                UpdateHud();
            }
            catch (Exception e)
            {
                Fail(e);
            }
        }

        public void Reset()
        {
            if (_disposed)
                return;
            try
            {
                _simulation?.Dispose();
                _world.Clear();
                _events.Clear();
                _states.Clear();
                _scenario = _catalog.CreateScenario(_scenarioIndex);
                _simulation = new BattleSimulation(_definitions, _rules, _scenario);
                _started = false;
                _paused = false;
                _accumulator = 0;
                _nextCommand = 0;
                _lastHudTick = -1;
                _hudDirty = true;
                _simulation.Capture(_states);
                Render(1);
                UpdateHud();
            }
            catch (Exception e)
            {
                Fail(e);
            }
        }

        public void NextScenario()
        {
            if (_disposed || Failure != null)
                return;
            _scenarioIndex = (_scenarioIndex + 1) % _catalog.ScenarioCount;
            Reset();
        }

        public void QueueDebugZone()
        {
            if (!CanRun() || _definitions.Zones.Count == 0)
                return;
            var point = _presentation.DebugZonePosition;
            var command = new BattleZoneCommand(0, ++_nextCommand, _simulation.Tick + 1, _definitions.Zones[0].Id, new BattleVec(point.x, point.y));
            if (!_simulation.TryQueueZone(command, out var reason))
            {
                Debug.LogWarning("Battle prototype debug command rejected: " + reason);
                return;
            }

            _hudDirty = true;
        }

        private bool CanRun() => !_disposed && Failure == null && _simulation != null && _simulation.Outcome == BattleOutcome.Running;
        public void Tick()
        {
            if (!IsReady || _disposed || Failure != null)
                return;
            try
            {
                if (_started && !_paused && _simulation.Outcome == BattleOutcome.Running)
                {
                    _accumulator += Time.unscaledDeltaTime;
                    int steps = 0;
                    while (_accumulator >= _rules.TickSeconds && steps++ < _presentation.MaxTicksPerFrame && _simulation.Outcome == BattleOutcome.Running)
                    {
                        _accumulator -= _rules.TickSeconds;
                        Advance();
                    }
                }

                float alpha = (!_started || _paused || _simulation.Outcome != BattleOutcome.Running) ? 1 : Mathf.Clamp01((float)(_accumulator / _rules.TickSeconds));
                Render(alpha);
                UpdateHud();
            }
            catch (Exception e)
            {
                Fail(e);
            }
        }

        private void Advance()
        {
            _simulation.Step();
            _simulation.Capture(_states);
            _simulation.DrainEvents(_events);
            _world.Present(_events, Time.unscaledTime);
        }

        private void Render(float alpha)
        {
            _world.Sync(_states, alpha, Time.unscaledTime);
            _world.TickEffects(Time.unscaledTime);
        }

        private void UpdateHud()
        {
            if (_simulation == null || (!_hudDirty && _lastHudTick == _simulation.Tick))
                return;
            _lastHudTick = _simulation.Tick;
            _hudDirty = false;
            string status = _presentation.Ready;
            switch (_simulation.Outcome)
            {
                case BattleOutcome.Side0Won:
                    status = _presentation.Side0Won;
                    break;
                case BattleOutcome.Side1Won:
                    status = _presentation.Side1Won;
                    break;
                case BattleOutcome.ReviewRequired:
                    status = _presentation.ReviewRequired;
                    break;
                default:
                    if (_started)
                        status = _paused ? _presentation.Paused : _presentation.Running;
                    break;
            }

            _screen.Render(status, string.Format(_presentation.ArmyFormat, _simulation.AliveSide0), string.Format(_presentation.ArmyFormat, _simulation.AliveSide1), string.Format(_presentation.StatsFormat, _world.ActiveUnitViews, _world.ActiveProjectileViews, _world.ActiveZoneViews), string.Format(_presentation.TimeFormat, _simulation.Tick * _rules.TickSeconds, _simulation.Tick), _scenario.Title, CanRun() && (!_started || _paused), CanRun() && _started, CanRun() && (!_started || _paused), CanRun() && _rules.AllowDebugCommands);
        }

        private void Fail(Exception e)
        {
            Failure = e;
            IsReady = false;
            _paused = true;
            Debug.LogException(e);
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            IsReady = false;
            _screen.Unbind();
            _simulation?.Dispose();
            _simulation = null;
            _world.Dispose();
            _events.Clear();
            _states.Clear();
        }
    }
}
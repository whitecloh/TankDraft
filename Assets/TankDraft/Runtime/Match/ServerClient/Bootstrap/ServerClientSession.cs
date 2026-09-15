using System;
using System.Collections.Generic;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TankDraft.BattlePresentation;
using TankDraft.Contracts.Battle;
using TankDraft.Match.Content;
using TankDraft.Match.Domain;
using TankDraft.Match.Presentation;
using UnityEngine;
using UnityEngine.SceneManagement;
using VContainer.Unity;

namespace TankDraft.Match.ServerClient
{
    public sealed class ServerClientSession : IStartable, ITickable, IDisposable
    {
        readonly ServerClientSettings _config;
        readonly MatchSettingsAsset _settings;
        readonly BattleWorldView _world;
        readonly UIMatchScreen _screen;
        readonly Queue<ReceivedServerFrame> _buffer = new Queue<ReceivedServerFrame>();
        readonly List<BattleEntityState> _display = new List<BattleEntityState>(512);
        readonly List<BattleEvent> _effects = new List<BattleEvent>(256);
        readonly Dictionary<int, BattleEntityState> _future = new Dictionary<int, BattleEntityState>();
        ServerClientTransport _transport;
        ReceivedServerFrame _presented, _latest;
        IMatchCredentials _credentials;
        readonly IMatchCredentialsFactory _credentialsFactory;
        MatchRules _rules;
        StreamWriter _diagnostics;
        bool _matchCompleted, _paused, _disposed, _auto, _interrupted, _wasConnected, _reset = true;
        string _failure;
        long _events, _diagnosticRevision = -1, _autoToken = -1;
        double _autoAt, _hudAt, _flushAt;
        double _quitAt;
        int _capturedRound;
        int _previousFrameRate, _previousVsync;
        bool _configuredFrameRate;
        bool _returningToMenu;
        bool _hudPresentationReset, _preparationPending, _preparationReset, _animateSpawn;
        float _battleTickSeconds;
        BattlePresentationClock _presentationClock;
        double _displayTime, _holdStarted = -1, _maxHoldSeconds, _maxFrameSeconds;
        long _interpolatedFrames, _heldFrames;
        public ServerClientSession(ServerClientSettings config, BattleWorldView world, UIMatchScreen screen, IMatchCredentialsFactory credentialsFactory)
        { _config = config; _settings = config.MatchSettings; _world = world; _screen = screen; _credentialsFactory = credentialsFactory; }
        public void Start()
        {
            try
            {
                _config.Validate(); _screen.Validate();
                _presentationClock = new BattlePresentationClock(_config.PresentationDelaySeconds, _config.MaxPresentationDelaySeconds);
                _previousFrameRate = Application.targetFrameRate; _previousVsync = QualitySettings.vSyncCount; _configuredFrameRate = true;
                QualitySettings.vSyncCount = 0; Application.targetFrameRate = _config.TargetFrameRate;
                _rules = _settings.CreateRules();
                _world.Initialize(_settings.Views, _settings.BattlePresentation);
                var rules = _settings.BattleCatalog.CreateRules(); _world.ConfigureBounds(rules.HalfWidth, rules.HalfHeight);
                _battleTickSeconds = rules.TickSeconds;
                _screen.Initialize(i => Submit("Choose", i), () => Submit("Order", 0), () => { }, ReturnToMenu);
                _screen.Show();
                _credentials = _credentialsFactory.Create();
                _auto = Environment.GetEnvironmentVariable("TD_LOCAL_AUTO") == "1";
                _diagnostics = new StreamWriter(Path.Combine(_credentials.RunDirectory, "presentation-" + _credentials.Side + ".jsonl"), true, new UTF8Encoding(false));
                _transport = new ServerClientTransport(_credentials, _config.Protocol, _config.ContentVersion, _config.PollMilliseconds,
                    _config.RetryMilliseconds, _config.TimeoutMilliseconds, _config.MaxResponseBytes, _config.MaxBufferedFrames);
                Application.runInBackground = true;
                _transport.SetSuspended(_paused); _transport.Start(); RenderHud();
            }
            catch (Exception error) { Fail(error.Message); }
        }
        public void SetApplicationPaused(bool paused)
        {
            if (_disposed) return;
            _paused = paused;
            _transport?.SetSuspended(paused);
            _buffer.Clear(); _reset = true; _hudAt = 0;
            if (_credentials != null)
                File.AppendAllText(Path.Combine(_credentials.RunDirectory, "lifecycle-" + _credentials.Side + ".jsonl"),
                    new JObject { ["Paused"] = paused, ["Utc"] = DateTime.UtcNow.ToString("O"), ["Revision"] = _latest?.Value.Revision ?? -1 }.ToString(Formatting.None) + Environment.NewLine);
        }
        public void Tick()
        {
            if (_disposed || _paused || _failure != null || _transport == null) return;
            try
            {
                var now = ReceivedServerFrame.Clock;
                _maxFrameSeconds = Math.Max(_maxFrameSeconds, Time.unscaledDeltaTime);
                // QA evidence need not force a filesystem flush on every displayed snapshot.
                if (now >= _flushAt) { _flushAt = now + 1; _diagnostics?.Flush(); }
                if (_matchCompleted)
                {
                    if (_auto) AutoTick(now);
                    _world.TickEffects(Time.unscaledTime);
                    if (now >= _hudAt) { _hudAt = now + .1; RenderHud(); }
                    return;
                }
                if (_transport.Failure != null) { Fail(_transport.Failure); return; }
                if (_wasConnected != _transport.Connected)
                {
                    _wasConnected = _transport.Connected;
                    if (!_wasConnected) { _buffer.Clear(); _reset = true; }
                    _hudAt = 0;
                }
                while (_transport.TryTake(out var frame))
                {
                    if (_latest != null && frame.Value.Revision < _latest.Value.Revision) continue;
                    bool clockReset = frame.Reset || _latest == null || _latest.Value.Round != frame.Value.Round || _latest.Value.Phase != frame.Value.Phase;
                    if (frame.Value.Phase == "Battle") _presentationClock.Observe(frame.ReceivedAt, clockReset);
                    _latest = frame;
                    if (frame.Reset || _buffer.Count >= _config.MaxBufferedFrames) { _buffer.Clear(); _reset = true; }
                    _buffer.Enqueue(frame);
                }
                if (_transport.Connected)
                {
                    _displayTime = _latest?.Value.Phase == "Battle" && _presented?.Value.Phase == "Battle" ? _presentationClock.Advance(now) : now - _config.PresentationDelaySeconds;
                    while (_buffer.Count > 0 && (_reset || _credentials is IMatchNativePresentation { NativePresentation: true } || _buffer.Peek().ReceivedAt <= _displayTime))
                        { Present(_buffer.Dequeue()); if (_matchCompleted) break; }
                    RenderWorld(now);
                    if (_auto) AutoTick(now);
                }
                _world.TickEffects(Time.unscaledTime);
                if (now >= _hudAt) { _hudAt = now + .1; RenderHud(); }
            }
            catch (Exception error) { Fail(error.Message); }
        }
        bool CanChoose(ServerFrame f) => f != null && f.Phase == "Draft" && !f.Committed && !f.CatchingUp && (!f.IsComeback || f.BonusSide == _transport.Side);
        void Submit(string kind, int index)
        {
            var f = _latest?.Value;
            if (!_transport.Connected || _transport.Pending || !CanChoose(f) || _presented == null || _presented.Value.ChoiceToken != f.ChoiceToken) return;
            if (kind == "Order" && !f.CanUseOrder || kind == "Choose" && (index < 0 || index >= f.Offers.Count)) return;
            try { _transport.Submit(f, kind, index); RenderHud(); }
            catch (Exception error) { Fail(error.Message); }
        }
        void Present(ReceivedServerFrame received)
        {
            var f = received.Value;
            var previous = _presented?.Value;
            bool recovery = _reset || received.Reset || previous == null || previous.MatchId != f.MatchId;
            bool reset = _reset || received.Reset || _presented == null || _presented.Value.Round != f.Round || _presented.Value.Phase != f.Phase;
            _hudPresentationReset |= recovery;
            bool draftToBattle = !recovery && previous.Round == f.Round && previous.Phase == "Draft" && f.Phase == "Battle";
            _preparationPending = f.Phase == "Draft" || draftToBattle;
            _preparationReset = recovery || previous.Round != f.Round;
            _animateSpawn = !reset && f.Phase == "Battle";
            if (reset) { if (!draftToBattle) _world.Clear(); _hudAt = 0; }
            _presented = received;
            _effects.Clear();
            if (_reset || received.Reset) _events = f.EventSequence;
            else foreach (var e in f.Events)
                if (e.Sequence > _events)
                {
                    _effects.Add(new BattleEvent(e.Sequence, e.Tick, e.Kind, e.EntityId, Side(e.Side), Map(e.Position), e.Amount, e.Radius, e.TimeWithinTick));
                    _events = e.Sequence;
                }
            _events = Math.Max(_events, f.EventSequence); _reset = false;
            RenderWorld(received.ReceivedAt, false);
            _world.Present(_effects, Time.unscaledTime);
            Record(f, received.Reset);
            if (f.Phase == "MatchResult") { _matchCompleted = true; _transport.Dispose(); }
            if (_capturedRound < f.Round && f.Phase == "Battle" && f.Entities.Count > 0)
            {
                _capturedRound = f.Round;
                CaptureScreenshot(Path.Combine(_credentials.RunDirectory, "battle-" + _transport.Side + "-" + f.Round + ".png"));
            }
        }
        void RenderWorld(double now, bool measure = true)
        {
            if (_presented == null) return;
            var current = _presented.Value;
            if (_credentials is IMatchNativePresentation { NativePresentation: true } native)
            {
                _display.Clear(); bool blended = false;
                foreach (var e in current.Entities)
                {
                    var position = e.Position; var facing = e.Facing;
                    if (current.Phase == "Battle" && native.TryGetPose(current.MatchId, current.Round, e.Id, out var p, out var f, out var smooth)) { position = p; facing = f; blended |= smooth; }
                    position = Map(position);
                    _display.Add(new BattleEntityState(e.Id, Side(e.Side), e.Kind, e.DefinitionId, position, position, Map(facing), e.Hp, e.MaxHp, e.Radius, e.Progress, e.TargetId, e.ShieldHp, e.ShieldMaxHp, e.FirstHitBlocks, e.Ammo, e.MagazineSize, e.ReloadRemaining, e.ReloadDuration, e.TransformationStage));
                }
                if (measure && current.Phase == "Battle")
                {
                    if (blended) { _interpolatedFrames++; _holdStarted = -1; }
                    else { _heldFrames++; if (_holdStarted < 0) _holdStarted = now; _maxHoldSeconds = Math.Max(_maxHoldSeconds, now - _holdStarted); }
                }
                else if (measure) _holdStarted = -1;
                SyncDisplay(); return;
            }
            var next = _buffer.Count == 0 ? null : _buffer.Peek();
            bool interpolate = next != null && current.Round == next.Value.Round && current.Phase == "Battle" && next.Value.Phase == "Battle" && !next.Reset;
            float alpha = interpolate ? Mathf.Clamp01((float)((_displayTime - _presented.ReceivedAt) / Math.Max(.001, next.ReceivedAt - _presented.ReceivedAt))) : 0;
            if (measure && current.Phase == "Battle")
            {
                if (interpolate) { _interpolatedFrames++; _holdStarted = -1; }
                else { _heldFrames++; if (_holdStarted < 0) _holdStarted = now; _maxHoldSeconds = Math.Max(_maxHoldSeconds, now - _holdStarted); }
            }
            else if (measure) _holdStarted = -1;
            _future.Clear();
            if (interpolate) foreach (var entity in next.Value.Entities) _future[entity.Id] = entity;
            _display.Clear();
            foreach (var e in current.Entities)
            {
                var position = e.Position;
                if (_future.TryGetValue(e.Id, out var future)) position += (future.Position - position) * alpha;
                position = Map(position);
                _display.Add(new BattleEntityState(e.Id, Side(e.Side), e.Kind, e.DefinitionId, position, position, Map(e.Facing), e.Hp, e.MaxHp, e.Radius, e.Progress, e.TargetId, e.ShieldHp, e.ShieldMaxHp, e.FirstHitBlocks, e.Ammo, e.MagazineSize, e.ReloadRemaining, e.ReloadDuration, e.TransformationStage));
            }
            SyncDisplay();
        }
        void SyncDisplay()
        {
            if (_preparationPending)
            {
                _world.SyncPreparation(_display, Time.unscaledTime, _preparationReset, _presented.Value.Phase == "Battle");
                _preparationPending = false;
            }
            else _world.Sync(_display, 1, Time.unscaledTime, _animateSpawn);
        }
        BattleVec Map(BattleVec value) => _transport.Side == 0 ? value : value * -1;
        int Side(int side) => _transport.Side == 0 ? side : 1 - side;
        void RenderHud()
        {
            var f = _presented?.Value;
            bool choosing = _transport != null && CanChoose(f);
            bool enabled = choosing && _transport.Connected && !_transport.Pending && _latest != null && _latest.Value.ChoiceToken == f.ChoiceToken;
            var cards = new List<MatchCardModel>(3);
            if (choosing) foreach (var offer in f.Offers)
            {
                var visual = _settings.MetaCatalog.GetVisual(offer.UnitId);
                int level = 0; foreach (var unit in f.Army) if (unit.UnitId == offer.UnitId) level = unit.UpgradeLevel;
                var label = offer.Kind == DraftActionKind.Add ? string.Format(_settings.AddFormat, offer.Amount) : offer.Kind == DraftActionKind.Double ? string.Format(_settings.DoubleFormat, offer.Amount * 2) : string.Format(_settings.UpgradeFormat, level + 1);
                cards.Add(new MatchCardModel { Title = visual.Title, ActionLabel = label, Description = visual.RoleLabel, Icon = visual.Icon, Enabled = enabled });
            }
            string status = _config.Waiting;
            if (f != null) switch (f.Phase)
            {
                case "Draft": status = f.IsComeback ? _settings.Comeback : string.Format(_settings.DraftFormat, f.ChoiceNumber, _rules.NormalChoices); break;
                case "Battle": status = _settings.Battle; break;
                case "RoundResult": status = f.LastWinner == _transport.Side ? _settings.RoundWin : _settings.RoundLoss; break;
                case "MatchResult": status = f.LastWinner == _transport.Side ? _settings.MatchWin : _settings.MatchLoss; break;
            }
            int own = 0, other = 0;
            if (f != null) foreach (var e in f.Entities) if (e.Kind == BattleEntityKind.Unit && e.Hp > 0) { if (e.Side == _transport.Side) own++; else other++; }
            if (f != null && f.Phase == "Draft") { own = 0; foreach (var unit in f.Army) own += unit.Count; }
            string hint = _transport == null || _transport.Connections == 0 ? _config.Connecting : !_transport.Connected ? _config.Recovering : _config.Waiting;
            if (f != null && (f.Phase == "MatchResult" || f.Phase != "Draft" && _transport.Connected)) hint = string.Empty;
            if (f != null && f.Phase == "Draft" && _transport.Connected)
                hint = string.Format(_config.TimerFormat, Math.Ceiling(Math.Max(0, f.RemainingSeconds - (ReceivedServerFrame.Clock - _presented.ReceivedAt))));
            _screen.Render(new MatchViewModel { Title = _config.Title + (_credentialsFactory is IMatchOpponentInfo opponent && opponent.IsBot ? " · " + _config.BotOpponentLabel : "") + (f == null ? "" : " · " + string.Format(_settings.RoundFormat, f.Round)),
                MatchPresentationId = f?.MatchId, PhaseName = f?.Phase, RoundNumber = f?.Round ?? 0,
                OwnWins = f == null ? 0 : _transport.Side == 0 ? f.Wins0 : f.Wins1,
                OpponentWins = f == null ? 0 : _transport.Side == 0 ? f.Wins1 : f.Wins0,
                WinsRequired = _rules?.WinsRequired ?? 0, BattleElapsedSeconds = (f?.SimulationTick ?? 0) * (double)_battleTickSeconds,
                PresentationReset = _hudPresentationReset, WaitingForOpponent = f != null && f.Phase == "Draft" && !choosing && !f.CatchingUp,
                WaitingLabel = _settings.Waiting,
                Score = f == null ? "" : string.Format(_settings.ScoreFormat, _transport.Side == 0 ? f.Wins0 : f.Wins1, _transport.Side == 0 ? f.Wins1 : f.Wins0, _rules.WinsRequired),
                OfferPresentationKey = f == null ? null : f.MatchId + ":" + f.ChoiceToken.ToString(CultureInfo.InvariantCulture),
                Status = _failure == null ? status : _config.Failed, Hint = _failure == null ? hint : _config.Failed,
                Army0 = own == 0 ? _settings.NoArmy : string.Format(_settings.ArmyFormat, own), Army1 = other == 0 ? _settings.NoArmy : string.Format(_settings.ArmyFormat, other),
                OrderLabel = f != null && f.OrderCharges > 0 ? string.Format(_settings.OrderFormat, f.OrderCharges, _rules.OrderArmorPercent) : _settings.NoOrder,
                NextLabel = _settings.Continue, MenuLabel = _settings.Menu, DraftVisible = choosing, OrderVisible = choosing,
                OrderEnabled = enabled && f.CanUseOrder, NextVisible = false, MenuVisible = (_credentialsFactory is not IMatchSessionNavigation && RemoteSessionContext.Current == null && Environment.GetEnvironmentVariable("TD_QUEUE_MODE") != "1") || f != null && f.Phase == "MatchResult", Cards = cards.ToArray() });
            _hudPresentationReset = false;
        }
        void AutoTick(double now)
        {
            if (_returningToMenu) return;
            var f = _latest?.Value;
            if (f == null) return;
            if (f.Phase == "MatchResult" && _presented?.Value.Phase == "MatchResult")
            {
                if (_quitAt == 0) { _quitAt = now + 3; RenderHud(); CaptureScreenshot(Path.Combine(_credentials.RunDirectory, "result-" + _transport.Side + ".png")); }
                if (now >= _quitAt)
                {
                    if (RemoteSessionContext.Current != null && int.TryParse(Environment.GetEnvironmentVariable("TD_REMOTE_AUTO_REMAINING"), out var remoteRemaining) && remoteRemaining > 1 && remoteRemaining <= 3)
                    {
                        Environment.SetEnvironmentVariable("TD_REMOTE_AUTO_REMAINING", (remoteRemaining - 1).ToString(CultureInfo.InvariantCulture));
                        Environment.SetEnvironmentVariable("TD_REMOTE_AUTO_QUEUE", "1");
                        ReturnToMenu();
                    }
                    else if (Environment.GetEnvironmentVariable("TD_QUEUE_MODE") == "1" && int.TryParse(Environment.GetEnvironmentVariable("TD_QUEUE_AUTO_REMAINING"), out var remaining) && remaining > 1)
                    { Environment.SetEnvironmentVariable("TD_QUEUE_AUTO_REMAINING", (remaining - 1).ToString()); ReturnToMenu(); }
                    else Application.Quit();
                }
            }
            if (RemoteSessionContext.Current == null && Environment.GetEnvironmentVariable("TD_QUEUE_MODE") != "1" && Environment.GetEnvironmentVariable("TD_LOCAL_ANDROID") != "1" && !_interrupted && _transport.Side == 1 && f.Phase == "Battle" && f.SimulationTick > 10)
            { _interrupted = true; File.WriteAllText(Path.Combine(_credentials.RunDirectory,"disconnect-request-1"),"40 seconds; exceeds access TTL"); _transport.DisconnectForValidation(40); return; }
            if (RemoteSessionContext.Current != null && Environment.GetEnvironmentVariable("TD_REMOTE_QA_DISCONNECT_LAST") == "1" &&
                Environment.GetEnvironmentVariable("TD_REMOTE_AUTO_REMAINING") == "1" && !_interrupted && f.Phase == "Battle" && f.SimulationTick > 10)
            {
                _interrupted = true;
                File.AppendAllText(Path.Combine(_credentials.RunDirectory, "remote-qa-faults.jsonl"),
                    new JObject { ["MatchId"] = f.MatchId, ["Kind"] = "SocketDisconnect", ["Seconds"] = 40,
                        ["Round"] = f.Round, ["Tick"] = f.SimulationTick, ["ClientTime"] = now }.ToString(Formatting.None) + Environment.NewLine);
                _transport.DisconnectForValidation(40);
                return;
            }
            if (CanChoose(f) && !_transport.Pending && now >= _autoAt && _autoToken != f.ChoiceToken)
            { _autoAt = now + .4; Submit("Choose", (_transport.Side + f.ChoiceNumber) % 3); if (_transport.Pending) _autoToken = f.ChoiceToken; }
        }
        void CaptureScreenshot(string path)
        {
            // Encoding a full Android framebuffer stalls the main thread. Keep captures explicit
            // in remote performance runs; existing local visual-QA runs retain their screenshots.
            if (RemoteSessionContext.Current != null && Environment.GetEnvironmentVariable("TD_REMOTE_CAPTURE_SCREENSHOTS") != "1") return;
            if (_credentialsFactory is IMatchSessionNavigation && Environment.GetEnvironmentVariable("TD_FUSION_CAPTURE_SCREENSHOTS") != "1") return;
#if UNITY_ANDROID && !UNITY_EDITOR
            // Android CaptureScreenshot prefixes persistentDataPath even for an absolute path.
            _screen.StartCoroutine(CapturePrivateScreenshot(path));
#else
            ScreenCapture.CaptureScreenshot(path);
#endif
        }
#if UNITY_ANDROID && !UNITY_EDITOR
        static IEnumerator CapturePrivateScreenshot(string path)
        {
            yield return new WaitForEndOfFrame();
            var texture = ScreenCapture.CaptureScreenshotAsTexture();
            try { File.WriteAllBytes(path, texture.EncodeToPNG()); }
            finally { if (texture != null) UnityEngine.Object.Destroy(texture); }
        }
#endif
        void Record(ServerFrame f, bool resync)
        {
            if (_diagnosticRevision == f.Revision && !resync) return;
            _diagnosticRevision = f.Revision;
            var canonical = new StringBuilder();
            foreach (var e in f.Entities)
            {
                canonical.Append(e.Id).Append('|').Append(e.Side).Append('|').Append((int)e.Kind).Append('|').Append(e.DefinitionId).Append('|').Append(e.Hp).Append('|').Append(e.MaxHp).Append('|').Append(e.TargetId).Append('|');
                canonical.Append(e.TransformationStage).Append('|').Append(e.ShieldHp).Append('|').Append(e.ShieldMaxHp).Append('|').Append(e.FirstHitBlocks).Append('|').Append(e.Ammo).Append('|').Append(e.MagazineSize).Append('|');
                Append(canonical,e.ReloadRemaining); Append(canonical,e.ReloadDuration);
                Append(canonical,e.Position.X); Append(canonical,e.Position.Y); Append(canonical,e.PreviousPosition.X); Append(canonical,e.PreviousPosition.Y);
                Append(canonical,e.Facing.X); Append(canonical,e.Facing.Y); Append(canonical,e.Radius); Append(canonical,e.Progress); canonical.Append(';');
            }
            string hash; using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(canonical.ToString()))).Replace("-", "");
            var timing = _presented.ExchangeTiming;
            var presentedAt = ReceivedServerFrame.Clock;
            var entry = new JObject { ["Round"] = f.Round, ["Phase"] = f.Phase, ["Revision"] = f.Revision, ["Tick"] = f.SimulationTick,
                ["DiagnosticsVersion"] = 2, ["MatchId"] = f.MatchId, ["ReceivedAt"] = _presented.ReceivedAt,
                ["NativePresentation"] = _credentials is IMatchNativePresentation { NativePresentation: true },
                ["NativeSamples"] = (_credentials as IMatchNativePresentation)?.NativeSamples ?? 0,
                ["NativeMeanGapMs"] = (_credentials as IMatchNativePresentation)?.NativeMeanGapMs ?? 0,
                ["NativeMaxGapMs"] = (_credentials as IMatchNativePresentation)?.NativeMaxGapMs ?? 0,
                ["NativeSourcePollMaxMs"] = (_credentials as IMatchNativePresentation)?.NativeSourcePollMaxMs ?? 0,
                ["NativeSourceGapMaxMs"] = (_credentials as IMatchNativePresentation)?.NativeSourceGapMaxMs ?? 0,
                ["NativeDecodeMaxMs"] = (_credentials as IMatchNativePresentation)?.NativeDecodeMaxMs ?? 0,
                ["NativeStaleStates"] = (_credentials as IMatchNativePresentation)?.NativeStaleStates ?? 0,
                ["PollStartedAt"] = timing.StartedAt, ["WireCompletedAt"] = timing.WireCompletedAt,
                ["PollMs"] = (timing.JsonCompletedAt - timing.StartedAt) * 1000,
                ["SendMs"] = (timing.SentAt - timing.StartedAt) * 1000,
                ["ResponseWaitMs"] = (timing.WireCompletedAt - timing.SentAt) * 1000,
                ["JsonDecodeMs"] = (timing.JsonCompletedAt - timing.WireCompletedAt) * 1000,
                ["SnapshotDecodeMs"] = (_presented.ReceivedAt - timing.JsonCompletedAt) * 1000,
                ["PresentationAgeMs"] = (presentedAt - _presented.ReceivedAt) * 1000,
                ["PayloadBytes"] = timing.PayloadBytes, ["RenewalMs"] = _presented.RenewalMilliseconds,
                ["AdaptiveDelayMs"] = _presentationClock.Delay * 1000, ["InterpolatedFrames"] = _interpolatedFrames, ["HeldFrames"] = _heldFrames,
                ["MaxHoldSeconds"] = _maxHoldSeconds, ["MaxRenderFrameSeconds"] = _maxFrameSeconds,
                ["Wins0"] = f.Wins0, ["Wins1"] = f.Wins1, ["Results"] = f.ResultCount, ["EntitiesHash"] = hash,
                ["Units"] = _world.ActiveUnitViews, ["Projectiles"] = _world.ActiveProjectileViews, ["Zones"] = _world.ActiveZoneViews, ["Effects"] = _world.ActiveEffectViews,
                ["Resync"] = resync, ["Connected"] = _transport.Connected, ["CatchingUp"] = f.CatchingUp, ["ClientTime"] = presentedAt, ["FrameSeconds"] = Time.unscaledDeltaTime, ["Connections"] = _presented.Connection, ["AuthGeneration"] = _presented.AuthGeneration, ["StreamId"] = _presented.StreamId, ["Pending"] = _transport.Pending };
            _diagnostics?.WriteLine(entry.ToString(Formatting.None));
        }
        static void Append(StringBuilder target, float value) => target.Append(value.ToString("R", CultureInfo.InvariantCulture)).Append('|');
        void Fail(string reason)
        {
            _failure = reason; _transport?.Dispose(); Debug.LogError("Server client: " + reason);
            if (_auto && _credentials != null)
            {
                File.WriteAllText(Path.Combine(_credentials.RunDirectory, "failure-" + _credentials.Side + ".txt"), reason);
                Application.Quit(2);
            }
            try { RenderHud(); } catch { /* Preserve the original initialization error. */ }
        }
        async void ReturnToMenu()
        {
            if (_disposed || _returningToMenu) return;
            if (_credentialsFactory is IMatchSessionNavigation navigation)
            {
                if (_latest?.Value.Phase != "MatchResult") return;
                navigation.PrepareReturn(_latest.Value.MatchId);
                _returningToMenu = true;
                try { await navigation.LoadMenuAsync(_settings.MenuSceneName); }
                catch { _returningToMenu = false; Fail("Unable to return to menu."); }
                return;
            }
            if (RemoteSessionContext.Current != null)
            {
                if (_latest?.Value.Phase != "MatchResult") return;
                RemoteSessionContext.Current.PendingLeaveMatchId = RemoteSessionContext.Current.MatchId;
                SceneManager.LoadScene(_settings.MenuSceneName); return;
            }
            if (Environment.GetEnvironmentVariable("TD_QUEUE_MODE") == "1")
            {
                if (_latest?.Value.Phase != "MatchResult") return;
                Environment.SetEnvironmentVariable("TD_QUEUE_LEAVE_MATCH", Environment.GetEnvironmentVariable("TD_QUEUE_MATCH_ID"));
            }
            SceneManager.LoadScene(_settings.MenuSceneName);
        }
        public void Dispose()
        {
            if (_disposed) return; _disposed = true;
            if (_configuredFrameRate) { Application.targetFrameRate = _previousFrameRate; QualitySettings.vSyncCount = _previousVsync; }
            _transport?.Dispose(); _diagnostics?.Dispose(); _screen.Unbind(); _world.Dispose(); _buffer.Clear();
        }
    }
}

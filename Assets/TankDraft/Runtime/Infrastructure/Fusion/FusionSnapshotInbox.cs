#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace TankDraft.Infrastructure.FusionTransport
{
    public readonly struct FusionPose
    {
        public readonly float X, Y, FacingX, FacingY;
        public FusionPose(float x, float y, float fx, float fy) { X = x; Y = y; FacingX = fx; FacingY = fy; }
    }

    // Per-runner mailbox. Network state supersedes older snapshots; commands never wait for this mailbox's lock.
    public sealed class FusionSnapshotInbox
    {
        readonly object sync = new object();
        TaskCompletionSource<bool> changed = NewSignal();
        JObject latest;
        long version, consumed;
        int epoch;
        int acknowledgedGeneration;
        int lastNativeSerial;
        public int StaleNativeStates { get; private set; }
        bool closed;
        public readonly Dictionary<int, FusionPose> Poses = new Dictionary<int, FusionPose>(); // Unity main thread only.
        public string RenderMatch { get; set; }
        public int RenderRound { get; set; }
        public bool Interpolating { get; set; }
        public long BattleSamples { get; private set; }
        public long BattleIntervals { get; private set; }
        public double TotalBattleGapMs { get; private set; }
        public double MaxBattleGapMs { get; private set; }
        public double MaxSourcePollMs { get; set; }
        public double MaxSourceGapMs { get; set; }
        public double MaxDecodeMs { get; set; }
        double previousAt;
        int previousRound;
        long previousTick = -1;
        public bool Accepts(int value) { lock (sync) return !closed && epoch == value; }
        public bool AcceptRenderState(int streamEpoch, int serial)
        {
            lock (sync)
            {
                if (closed || epoch != streamEpoch || serial <= 0) return false;
                if (serial < lastNativeSerial) { StaleNativeStates++; return false; }
                lastNativeSerial = serial; return true;
            }
        }
        bool StaleRefresh(JObject value) => value?.Value<string>("Kind") == "AccessRefreshRequired" && value.Value<int>("NativeAccessGeneration") < acknowledgedGeneration;
        public void AcknowledgeAccess(int generation)
        {
            lock (sync) { acknowledgedGeneration = Math.Max(acknowledgedGeneration, generation); if (StaleRefresh(latest)) { latest = null; consumed = version; } }
        }
        static TaskCompletionSource<bool> NewSignal() => new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Bind(int value)
        {
            if (value <= 0) throw new InvalidDataException("Invalid native stream epoch.");
            lock (sync) { epoch = value; lastNativeSerial = StaleNativeStates = acknowledgedGeneration = 0; latest = null; consumed = version; closed = false; BattleSamples = BattleIntervals = 0; TotalBattleGapMs = MaxBattleGapMs = previousAt = 0; MaxSourcePollMs = MaxSourceGapMs = MaxDecodeMs = 0; previousTick = -1; }
        }
        public void Publish(int value, JObject snapshot)
        {
            lock (sync)
            {
                if (closed || epoch != value || StaleRefresh(snapshot)) return;
                if (snapshot["Snapshot"] is JObject state && state.Value<string>("Phase") == "Battle")
                {
                    var tick = state.Value<long>("SimulationTick"); var round = state.Value<int>("Round");
                    if (tick != previousTick || round != previousRound)
                    {
                        var now = (double)System.Diagnostics.Stopwatch.GetTimestamp() / System.Diagnostics.Stopwatch.Frequency;
                        if (previousAt > 0 && previousRound == round) { var gap = (now - previousAt) * 1000; BattleIntervals++; TotalBattleGapMs += gap; MaxBattleGapMs = Math.Max(MaxBattleGapMs, gap); }
                        previousAt = now; previousRound = round; previousTick = tick; BattleSamples++;
                    }
                }
                else previousAt = 0;
                latest = snapshot; version++;
                var wake = changed; changed = NewSignal(); wake.TrySetResult(true);
            }
        }
        public async Task<JObject> ReadAsync(long? cursor, CancellationToken token)
        {
            while (true)
            {
                Task wait;
                lock (sync)
                {
                    if (closed) throw new IOException("Fusion stream closed.");
                    if (latest != null && version != consumed)
                    {
                        consumed = version;
                        var result = (JObject)latest.DeepClone();
                        result.Remove("NativeAccessGeneration");
                        if (result["Snapshot"] is JObject frame && cursor.HasValue)
                        {
                            var events = (JArray)frame["Events"];
                            if (events.Count > 0 && cursor.Value < events[0].Value<long>("Sequence") - 1) frame["ResyncRequired"] = true;
                            for (int i = events.Count - 1; i >= 0; i--) if (events[i].Value<long>("Sequence") <= cursor.Value) events.RemoveAt(i);
                        }
                        return result;
                    }
                    wait = changed.Task;
                }
                using (var stop = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    var canceled = Task.Delay(10000, stop.Token);
                    if (await Task.WhenAny(wait, canceled).ConfigureAwait(false) != wait) { token.ThrowIfCancellationRequested(); throw new IOException("Native snapshot timeout."); }
                    stop.Cancel(); await wait.ConfigureAwait(false);
                }
            }
        }
        public void Close() { lock (sync) { closed = true; changed.TrySetResult(true); } }
        // Main-thread runner teardown; no stale poses survive into the next authenticated runner.
        public void Disconnect()
        {
            lock (sync) { closed = true; epoch = 0; latest = null; consumed = version; changed.TrySetResult(true); changed = NewSignal(); }
            Poses.Clear(); RenderMatch = null; Interpolating = false;
        }
    }
}

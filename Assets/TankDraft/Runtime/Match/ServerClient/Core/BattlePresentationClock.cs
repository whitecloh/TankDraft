using System;

namespace TankDraft.Match.ServerClient
{
    // Arrival jitter buffering only. Never advances the authoritative simulation or invents entities.
    public sealed class BattlePresentationClock
    {
        readonly double minimum, maximum;
        readonly double[] intervals = new double[16], sorted = new double[16];
        int count, index;
        double lastArrival = double.NaN, lastNow = double.NaN, cursor;
        public double Delay { get; private set; }
        public BattlePresentationClock(double minimum, double maximum)
        {
            if (minimum <= 0 || maximum < minimum || maximum > 2) throw new ArgumentOutOfRangeException();
            this.minimum = minimum; this.maximum = maximum; Delay = minimum;
        }
        public void Observe(double arrival, bool reset)
        {
            if (reset) { count = index = 0; lastArrival = lastNow = double.NaN; Delay = minimum; }
            if (!double.IsNaN(lastArrival) && arrival > lastArrival)
            {
                intervals[index] = Math.Min(maximum, arrival - lastArrival); index = (index + 1) % intervals.Length;
                count = Math.Min(count + 1, intervals.Length); Array.Copy(intervals, sorted, count); Array.Sort(sorted, 0, count);
                Delay = Math.Min(maximum, Math.Max(minimum, sorted[(int)Math.Ceiling(count * .95) - 1] * 1.15 + .03));
            }
            lastArrival = arrival;
        }
        public double Advance(double now)
        {
            if (double.IsNaN(lastNow)) cursor = now - Delay;
            else
            {
                double elapsed = Math.Max(0, now - lastNow), target = now - Delay;
                // Gradual catch-up/slow-down avoids jumping backwards when the delay estimate changes.
                double rate = Math.Max(.85, Math.Min(1.1, 1 + (target - cursor - elapsed) * 2));
                cursor = Math.Min(now, cursor + elapsed * rate);
            }
            lastNow = now; return cursor;
        }
    }
}

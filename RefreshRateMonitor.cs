namespace AuroraPAR
{
    /// <summary>
    /// Measures how often the aircraft positions received from Aurora really change, to detect when Aurora's
    /// traffic refresh rate is set to the normal 3 s instead of the fast rate needed by a PAR (0.5 s).
    ///
    /// AuroraPAR polls Aurora many times per second; for each moving aircraft the time between two consecutive
    /// position changes is recorded. The result is the median of the recent intervals of all aircraft, so a single
    /// late update does not matter. Aircraft slower than <see cref="MinSpeedKt"/> (e.g. on the ground) are ignored
    /// because their position may not change at all.
    /// </summary>
    internal class RefreshRateMonitor
    {
        private const double MinSpeedKt = 50;
        /// <summary>Intervals older than this are forgotten, so the value follows a change of the setting.</summary>
        private static readonly TimeSpan Window = TimeSpan.FromSeconds(30);
        /// <summary>Minimum number of intervals before a value is given.</summary>
        private const int MinSamples = 6;
        private const int MaxSamples = 60;

        private sealed class State
        {
            public double Latitude;
            public double Longitude;
            public double Altitude;
            /// <summary>Time of the last observed position change (null until the first change is seen).</summary>
            public DateTime? LastChange;
        }

        private readonly Dictionary<string, State> states = [];
        private readonly Queue<(DateTime Time, double Seconds)> intervals = new();

        /// <summary>
        /// Median time between position updates in seconds, or NaN when there is not enough moving traffic to tell.
        /// </summary>
        public double IntervalSeconds { get; private set; } = double.NaN;

        /// <summary>
        /// Call after every poll of Aurora with all the aircraft received.
        /// </summary>
        public void Update(IReadOnlyList<Aircraft> aircrafts, DateTime now)
        {
            HashSet<string> present = [];
            foreach (Aircraft aircraft in aircrafts)
            {
                present.Add(aircraft.Callsign);
                if (aircraft.Speed < MinSpeedKt)
                {
                    states.Remove(aircraft.Callsign);
                    continue;
                }
                if (!states.TryGetValue(aircraft.Callsign, out State? state))
                {
                    states[aircraft.Callsign] = new State
                    {
                        Latitude = aircraft.Latitude,
                        Longitude = aircraft.Longitude,
                        Altitude = aircraft.Altitude
                    };
                    continue;
                }
                bool changed = aircraft.Latitude != state.Latitude || aircraft.Longitude != state.Longitude || aircraft.Altitude != state.Altitude;
                if (!changed) continue;
                if (state.LastChange is DateTime previous)
                {
                    double seconds = (now - previous).TotalSeconds;
                    if (seconds > 0.05 && seconds < 30)
                    {
                        intervals.Enqueue((now, seconds));
                    }
                }
                state.Latitude = aircraft.Latitude;
                state.Longitude = aircraft.Longitude;
                state.Altitude = aircraft.Altitude;
                state.LastChange = now;
            }
            foreach (string callsign in states.Keys.Where(c => !present.Contains(c)).ToList())
            {
                states.Remove(callsign);
            }
            while (intervals.Count > 0 && (now - intervals.Peek().Time > Window || intervals.Count > MaxSamples))
            {
                intervals.Dequeue();
            }
            if (intervals.Count >= MinSamples)
            {
                double[] sorted = intervals.Select(i => i.Seconds).OrderBy(s => s).ToArray();
                IntervalSeconds = sorted[sorted.Length / 2];
            }
            else
            {
                IntervalSeconds = double.NaN;
            }
        }

        /// <summary>
        /// Forgets everything (e.g. after a reconnection).
        /// </summary>
        public void Reset()
        {
            states.Clear();
            intervals.Clear();
            IntervalSeconds = double.NaN;
        }
    }
}

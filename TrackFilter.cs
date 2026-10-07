namespace AuroraPAR
{
    /// <summary>How much the tracks of the modern display are smoothed (see <see cref="TrackFilter"/>).</summary>
    internal enum TrackSmoothing
    {
        Off,
        Light,
        Strong
    }

    /// <summary>
    /// Track filter of the modern display, as the computer of a radar: the tracks move smoothly instead of jumping
    /// at every position received from Aurora (and the altitude without its steps).
    ///
    /// For each aircraft the filter keeps an estimated position and moves it on at every redraw with the ground
    /// speed and track given by Aurora and the vertical speed (as the coasting track). When Aurora gives a new
    /// position, the estimate is pulled towards it by a part of the difference (alpha filter): the larger the part,
    /// the closer and the less smooth the track. So the track stays on the real aircraft and moves smoothly.
    ///
    /// The history dots (plots) and the beam check use the real positions; only the symbol and the label of the
    /// track use the estimate (<see cref="Aircraft.Filtered"/>).
    /// </summary>
    internal sealed class TrackFilter
    {
        /// <summary>The estimate is not moved on longer than this without a new position (data stopped).</summary>
        private const double MaxPredictSeconds = 2;
        /// <summary>Beyond this difference (NM, ft) the estimate jumps to the real position (new or teleported aircraft).</summary>
        private const double ResetDistanceNM = 1;
        private const double ResetAltitudeFt = 1500;

        private sealed class State
        {
            public double Latitude;
            public double Longitude;
            public double Altitude;
            /// <summary>Time of the estimate above.</summary>
            public DateTime Time;
            /// <summary>When the last new position was received.</summary>
            public DateTime RawTime;
            /// <summary>Last position received, to tell a new one.</summary>
            public double RawLatitude = double.NaN;
            public double RawLongitude = double.NaN;
            public double RawAltitude = double.NaN;
        }

        private readonly Dictionary<string, State> states = [];

        /// <summary>
        /// Sets <see cref="Aircraft.Filtered"/> of each aircraft to its estimated position at <paramref name="now"/>
        /// (null with <see cref="TrackSmoothing.Off"/>). Call at every redraw with the last positions received.
        /// </summary>
        public void Apply(IReadOnlyList<Aircraft> aircrafts, DateTime now, TrackSmoothing smoothing)
        {
            if (smoothing == TrackSmoothing.Off)
            {
                foreach (Aircraft aircraft in aircrafts) aircraft.Filtered = null;
                states.Clear();
                return;
            }
            // Part of the difference corrected at each new position: horizontal (Aurora sends a position every
            // 0.5 s) and altitude (it changes in steps, so it is corrected less).
            (double alpha, double altitudeAlpha) = smoothing == TrackSmoothing.Strong ? (0.25, 0.10) : (0.5, 0.25);
            HashSet<string> present = [];
            foreach (Aircraft aircraft in aircrafts)
            {
                present.Add(aircraft.Callsign);
                if (!states.TryGetValue(aircraft.Callsign, out State? state))
                {
                    state = Reset(new State(), aircraft, now);
                    states[aircraft.Callsign] = state;
                }
                else
                {
                    double elapsed = Math.Max(0, (now - state.Time).TotalSeconds);
                    Predict(state, aircraft, now);
                    bool newPosition = aircraft.Latitude != state.RawLatitude || aircraft.Longitude != state.RawLongitude
                        || aircraft.Altitude != state.RawAltitude;
                    if (newPosition)
                    {
                        double northNM = (aircraft.Latitude - state.Latitude) * 60;
                        double eastNM = (aircraft.Longitude - state.Longitude) * 60 * Math.Cos(aircraft.Latitude * Math.PI / 180);
                        if (Math.Sqrt(northNM * northNM + eastNM * eastNM) > ResetDistanceNM
                            || Math.Abs(aircraft.Altitude - state.Altitude) > ResetAltitudeFt)
                        {
                            Reset(state, aircraft, now);
                        }
                        else
                        {
                            state.Latitude += alpha * (aircraft.Latitude - state.Latitude);
                            state.Longitude += alpha * (aircraft.Longitude - state.Longitude);
                            state.Altitude += altitudeAlpha * (aircraft.Altitude - state.Altitude);
                            state.RawLatitude = aircraft.Latitude;
                            state.RawLongitude = aircraft.Longitude;
                            state.RawAltitude = aircraft.Altitude;
                            state.RawTime = now;
                        }
                    }
                    else if ((now - state.RawTime).TotalSeconds > MaxPredictSeconds)
                    {
                        // No new position for a while (simulator paused, data stopped): back onto the last real one in
                        // about a second, whatever the number of calls per second.
                        double back = 1 - Math.Exp(-elapsed / 0.5);
                        state.Latitude += back * (aircraft.Latitude - state.Latitude);
                        state.Longitude += back * (aircraft.Longitude - state.Longitude);
                        state.Altitude += back * (aircraft.Altitude - state.Altitude);
                    }
                    // Vertical speed not known yet (first seconds of a track): the real altitude.
                    if (aircraft.VerticalSpeedFpm == null) state.Altitude = aircraft.Altitude;
                }
                Aircraft filtered = aircraft.Copy();
                filtered.Latitude = state.Latitude;
                filtered.Longitude = state.Longitude;
                filtered.Altitude = state.Altitude;
                aircraft.Filtered = filtered;
            }
            foreach (string callsign in states.Keys.Where(c => !present.Contains(c)).ToList())
            {
                states.Remove(callsign);
            }
        }

        private static State Reset(State state, Aircraft aircraft, DateTime now)
        {
            state.Latitude = state.RawLatitude = aircraft.Latitude;
            state.Longitude = state.RawLongitude = aircraft.Longitude;
            state.Altitude = state.RawAltitude = aircraft.Altitude;
            state.Time = now;
            state.RawTime = now;
            return state;
        }

        /// <summary>
        /// Moves the estimate on to <paramref name="now"/> with the speed, track and vertical speed of the aircraft;
        /// not more than <see cref="MaxPredictSeconds"/> after the last new position.
        /// </summary>
        private static void Predict(State state, Aircraft aircraft, DateTime now)
        {
            DateTime end = now < state.RawTime.AddSeconds(MaxPredictSeconds) ? now : state.RawTime.AddSeconds(MaxPredictSeconds);
            double seconds = (end - state.Time).TotalSeconds;
            state.Time = now;
            if (seconds <= 0) return;
            double nm = aircraft.Speed * seconds / 3600;
            double track = aircraft.Track * Math.PI / 180;
            double cosLatitude = Math.Max(0.01, Math.Cos(state.Latitude * Math.PI / 180));
            state.Latitude += nm * Math.Cos(track) / 60;
            state.Longitude += nm * Math.Sin(track) / (60 * cosLatitude);
            state.Altitude += (aircraft.VerticalSpeedFpm ?? 0) * seconds / 60;
        }
    }
}

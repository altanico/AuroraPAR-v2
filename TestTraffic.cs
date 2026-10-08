namespace AuroraPAR
{
    /// <summary>
    /// Test traffic: virtual aircraft for testing the radar without waiting for real traffic (also without Aurora).
    /// Each one flies towards the runway in use at a set speed, at a lateral offset from the centreline and a
    /// height offset from the glide path that the user changes (joystick of <see cref="TestTrafficWindow"/>); in
    /// Auto mode the offsets go back to zero by themselves, so it flies down the glide path on the centreline.
    /// Positions are given as if they came from Aurora (merged into the traffic list), optionally with Aurora's
    /// irregular rhythm (positions every 0.5 s, altitude in steps) to test the track smoothing.
    ///
    /// Used from the UI thread (window) and from the data timer: everything is under a lock.
    /// </summary>
    internal sealed class TestTraffic
    {
        public sealed class Plane
        {
            public string Callsign = "";
            /// <summary>Distance from the touchdown point along the centreline (NM).</summary>
            public double Distance;
            /// <summary>Lateral offset from the centreline (NM, positive right as seen by the pilot).</summary>
            public double Lateral;
            /// <summary>Height above (+) or below (−) the glide path (ft).</summary>
            public double HeightOffset;
            public double Speed = 140;
            public string? Squawk;
            /// <summary>Back onto the glide path and centreline by itself.</summary>
            public bool Auto = true;
            /// <summary>Joystick: change of the lateral offset (NM/s) and of the height offset (ft/s).</summary>
            public double LateralRate;
            public double HeightRate;
            // Aurora-like data: last positions given and when.
            public double GivenLatitude = double.NaN;
            public double GivenLongitude = double.NaN;
            public double GivenAltitude = double.NaN;
            public DateTime GivenPositionTime;
            public DateTime GivenAltitudeTime;
        }

        /// <summary>Fastest change of the offsets with the joystick at its edge.</summary>
        public const double MaxLateralRateNM = 0.01;
        public const double MaxHeightRateFt = 20;
        /// <summary>Auto mode: time constant to go back onto the glide path and centreline (s).</summary>
        private const double AutoSeconds = 6;

        private readonly object sync = new();
        private readonly List<Plane> planes = [];
        private readonly Random random = new();
        private DateTime lastTime = DateTime.MinValue;
        private int number;

        public bool Paused { get; set; }
        /// <summary>Positions every about 0.5 s (a little irregular) and the altitude every 3 s, as with Aurora.</summary>
        public bool AuroraLike { get; set; }

        public int Count
        {
            get { lock (sync) return planes.Count; }
        }

        /// <summary>Copies of the aircraft, for the window.</summary>
        public List<Plane> List()
        {
            lock (sync) return [.. planes];
        }

        /// <summary>Adds an aircraft on the glide path and the centreline; returns its callsign.</summary>
        public string Add(double distanceNM, double speedKt, string? squawk)
        {
            lock (sync)
            {
                number++;
                Plane plane = new() { Callsign = $"TEST{number}", Distance = distanceNM, Speed = speedKt, Squawk = squawk };
                planes.Add(plane);
                return plane.Callsign;
            }
        }

        /// <summary>Changes an aircraft (under the lock).</summary>
        public void Change(string callsign, Action<Plane> change)
        {
            lock (sync)
            {
                Plane? plane = planes.FirstOrDefault(p => p.Callsign == callsign);
                if (plane != null) change(plane);
            }
        }

        public void Remove(string callsign)
        {
            lock (sync) planes.RemoveAll(p => p.Callsign == callsign);
        }

        public void Clear()
        {
            lock (sync) planes.Clear();
        }

        /// <summary>
        /// Moves the aircraft on to <paramref name="now"/> and gives them as Aurora traffic for the runway in use.
        /// Aircraft beyond the touchdown point are removed (landed).
        /// </summary>
        public List<Aircraft> Snapshot(Runway runway, DateTime now)
        {
            lock (sync)
            {
                double seconds = lastTime == DateTime.MinValue ? 0 : Math.Clamp((now - lastTime).TotalSeconds, 0, 1);
                lastTime = now;
                if (Paused) seconds = 0;
                List<Aircraft> result = [];
                foreach (Plane plane in planes.ToList())
                {
                    plane.Distance -= plane.Speed * seconds / 3600;
                    plane.Lateral += plane.LateralRate * seconds;
                    plane.HeightOffset += plane.HeightRate * seconds;
                    if (plane.Auto && plane.LateralRate == 0 && plane.HeightRate == 0)
                    {
                        double back = Math.Exp(-seconds / AutoSeconds);
                        plane.Lateral *= back;
                        plane.HeightOffset *= back;
                    }
                    if (plane.Distance < 0)
                    {
                        planes.Remove(plane);
                        continue;
                    }
                    result.Add(ToAircraft(plane, runway, now));
                }
                return result;
            }
        }

        private Aircraft ToAircraft(Plane plane, Runway runway, DateTime now)
        {
            // From the threshold: back along the approach (opposite of the runway heading), then to the pilot's right.
            double along = plane.Distance - runway.TouchdownNM;
            double approach = (runway.Heading + 180) * Math.PI / 180;
            double right = (runway.Heading + 90) * Math.PI / 180;
            double northNM = along * Math.Cos(approach) + plane.Lateral * Math.Cos(right);
            double eastNM = along * Math.Sin(approach) + plane.Lateral * Math.Sin(right);
            double latitude = runway.Latitude + northNM / 60;
            double longitude = runway.Longitude + eastNM / (60 * Math.Max(0.01, Math.Cos(runway.Latitude * Math.PI / 180)));
            double altitude = runway.Elevation + Math.Max(0, runway.GlidePathHeight(plane.Distance) + plane.HeightOffset);
            // Track: the runway heading, turned by the lateral drift.
            double driftKt = plane.LateralRate * 3600;
            double track = (runway.Heading + Math.Atan2(driftKt, Math.Max(1, plane.Speed)) * 180 / Math.PI + 360) % 360;
            if (AuroraLike)
            {
                // As Aurora: a new position every 0.4–0.65 s, a new altitude every 3 s; in between the last ones.
                if (double.IsNaN(plane.GivenLatitude) || now >= plane.GivenPositionTime)
                {
                    plane.GivenLatitude = latitude;
                    plane.GivenLongitude = longitude;
                    plane.GivenPositionTime = now.AddSeconds(0.4 + 0.25 * random.NextDouble());
                }
                if (double.IsNaN(plane.GivenAltitude) || now >= plane.GivenAltitudeTime)
                {
                    plane.GivenAltitude = Math.Round(altitude / 10) * 10;
                    plane.GivenAltitudeTime = now.AddSeconds(3);
                }
                latitude = plane.GivenLatitude;
                longitude = plane.GivenLongitude;
                altitude = plane.GivenAltitude;
            }
            else
            {
                plane.GivenLatitude = plane.GivenLongitude = plane.GivenAltitude = double.NaN;
            }
            return new Aircraft
            {
                Callsign = plane.Callsign,
                Latitude = latitude,
                Longitude = longitude,
                Altitude = altitude,
                Track = track,
                Speed = plane.Speed,
                Squawk = plane.Squawk,
                IsTest = true
            };
        }
    }
}

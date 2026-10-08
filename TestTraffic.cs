namespace AuroraPAR
{
    /// <summary>How the test aircraft turn with the joystick (<see cref="TestTraffic.TurnMode"/>).</summary>
    internal enum TestTurnMode
    {
        /// <summary>Half rate: 1.5°/s at full stick.</summary>
        Half,
        /// <summary>Rate one: 3°/s at full stick.</summary>
        Standard,
        /// <summary>The longer the stick is held at full deflection, the faster the turn.</summary>
        Free
    }

    /// <summary>
    /// Test traffic: virtual aircraft for testing the radar without waiting for real traffic (also without Aurora).
    ///
    /// Each aircraft has a course (relative to the final course of the runway in use), a vertical speed and a speed,
    /// and keeps them until changed, as a real one: the joystick of <see cref="TestTrafficWindow"/> turns it (at the
    /// selected turn rate) and changes its vertical speed; released, course and vertical speed stay. Buttons bring it
    /// back to the final course or to the vertical speed of the glide path (the "best" one for its speed), or move
    /// the vertical speed in steps aligned on that best value. In Auto it intercepts the centreline and the glide
    /// path by itself.
    ///
    /// Positions are given as if they came from Aurora (merged into the traffic list), optionally with Aurora's
    /// irregular rhythm (positions every 0.5 s, altitude in steps) to test the track smoothing.
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
            /// <summary>Height above the threshold (ft).</summary>
            public double Height;
            public double Speed = 140;
            public string? Squawk;
            /// <summary>Course relative to the final course (degrees, positive to the right).</summary>
            public double Course;
            /// <summary>Vertical speed (ft/min, negative descending).</summary>
            public double VerticalSpeed;
            /// <summary>Turn rate now (°/s, positive to the right).</summary>
            public double TurnRate;
            /// <summary>Keeps the vertical speed of the glide path (Optimal GP), also when the speed or course change.</summary>
            public bool HoldGlidePath = true;
            /// <summary>Turning back to the final course (Final CRS).</summary>
            public bool BackToFinal;
            /// <summary>Intercepts the centreline and the glide path by itself.</summary>
            public bool Auto;
            /// <summary>Joystick deflection (−1..1, X right, Y up) and for how long X has been at its edge (Free).</summary>
            public double StickX;
            public double StickY;
            public double StickHeld;
            /// <summary>For the window: vertical speed of the glide path for this aircraft now, and the GP angle.</summary>
            public double BestVerticalSpeed;
            public double GlideSlope = 3;
            // Aurora-like data: last positions given and when.
            public double GivenLatitude = double.NaN;
            public double GivenLongitude = double.NaN;
            public double GivenAltitude = double.NaN;
            public DateTime GivenPositionTime;
            public DateTime GivenAltitudeTime;

            public Plane Copy() => (Plane)MemberwiseClone();
        }

        /// <summary>Largest course off the final course (degrees).</summary>
        public const double MaxCourse = 60;
        /// <summary>Vertical speed limits (ft/min) and how fast the full stick changes it (ft/min per second).</summary>
        public const double MaxClimb = 2000;
        public const double MaxDescent = -3000;
        private const double StickVerticalRate = 600;
        /// <summary>Step of the vertical speed buttons (ft/min), aligned on the best vertical speed.</summary>
        public const double VerticalStep = 100;
        /// <summary>Free turn: rate at full stick at once, growth per second held at the edge, maximum (°/s).</summary>
        private const double FreeStartRate = 1.5;
        private const double FreeGrowth = 1.5;
        private const double FreeMaxRate = 10;
        /// <summary>Feet per minute of vertical speed for each knot along the track and each unit of tan(GP).</summary>
        private const double FeetPerMinutePerKnot = Runway.FeetPerNM / 60;

        private readonly object sync = new();
        private readonly List<Plane> planes = [];
        private readonly Random random = new();
        private DateTime lastTime = DateTime.MinValue;
        private int number;

        public bool Paused { get; set; }
        /// <summary>Positions every about 0.5 s (a little irregular) and the altitude every 3 s, as with Aurora.</summary>
        public bool AuroraLike { get; set; }
        public TestTurnMode TurnMode { get; set; } = TestTurnMode.Standard;

        public int Count
        {
            get { lock (sync) return planes.Count; }
        }

        /// <summary>Copies of the aircraft, for the window.</summary>
        public List<Plane> List()
        {
            lock (sync) return planes.Select(p => p.Copy()).ToList();
        }

        /// <summary>
        /// Adds an aircraft on the final course, at a lateral offset (m, + right) and a height offset from the glide
        /// path (ft, + above), descending at the glide path rate; returns its callsign. The height is set at the
        /// next <see cref="Snapshot"/> (it needs the runway).
        /// </summary>
        public string Add(double distanceNM, double speedKt, string? squawk, double lateralM, double heightOffsetFt)
        {
            lock (sync)
            {
                // The first one after a pause of the list: no jump for the time with no test traffic.
                if (planes.Count == 0) lastTime = DateTime.MinValue;
                number++;
                Plane plane = new()
                {
                    Callsign = $"TEST{number}",
                    Distance = distanceNM,
                    Lateral = lateralM / 1852,
                    Height = double.NaN,
                    VerticalSpeed = heightOffsetFt,
                    Speed = speedKt,
                    Squawk = squawk
                };
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

        /// <summary>Next vertical speed of the step buttons: on the grid of <see cref="VerticalStep"/> through the best one.</summary>
        public static double StepVerticalSpeed(double current, double best, bool up)
        {
            double k = (current - best) / VerticalStep;
            double next = up ? Math.Floor(k + 1e-6) + 1 : Math.Ceiling(k - 1e-6) - 1;
            return Math.Clamp(best + next * VerticalStep, MaxDescent, MaxClimb);
        }

        /// <summary>Vertical speed that keeps the glide path at this speed and course (ft/min, negative).</summary>
        private static double BestVerticalSpeed(Plane plane, Runway runway)
        {
            double alongKt = plane.Speed * Math.Cos(plane.Course * Math.PI / 180);
            return -alongKt * FeetPerMinutePerKnot * Math.Tan(runway.GlideSlope * Math.PI / 180);
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
                    // New aircraft: on the glide path plus its height offset (kept in VerticalSpeed by Add).
                    if (double.IsNaN(plane.Height))
                    {
                        plane.Height = Math.Max(0, runway.GlidePathHeight(plane.Distance) + plane.VerticalSpeed);
                        plane.VerticalSpeed = BestVerticalSpeed(plane, runway);
                    }
                    Fly(plane, runway, seconds);
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

        private void Fly(Plane plane, Runway runway, double seconds)
        {
            // Turn.
            double stickRate = TurnMode switch
            {
                TestTurnMode.Half => 1.5,
                TestTurnMode.Free => Math.Min(FreeMaxRate, FreeStartRate + FreeGrowth * plane.StickHeld),
                _ => 3
            };
            if (Math.Abs(plane.StickX) > 0.95) plane.StickHeld += seconds; else plane.StickHeld = 0;
            if (plane.StickX != 0)
            {
                plane.TurnRate = plane.StickX * stickRate;
            }
            else if (plane.Auto)
            {
                // Intercept the centreline: a course towards it, the closer the smaller (at most 20°).
                double target = Math.Clamp(-plane.Lateral * 150, -20, 20);
                plane.TurnRate = Math.Clamp((target - plane.Course) / 2, -3, 3);
            }
            else if (plane.BackToFinal)
            {
                double rate = TurnMode == TestTurnMode.Half ? 1.5 : 3;
                plane.TurnRate = -Math.Sign(plane.Course) * Math.Min(rate, Math.Abs(plane.Course) / Math.Max(seconds, 1e-3));
                if (Math.Abs(plane.Course) < 0.01)
                {
                    plane.Course = 0;
                    plane.TurnRate = 0;
                    plane.BackToFinal = false;
                }
            }
            else
            {
                // Stick released: the course reached stays.
                plane.TurnRate = 0;
            }
            plane.Course = Math.Clamp(plane.Course + plane.TurnRate * seconds, -MaxCourse, MaxCourse);

            // Vertical speed.
            double best = BestVerticalSpeed(plane, runway);
            plane.BestVerticalSpeed = best;
            plane.GlideSlope = runway.GlideSlope;
            if (plane.StickY != 0)
            {
                plane.VerticalSpeed = Math.Clamp(plane.VerticalSpeed + plane.StickY * StickVerticalRate * seconds, MaxDescent, MaxClimb);
            }
            else if (plane.Auto)
            {
                // Intercept the glide path: the best vertical speed, corrected by the height off the glide path.
                double off = plane.Height - runway.GlidePathHeight(plane.Distance);
                plane.VerticalSpeed = Math.Clamp(best - Math.Clamp(off * 3, -500, 500), MaxDescent, MaxClimb);
            }
            else if (plane.HoldGlidePath)
            {
                plane.VerticalSpeed = best;
            }

            // Move.
            double course = plane.Course * Math.PI / 180;
            double nm = plane.Speed * seconds / 3600;
            plane.Distance -= nm * Math.Cos(course);
            plane.Lateral += nm * Math.Sin(course);
            plane.Height = Math.Max(0, plane.Height + plane.VerticalSpeed * seconds / 60);
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
            double altitude = runway.Elevation + plane.Height;
            double track = (runway.Heading + plane.Course + 360) % 360;
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

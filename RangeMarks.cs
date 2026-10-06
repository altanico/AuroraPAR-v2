namespace AuroraPAR
{
    /// <summary>
    /// Range marks of the views, measured from the touchdown point: distance and whether it is a major mark
    /// (solid, with its distance written below) or a minor one (dashed, no text).
    /// </summary>
    internal static class RangeMarks
    {
        public static IEnumerable<(double Distance, bool Major)> For(double range)
        {
            // Major and minor step (NM) for each range; 0 = no minor marks.
            (double major, double minor) = range switch
            {
                >= 20 => (2.0, 0.0),
                >= 10 => (1.0, 0.0),
                >= 5 => (1.0, 0.5),
                _ => (0.25, 0.0)
            };
            double step = minor > 0 ? minor : major;
            int count = (int)Math.Round(range / step);
            for (int i = 1; i <= count; i++)
            {
                double distance = Math.Round(i * step, 3);
                bool isMajor = Math.Abs(distance / major - Math.Round(distance / major)) < 1e-6;
                yield return (distance, isMajor);
            }
        }

        public static string Label(double distance)
        {
            return distance.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "NM";
        }
    }
}

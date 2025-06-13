namespace CircularBufferAsync.LoadProfiles
{
    public class StatisticalProfile
    {
        private readonly int[] _sizes;
        private readonly double[] _cumulative;
        private readonly Random _rnd = new();

        public StatisticalProfile(Dictionary<int, int> histogram)
        {
            if (histogram is null || histogram.Count == 0)
                throw new ArgumentException("Histogram is empty or null");

            _sizes = histogram.Keys.ToArray();
            var counts = _sizes.Select(s => histogram[s]).ToArray();
            double total = counts.Sum();

            _cumulative = new double[counts.Length];
            double acc = 0;
            for (int i = 0; i < counts.Length; i++)
            {
                acc += counts[i] / total;
                _cumulative[i] = acc;
            }
        }

        public int NextPacketSize()
        {
            double r = _rnd.NextDouble();
            for (int i = 0; i < _cumulative.Length; i++)
            {
                if (r <= _cumulative[i])
                    return _sizes[i];
            }

            return _sizes[^1]; // fallback
        }
    }
}

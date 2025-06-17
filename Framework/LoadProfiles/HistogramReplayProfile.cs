using System.Text.Json;

namespace NETwork.LoadProfiles
{
    public class HistogramReplayProfile : IReplayProfile
    {
        private readonly List<(int Size, long Timestamp)> _scheduledPackets;
        private int _index;
        private readonly long _startTime;

        public HistogramReplayProfile(string path)
        {
            var json = File.ReadAllText(path);
            var matrix = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, int>>>(json)!;

            _scheduledPackets = matrix
                .SelectMany(sizeEntry =>
                    sizeEntry.Value.SelectMany(timeEntry =>
                    {
                        var size = int.Parse(sizeEntry.Key);
                        var timestamp = long.Parse(timeEntry.Key);
                        var count = timeEntry.Value;
                        return Enumerable.Repeat((Size: size, Timestamp: timestamp), count);
                    }))
                .OrderBy(p => p.Timestamp)
                .ToList();

            _startTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }

        public bool TryGetNext(out int size)
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - _startTime;

            if (_index >= _scheduledPackets.Count || _scheduledPackets[_index].Timestamp > now)
            {
                size = 0;
                return false;
            }

            size = _scheduledPackets[_index].Size;
            _index++;
            return true;
        }
    }
}

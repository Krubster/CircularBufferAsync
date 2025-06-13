using System.Text.Json;

namespace CircularBufferAsync.LoadProfiles
{
    /*
  Generator use:
  var matrix = TrafficMatrixGenerator.Generate(
      durationSeconds: 60,
      packetsPerSecond: 100,
      minSize: 20,
      maxSize: 150,
      distribution: "normal"
  );
  TrafficMatrixGenerator.Save("test-matrix.json", matrix); 
   */
    public class TrafficMatrixGenerator
    {
        public static Dictionary<int, Dictionary<long, int>> Generate(
            int durationSeconds,
            int packetsPerSecond,
            int minSize,
            int maxSize,
            string distribution = "uniform")
        {
            var matrix = new Dictionary<int, Dictionary<long, int>>();

            var rnd = new Random();

            for (long t = 0; t < durationSeconds; t++)
            {
                for (int i = 0; i < packetsPerSecond; i++)
                {
                    int size = distribution switch
                    {
                        "normal" => Clamp((int)Math.Round(Normal(rnd, (minSize + maxSize) / 2, (maxSize - minSize) / 6.0)), minSize, maxSize),
                        "skewed" => Clamp(minSize + (int)(Math.Pow(rnd.NextDouble(), 2) * (maxSize - minSize)), minSize, maxSize),
                        _ => rnd.Next(minSize, maxSize + 1)
                    };

                    if (!matrix.TryGetValue(size, out var bucket))
                    {
                        bucket = new Dictionary<long, int>();
                        matrix[size] = bucket;
                    }

                    if (!bucket.ContainsKey(t))
                    {
                        bucket[t] = 0;
                    }

                    bucket[t]++;
                }
            }

            return matrix;
        }

        public static void Save(string path, Dictionary<int, Dictionary<long, int>> matrix)
        {
            var serializable = matrix.ToDictionary(
                kv => kv.Key.ToString(),
                kv => kv.Value.ToDictionary(inner => inner.Key.ToString(), inner => inner.Value)
            );

            var json = JsonSerializer.Serialize(serializable, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }

        private static int Clamp(int val, int min, int max) => Math.Max(min, Math.Min(max, val));

        private static double Normal(Random rnd, double mean, double stddev)
        {
            // Box-Muller transform
            double u1 = 1.0 - rnd.NextDouble();
            double u2 = 1.0 - rnd.NextDouble();
            return mean + stddev * Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        }
    }
}

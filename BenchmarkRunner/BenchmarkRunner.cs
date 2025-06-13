// Заготовка BenchmarkRunner с конфигом и сбором статистики из stats.csv
using System.Diagnostics;
using System.Text.Json;

public class BenchmarkConfig
{
    public int BenchmarkAmount { get; set; } = 1;
    public List<string> ServerModes { get; set; } = new();
    public List<string> Workloads { get; set; } = new();
    public List<string> ProfilePaths { get; set; } = new();
    public IntMulRange CpuLoad { get; set; } = new();
    public IntRange Connections { get; set; } = new();
    public IntRange SendIntervalMs { get; set; } = new();
    public IntRange DurationSeconds { get; set; } = new() { Min = 40, Max = 40, Step = 1 };
}
public class IntMulRange
{
    public int Min { get; set; }
    public int Max { get; set; }
    public int Step { get; set; }

    public IEnumerable<int> AsEnumerable()
    {
        for (int i = Min; i <= Max; i *= Step)
            yield return i;
    }
}
public class IntRange
{
    public int Min { get; set; }
    public int Max { get; set; }
    public int Step { get; set; }

    public IEnumerable<int> AsEnumerable()
    {
        for (int i = Min; i <= Max; i += Step)
            yield return i;
    }
}

public class BenchmarkRunner
{
    private readonly BenchmarkConfig _config;
    private readonly string _statsPath = "stats.csv";
    private readonly string _resultsPath = "results.csv";
    private readonly string _serverPath;
    private readonly string _clientPath;

    public BenchmarkRunner(BenchmarkConfig config, string serverPath, string clientPath)
    {
        _config = config;
        _serverPath = serverPath;
        _clientPath = clientPath;
        _statsPath = Path.Combine(Path.GetDirectoryName(_serverPath)!, "stats.csv");
    }

    public async Task RunAllAsync()
    {
        File.WriteAllText(_resultsPath,
            "Mode,Workload,CpuLoad,Connections,SendInterval,Duration,Timestamp,Tag,SWorkload,RecvBytes,SentBytes,Packets,Uptime,GC0,GC1,GC2,T1Ticks,T2Ticks,T3Ticks,LogicCycles,NetCycles,WriteCycles\n");

        foreach (var mode in _config.ServerModes)
            foreach (var workload in _config.Workloads)
            {
                foreach (var cpuLoad in _config.CpuLoad.AsEnumerable())
                {
                    foreach (var profile in _config.ProfilePaths)
                        foreach (var conn in _config.Connections.AsEnumerable())
                        {
                            foreach (var sendInt in _config.SendIntervalMs.AsEnumerable())
                            {
                                foreach (var dur in _config.DurationSeconds.AsEnumerable())
                                {
                                    for (int i = 0; i < _config.BenchmarkAmount; ++i)
                                    {
                                        Console.WriteLine(
                                            $"[Running] mode={mode}, workload={workload}, cpuLoad={cpuLoad}, profile={profile}, conn={conn}, send={sendInt}, duration={dur}s");

                                        if (File.Exists(_statsPath)) File.Delete(_statsPath);

                                        var serverArgs = $"--mode={mode} --workload={workload} " +
                                                         $"--cpuload={cpuLoad} " +
                                                         (string.IsNullOrWhiteSpace(profile) ? "" : $"--profile={profile}") +
                                                         $" --duration={dur}";

                                        var clientArgs = $"--connections={conn} " +
                                                         (string.IsNullOrWhiteSpace(profile) ? "" : $"--profile={profile}") +
                                                         $" --sendInterval={sendInt} --finite" +
                                                         (profile.Contains("matrix") ? " --matrix" : "");

                                        var client = new ProcessStartInfo(_clientPath, clientArgs)
                                        {
                                            UseShellExecute = false
                                        };
                                        var clientProcess = Process.Start(client);
                                        await Task.Delay(1000); // Дать клиенту подняться

                                        var server = new ProcessStartInfo(_serverPath, serverArgs)
                                        {
                                            UseShellExecute = false
                                        };
                                        var serverProcess = Process.Start(server);

                                        await Task.WhenAll(
                                            Task.Run(() => serverProcess!.WaitForExit()),
                                            Task.Run(() => clientProcess!.WaitForExit())
                                        );

                                        await AppendStatsToResults(mode, workload, cpuLoad, profile, conn, sendInt, dur);
                                    }
                                }
                            }
                        }
                }
            }
    }

    private async Task AppendStatsToResults(string mode, string workload, int cpuLoad, string profile, int conn, int send, int dur)
    {
        if (!File.Exists(_statsPath)) return;
        var lines = await File.ReadAllLinesAsync(_statsPath);
        foreach (var line in lines) // Пропустить заголовок
        {
            var stats = line.Trim();
            var row = $"{mode},{workload},{cpuLoad},{conn},{send},{dur},{stats}\n";
            await File.AppendAllTextAsync(_resultsPath, row);
        }
    }

    public static BenchmarkConfig LoadConfig(string path)
    {
        return JsonSerializer.Deserialize<BenchmarkConfig>(File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }
}

using CircularBufferAsync;
using CircularBufferAsync.Buffers.Factories;
using CircularBufferAsync.StatsCollectors;
using CircularBufferAsync.Workloads.Factories;
using System.Net;
using Test;
using Test.ConnectionProcessors;
using Test.StatsCollectors;

var runDuration = TimeSpan.FromSeconds(40);
var mode = ServerMode.Async;
var port = 5000;
var workloadType = WorkloadType.Cpu;
var cpuLoad = 10;
string? profilePath = null;

foreach (var arg in args)
{
    if (arg.StartsWith("--mode="))
        mode = Enum.Parse<ServerMode>(arg.Split('=')[1], true);
    else if (arg.StartsWith("--port="))
        port = int.Parse(arg.Split('=')[1]);
    else if (arg.StartsWith("--cpuload="))
        cpuLoad = int.Parse(arg.Split('=')[1]);
    else if (arg.StartsWith("--workload="))
        workloadType = Enum.Parse<WorkloadType>(arg.Split('=')[1], true);
    else if (arg.StartsWith("--profile="))
        profilePath = AppContext.BaseDirectory + arg.Split('=')[1];
    else if (arg.StartsWith("--duration="))
        runDuration = TimeSpan.FromSeconds(int.Parse(arg.Split('=')[1]));
}

var inBufferFactory = new PagingBufferFactory();
var outBufferFactory = new PagingBufferFactory();
var workloadFactory = new DefaultWorkloadFactory();
var workload = workloadFactory.Create(workloadType, profilePath, cpuLoad);

var statsCollector = new FileStatsCollector(AppContext.BaseDirectory+ "\\stats.csv", mode.ToString(), workload.GetType().Name);

var cts = new CancellationTokenSource();
var server = new TcpWorkloadServer(
    new IPEndPoint(IPAddress.Any, port),
    processorFactory: () => mode switch
    {
        ServerMode.Sync => new SyncConnectionProcessor(workload, statsCollector),
        ServerMode.Async => new AsyncConnectionProcessor(inBufferFactory.Create, outBufferFactory.Create, workload, statsCollector),
        ServerMode.Triplex => new TriplexConnectionProcessor(inBufferFactory.Create, outBufferFactory.Create, workload, statsCollector),
        _ => throw new NotSupportedException()
    },
    token: cts.Token
);

Console.CancelKeyPress += (s, e) => {
    e.Cancel = true;
    cts.Cancel();
    server.Stop();
};

server.Start();
Console.WriteLine($"[Server] Running {mode} mode for {runDuration}");
await Task.Delay(runDuration);

cts.Cancel();
server.Stop();
Console.WriteLine("[Server] Exited cleanly.");
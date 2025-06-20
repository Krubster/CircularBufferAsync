using Framework.Workloads;
using NETwork;
using NETwork.Buffers.Factories;
using NETwork.Workloads.Factories;
using System.Net;
using System.Diagnostics;
using Framework.StatsCollectors;
using System.Net.Sockets;

namespace TcpWorkloadServerWorker
{
    public class Program
    {
        private static TcpWorkloadServer Server;
        public static async Task Main(string[] args)
        {
            long startTicks = Stopwatch.GetTimestamp();
            var runDuration = TimeSpan.FromSeconds(30);
            bool infinite = false;
            var mode = ServerMode.Triplex;
            var port = 5000;
            var workloadType = WorkloadType.Cpu;
            var cpuLoad = 10;
            var backgroundWorkload = 10000;
            var sendChance = 0.3;
            var lowBorder = 40;
            var highBorder = 400;
            string? profilePath = null;

            foreach (var arg in args)
            {
                if (arg.StartsWith("--mode="))
                    mode = Enum.Parse<ServerMode>(arg.Split('=')[1], true);
                else if (arg.StartsWith("--port="))
                    port = int.Parse(arg.Split('=')[1]);
                else if (arg.StartsWith("--infinite"))
                    infinite = true;
                else if (arg.StartsWith("--cpuload="))
                    cpuLoad = int.Parse(arg.Split('=')[1]);
                else if (arg.StartsWith("--sendChance="))
                    sendChance = double.Parse(arg.Split('=')[1]);
                else if (arg.StartsWith("--lowBorder="))
                    lowBorder = int.Parse(arg.Split('=')[1]);
                else if (arg.StartsWith("--highBorder="))
                    highBorder = int.Parse(arg.Split('=')[1]);
                else if (arg.StartsWith("--backgroundWorkload="))
                    backgroundWorkload = int.Parse(arg.Split('=')[1]);
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

            //var statsCollector = new FileStatsCollector(AppContext.BaseDirectory + "\\stats.csv", mode.ToString(),
              //  workload.GetType().Name);

            var statsCollector = new GraphingStatsCollector(AppContext.BaseDirectory + "\\stats_log.csv");
            statsCollector.EnableLiveBroadcast(12345);

            var cts = new CancellationTokenSource();

            Server = new TcpWorkloadServer(
                new IPEndPoint(IPAddress.Any, port),
                processorFactory: () => mode switch
                {
                    ServerMode.Sync => new SyncConnectionProcessor(workload,
                        new SpinWaitLogicWorkload(backgroundWorkload),
                        new SimpleServerPacketPattern(sendChance, lowBorder, highBorder),
                        statsCollector),
                    ServerMode.Async => new AsyncConnectionProcessor(inBufferFactory.Create, outBufferFactory.Create,
                        workload,
                        new SpinWaitLogicWorkload(backgroundWorkload),
                        new SimpleServerPacketPattern(sendChance, lowBorder, highBorder),
                        statsCollector),
                    ServerMode.Triplex => new TriplexConnectionProcessor(inBufferFactory.Create,
                        outBufferFactory.Create,
                        workload,
                        new SpinWaitLogicWorkload(backgroundWorkload),
                        new SimpleServerPacketPattern(sendChance, lowBorder, highBorder),
                        statsCollector),
                    _ => throw new NotSupportedException()
                },
                token: cts.Token
            );

            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
                Server.Stop();
            };

            Server.Start();
            Console.WriteLine($"Running {mode} mode for {(infinite? "infinite" : runDuration)}");
            Task.Run(async () => await ListenForCommands(23456));
            await Task.Delay((int)(infinite ? -1 : runDuration.TotalMilliseconds));

            cts.Cancel();
            Server.Stop();
            Console.WriteLine($"Exited cleanly. Process run took {(Stopwatch.GetTimestamp() - startTicks) / 10000000}s");
        }

        public static async Task ListenForCommands(int port)
        {
            var listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
            Console.WriteLine($"Command listener started on port {port}");

            while (true)
            {
                var client = await listener.AcceptTcpClientAsync();
                _ = Task.Run(async () =>
                {
                    using var reader = new StreamReader(client.GetStream());
                    string? line;
                    while ((line = await reader.ReadLineAsync()) != null)
                    {
                        Console.WriteLine($"[CMD] {line}");
                        HandleCommand(line);
                    }
                });
            }
        }
        private static void HandleCommand(string cmd)
        {
            if (cmd.StartsWith("set "))
            {
                var pair = cmd.Substring(4).Split('=');
                if (pair.Length == 2)
                {
                    var key = pair[0].Trim();
                    var value = pair[1].Trim();
                    Console.WriteLine($"Setting {key} = {value}");
                    Server.Processor.SetRuntime(key, value);
                }
            }
            else if (cmd == "stop")
            {
                Environment.Exit(0);
            }
            else if (cmd.StartsWith("command "))
            {
                var arg = cmd.Substring("command ".Length);
                // handle custom logic
            }
        }
    }

}
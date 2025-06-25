using System.Diagnostics;
using NETwork.LoadProfiles;
using System.Net;
using TcpTestFramework;
using System.Net.Sockets;

namespace TcpWorkloadClientWorker
{
    public class Program
    {
        private static TcpWorkloadClient Client;

        public static async Task Main(string[] args)
        {
            IPAddress serverIp = IPAddress.Loopback;
            var serverPort = 5000;
            int connections = 10;
            bool infinite = true;
            string? profilePath = null;
            bool useReplayProfile = false;
            int sendInterval = 2;
            var lowEnd = 20;
            var highEnd = 150;
            foreach (var arg in args)
            {
                if (arg.StartsWith("--connections="))
                    connections = int.Parse(arg.Split('=')[1]);
                else if (arg == "--finite")
                    infinite = false;
                else if (arg.StartsWith("--profile="))
                    profilePath = AppContext.BaseDirectory + arg.Split('=')[1];
                else if (arg == "--matrix")
                    useReplayProfile = true;
                else if (arg.StartsWith("--sendInterval="))
                    sendInterval = int.Parse(arg.Split('=')[1]);
                else if (arg.StartsWith("--lowEnd="))
                    lowEnd = int.Parse(arg.Split('=')[1]);
                else if (arg.StartsWith("--highEnd="))
                    highEnd = int.Parse(arg.Split('=')[1]);
                else if (arg.StartsWith("--serverPort="))
                    serverPort = int.Parse(arg.Split('=')[1]);
            }

            LoadProfile profile;
            if (profilePath != null && useReplayProfile)
            {
                var replay = new HistogramReplayProfile(profilePath);
                profile = new LoadProfile
                {
                    Connections = connections,
                    InfiniteTraffic = infinite,
                    LoopDelay = TimeSpan.FromMilliseconds(sendInterval),
                    PacketSizeGenerator = () =>
                    {
                        int size;
                        while (!replay.TryGetNext(out size))
                            Thread.Sleep(10);
                        return size;
                    }
                };
            }
            else if (profilePath != null)
            {
                profile = LoadProfile.FromHistogramFile(profilePath);
                profile.Connections = connections;
                profile.InfiniteTraffic = infinite;
                profile.LoopDelay = TimeSpan.FromMilliseconds(sendInterval);
            }
            else
            {
                profile = new LoadProfile
                {
                    Connections = connections,
                    InfiniteTraffic = infinite,
                    LoopDelay = TimeSpan.FromMilliseconds(sendInterval),
                    PacketSizeGenerator = () => Random.Shared.Next(lowEnd, highEnd)
                };
            }

            Client = new TcpWorkloadClient(profile, new IPEndPoint(serverIp, serverPort));
            using var cts = new CancellationTokenSource();

            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
                Console.WriteLine("Stopping load client...");
            };
            Task.Run(async () => await ListenForCommands(23457));

            try
            {
                await Client.StartAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("Client cancelled.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Client error: {ex}");
            }

            Console.WriteLine("Client terminated.");
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
                    Client.SetRuntime(key, value);
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
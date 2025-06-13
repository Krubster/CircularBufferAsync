using CircularBufferAsync.LoadProfiles;
using System.Net;
using TcpTestFramework;

public static class Program
{
    public static async Task Main(string[] args)
    {
        var serverIp = IPAddress.Loopback;
        var serverPort = 5000;
        int connections = 10;
        bool infinite = true;
        string? profilePath = null;
        bool useReplayProfile = false;
        int sendInterval = 2;

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
                SendInterval = TimeSpan.FromMilliseconds(sendInterval),
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
            profile.SendInterval = TimeSpan.FromMilliseconds(sendInterval);
        }
        else
        {
            profile = new LoadProfile
            {
                Connections = connections,
                InfiniteTraffic = infinite,
                SendInterval = TimeSpan.FromMilliseconds(sendInterval),
                PacketSizeGenerator = () => Random.Shared.Next(30, 150)
            };
        }

        var client = new TcpLoadClient(profile, new IPEndPoint(serverIp, serverPort));
        using var cts = new CancellationTokenSource();

        Console.CancelKeyPress += (s, e) => {
            e.Cancel = true;
            cts.Cancel();
            Console.WriteLine("Stopping load client...");
        };

        try
        {
            await client.StartAsync(cts.Token);
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
}

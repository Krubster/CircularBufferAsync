using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using NETwork.LoadProfiles;
using Exception = System.Exception;

namespace TcpTestFramework
{
    public class TcpWorkloadClient
    {
        private readonly LoadProfile _profile;
        private readonly IPEndPoint _serverEndPoint;
        private readonly object _lock = new();
        private List<(Task Task, CancellationTokenSource Cts)> _connections = new();

        public TcpWorkloadClient(LoadProfile profile, IPEndPoint serverEndPoint)
        {
            _profile = profile;
            _serverEndPoint = serverEndPoint;
        }

        public async Task StartAsync(CancellationToken ct = default)
        {
            Console.WriteLine($"Starting.");
            for (int i = 0; i < _profile.Connections; ++i)
            {
                StartConnection(i);
            }

            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(1000, ct); // живой фрейм, блокирующий завершение
            }
        }

        private void StartConnection(int id)
        {
            var cts = new CancellationTokenSource();
            var task = Task.Run(async () =>
            {
                try
                {
                    await RunConnection(id, cts.Token);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Connection {id}] ERROR: {ex}");
                }
            }, cts.Token);

            lock (_lock)
                _connections.Add((task, cts));
        }

        private async Task RunConnection(int id, CancellationToken ct)
        {

            Socket socket = await ConnectWithRetryAsync(id, _serverEndPoint, ct);
            if (socket == null)
                return;
            _ = Task.Run(async () =>
            {
                var recvBuffer = new byte[1024*16];
                try
                {
                    while (!ct.IsCancellationRequested)
                    {
                        int read = await socket.ReceiveAsync(recvBuffer, SocketFlags.None, ct);
                        if (read == 0)
                            break; // socket closed

                        // optionally: обработка ответа, например подсчёт/лог
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Connection {id}] Receive loop error: {ex.Message}");
                }
            });

            byte[] sendBuffer = new byte[8192];
            int receiveBufferCount = 0, totalSent = 0;
            try
            {
                await Task.Delay(Random.Shared.Next((int)_profile.LoopDelay.Value.TotalMilliseconds), ct);

                while ((_profile.InfiniteTraffic || totalSent < _profile.TotalBytesToSend) && !ct.IsCancellationRequested)
                {
                    try
                    {
                        int len;
                        if (_profile.Replay is not null)
                        {
                            while (!_profile.Replay.TryGetNext(out len))
                                await Task.Delay(10, ct);
                        }
                        else
                        {
                            len = _profile.PacketSizeGenerator();
                        }

                        if (len + 2 > sendBuffer.Length)
                            len = sendBuffer.Length - 2;

                        BinaryPrimitives.WriteUInt16LittleEndian(sendBuffer.AsSpan(0, 2), (ushort)len);
                        Random.Shared.NextBytes(sendBuffer.AsSpan(2, len));
                        int toSend = len + 2;

                        await socket.SendAsync(sendBuffer.AsMemory(0, toSend), SocketFlags.None, ct);
                        totalSent += toSend;

                        if (_profile.LoopDelay.HasValue)
                            await Task.Delay(_profile.LoopDelay.Value, ct);

                        if (!socket.Connected)
                            break;
                    }
                    catch (SocketException ex)
                    {
                        Console.WriteLine($"[Client #{id}] SocketException ({ex.ErrorCode}): {ex.SocketErrorCode}");
                        break;
                    }
                }
            }
            catch (SocketException ex)
            {
                Console.WriteLine($"[Client #{id}] SocketException ({ex.ErrorCode}): {ex.SocketErrorCode}");
            }
            catch (OperationCanceledException ex)
            {
                Console.WriteLine($"Client exception: {ex}");
            }
            finally
            {
                try
                {
                    if (socket.Connected)
                    {
                        socket.Shutdown(SocketShutdown.Both);
                    }

                    socket.Close(); // безопаснее, чем Dispose()
                }
                catch (SocketException ex)
                {
                    Console.WriteLine($"[Client #{id}] SocketException ({ex.ErrorCode}): {ex.SocketErrorCode}");
                }
                catch(Exception ex)
                {
                    Console.WriteLine($"[Client #{id}] Exception during shutdown: {ex}");
                }

                Console.WriteLine($"[Client #{id}] Connection closed.");
            }
        }

        private static async Task<Socket?> ConnectWithRetryAsync(int id, IPEndPoint _serverEndPoint, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
                    {
                        NoDelay = true
                    };

                    await socket.ConnectAsync(_serverEndPoint, ct);
                    //Console.WriteLine($"[Client #{id}] Connected.");
                    return socket;
                }
                catch (SocketException)
                {
                    await Task.Delay(10, ct); // Server not ready yet
                }
                catch (OperationCanceledException)
                {
                    return null;
                }
            }

            return null;
        }

        public void SetRuntime(string key, string value)
        {
            switch (key)
            {
                case "connections":
                    {
                        SetConnections(int.Parse(value));
                        break;
                    }
                case "sendInterval":
                    {
                        _profile.LoopDelay = TimeSpan.FromMilliseconds(int.Parse(value));
                        break;
                    }
                case "lowEnd":
                    {
                        _profile.LowEnd = int.Parse(value);
                        _profile.PacketSizeGenerator = () => Random.Shared.Next(_profile.LowEnd, _profile.HighEnd);
                        break;
                    }
                case "highEnd":
                    {
                        _profile.HighEnd = int.Parse(value);
                        _profile.PacketSizeGenerator = () => Random.Shared.Next(_profile.LowEnd, _profile.HighEnd);
                        break;
                    }
            }
        }

        private void SetConnections(int desiredCount)
        {
            lock (_lock)
            {
                int current = _connections.Count;
                Console.WriteLine($"Adding {desiredCount - current} connections...");

                if (desiredCount == current)
                    return;

                if (desiredCount > current)
                {
                    for (int i = current; i < desiredCount; ++i)
                        StartConnection(i);
                }
                else // уменьшение
                {
                    var toRemove = _connections.Skip(desiredCount).ToList();
                    foreach (var (task, cts) in toRemove)
                    {
                        cts.Cancel();
                    }

                    _connections = _connections.Take(desiredCount).ToList();
                }

                _profile.Connections = desiredCount;
                Console.WriteLine($"Updated connection count: {desiredCount}");
            }
        }
    }
}

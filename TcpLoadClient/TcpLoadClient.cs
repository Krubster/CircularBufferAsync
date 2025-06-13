using CircularBufferAsync.LoadProfiles;
using System.Buffers;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace TcpTestFramework
{
    public class TcpLoadClient
    {
        private readonly LoadProfile _profile;
        private readonly IPEndPoint _serverEndPoint;

        public TcpLoadClient(LoadProfile profile, IPEndPoint serverEndPoint)
        {
            _profile = profile;
            _serverEndPoint = serverEndPoint;
        }

        public async Task StartAsync(CancellationToken ct = default)
        {
            Console.WriteLine($"[Client] starting.");
            var tasks = new Task[_profile.Connections];
            for (int i = 0; i < _profile.Connections; ++i)
            {
                tasks[i] = Task.Run(() => RunConnection(i, ct), ct);
            }
            await Task.WhenAll(tasks);
        }

        private async Task RunConnection(int id, CancellationToken ct)
        {
            Socket socket = await ConnectWithRetryAsync(id, _serverEndPoint, ct);
            if (socket == null)
                return;

            byte[] sendBuffer = new byte[8192];
            int totalSent = 0;

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

                    await Task.Delay(_profile.SendInterval, ct);

                    if (socket.Available > 0)
                    {
                        byte[] receiveBuffer = ArrayPool<byte>.Shared.Rent(1024);
                        try
                        {
                            _ = await socket.ReceiveAsync(receiveBuffer, SocketFlags.None, ct);
                        }
                        finally
                        {
                            ArrayPool<byte>.Shared.Return(receiveBuffer);
                        }
                    }

                    if (_profile.LoopDelay.HasValue)
                    {
                        await Task.Delay(_profile.LoopDelay.Value, ct);
                    }

                    if (!socket.Connected)
                        break;
                }
                catch (SocketException ex)
                {
                    //Console.WriteLine($"[Client]: server closed connection");
                    break;
                }
            }

            try
            {
                socket.Shutdown(SocketShutdown.Both);
                socket.Dispose();
            }
            catch (Exception ex)
            {
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
    }
}

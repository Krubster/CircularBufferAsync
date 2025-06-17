using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using NETwork.LoadProfiles;

namespace TcpTestFramework
{
    public class TcpWorkloadClient
    {
        private readonly LoadProfile _profile;
        private readonly IPEndPoint _serverEndPoint;
        private readonly Queue<byte[]> _pendingEcho = new Queue<byte[]>();

        public TcpWorkloadClient(LoadProfile profile, IPEndPoint serverEndPoint)
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
            byte[] receiveBuffer = new byte[16384];
            int receiveBufferCount = 0, totalSent = 0;

            Queue<byte[]> pendingEcho = new Queue<byte[]>();

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

                    // Сохраняем отправленное, чтобы потом проверить
                    var copy = new byte[len];
                    sendBuffer.AsSpan(2, len).CopyTo(copy);
                    pendingEcho.Enqueue(copy);

                    await socket.SendAsync(sendBuffer.AsMemory(0, toSend), SocketFlags.None, ct);
                    totalSent += toSend;

                    // Чтение и аккумуляция ответа
                    if (socket.Available > 0 || receiveBufferCount > 0)
                    {
                        int bytesReceived = 0;
                        if (socket.Available > 0)
                        {
                            var mem = receiveBuffer.AsMemory(receiveBufferCount, receiveBuffer.Length - receiveBufferCount);
                            bytesReceived = await socket.ReceiveAsync(mem, SocketFlags.None, ct);
                        }

                        receiveBufferCount += bytesReceived;
                        int offset = 0;

                        while (receiveBufferCount - offset >= 2)
                        {
                            ushort packetLen = BinaryPrimitives.ReadUInt16LittleEndian(receiveBuffer.AsSpan(offset, 2));
                            if (receiveBufferCount - offset - 2 < packetLen)
                                break; // ждём остаток

                            ReadOnlyMemory<byte> actual = receiveBuffer.AsMemory(offset + 2, packetLen);
                            if (pendingEcho.Count == 0)
                            {
                            //    Console.WriteLine($"[Client #{id}] Unexpected packet received.");
                                break;
                            }

                            var expected = pendingEcho.Dequeue();
                            if (!actual.Span.SequenceEqual(expected))
                            {
                            //    Console.WriteLine($"[Client #{id}] Echo mismatch!");
                            }

                            offset += 2 + packetLen;
                        }

                        if (offset > 0)
                        {
                            // сдвигаем остаток в начало буфера
                            receiveBuffer.AsSpan(offset, receiveBufferCount - offset).CopyTo(receiveBuffer);
                            receiveBufferCount -= offset;
                        }
                    }

                    if (_profile.LoopDelay.HasValue)
                        await Task.Delay(_profile.LoopDelay.Value, ct);

                    if (!socket.Connected)
                        break;
                }
                catch (SocketException)
                {
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

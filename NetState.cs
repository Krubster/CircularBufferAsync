using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using CircularBufferAsync.Buffers;
using Framework.Network;
using Test.ConnectionProcessors;

namespace Test
{
    public delegate void PacketHandler(ReadOnlySpan<byte> packet, NetState state);

    public class NetState : IDisposable
    {
        public Socket Socket { get; }
        public GCHandle Handle { get; }
        public int Id { get; } = Interlocked.Increment(ref _nextId);

        public Pipe RecvPipe { get; }
        public Pipe SendPipe { get; }

        private static int _nextId = 1;
        private bool _disposed;
        private bool _flushQueued;
        private IConnectionProcessor Server;

        public NetState(Socket socket, IConnectionProcessor server)
        {
            Server = server;
            Socket = socket;
            RecvPipe = new Pipe(64 * 1024);
            SendPipe = new Pipe(256 * 1024);
            Handle = GCHandle.Alloc(this);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try { Socket.Shutdown(SocketShutdown.Both); } catch { }
            Socket.Close();

            RecvPipe.Dispose();
            SendPipe.Dispose();

            if (Handle.IsAllocated)
                Handle.Free();
        }

        public void ReceiveFromSocket()
        {
            var writer = RecvPipe.Writer;
            var buffer = writer.AvailableToWrite();
            if (buffer.Length == 0 || writer.IsClosed) return;

            try
            {
                int received = Socket.Receive(buffer, SocketFlags.None);
                if (received <= 0)
                {
                    Dispose();
                    return;
                }

                writer.Advance((uint)received);
            }
            catch (SocketException ex)
            {
                // 54, 10054: connection reset
                if (ex.ErrorCode is not 54 and not 10054)
                    Console.WriteLine($"[NetState] SocketException: {ex}");
                Dispose();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[NetState] Receive exception: {ex}");
                Dispose();
            }
        }
        
        public void FlushReset() => _flushQueued = false;

        public int HandleReceive(PacketHandler handler)
        {
            ReceiveFromSocket();

            var reader = RecvPipe.Reader;
            int totalReceived = 0;
            while (true)
            {
                var span = reader.AvailableToRead();
                if (span.Length < 2)
                    break;

                ushort len = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(0, 2));
                if (span.Length < 2 + len)
                    break;

                var payload = span.Slice(0, len + 2);

                try
                {
                    handler(payload, this);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[NetState] Exception in handler: {ex}");
                    Dispose();
                    break;
                }

                totalReceived += 2 + len;
                reader.Advance((uint)(2 + len));
            }
            return totalReceived;
        }

        public void Send(ReadOnlySpan<byte> span, int length)
        {
            Send(span.Slice(0, length));
        }

        public void Send(ReadOnlySpan<byte> span)
        {
            if (span.IsEmpty || SendPipe.Writer.IsClosed)
                return;

            if (!GetSendBuffer(out var buffer))
                return;

            try
            {
                if (span.Length > buffer.Length)
                {
                    // Отбрасываем, если не влезает — можно сделать очередь если нужно
                    return;
                }

                // Простой копир
                span.CopyTo(buffer);
                SendPipe.Writer.Advance((uint)span.Length);

                if (!_flushQueued)
                {
                    Server.FlushPending.Enqueue(this);
                    _flushQueued = true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[NetState] Exception during Send: {ex}");
                Dispose();
            }
        }

        private bool GetSendBuffer(out Span<byte> buffer)
        {
            buffer = SendPipe.Writer.AvailableToWrite();
            return buffer.Length > 0;
        }
    }
}

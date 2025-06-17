
namespace CircularBufferAsync.Buffers
{
    public class CircularNetworkBuffer : INetworkBuffer
    {
        private readonly CircularBuffer _buffer;
        private byte[] _readBuffer;

        public CircularNetworkBuffer(int capacity = 65536, int maxPacketSize = 8192)
        {
            _buffer = new CircularBuffer(capacity);
            _readBuffer = new byte[maxPacketSize];
        }
        public bool Write(ReadOnlySpan<byte> packet, int length)
        {
            return Write(packet.Slice(0, length));
        }
        public bool Write(ReadOnlySpan<byte> packet)
        {
            return _buffer.Write(packet);
        }

        public bool TryPeek(out ReadOnlySpan<byte> packet)
        {
            packet = ReadOnlySpan<byte>.Empty;
            return true;
        }

        public void Advance(int count)
        {
        }
    }
}

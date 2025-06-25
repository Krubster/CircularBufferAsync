using NETwork.Buffers;

namespace NETwork
{
    public class PagingNetworkBuffer : INetworkBuffer
    {
        private readonly PagingBuffer _buffer;

        public long? WrittenBytes => _buffer?.WrittenBytes();

        public PagingNetworkBuffer(PagingBuffer pagingBuffer)
        {
            _buffer = pagingBuffer;
        }

        public Span<byte> GetWriteSpan(int sizeHint, out Action<int> commit)
        {
            return _buffer.GetWriteSpan(sizeHint, out commit);
        }

        public bool Write(ReadOnlySpan<byte> packet, int length)
        {
            return Write(packet.Slice(0, length));
        }

        public bool Write(ReadOnlySpan<byte> packet)
        {
            try
            {
                _buffer.Write(packet);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool TryPeek(out ReadOnlySpan<byte> packet)
        {
            var result = _buffer.Read(out packet);
            return result == ReadState.Success;
        }

        public void Advance(int count)
        {
            _buffer.Advance(count);
        }
    }
}

using System.Buffers.Binary;

namespace CircularBufferAsync.Buffers
{
    public class CircularBuffer
    {
        private readonly byte[] _buffer;
        private int _head;
        private int _tail;
        private bool _wrapped;

        public CircularBuffer(int size = 65536)
        {
            _buffer = new byte[size];
            _head = 0;
            _tail = 0;
            _wrapped = false;
        }

        public bool Write(ReadOnlySpan<byte> packet)
        {
            if (packet.Length > ushort.MaxValue)
                return false;

            int totalLength = packet.Length + 2;
            if (FreeSpace < totalLength)
                return false;

            Span<byte> header = stackalloc byte[2];
            BinaryPrimitives.WriteUInt16LittleEndian(header, (ushort)packet.Length);

            WriteBytes(header);
            WriteBytes(packet);

            return true;
        }

        public ReadState Read(ref byte[] buffer, out int length)
        {
            length = 0;
            if (AvailableBytes < 2)
                return ReadState.NoData;

            ushort packetLen = PeekUShort();
            if (AvailableBytes < 2 + packetLen)
                return ReadState.NoData;

            if (buffer.Length < packetLen)
                return ReadState.InsufficientBuffer;

            Advance(2); // Skip length
            ReadBytes(buffer.AsSpan(0, packetLen));
            length = packetLen;
            return ReadState.Success;
        }

        private ushort PeekUShort()
        {
            if (_head + 1 < _buffer.Length)
                return BinaryPrimitives.ReadUInt16LittleEndian(_buffer.AsSpan(_head, 2));

            // Cross-boundary read
            Span<byte> tmp = stackalloc byte[2];
            tmp[0] = _buffer[_head];
            tmp[1] = _buffer[0];
            return BinaryPrimitives.ReadUInt16LittleEndian(tmp);
        }

        private void Advance(int count)
        {
            _head = (_head + count) % _buffer.Length;
            if (_tail == _head)
                _wrapped = false;
        }

        private void WriteBytes(ReadOnlySpan<byte> data)
        {
            int spaceEnd = _buffer.Length - _tail;

            if (data.Length <= spaceEnd)
            {
                data.CopyTo(_buffer.AsSpan(_tail));
            }
            else
            {
                data.Slice(0, spaceEnd).CopyTo(_buffer.AsSpan(_tail));
                data.Slice(spaceEnd).CopyTo(_buffer);
            }

            _tail = (_tail + data.Length) % _buffer.Length;
            if (_tail == _head)
                _wrapped = true;
        }

        private void ReadBytes(Span<byte> dest)
        {
            int spaceEnd = _buffer.Length - _head;

            if (dest.Length <= spaceEnd)
            {
                _buffer.AsSpan(_head, dest.Length).CopyTo(dest);
            }
            else
            {
                _buffer.AsSpan(_head, spaceEnd).CopyTo(dest);
                _buffer.AsSpan(0, dest.Length - spaceEnd).CopyTo(dest.Slice(spaceEnd));
            }

            _head = (_head + dest.Length) % _buffer.Length;
            if (_tail == _head)
                _wrapped = false;
        }

        private int FreeSpace
        {
            get
            {
                if (_wrapped)
                    return _head - _tail - 1 < 0 ? _buffer.Length + _head - _tail - 1 : _head - _tail - 1;

                return _head <= _tail
                    ? _buffer.Length - (_tail - _head) - 1
                    : _head - _tail - 1;
            }
        }

        private int AvailableBytes
        {
            get
            {
                if (_tail == _head && !_wrapped)
                    return 0;

                if (_tail >= _head)
                    return _tail - _head;

                return _buffer.Length - _head + _tail;
            }
        }
    }
}


public enum WriteState : byte
{
    Success = 0,
    NoSpace = 1
}
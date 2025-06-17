using CircularBufferAsync.Buffers;

namespace CircularBufferAsync
{
    public class PagingBufferOld
    {
        private BufferLink _WriteBufferLink;
        private BufferLink _Start;
        private int _ChainLength;
        public PagingBufferOld(int bufSize, int capacity)
        {
            if (capacity < 2)
            {
                capacity = 2;
            }
            _ChainLength = capacity;
            BufferLink temp = null, swap = null;
            for (int i = 0; i < _ChainLength; ++i)
            {
                if (temp != null)
                {
                    temp.Next = new BufferLink(bufSize, i);
                    swap = temp;
                    temp = temp.Next;
                    temp.Previous = swap;
                    continue;
                }
                _Start = new BufferLink(bufSize, i);
                temp = _Start;
            }
            _WriteBufferLink = _Start;
        }

        public void Write(Span<byte> data)
        {
            int dataLength = data.Length;
            if (dataLength > _WriteBufferLink.Buffer.Length - _WriteBufferLink.WrittenOffset)
            {
                // find new one
                _WriteBufferLink.Finished = true; // finishing previous
                _WriteBufferLink = _Start;
                while (_WriteBufferLink.Next != null && _WriteBufferLink.Finished)
                {
                    _WriteBufferLink = _WriteBufferLink.Next;
                }
                if (_WriteBufferLink.Finished) // create one
                {
                    BufferLink newLink = new BufferLink(_WriteBufferLink.Buffer.Length, _WriteBufferLink.Index + 1);
                    _WriteBufferLink.Next = newLink;
                    newLink.Previous = _WriteBufferLink;
                    _WriteBufferLink = newLink;
                }
            }
            Span<byte> span = new Span<byte>(_WriteBufferLink.Buffer);
            data.CopyTo(span.Slice(_WriteBufferLink.WrittenOffset, dataLength));
            _WriteBufferLink.WrittenOffset += dataLength;
        }

        public ReadState Read(ref byte[] buffer, out int length)
        {
            BufferLink? _toRead = _Start;
            length = 0;
            while (_toRead != null && (!_toRead.Finished || _toRead.WrittenOffset == 0))
            {
                _toRead = _toRead.Next;
            }
            if (_toRead == null) // no data for us
            {
                return ReadState.NoData;
            }
            Span<byte> span = new Span<byte>(_toRead.Buffer);
            if (_toRead.Finished)
            {
                span.Slice(_toRead.StartOffset, _toRead.Buffer.Length - _toRead.StartOffset).CopyTo(buffer);
                length = _toRead.WrittenOffset - _toRead.StartOffset;
                _toRead.WrittenOffset = 0;
                _toRead.Finished = false;
                return ReadState.Success;
            }
            if (_toRead.WrittenOffset == 0)
            {
                return ReadState.NoData;
            }
            // not finished but contains data
            span.Slice(_toRead.StartOffset, _toRead.Buffer.Length - _toRead.StartOffset).CopyTo(buffer);
            length = _toRead.WrittenOffset - _toRead.StartOffset;
            _toRead.StartOffset += length;
            return ReadState.Success;
        }

        private class BufferLink
        {
            public int Index = 0;
            public int WrittenOffset = 0;
            public object LockStartOffset = new object();
            public int StartOffset = 0;
            public bool Finished = false;
            public byte[] Buffer;
            public BufferLink? Next;
            public BufferLink? Previous;
            public BufferLink(int bufferSize, int index)
            {
                Buffer = new byte[bufferSize];
                Index = index;
            }
        }
    }
}

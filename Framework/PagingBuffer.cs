using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace NETwork.Buffers
{
    public class PagingBuffer
    {
        private BufferLink _write;
        private readonly BufferLink _start;
        private BufferLink? _lastReadNode;
        private ConcurrentQueue<BufferLink> _readyPages = new ConcurrentQueue<BufferLink>();
        public PagingBuffer(int bufferSize = 8192, int capacity = 4)
        {
            if (capacity < 2) capacity = 2;

            BufferLink? prev = null;

            for (int i = 0; i < capacity; i++)
            {
                var node = new BufferLink(bufferSize, i);
                if (prev != null)
                {
                    prev.Next = node;
                }
                else
                {
                    _start = node;
                }
                prev = node;
            }

            _write = _start;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public long WrittenBytes()
        {
            long total = 0;

            foreach (var page in _readyPages)
            {
                total += page.Available();
            }

            return total;
        }

        public Span<byte> GetWriteSpan(int sizeHint, out Action<int> commit)
        {
            if (_write.Remaining() < sizeHint)
            {
                _write.Finished = true;

                BufferLink? next = _start;
                while (next != null && next.Finished)
                    next = next.Next;

                if (next == null)
                {
                    var newLink = new BufferLink(_write.Buffer.Length, _write.Index + 1);
                    _write.Next = newLink;
                    _write = newLink;
                }
                else
                {
                    _write = next;
                }
            }

            bool wasEmpty = _write.Available() == 0;
            var span = _write.Buffer.AsSpan(_write.WrittenOffset);

            // Возвращаем делегат для фиксации записи
            commit = (writtenBytes) =>
            {
                _write.WrittenOffset += writtenBytes;
                if (wasEmpty)
                    _readyPages.Enqueue(_write);
            };

            return span;
        }

        public void Write(ReadOnlySpan<byte> data)
        {
            if (data.Length > _write.Remaining())
            {
                _write.Finished = true;
                BufferLink? next = _start;

                while (next != null && next.Finished)
                    next = next.Next;

                if (next == null)
                {
                    var newLink = new BufferLink(_write.Buffer.Length, _write.Index + 1);
                    _write.Next = newLink;
                    _write = newLink;
                }
                else
                {
                    _write = next;
                }
            }
            bool wasEmpty = _write.Available() == 0;
            data.CopyTo(_write.Buffer.AsSpan(_write.WrittenOffset));
            _write.WrittenOffset += data.Length; 

            if (wasEmpty)
                _readyPages.Enqueue(_write);
        }

        public ReadState Read(out ReadOnlySpan<byte> span)
        {
            /*  BufferLink? selected = null;
              int minAge = int.MaxValue;

              var node = _start;
              while (node != null)
              {
                  if (node.Available() > 0 && node.Age < minAge)
                  {
                      minAge = node.Age;
                      selected = node;
                  }
                  node = node.Next;
              }

              if (selected == null)
              {
                  span = default;
                  _lastReadNode = null;
                  return ReadState.NoData;
              }

              _lastReadNode = selected;
              span = selected.Buffer.AsSpan(selected.StartOffset, selected.Available());
              return ReadState.Success;*/
            // сдвигаемся вперёд, пока в голове очереди пустые страницы
            while (_readyPages.TryPeek(out var p) && p.Available() == 0)
                _readyPages.TryDequeue(out _);   // отбросили и смотрим дальше

            if (!_readyPages.TryPeek(out var page))
            {
                span = default;
                _lastReadNode = null;
                return ReadState.NoData;
            }

            span = page.Buffer.AsSpan(page.StartOffset, page.Available());
            _lastReadNode = page;
            return ReadState.Success;
        }

        public void Advance(int count)
        {
            if (_lastReadNode == null || count <= 0)
                throw new InvalidOperationException("No prior read node or invalid advance.");

            if (_lastReadNode.Available() < count)
                throw new InvalidOperationException("Advance exceeds available data in last read node.");

            _lastReadNode.StartOffset += count;
            
            if (_lastReadNode.Available() == 0 && _lastReadNode.Finished)
            {
                _lastReadNode.Reset();                     // теперь страница свободна
                _readyPages.TryDequeue(out _);             // убираем из очереди
            }

            _lastReadNode = null; // сбрасываем после использования
        }

        private class BufferLink
        {
            public readonly byte[] Buffer;
            public int WrittenOffset;
            public int StartOffset;
            public int Index;
            public bool Finished;

            public BufferLink? Next;

            public BufferLink(int size, int index)
            {
                Buffer = new byte[size];
                Index = index;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public int Remaining() => Buffer.Length - WrittenOffset;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public int Available() => WrittenOffset - StartOffset;

            public void Reset()
            {
                WrittenOffset = 0;
                StartOffset = 0;
                Finished = false;
            }
        }
    }

    public enum ReadState
    {
        Success,
        NoData
    }
}

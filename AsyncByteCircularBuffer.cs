using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace CircularBufferAsync
{
    public class AsyncByteCircularBuffer
    {
        private object _FPLockObject = new object();
        private int _FirstPosition = 0;

        private int _Written = 0;
        private object _LockObject = new object();

        public int Written => _Written;
        private int _Rounds = 0;
        public int Rounds => _Rounds;
        private byte[] _Buffer;
        private MemoryPointer[] _MemoryPointers;
        private int _FirstPointer = 0;
        private int _LastPointer = 0;

        public AsyncByteCircularBuffer(int bufferSize)
        {
            bufferSize = (bufferSize / 2) * 2; // making sure it is power of two
            _Buffer = new byte[bufferSize];
            _MemoryPointers = new MemoryPointer[bufferSize / 2];
            for (int i = 0; i < _MemoryPointers.Length; ++i)
            {
                _MemoryPointers[i] = new MemoryPointer();
            }
        }

        public WriteState Write(Span<byte> data)
        {
            MemoryPointer ptrLast = _MemoryPointers[_LastPointer];
            if (ptrLast.m_State == 1) // if everything is written 
            {
                // Log buffer overflow
                return WriteState.NoSpace;
            }

            Span<byte> bufferSpan = new Span<byte>(_Buffer);
            int pStartIndex = 0;
            int dataLength = data.Length;
            int bufferLength = _Buffer.Length;
            if (dataLength + ptrLast.m_End < bufferLength)
            {
                data.CopyTo(bufferSpan.Slice(ptrLast.m_End, dataLength));
                pStartIndex = ptrLast.m_End;
            }
            else
            {
                if (ptrLast.m_End > bufferLength) // last entry has ending point at left side of buffer
                {
                    int startIndex = ptrLast.m_End - bufferLength;
                    int endIndex = dataLength + startIndex;

                    if (endIndex > _FirstPosition) // while we have less available space than left for writing
                    {
                        // Log buffer overflow
                        return WriteState.NoSpace;
                    }

                    pStartIndex = startIndex;
                    data.CopyTo(bufferSpan.Slice(startIndex, dataLength));
                    _Rounds++;
                }
                else
                {
                    int leftover = dataLength + ptrLast.m_End - bufferLength;

                    if (leftover > _FirstPosition) // while we have less available space than left for writing
                    {
                        // Log buffer overflow
                        return WriteState.NoSpace;
                    }

                    pStartIndex = ptrLast.m_End;
                    int firstPart = dataLength - leftover;
                    data.Slice(0, firstPart).CopyTo(bufferSpan.Slice(ptrLast.m_End, firstPart));
                    data.Slice(dataLength - leftover, leftover)
                        .CopyTo(bufferSpan.Slice(0, leftover)); // copying at the start of buffer
                    _Rounds++;
                }
            }
            // moving to next data pointer
            _LastPointer = (_LastPointer + 1) % _MemoryPointers.Length; // advance pointer to right

            ptrLast.m_Start = pStartIndex;
            ptrLast.m_Length = dataLength;
            ptrLast.m_End = ptrLast.m_Start + ptrLast.m_Length;
            ptrLast.m_State = 1;
            lock (_LockObject)
                _Written++;
            return WriteState.Success;
        }

        public ReadState TryRead(ref byte[] memory, out int length)
        {
            MemoryPointer ptrFirst = _MemoryPointers[_FirstPointer];

            length = 0;
            lock (_LockObject)
            {
                if (_Written == 0)
                {
                    return ReadState.NoData;
                }
            }
            Span<byte> bufferSpan = new Span<byte>(_Buffer);
            Span<byte> span = new Span<byte>(memory);
            length = ptrFirst.m_Length;
            int bufferLength = _Buffer.Length;

            if (ptrFirst.m_End < bufferLength) // if we reading solid data without splitting
            {
                bufferSpan.Slice(ptrFirst.m_Start, ptrFirst.m_Length).CopyTo(span);
            }
            else
            {
                int left = ptrFirst.m_End - bufferLength;
                int firstPart = ptrFirst.m_Length - left;
                bufferSpan.Slice(ptrFirst.m_Start, firstPart).CopyTo(span.Slice(0, firstPart));
                bufferSpan.Slice(0, left).CopyTo(span.Slice(firstPart, left));
            }
            lock (_FPLockObject)
            {
                _FirstPosition = (_FirstPosition + ptrFirst.m_Length) % bufferLength;
            }

            _FirstPointer = (_FirstPointer + 1) % _MemoryPointers.Length; // advance first to right
            ptrFirst.m_State = 0; // unlocking thread by changing state
            
            lock (_LockObject)
                _Written--;

            return ReadState.Success;
        }
        private class MemoryPointer
        {
            public int m_Start = 0;
            public int m_Length = 0;
            public byte m_State  = 0; // 0 - read, 1 - written
            public int m_End = 0;

            public MemoryPointer()
            {
            }

        }
    }
    public enum WriteState : byte
    {
        Success = 0,
        NoSpace = 1
    }
    public enum ReadState : byte
    {
        Success = 0,
        NoData = 1
    }
}

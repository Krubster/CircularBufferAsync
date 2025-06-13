
using CircularBufferAsync.Buffers;

namespace CircularBufferAsync
{
    public class AsyncBytePoolCircularBuffer
    {
        private ManualResetEventSlim WriteWaitHandle { get; set; }

        private object _LockObject = new object();
        private object _LockFPointerObject = new object();
        private object _LockLPointerObject = new object();
        private int _Written = 0;
        public int Written => _Written;
        public int Rounds { get; private set; } = 0;
        private MemoryPointer[] Pointers { get; set; }
        private int FirstPointer { get; set; } = 0;
        private int LastPointer { get; set; } = 0;
        public AsyncBytePoolCircularBuffer(int bufferSize)
        {
            bufferSize = (bufferSize / 2) * 2; // making sure it is power of two
            Pointers = new MemoryPointer[bufferSize];
            for (int i = 0; i < Pointers.Length; ++i)
            {
                Pointers[i] = new MemoryPointer();
            }

            WriteWaitHandle = new ManualResetEventSlim(false);
        }

        public void Write(byte[] data)
        {
            if (_Written == Pointers.Length) // if everything is written 
            {
                // Log buffer overflow
                // Console.WriteLine("Hit pointer capacity");
                WriteWaitHandle.Wait(); // waiting for reader thread to process data
            }

            MemoryPointer ptrLast = Pointers[LastPointer];
            ptrLast.Data = data;
            lock (_LockObject)
                _Written++;

            // moving to next data pointer
            LastPointer = (LastPointer + 1) % Pointers.Length;

            if (LastPointer == 0)
                Rounds++;
        }

        public ReadState TryRead(out byte[]? data)
        {

            lock (_LockObject)
            {
                if (_Written == 0)
                {
                    data = null;
                    return ReadState.NoData;
                }
            }

            MemoryPointer ptrFirst = Pointers[FirstPointer];
            data = ptrFirst.Data;
            lock (_LockObject)
                _Written--;

            FirstPointer = (FirstPointer + 1) % Pointers.Length;
            WriteWaitHandle.Set();
            return ReadState.Success;
        }

        private class MemoryPointer
        {
            public byte State { get; set; } = 0; // 0 - read, 1 - written
            public byte[]? Data { get; set; }

            public MemoryPointer()
            {
            }

        }
    }
}

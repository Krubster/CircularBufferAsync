using CircularBufferAsync;
using System;
using System.Buffers;
using System.Diagnostics;
using System.Reflection.PortableExecutable;
using System.Xml.Linq;

AsyncByteCircularBuffer cBuffer = new AsyncByteCircularBuffer(65536 * 4 * 4);
AsyncByteCircularBuffer outBuffer = new AsyncByteCircularBuffer(65536 * 4 * 4);

AsyncBytePoolCircularBuffer cPoolBuffer = new AsyncBytePoolCircularBuffer(65536 * 4 * 4);
AsyncBytePoolCircularBuffer outPoolBuffer = new AsyncBytePoolCircularBuffer(65536 * 4 * 4);

PagingBuffer cPagingBuffer = new PagingBuffer(65536, 2);
PagingBuffer outPagingBuffer = new PagingBuffer(65536, 2);

Random random = new Random();
Console.WriteLine("Preparing IN stream...");
byte[] inputNetworkBuffer = new byte[1024 * 1024 * 128];
int _bytesWritten = 0;
byte[] buffer = new byte[1024 * 4 * 4];
byte[] bufferReader = new byte[1024 * 4 * 4];

MemoryStream msIn = new MemoryStream(inputNetworkBuffer);

using (BinaryWriter writer = new BinaryWriter(msIn))
{
    while (msIn.Position != msIn.Length)
    {
        int length = random.Next(15, 25);
        if (length > msIn.Length - msIn.Position - 1)
        {
            length = (int)(msIn.Length - msIn.Position - 1);
        }

        writer.Write((byte)length);
        for (int i = 0; i < length; ++i)
        {
            writer.Write((byte)random.Next(0, 255));
        }
    }
}

Console.WriteLine("Pregenerating random values...");
int preGeneratedLength = 10000;
byte[] preGenerated = new byte[preGeneratedLength];
for (int i = 0; i < preGenerated.Length; ++i)
{
    preGenerated[i] = (byte)random.Next(0, 255);
}

int preGeneratedIndex = 0;
int preGeneratedIndexRT = 0;

int preGeneratedOutLength = 10000;
byte[] preGeneratedOut = new byte[preGeneratedOutLength];
for (int i = 0; i < preGeneratedOut.Length; ++i)
{
    preGeneratedOut[i] = (byte)random.Next(10, 255);
}

int preGeneratedOutIndex = 0;

bool readFinished = false;
bool writeFinished = false;

long threadsStarted = Stopwatch.GetTimestamp();
Thread writerThread = new Thread(WritePaging);
writerThread.Name = "Paging Write Thread";
writerThread.Start();

Thread readerThread = new Thread(ReadPaging);
readerThread.Name = "Paging Read Thread";
readerThread.Start();

readerThread.Join(); // wait until previous threads finish
long threadsEnded = Stopwatch.GetTimestamp();
Console.WriteLine($"[THREAD RUN TIME]: threads finished in {((double)threadsEnded - (double)threadsStarted) / 10000000.0} seconds");

/*Thread writerThread = new Thread(Write);
writerThread.Name = "Write Thread";
writerThread.Start();

Thread readerThread = new Thread(Read);
readerThread.Name = "Read Thread";
readerThread.Start();

readerThread.Join(); // wait until previous threads finish
threadsEnded = Stopwatch.GetTimestamp();
Console.WriteLine($"[THREAD RUN TIME]: threads finished in {((double)threadsEnded - (double)threadsStarted) / 10000000.0} seconds");

Console.WriteLine($"[BUFFER STATS]: cBuffer.Rounds = {cBuffer.Rounds}, outBuffer.Rounds = {outBuffer.Rounds}");
*/
/*readFinished = false;
writeFinished = false;
writerThread = new Thread(WritePool);
writerThread.Start();

readerThread = new Thread(ReadPool);
readerThread.Start();

readerThread.Join(); // wait until previous threads finish
Console.WriteLine($"[BUFFER STATS]: cPoolBuffer.Rounds = {cPoolBuffer.Rounds}, outPoolBuffer.Rounds = {outPoolBuffer.Rounds}");
*/
preGeneratedOutIndex = 0;
preGeneratedIndex = 0;

threadsStarted = Stopwatch.GetTimestamp();
Thread singleThread = new Thread(Work);
singleThread.Name = "Read\\Write Thread";
singleThread.Start();
singleThread.Join();
threadsEnded = Stopwatch.GetTimestamp();
Console.WriteLine($"[THREAD RUN TIME]: thread finished in {((double)threadsEnded - (double)threadsStarted) / 10000000.0} seconds");

Console.ReadKey();

void Work()
{
    Console.WriteLine("[SINGLE THREAD]: Started single thread worker");
    long start = Stopwatch.GetTimestamp();
    long processed = 0, writtenOut = 0;
    msIn = new MemoryStream(inputNetworkBuffer);
    int pipeLength = 0;
    int bytesRead = 0;
    long msLength = msIn.Length;
    byte[] outPacketBuffer = new byte[65536];
    using (BinaryReader reader = new BinaryReader(msIn))
    {
        while (bytesRead != msLength)
        {
            // simulating receiving packets from EPoolGroup
            int amt = preGenerated[preGeneratedIndex];
            preGeneratedIndex = (++preGeneratedIndex) % preGeneratedLength;
            //  Thread.SpinWait(amt * 10); // simulating other work
            for (int i = 0; i < amt && bytesRead != msLength; ++i)
            {
                byte length = reader.ReadByte();
                byte[] arr = reader.ReadBytes(length);
                bytesRead += 1 + length;
                Thread.SpinWait(length); // simulating packet processing
                processed++;

                // simulating packet out writing
                pipeLength = preGeneratedOut[preGeneratedOutIndex];
                preGeneratedOutIndex = (++preGeneratedOutIndex) % preGeneratedOutLength;
                new Span<byte>(outPacketBuffer).Slice(0, pipeLength + 1);
                Thread.SpinWait(pipeLength); // simulating packet processing

                writtenOut++;
            }
        }
    }

    long end = Stopwatch.GetTimestamp();
    long ticks = end - start;
    double speed = (double)processed / (double)ticks;
    double wspeed = (double)writtenOut / (double)ticks;
    Console.WriteLine($"[SINGLE THREAD]: Processed packets: {processed}, ticks: {ticks}, P\\T: {speed:0.###}, mb\\s: {((double)inputNetworkBuffer.Length / (double)ticks * 10000000.0 / 1024.0 / 1024.0):0.###}");
    Console.WriteLine($"[SINGLE THREAD]: WrittenOut packets: {writtenOut}, ticks: {ticks}, P\\T: {wspeed:0.###}, mb\\s: {((double)inputNetworkBuffer.Length / (double)ticks * 10000000.0 / 1024.0 / 1024.0):0.###}");
}

void WritePaging()
{
    Console.WriteLine("[WRITE THREAD]: Started thread worker");
    long written = 0, processed = 0;
    msIn = new MemoryStream(inputNetworkBuffer);
    int bytesRead = 0;
    long msLength = msIn.Length;
    byte[] outBuffer = new byte[65536];
    long start = Stopwatch.GetTimestamp();
    using (BinaryReader reader = new BinaryReader(msIn))
    {
        // simulating receiving packets from EPoolGroup
        while (bytesRead != msLength)
        {
            int amt = preGenerated[preGeneratedIndex];
            preGeneratedIndex = (++preGeneratedIndex) % preGeneratedLength;
            for (int i = 0; i < amt && bytesRead != msLength; ++i)
            {
                byte length = reader.ReadByte();
                msIn.Position -= 1;
                byte[] arr = reader.ReadBytes(length + 1);
                bytesRead += 1 + length;
                cPagingBuffer.Write(arr);
                written++;

                if (readFinished)
                    break;

                // simulating sending packets out
                if (!readFinished)
                {
                    ReadState state = outPagingBuffer.Read(ref outBuffer, out int l);
                    if (state == ReadState.Success)
                    {
                        // simulating out sending
                        _bytesWritten += l;
                        Span<byte> outSpan = new Span<byte>(outBuffer, 0, l);

                        for (int j = 0; j < l;)
                        {
                            byte packetLength = outSpan[j];
                            j += 1;
                            Span<byte> outArr = outSpan.Slice(j, packetLength);
                            j += packetLength;
                            processed++;
                        }
                    }
                }
            }
        }
    }
    writeFinished = true;
    long end = Stopwatch.GetTimestamp();
    long ticks = end - start;
    double speed = ((double)written) / (double)ticks;
    double ospeed = ((double)processed) / (double)ticks;
    Console.WriteLine($"[WRITE THREAD]: Processed packets: {written}, ticks: {ticks}, P\\T: {speed:0.###}, mb\\s: {((double)inputNetworkBuffer.Length / (double)ticks * 10000000.0 / 1024.0 / 1024.0):0.###}");
    Console.WriteLine($"[WRITE THREAD]: WrittenIn packets: {processed}, ticks: {ticks}, P\\T: {ospeed:0.###}, mb\\s: {((double)inputNetworkBuffer.Length / (double)ticks * 10000000.0 / 1024.0 / 1024.0):0.###}");
}

void ReadPaging()
{
    Console.WriteLine("[READ THREAD]: Started thread worker");
    long processed = 0, writtenOut = 0;
    byte[] readBuffer = new byte[65536];
    byte[] outPacketBuffer = new byte[65536];
    long start = Stopwatch.GetTimestamp();
    while (!writeFinished)
    {
        int amt = preGenerated[preGeneratedIndexRT];
        preGeneratedIndexRT = (++preGeneratedIndexRT) % preGeneratedLength;
        // Thread.SpinWait(amt * 10); // simulating other work
        // simulating processing received packets

        ReadState state = cPagingBuffer.Read(ref readBuffer, out int length);
        if (state == ReadState.Success)
        {
            if (writeFinished)
                break;
            Span<byte> inSpan = new Span<byte>(readBuffer, 0, length);

            for (int j = 0; j < length;)
            {
                byte packetLength = inSpan[j];
                j += 1;
                Span<byte> outArr = inSpan.Slice(j, packetLength);
                j += packetLength;
                Thread.SpinWait(length); // simulating packet processing
                processed++;

                // simulating packet out writing
                byte arrLength = preGeneratedOut[preGeneratedOutIndex];
                preGeneratedOutIndex = (++preGeneratedOutIndex) % preGeneratedOutLength;
                outPacketBuffer[0] = arrLength;
                outPagingBuffer.Write(new Span<byte>(outPacketBuffer).Slice(0, arrLength + 1));
                writtenOut++;
            }

        }
    }
    readFinished = true;
    long end = Stopwatch.GetTimestamp();
    long ticks = end - start;
    double speed = ((double)processed) / (double)ticks;
    double wspeed = ((double)writtenOut) / (double)ticks;
    Console.WriteLine($"[READ THREAD]: ProcessedIn packets: {processed}, ticks: {ticks}, P\\T: {speed:0.###}, mb\\s: {((double)inputNetworkBuffer.Length / (double)ticks * 10000000.0 / 1024.0 / 1024.0):0.###}");
    Console.WriteLine($"[READ THREAD]: WrittenOut packets: {writtenOut},  ticks: {ticks}, P\\T: {wspeed:0.###}, mb\\s: {((double)inputNetworkBuffer.Length / (double)ticks * 10000000.0 / 1024.0 / 1024.0):0.###}");
}


void Write()
{
    Console.WriteLine("[WRITE THREAD]: Started thread worker");
    long start = Stopwatch.GetTimestamp();
    long written = 0, processed = 0;
    byte[] cached = new byte[1024 * 4 * 4];
    int cachedLength = 0;

    msIn = new MemoryStream(inputNetworkBuffer);
    int bytesRead = 0;
    long msLength = msIn.Length;
    using (BinaryReader reader = new BinaryReader(msIn))
    {
        // simulating receiving packets from EPoolGroup
        while (bytesRead != msLength)
        {
            int amt = preGenerated[preGeneratedIndex];
            preGeneratedIndex = (++preGeneratedIndex) % preGeneratedLength;
            for (int i = 0; i < amt && bytesRead != msLength; ++i)
            {
                if (cachedLength > 0)
                {
                    if (cBuffer.Write(new Span<byte>(cached).Slice(0, cachedLength)) == WriteState.Success)
                    {
                        cachedLength = 0;
                        written++;
                    }
                    else
                    {
                        break;
                    }
                }

                byte length = reader.ReadByte();
                byte[] arr = reader.ReadBytes(length);
                bytesRead += 1 + length;
                if (cBuffer.Write(arr) == WriteState.Success)
                {
                    written++;
                }
                else
                {
                    arr.CopyTo(cached, 0);
                    cachedLength = length;
                    break;
                }

                if (readFinished)
                    break;

                // simulating sending packets out
                uint writtenOut = outBuffer.Written;
                if (writtenOut > 0 && !readFinished)
                {
                    for (int j = 0; j < writtenOut; ++j)
                    {
                        ReadState state = outBuffer.TryRead(ref bufferReader, out int l);
                        if (state == ReadState.Success)
                        {
                            // simulating out sending
                            _bytesWritten += 1 + l;
                            processed++;
                        }
                        else
                        {
                            break;
                        }
                    }
                }

            }
        }
    }
    writeFinished = true;
    long end = Stopwatch.GetTimestamp();
    long ticks = end - start;
    double speed = ((double)written) / (double)ticks;
    double ospeed = ((double)processed) / (double)ticks;
    Console.WriteLine($"[WRITE THREAD]: Processed packets: {written}, ticks: {ticks}, P\\T: {speed:0.###}, mb\\s: {((double)inputNetworkBuffer.Length / (double)ticks * 10000000.0 / 1024.0 / 1024.0):0.###}");
    Console.WriteLine($"[WRITE THREAD]: WrittenIn packets: {processed}, ticks: {ticks}, P\\T: {ospeed:0.###}, mb\\s: {((double)inputNetworkBuffer.Length / (double)ticks * 10000000.0 / 1024.0 / 1024.0):0.###}");
}

void Read()
{
    Console.WriteLine("[READ THREAD]: Started thread worker");
    long start = Stopwatch.GetTimestamp();
    long processed = 0, writtenOut = 0;
    byte[] cached = new byte[1024 * 4 * 4];
    int cachedLength = 0;
    while (!writeFinished || cBuffer.Written > 0)
    {
        uint written = cBuffer.Written;
        int amt = preGenerated[preGeneratedIndexRT];
        preGeneratedIndexRT = (++preGeneratedIndexRT) % preGeneratedLength;
        // Thread.SpinWait(amt * 10); // simulating other work
        // simulating processing received packets
        if (cachedLength > 0)
        {
            if (outBuffer.Write(new Span<byte>(cached).Slice(0, cachedLength)) == WriteState.Success)
            {
                Thread.SpinWait(cachedLength); // simulating packet processing
                cachedLength = 0;
                writtenOut++;
            }
            else
            {
                continue;
            }
        }
        if (written > 0)
        {
            for (int i = 0; i < written; ++i)
            {
                ReadState state = cBuffer.TryRead(ref buffer, out int length);
                if (state == ReadState.Success)
                {
                    Thread.SpinWait(length); // simulating packet processing
                    processed++;

                    if (writeFinished)
                        break;

                    // simulating packet out writing
                    int arrLength = preGeneratedOut[preGeneratedOutIndex];
                    preGeneratedOutIndex = (++preGeneratedOutIndex) % preGeneratedOutLength;
                    Thread.SpinWait(arrLength); // simulating packet processing

                    if (outBuffer.Write(new Span<byte>(cached).Slice(0, arrLength)) == WriteState.Success)
                    {
                        writtenOut++;
                    }
                    else
                    {
                        cachedLength = arrLength;
                        break;
                    }
                }
                else
                {
                    break;
                }
            }
        }
    }
    readFinished = true;
    long end = Stopwatch.GetTimestamp();
    long ticks = end - start;
    double speed = ((double)processed) / (double)ticks;
    double wspeed = ((double)writtenOut) / (double)ticks;
    Console.WriteLine($"[READ THREAD]: ProcessedIn packets: {processed}, ticks: {ticks}, P\\T: {speed:0.###}, mb\\s: {((double)inputNetworkBuffer.Length / (double)ticks * 10000000.0 / 1024.0 / 1024.0):0.###}");
    Console.WriteLine($"[READ THREAD]: WrittenOut packets: {writtenOut},  ticks: {ticks}, P\\T: {wspeed:0.###}, mb\\s: {((double)inputNetworkBuffer.Length / (double)ticks * 10000000.0 / 1024.0 / 1024.0):0.###}");
}

void WritePool()
{
    Console.WriteLine("[WRITE POOL THREAD]: Started thread worker");
    long start = Stopwatch.GetTimestamp();
    long written = 0, processed = 0;
    MemoryStream msIn = new MemoryStream(inputNetworkBuffer);
    using (BinaryReader reader = new BinaryReader(msIn))
    {
        // simulating receiving packets from EPoolGroup
        while (msIn.Position != msIn.Length)
        {
            // simulating receiving packets
            int amt = preGenerated[preGeneratedIndex];
            preGeneratedIndex = (++preGeneratedIndex) % preGenerated.Length;
            for (int i = 0; i < amt && msIn.Position != msIn.Length; ++i)
            {
                byte length = reader.ReadByte();
                byte[] arrRead = reader.ReadBytes(length);
                byte[] arr = ArrayPool<byte>.Shared.Rent(arrRead.Length);
                arrRead.CopyTo(arr, 0);
                cPoolBuffer.Write(arr);
                written++;

                if (readFinished)
                    break;
            }

            // simulating sending packets out
            int writtenOut = outPoolBuffer.Written;
            if (writtenOut > 0 && !readFinished)
            {
                for (int i = 0; i < writtenOut; ++i)
                {
                    byte[] buffer = null;
                    ReadState state = outPoolBuffer.TryRead(out buffer);
                    if (state == ReadState.Success)
                    {
                        // simulating out sending
                        byte[] arr2 = ArrayPool<byte>.Shared.Rent(buffer.Length);
                        buffer.CopyTo(arr2, 0);
                        ArrayPool<byte>.Shared.Return(arr2);
                        ArrayPool<byte>.Shared.Return(buffer);
                        processed++;
                    }
                }
            }
        }
    }
    writeFinished = true;
    long end = Stopwatch.GetTimestamp();
    long ticks = end - start;
    double speed = ((double)written + (double)processed) / (double)ticks;
    Console.WriteLine($"[WRITE POOL THREAD]: WrittenIn packets: {written}, ProcessedOut: {processed}, Sum: {(written + processed)}, ticks: {ticks}, P\\T: {speed:0.###}");
}

void ReadPool()
{
    Console.WriteLine("[READ POOL THREAD]: Started thread worker");
    long start = Stopwatch.GetTimestamp();
    long processed = 0, writtenOut = 0;
    while (!writeFinished)
    {
        int written = cPoolBuffer.Written;
        int amt = preGenerated[preGeneratedIndex];
        // Thread.SpinWait(amt * 10); // simulating other work
        // simulating processing received packets
        if (written > 0)
        {
            for (int i = 0; i < written; ++i)
            {
                byte[]? buffer = null;
                ReadState state = cPoolBuffer.TryRead(out buffer);
                if (state == ReadState.Success)
                {
                    // Thread.SpinWait(buffer.Length); // simulating packet processing
                    processed++;

                    if (writeFinished)
                        break;

                    // simulating packet out writing
                    byte[] arr = ArrayPool<byte>.Shared.Rent(preGeneratedOut[preGeneratedOutIndex]);
                    outPoolBuffer.Write(arr);
                    ArrayPool<byte>.Shared.Return(arr);
                    ArrayPool<byte>.Shared.Return(buffer);
                    writtenOut++;

                    preGeneratedOutIndex = (++preGeneratedOutIndex) % preGeneratedOut.Length;
                }

            }
        }
    }
    readFinished = true;
    long end = Stopwatch.GetTimestamp();
    long ticks = end - start;
    double speed = ((double)processed + (double)writtenOut) / (double)ticks;
    Console.WriteLine($"[READ POOL THREAD]: ProcessedIn packets: {processed}, WrittenOut: {writtenOut}, Sum: {(writtenOut + processed)}, ticks: {ticks}, P\\T: {speed:0.###}");
}
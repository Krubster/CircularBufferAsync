using CircularBufferAsync.Buffers;
using System.Buffers.Binary;
using System.Text;

namespace PagingBufferTests
{
    public class PagingBufferTests
    {
        private static PagingBuffer Create(int pageSize = 256, int capacity = 4)
            => new PagingBuffer(pageSize, capacity);

        [Fact]
        public void WriteThenReadSameData()
        {
            var buf = Create();
            var data = Encoding.ASCII.GetBytes("HelloWorld");
            buf.Write(data);

            Assert.True(buf.Read(out var span) == ReadState.Success);
            Assert.Equal(data, span.ToArray());
        }

        [Fact]
        public void PartialReadAndAdvanceMaintainsOrder()
        {
            var buf = Create();

            // Фрейминг: [len][payload]
            static byte[] Frame(string s)
            {
                var payload = Encoding.ASCII.GetBytes(s);
                var framed = new byte[payload.Length + 2];
                BinaryPrimitives.WriteUInt16LittleEndian(framed, (ushort)payload.Length);
                payload.CopyTo(framed.AsSpan(2));
                return framed;
            }

            var framed1 = Frame("First");
            var framed2 = Frame("Second");

            buf.Write(framed1);
            buf.Write(framed2);

            var staging = new List<byte>();

            while (staging.Count < framed1.Length + framed2.Length)
            {
                if (buf.Read(out var span) == ReadState.Success && span.Length > 0)
                {
                    staging.AddRange(span.ToArray());
                    buf.Advance(span.Length);
                }
            }

            // Распаковываем
            int offset = 0;

            ushort len1 = BinaryPrimitives.ReadUInt16LittleEndian(staging.GetRange(offset, 2).ToArray());
            offset += 2;
            var first = staging.GetRange(offset, len1).ToArray();
            offset += len1;

            ushort len2 = BinaryPrimitives.ReadUInt16LittleEndian(staging.GetRange(offset, 2).ToArray());
            offset += 2;
            var second = staging.GetRange(offset, len2).ToArray();
            offset += len2;

            Assert.Equal("First", Encoding.ASCII.GetString(first));
            Assert.Equal("Second", Encoding.ASCII.GetString(second));
        }

        [Fact]
        public void WrapAroundDoesNotCorruptData()
        {
            var buf = Create(pageSize: 32, capacity: 2);
            var rnd = new Random(42);
            var src = new byte[256];
            rnd.NextBytes(src);

            // Запишем много раз, чтобы вынудить wrap‑around
            for (int i = 0; i < 20; i++)
            {
                var slice = src.AsSpan(i * 5, 5);
                buf.Write(slice);
                Assert.True(buf.Read(out var r) == ReadState.Success);
                Assert.Equal(slice.ToArray(), r.ToArray());
                buf.Advance(slice.Length);
            }
        }

        [Fact]
        public void ConcurrentProducerConsumerIntegrityWithFraming()
        {
            const int packets = 10_000;
            var rnd = new Random(17);

            // ───---------------------------  данные  ----------------------------─
            var messages = new byte[packets][];
            for (int i = 0; i < packets; i++)
            {
                messages[i] = new byte[rnd.Next(10, 50)];
                rnd.NextBytes(messages[i]);
            }

            var buf = Create(pageSize: 128, capacity: 8);

            var produced = 0;
            var consumed = 0;
            var received = new byte[packets][];

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var token = cts.Token;

            // ───--------------------  Producer  ----------------------------─
            var writer = Task.Run(() =>
            {
                Span<byte> header = stackalloc byte[2];

                foreach (var msg in messages)
                {
                    // формируем кадр: [len][payload]
                    BinaryPrimitives.WriteUInt16LittleEndian(header, (ushort)msg.Length);

                    var frame = new byte[header.Length + msg.Length];
                    header.CopyTo(frame);
                    msg.CopyTo(frame.AsSpan(2));

                    buf.Write(frame);

                    Interlocked.Increment(ref produced);
                    Thread.SpinWait(100);
                }
            }, token);

            // ───--------------------  Consumer  ----------------------------─
            var reader = Task.Run(() =>
            {
                var staging = new List<byte>(1024);

                while (!token.IsCancellationRequested && Volatile.Read(ref consumed) < packets)
                {
                    if (buf.Read(out var page) != ReadState.Success || page.Length == 0)
                    {
                        Thread.SpinWait(50);
                        continue;
                    }

                    staging.AddRange(page.ToArray());
                    buf.Advance(page.Length);

                    // распаковываем всё, что можем
                    int offset = 0;
                    while (staging.Count - offset >= 2)
                    {
                        ushort len = BinaryPrimitives.ReadUInt16LittleEndian(
                            staging[offset..(offset + 2)].ToArray());

                        if (staging.Count - offset < 2 + len) break;  // ждём остаток кадра

                        var payload = staging.GetRange(offset + 2, len).ToArray();
                        int idx = Interlocked.Increment(ref consumed) - 1;
                        received[idx] = payload;

                        offset += 2 + len;
                    }

                    if (offset > 0)
                        staging.RemoveRange(0, offset); // discard parsed bytes
                }
            }, token);

            // ───--------------------  Проверка  ----------------------------─
            Task.WaitAll(new[] { writer, reader }, token);
            Assert.Equal(packets, produced);
            Assert.Equal(packets, consumed);

            for (int i = 0; i < packets; i++)
                Assert.Equal(messages[i], received[i]);
        }
    }
}
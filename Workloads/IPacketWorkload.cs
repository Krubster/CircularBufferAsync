using Framework.Buffers;
using Test;

namespace CircularBufferAsync
{
    public interface IPacketWorkload
    {
        /// <summary>
        /// Обрабатывает входной буфер и возвращает готовый фреймированный буфер с данными для передачи.
        /// </summary>
        /// <param name="buffer">Буфер с полными пакетами</param>
        /// <param name="senderId">ID отправителя</param>
        /// <returns>Обработанный выходной буфер</returns>
        (int, int, SpanWriter.SpanOwner?) ProcessBuffer(ReadOnlySpan<byte> buffer, NetState state);

    }
}

using Framework.Buffers;

namespace NETwork
{
    public interface IPacketProcessor
    {
        /// <summary>
        /// Обрабатывает входной буфер и возвращает готовый фреймированный буфер с данными для передачи.
        /// </summary>
        /// <param name="inBuffer">Буфер с полными пакетами</param>
        /// <param name="senderId">ID отправителя</param>
        /// <returns>Обработанный выходной буфер</returns>
        (int, List<uint>) ProcessBuffer(ReadOnlySpan<byte> inBuffer, NetState state);
    }
}

namespace CircularBufferAsync.Buffers
{
    public interface INetworkBuffer
    {
        bool Write(ReadOnlySpan<byte> packet, int length);
        bool Write(ReadOnlySpan<byte> packet);
        bool TryPeek(out ReadOnlySpan<byte> packet); // Чтение без копирования
        void Advance(int count); // Подтверждение прочтения
    }
}

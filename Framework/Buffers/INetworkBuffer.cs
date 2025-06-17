namespace NETwork.Buffers
{
    public interface INetworkBuffer
    {
        Span<byte> GetWriteSpan(int sizeHint, out Action<int> commit);
        bool Write(ReadOnlySpan<byte> packet, int length);
        bool Write(ReadOnlySpan<byte> packet);
        bool TryPeek(out ReadOnlySpan<byte> packet); // Чтение без копирования
        void Advance(int count); // Подтверждение прочтения
    }
}

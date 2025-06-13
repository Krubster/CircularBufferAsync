namespace CircularBufferAsync.Buffers.Factories
{
    public interface IBufferFactory
    {
        INetworkBuffer Create(BufferType type);
    }
}

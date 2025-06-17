namespace NETwork.Buffers.Factories
{
    public interface IBufferFactory
    {
        INetworkBuffer Create(BufferType type);
    }
}

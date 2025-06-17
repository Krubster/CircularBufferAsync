
namespace NETwork.Buffers.Factories
{
    public class DefaultBufferFactory : IBufferFactory
    {
        public INetworkBuffer Create(BufferType type)
        {
            return type switch
            {
                BufferType.Paging => new PagingNetworkBuffer(new PagingBuffer(65536, 10)),
                BufferType.Circular => throw new NotImplementedException("CircularBuffer adapter not yet added"),
                _ => throw new NotImplementedException()
            };
        }
    }
}

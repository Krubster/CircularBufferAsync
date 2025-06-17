namespace NETwork.Buffers.Factories
{
    public class PagingBufferFactory
    {
        public INetworkBuffer Create()
        {
            return new PagingNetworkBuffer(new PagingBuffer(65536, 2));
        }
    }
}

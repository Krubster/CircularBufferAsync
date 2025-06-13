namespace CircularBufferAsync.Workloads
{
    public interface IWorkloadFactory
    {
        IPacketWorkload Create(WorkloadType type, string? profilePath = null, int cpuCost = 10);
    }
}

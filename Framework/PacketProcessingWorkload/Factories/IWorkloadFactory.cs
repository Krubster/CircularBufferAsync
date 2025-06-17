namespace NETwork.Workloads
{
    public interface IWorkloadFactory
    {
        IPacketProcessor Create(WorkloadType type, string? profilePath = null, int cpuCost = 10);
    }
}

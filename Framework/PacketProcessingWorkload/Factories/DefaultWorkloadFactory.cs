using NETwork.LoadProfiles;

namespace NETwork.Workloads.Factories
{
    public class DefaultWorkloadFactory : IWorkloadFactory
    {
        public IPacketProcessor Create(WorkloadType type, string? profilePath = null, int cpuCost = 10)
        {
            return type switch
            {
                WorkloadType.Cpu => new CpuIntensiveProcessor(cpuCost),
                _ => throw new NotImplementedException()
            };
        }
    }
}

using CircularBufferAsync.LoadProfiles;

namespace CircularBufferAsync.Workloads.Factories
{
    public class DefaultWorkloadFactory : IWorkloadFactory
    {
        public IPacketWorkload Create(WorkloadType type, string? profilePath = null, int cpuCost = 10)
        {
            return type switch
            {
                WorkloadType.Echo => new EchoWorkload(),
                WorkloadType.Cpu => new CpuIntensiveWorkload(cpuCost),
                WorkloadType.Replay => new ReplayDrivenWorkload(new HistogramReplayProfile(profilePath!), cpuCost),
                _ => throw new NotImplementedException()
            };
        }
    }
}

namespace NETwork.LoadProfiles
{
    public interface IReplayProfile
    {
        bool TryGetNext(out int size);
    }
}

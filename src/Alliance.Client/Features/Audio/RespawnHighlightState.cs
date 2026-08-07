namespace Alliance.Client.Features.Audio;

public sealed class RespawnHighlightState
{
    private readonly Dictionary<string, DateTime> _expiryBySlot = new();

    public void MarkRespawn(string slotLabel, DateTime now, TimeSpan duration)
    {
        _expiryBySlot[slotLabel] = now + duration;
    }

    public bool IsHighlighted(string slotLabel, DateTime now)
    {
        return _expiryBySlot.TryGetValue(slotLabel, out var expiry) && now < expiry;
    }
}

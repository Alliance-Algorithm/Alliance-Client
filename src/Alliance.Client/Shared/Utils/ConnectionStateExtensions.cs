using Alliance.Client.Shared.Models;

namespace Alliance.Client.Shared.Utils;

public static class ConnectionStateExtensions
{
    public static string ToDisplayText(this ConnectionState state)
    {
        return state switch
        {
            ConnectionState.NotConnected => "Not Connected",
            ConnectionState.Connecting => "Connecting",
            ConnectionState.Ready => "Ready",
            ConnectionState.Degraded => "Degraded",
            _ => "Unknown"
        };
    }
}

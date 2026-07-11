using System.Text.Json;

namespace Alliance.Video.Common;

public static class WorkerProtocol
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };
}

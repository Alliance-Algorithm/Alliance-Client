using Alliance.Video.Common;
using Alliance.VideoWorker;
using Microsoft.Extensions.Logging;

var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.ClearProviders();
    builder.AddSimpleConsole(options =>
    {
        options.SingleLine = true;
        options.TimestampFormat = "HH:mm:ss ";
    });
    builder.SetMinimumLevel(LogLevel.Information);
});

var logger = loggerFactory.CreateLogger("Alliance.VideoWorker");

try
{
    if (args.Length != 1)
    {
        logger.LogError("Expected a single base64-encoded worker control message argument.");
        return 2;
    }

    var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(args[0]));
    var message = System.Text.Json.JsonSerializer.Deserialize<VideoControlMessage>(json, WorkerProtocol.JsonOptions);
    if (message is null)
    {
        logger.LogError("Failed to deserialize worker control message.");
        return 2;
    }

    using var runtime = new WorkerRuntime(message, loggerFactory.CreateLogger<WorkerRuntime>());
    await runtime.RunAsync();
    return 0;
}
catch (OperationCanceledException)
{
    return 0;
}
catch (Exception ex)
{
    logger.LogError(ex, "Alliance.VideoWorker terminated unexpectedly.");
    return 1;
}

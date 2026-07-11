using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace Alliance.VideoWorker;

internal static class ParentDeathGuard
{
    private const int PR_SET_PDEATHSIG = 1;
    private const int SIGTERM = 15;

    [DllImport("libc", SetLastError = true)]
    private static extern int prctl(int option, ulong arg2, ulong arg3, ulong arg4, ulong arg5);

    [DllImport("libc", SetLastError = true)]
    private static extern int getppid();

    public static void Enable(ILogger logger)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return;
        }

        try
        {
            var result = prctl(PR_SET_PDEATHSIG, SIGTERM, 0, 0, 0);
            if (result != 0)
            {
                logger.LogWarning("Failed to set parent-death signal (prctl returned {Result}).", result);
                return;
            }

            if (getppid() == 1)
            {
                logger.LogWarning("Parent process already exited before worker startup; terminating.");
                Environment.Exit(0);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Unable to install parent-death guard; worker may outlive its parent.");
        }
    }
}

using System.Reflection;
using System.Runtime.InteropServices;
using Alliance.Client.Features.Video;
using Alliance.VideoWorker;
using Alliance.Video.Common;

namespace Alliance.Client.Tests;

public sealed class VideoInfrastructureTests
{
    [Fact]
    public void SharedFrameLayout_Computes_Expected_Buffer_Sizes()
    {
        var layout = new SharedFrameLayout(1920, 1080);

        Assert.Equal(7680, layout.Stride);
        Assert.Equal(8_294_400, layout.FrameBytes);
        Assert.Equal(VideoConstants.FrameHeaderSize + layout.FrameBytes, layout.SlotSize);
        Assert.Equal(VideoConstants.SharedHeaderSize + (layout.SlotSize * VideoConstants.SharedBufferSlots), layout.TotalBytes);
    }

    [Theory]
    [InlineData(0, 1080, "width")]
    [InlineData(-1, 1080, "width")]
    [InlineData(1920, 0, "height")]
    [InlineData(1920, -1, "height")]
    public void SharedFrameLayout_Rejects_NonPositive_Dimensions(int width, int height, string paramName)
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => new SharedFrameLayout(width, height));

        Assert.Equal(paramName, ex.ParamName);
    }

    [Fact]
    public void SharedFrameLayout_Throws_On_Overflowing_Dimensions()
    {
        Assert.Throws<OverflowException>(() => new SharedFrameLayout(int.MaxValue / 2, 2));
    }

    [Fact]
    public void SharedFrameReader_Reads_Frame_Info_Before_Frame_Data()
    {
        var tempDirectory = CreateTempDirectory();
        var sharedMemoryPath = Path.Combine(tempDirectory, "frame.mmap");
        var layout = new SharedFrameLayout(2, 2);
        var frameBytes = new byte[layout.FrameBytes];
        for (var index = 0; index < frameBytes.Length; index++)
        {
            frameBytes[index] = (byte)index;
        }

        try
        {
            using (var stream = new FileStream(sharedMemoryPath, FileMode.Create, FileAccess.ReadWrite, FileShare.ReadWrite))
            {
                stream.SetLength(layout.TotalBytes);

                var sharedHeader = new byte[VideoConstants.SharedHeaderSize];
                SharedFrameLayout.WriteSharedHeader(sharedHeader, layout.Width, layout.Height, layout.Stride);
                stream.Position = 0;
                stream.Write(sharedHeader);

                var slotHeader = new byte[VideoConstants.FrameHeaderSize];
                SharedFrameLayout.WriteSlotHeader(
                    slotHeader,
                    version: 2,
                    frameNumber: 1,
                    width: layout.Width,
                    height: layout.Height,
                    stride: layout.Stride,
                    frameBytes: layout.FrameBytes,
                    timestampUnixMs: 1234,
                    stable: true);
                stream.Position = layout.GetSlotOffset(0);
                stream.Write(slotHeader);
                stream.Position = layout.GetFrameDataOffset(0);
                stream.Write(frameBytes);
            }

            using var reader = new SharedFrameReader(sharedMemoryPath, layout.Width, layout.Height);

            Assert.True(reader.TryGetLatestFrameInfo(out var info));
            Assert.Equal(2, info.Version);
            Assert.Equal(1, info.FrameNumber);
            Assert.Equal(layout.FrameBytes, info.FrameBytes);
            Assert.True(reader.TryReadFrame(info, out var frame, out var version, out var timestampUnixMs));
            Assert.Equal(2, version);
            Assert.Equal(1234, timestampUnixMs);
            Assert.Equal(frameBytes, frame.ToArray());
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void VideoFeedControl_Overrides_Attach_And_Detach_Handlers()
    {
        var type = typeof(VideoFeedControl);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        Assert.NotNull(type.GetMethod("OnAttachedToVisualTree", flags));
        Assert.NotNull(type.GetMethod("OnDetachedFromVisualTree", flags));
    }

    [Fact]
    public void VideoSupervisorService_Prefers_Packaged_Worker_Executable()
    {
        var tempDirectory = CreateTempDirectory();
        try
        {
            var payload = "payload";
            var packagedDirectory = Path.Combine(tempDirectory, "worker");
            Directory.CreateDirectory(packagedDirectory);

            var executablePath = Path.Combine(packagedDirectory, GetWorkerExecutableName());
            File.WriteAllText(executablePath, string.Empty);
            File.WriteAllText(Path.Combine(tempDirectory, "Alliance.VideoWorker.dll"), string.Empty);

            var launchInfo = VideoSupervisorService.ResolveWorkerLaunchInfo(tempDirectory, payload);

            Assert.Equal(executablePath, launchInfo.FileName);
            Assert.Equal(payload, launchInfo.Arguments);
            Assert.Equal(tempDirectory, launchInfo.WorkingDirectory);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void VideoSupervisorService_Falls_Back_To_Dotnet_For_Worker_Dll()
    {
        var tempDirectory = CreateTempDirectory();
        try
        {
            var workerDllPath = Path.Combine(tempDirectory, "Alliance.VideoWorker.dll");
            File.WriteAllText(workerDllPath, string.Empty);

            var launchInfo = VideoSupervisorService.ResolveWorkerLaunchInfo(tempDirectory, "payload");

            Assert.Equal("dotnet", launchInfo.FileName);
            Assert.Equal($"\"{workerDllPath}\" payload", launchInfo.Arguments);
            Assert.Equal(tempDirectory, launchInfo.WorkingDirectory);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void VideoSupervisorService_Finds_Worker_In_Repo_Bin_Root()
    {
        var repoRoot = CreateTempDirectory();
        try
        {
            File.WriteAllText(Path.Combine(repoRoot, "Alliance.sln"), string.Empty);

            var appBaseDirectory = Path.Combine(repoRoot, "src", "Alliance.Client", "bin", "Debug", "net10.0");
            Directory.CreateDirectory(appBaseDirectory);

            var workerDirectory = Path.Combine(repoRoot, "src", "Alliance.VideoWorker", "bin", "Debug", "net10.0");
            Directory.CreateDirectory(workerDirectory);

            var workerDllPath = Path.Combine(workerDirectory, "Alliance.VideoWorker.dll");
            File.WriteAllText(workerDllPath, string.Empty);

            var launchInfo = VideoSupervisorService.ResolveWorkerLaunchInfo(appBaseDirectory, "payload");

            Assert.Equal("dotnet", launchInfo.FileName);
            Assert.Equal($"\"{workerDllPath}\" payload", launchInfo.Arguments);
        }
        finally
        {
            Directory.Delete(repoRoot, recursive: true);
        }
    }

    [Fact]
    public void VideoSupervisorService_Prefers_Current_Rid_Over_Other_Repo_Runtime_Directories()
    {
        var repoRoot = CreateTempDirectory();
        try
        {
            File.WriteAllText(Path.Combine(repoRoot, "Alliance.sln"), string.Empty);

            var appBaseDirectory = Path.Combine(repoRoot, "src", "Alliance.Client", "bin", "Release", "net10.0");
            Directory.CreateDirectory(appBaseDirectory);

            var workerBinRoot = Path.Combine(repoRoot, "src", "Alliance.VideoWorker", "bin", "Debug", "net10.0");
            Directory.CreateDirectory(workerBinRoot);

            var currentRid = GetCurrentRid();
            if (currentRid is null)
            {
                return;
            }

            var otherRid = currentRid == "linux-x64" ? "linux-arm64" : "linux-x64";

            var otherRidDirectory = Path.Combine(workerBinRoot, otherRid);
            Directory.CreateDirectory(otherRidDirectory);
            File.WriteAllText(Path.Combine(otherRidDirectory, GetWorkerExecutableNameForRid(otherRid)), string.Empty);

            var currentRidDirectory = Path.Combine(workerBinRoot, currentRid);
            Directory.CreateDirectory(currentRidDirectory);
            var currentExecutablePath = Path.Combine(currentRidDirectory, GetWorkerExecutableName());
            File.WriteAllText(currentExecutablePath, string.Empty);

            var launchInfo = VideoSupervisorService.ResolveWorkerLaunchInfo(appBaseDirectory, "payload");

            Assert.Equal(currentExecutablePath, launchInfo.FileName);
            Assert.Equal("payload", launchInfo.Arguments);
        }
        finally
        {
            Directory.Delete(repoRoot, recursive: true);
        }
    }

    [Fact]
    public void FfmpegLoader_Prefers_App_Local_Bundle_Directory()
    {
        var tempDirectory = CreateTempDirectory();
        var originalOverride = Environment.GetEnvironmentVariable("ALLIANCE_FFMPEG_ROOT");

        try
        {
            var bundledDirectory = Path.Combine(tempDirectory, "ffmpeg");
            Directory.CreateDirectory(bundledDirectory);
            File.WriteAllText(Path.Combine(bundledDirectory, "libavcodec.so.62"), string.Empty);

            var overrideDirectory = Path.Combine(tempDirectory, "override");
            Directory.CreateDirectory(overrideDirectory);
            File.WriteAllText(Path.Combine(overrideDirectory, "libavcodec.so.62"), string.Empty);
            Environment.SetEnvironmentVariable("ALLIANCE_FFMPEG_ROOT", overrideDirectory);

            var resolved = FfmpegLoader.ResolveFfmpegRoot(tempDirectory);

            Assert.Equal(bundledDirectory, resolved);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ALLIANCE_FFMPEG_ROOT", originalOverride);
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"alliance-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string GetWorkerExecutableName()
    {
        return OperatingSystem.IsWindows() ? "Alliance.VideoWorker.exe" : "Alliance.VideoWorker";
    }

    private static string? GetCurrentRid()
    {
        if (OperatingSystem.IsLinux())
        {
            return RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => "linux-x64",
                Architecture.Arm64 => "linux-arm64",
                _ => null
            };
        }

        if (OperatingSystem.IsWindows())
        {
            return RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => "win-x64",
                Architecture.Arm64 => "win-arm64",
                _ => null
            };
        }

        if (OperatingSystem.IsMacOS())
        {
            return RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => "osx-x64",
                Architecture.Arm64 => "osx-arm64",
                _ => null
            };
        }

        return null;
    }

    private static string GetWorkerExecutableNameForRid(string rid)
    {
        return rid.StartsWith("win-", StringComparison.Ordinal) ? "Alliance.VideoWorker.exe" : "Alliance.VideoWorker";
    }
}

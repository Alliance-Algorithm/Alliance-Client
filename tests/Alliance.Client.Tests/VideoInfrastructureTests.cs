using System.Reflection;
using Alliance.Client.Features.Video;
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
    public void VideoFeedControl_Overrides_Attach_And_Detach_Handlers()
    {
        var type = typeof(VideoFeedControl);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        Assert.NotNull(type.GetMethod("OnAttachedToVisualTree", flags));
        Assert.NotNull(type.GetMethod("OnDetachedFromVisualTree", flags));
    }
}

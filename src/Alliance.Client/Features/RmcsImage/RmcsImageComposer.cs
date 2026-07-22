using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;

namespace Alliance.Client.Features.RmcsImage;

public static class RmcsImageComposer
{
    public static unsafe WriteableBitmap Compose(byte[] bgJpeg, byte[] trajJpeg)
    {
        using var bgBmp = SKBitmap.Decode(bgJpeg);
        using var trajBmp = SKBitmap.Decode(trajJpeg);

        int width = bgBmp.Width;
        int height = bgBmp.Height;

        var result = new WriteableBitmap(
            new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);

        using (var locked = result.Lock())
        {
            var dst = (uint*)locked.Address;
            var bg = (uint*)bgBmp.GetPixels();
            var traj = (uint*)trajBmp.GetPixels();

            int bgStride = bgBmp.RowBytes >> 2;
            int trajStride = trajBmp.RowBytes >> 2;
            int dstStride = locked.RowBytes >> 2;

            for (int y = 0; y < height; y++)
            {
                int offset = y * dstStride;
                int bgOffset = y * bgStride;
                int trajOffset = y * trajStride;

                for (int x = 0; x < width; x++)
                {
                    uint t = traj[trajOffset + x];
                    uint b = bg[bgOffset + x];

                    byte rr = (byte)Math.Min(((b >> 16) & 0xFF) + ((t >> 16) & 0xFF), 255);
                    byte gg = (byte)Math.Min(((b >> 8) & 0xFF)  + ((t >> 8) & 0xFF),  255);
                    byte bb = (byte)Math.Min((b & 0xFF)         + (t & 0xFF),         255);

                    dst[offset + x] = 0xFF000000 | (uint)(rr << 16) | (uint)(gg << 8) | bb;
                }
            }
        }

        return result;
    }
}

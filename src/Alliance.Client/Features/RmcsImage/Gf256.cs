namespace Alliance.Client.Features.RmcsImage;

public static class Gf256
{
    private static readonly byte[] ExpTable = new byte[512];
    private static readonly byte[] LogTable = new byte[256];

    static Gf256()
    {
        uint value = 1;
        for (int i = 0; i < 255; i++)
        {
            ExpTable[i] = (byte)value;
            LogTable[value] = (byte)i;
            value <<= 1;
            if (value >= 256)
                value ^= 0x11D;
        }

        for (int i = 255; i < 512; i++)
            ExpTable[i] = ExpTable[i - 255];
    }

    public static byte Add(byte a, byte b) => (byte)(a ^ b);

    public static byte Sub(byte a, byte b) => (byte)(a ^ b);

    public static byte Mul(byte a, byte b)
    {
        if (a == 0 || b == 0) return 0;
        return ExpTable[LogTable[a] + LogTable[b]];
    }

    public static byte Inv(byte a)
    {
        return ExpTable[255 - LogTable[a]];
    }
}

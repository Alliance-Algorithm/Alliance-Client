namespace Alliance.Client.Features.RmcsImage;

public static class CauchyRsDecoder
{
    public static byte[][] Decode(int kg, int r, byte[][] survivingPayloads, int[] survivingIndices)
    {
        if (survivingPayloads.Length != kg || survivingIndices.Length != kg)
            throw new ArgumentException($"Expected {kg} surviving payloads, got {survivingPayloads.Length}");

        var matrix = BuildSubmatrix(kg, r, survivingIndices);
        var inverse = InvertMatrix(kg, matrix);

        var result = new byte[kg][];
        for (int row = 0; row < kg; row++)
        {
            result[row] = new byte[296];
            for (int col = 0; col < 296; col++)
            {
                byte sum = 0;
                for (int k = 0; k < kg; k++)
                {
                    sum = Gf256.Add(sum, Gf256.Mul(inverse[row, k], survivingPayloads[k][col]));
                }
                result[row][col] = sum;
            }
        }

        return result;
    }

    private static byte[,] BuildSubmatrix(int kg, int r, int[] survivingIndices)
    {
        var m = new byte[kg, kg];
        for (int row = 0; row < kg; row++)
        {
            int idx = survivingIndices[row];
            if (idx < kg)
            {
                m[row, idx] = 1;
            }
            else
            {
                int fecIdx = idx - kg;
                for (int col = 0; col < kg; col++)
                {
                    byte x = (byte)(kg + fecIdx);
                    byte y = (byte)col;
                    m[row, col] = Gf256.Inv((byte)(x ^ y));
                }
            }
        }

        return m;
    }

    private static byte[,] InvertMatrix(int n, byte[,] matrix)
    {
        var aug = new byte[n, n * 2];
        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < n; c++)
                aug[r, c] = matrix[r, c];
            aug[r, n + r] = 1;
        }

        for (int col = 0; col < n; col++)
        {
            int pivot = FindPivot(aug, n, col);
            if (pivot < 0)
                throw new InvalidOperationException("Matrix is singular in GF(256)");

            SwapRows(aug, n, col, pivot);

            byte invPivot = Gf256.Inv(aug[col, col]);
            for (int c = 0; c < n * 2; c++)
                aug[col, c] = Gf256.Mul(aug[col, c], invPivot);

            for (int r = 0; r < n; r++)
            {
                if (r == col) continue;
                byte factor = aug[r, col];
                if (factor == 0) continue;
                for (int c = 0; c < n * 2; c++)
                    aug[r, c] = Gf256.Add(aug[r, c], Gf256.Mul(factor, aug[col, c]));
            }
        }

        var inverse = new byte[n, n];
        for (int r = 0; r < n; r++)
            for (int c = 0; c < n; c++)
                inverse[r, c] = aug[r, n + c];

        return inverse;
    }

    private static int FindPivot(byte[,] aug, int n, int col)
    {
        for (int r = col; r < n; r++)
        {
            if (aug[r, col] != 0)
                return r;
        }

        return -1;
    }

    private static void SwapRows(byte[,] aug, int n, int r1, int r2)
    {
        if (r1 == r2) return;
        for (int c = 0; c < n * 2; c++)
            (aug[r1, c], aug[r2, c]) = (aug[r2, c], aug[r1, c]);
    }
}

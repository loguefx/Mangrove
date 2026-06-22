using System.Globalization;

namespace Mangrove.Server.Readers;

/// <summary>
/// Compares strings the way humans expect page filenames to sort: "page2" before "page10".
/// </summary>
public sealed class NaturalComparer : IComparer<string>
{
    public static readonly NaturalComparer Instance = new();

    public int Compare(string? x, string? y)
    {
        if (x is null) return y is null ? 0 : -1;
        if (y is null) return 1;

        int ix = 0, iy = 0;
        while (ix < x.Length && iy < y.Length)
        {
            if (char.IsDigit(x[ix]) && char.IsDigit(y[iy]))
            {
                int sx = ix, sy = iy;
                while (ix < x.Length && char.IsDigit(x[ix])) ix++;
                while (iy < y.Length && char.IsDigit(y[iy])) iy++;

                var numX = x.Substring(sx, ix - sx).TrimStart('0');
                var numY = y.Substring(sy, iy - sy).TrimStart('0');
                if (numX.Length != numY.Length) return numX.Length - numY.Length;
                var cmp = string.CompareOrdinal(numX, numY);
                if (cmp != 0) return cmp;
            }
            else
            {
                var cmp = char.ToLower(x[ix], CultureInfo.InvariantCulture)
                    .CompareTo(char.ToLower(y[iy], CultureInfo.InvariantCulture));
                if (cmp != 0) return cmp;
                ix++;
                iy++;
            }
        }
        return (x.Length - ix) - (y.Length - iy);
    }
}

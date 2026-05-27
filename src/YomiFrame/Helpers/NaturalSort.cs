using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace YomiFrame.Helpers;

/// <summary>
/// A natural sort comparer that matches Windows Explorer's sorting order exactly.
/// </summary>
public sealed class NaturalSortComparer : IComparer<string>
{
    public static readonly NaturalSortComparer Instance = new();

    private NaturalSortComparer() { }

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int StrCmpLogicalW(string psz1, string psz2);

    /// <inheritdoc />
    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;

        return StrCmpLogicalW(x, y);
    }
}

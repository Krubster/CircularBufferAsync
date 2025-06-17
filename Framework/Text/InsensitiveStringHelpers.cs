﻿using System.Runtime.CompilerServices;

namespace Framework.Text;

public static class InsensitiveStringHelpers
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int InsensitiveCompare(this ReadOnlySpan<char> a, ReadOnlySpan<char> b) =>
        a.CompareTo(b, StringComparison.OrdinalIgnoreCase);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int InsensitiveCompare(this string a, string b) => string.Compare(a, b, StringComparison.OrdinalIgnoreCase);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool InsensitiveEquals(this ReadOnlySpan<char> a, ReadOnlySpan<char> b) =>
        a.Equals(b, StringComparison.OrdinalIgnoreCase);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool InsensitiveEquals(this string a, ReadOnlySpan<char> b) =>
        a?.AsSpan().Equals(b, StringComparison.OrdinalIgnoreCase) ?? false;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool InsensitiveEquals(this string a, string b) =>
        a?.Equals(b, StringComparison.OrdinalIgnoreCase) ?? b == null;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool InsensitiveStartsWith(this ReadOnlySpan<char> a, ReadOnlySpan<char> b) =>
        a.StartsWith(b, StringComparison.OrdinalIgnoreCase);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool InsensitiveStartsWith(this string a, string b) =>
        a?.StartsWith(b, StringComparison.OrdinalIgnoreCase) == true;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool InsensitiveEndsWith(this string a, string b) =>
        a?.EndsWith(b, StringComparison.OrdinalIgnoreCase) == true;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool InsensitiveEndsWith(this ReadOnlySpan<char> a, ReadOnlySpan<char> b) =>
        a.EndsWith(b, StringComparison.OrdinalIgnoreCase);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool InsensitiveContains(this ReadOnlySpan<char> a, string b) =>
        a.Contains(b, StringComparison.OrdinalIgnoreCase);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool InsensitiveContains(this string a, string b) =>
        a?.Contains(b, StringComparison.OrdinalIgnoreCase) == true;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool InsensitiveContains(this ReadOnlySpan<char> a, ReadOnlySpan<char> b) =>
        a.Contains(b, StringComparison.OrdinalIgnoreCase);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool InsensitiveContains(this string a, char b) =>
        a?.Contains(b, StringComparison.OrdinalIgnoreCase) == true;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int InsensitiveIndexOf(this string a, char b) =>
        a?.IndexOf(b, StringComparison.OrdinalIgnoreCase) ?? -1;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int InsensitiveIndexOf(this string a, string b) =>
        a?.IndexOf(b, StringComparison.OrdinalIgnoreCase) ?? -1;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int InsensitiveIndexOf(this string a, string b, int startIndex) =>
        a?.IndexOf(b, startIndex, StringComparison.OrdinalIgnoreCase) ?? -1;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int InsensitiveIndexOf(this ReadOnlySpan<char> a, ReadOnlySpan<char> b) =>
        a.IndexOf(b, StringComparison.OrdinalIgnoreCase);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string InsensitiveReplace(this string a, string o, string n) =>
        a?.Replace(o, n, StringComparison.OrdinalIgnoreCase);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void InsensitiveRemove(
        this ReadOnlySpan<char> a,
        ReadOnlySpan<char> b,
        Span<char> buffer,
        out int size
    ) => a.Remove(b, StringComparison.OrdinalIgnoreCase, buffer, out size);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string InsensitiveRemove(this ReadOnlySpan<char> a, ReadOnlySpan<char> b) =>
        a.Remove(b, StringComparison.OrdinalIgnoreCase);
}
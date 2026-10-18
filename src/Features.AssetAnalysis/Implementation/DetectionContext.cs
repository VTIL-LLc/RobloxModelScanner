using System;

namespace RobloxModelScanner.Features.AssetAnalysis;

/// <summary>
/// Wraps an asset payload and exposes fast, case-insensitive ASCII searching.
/// </summary>
/// <remarks>
/// <para>
/// The payload is lower-cased exactly once, when the context is created. Every rule
/// therefore performs a plain byte search against an already-normalized buffer rather
/// than lower-casing per byte per needle, which is what made the previous
/// <c>BytePattern</c> helper scale poorly on multi-megabyte model files.
/// </para>
/// <para>
/// All helpers are allocation free and use <see cref="MemoryExtensions.IndexOf{T}(ReadOnlySpan{T},ReadOnlySpan{T})"/>,
/// which is vectorized by the runtime. Non-ASCII bytes are preserved unchanged; they
/// simply never match an ASCII needle.
/// </para>
/// <para>
/// The type is a <see langword="readonly struct"/> so evaluating a rule never
/// allocates, and it is immutable once constructed so it is safe to share across the
/// analyzer's worker threads.
/// </para>
/// </remarks>
public readonly struct DetectionContext
{
    /// <summary>
    /// Maximum needle length that can be normalized on the stack. Longer needles fall
    /// back to a heap buffer; the limit only bounds the stack allocation.
    /// </summary>
    private const int StackBufferLimit = 512;

    private readonly byte[]? _payload;
    private readonly byte[]? _lowercased;

    /// <summary>
    /// Initializes a new instance of the <see cref="DetectionContext"/> structure for a payload.
    /// </summary>
    /// <param name="payload">The raw asset bytes. A null value is treated as empty.</param>
    public DetectionContext(byte[]? payload)
    {
        _payload = payload ?? Array.Empty<byte>();
        _lowercased = new byte[_payload.Length];
        for (int index = 0; index < _payload.Length; index++)
        {
            byte value = _payload[index];
            _lowercased[index] = (value is >= (byte)'A' and <= (byte)'Z') ? (byte)(value + 32) : value;
        }
    }

    /// <summary>
    /// Gets the number of bytes in the payload.
    /// </summary>
    public int Length => _lowercased?.Length ?? 0;

    /// <summary>
    /// Gets the original, unmodified payload bytes.
    /// </summary>
    public ReadOnlySpan<byte> Payload => _payload.AsSpan();

    /// <summary>
    /// Determines whether the payload contains the supplied substring, ignoring ASCII case.
    /// </summary>
    /// <param name="substring">The ASCII text to search for.</param>
    /// <returns><see langword="true"/> when the substring occurs at least once.</returns>
    public bool Contains(string substring) => IndexOf(substring, 0) >= 0;

    /// <summary>
    /// Determines whether the payload contains any of the supplied substrings.
    /// </summary>
    /// <param name="substrings">The ASCII texts to search for.</param>
    /// <returns><see langword="true"/> when at least one substring occurs.</returns>
    public bool ContainsAny(params string[] substrings)
    {
        if (substrings is null) return false;
        foreach (string substring in substrings)
            if (Contains(substring))
                return true;
        return false;
    }

    /// <summary>
    /// Determines whether the payload contains every one of the supplied substrings.
    /// </summary>
    /// <param name="substrings">The ASCII texts that must all be present.</param>
    /// <returns><see langword="true"/> when all substrings occur.</returns>
    /// <remarks>
    /// Requiring several independent markers is how the heuristic rules keep their
    /// false-positive rate down.
    /// </remarks>
    public bool ContainsAll(params string[] substrings)
    {
        if (substrings is null || substrings.Length == 0) return false;
        foreach (string substring in substrings)
            if (!Contains(substring))
                return false;
        return true;
    }

    /// <summary>
    /// Determines whether the payload contains the supplied identifier as a whole word.
    /// </summary>
    /// <param name="word">The ASCII identifier to search for.</param>
    /// <returns><see langword="true"/> when the word appears delimited by non-identifier bytes.</returns>
    /// <remarks>
    /// A word boundary is any byte that is not <c>A-Z</c>, <c>a-z</c>, <c>0-9</c> or <c>_</c>.
    /// This is what allows a rule to match <c>GetAsync</c> without also matching
    /// <c>GetAsynchronous</c>.
    /// </remarks>
    public bool ContainsWord(string word) => IndexOfWord(word, 0) >= 0;

    /// <summary>
    /// Determines whether the payload contains any of the supplied identifiers as a whole word.
    /// </summary>
    /// <param name="words">The ASCII identifiers to search for.</param>
    /// <returns><see langword="true"/> when at least one word occurs.</returns>
    public bool ContainsAnyWord(params string[] words)
    {
        if (words is null) return false;
        foreach (string word in words)
            if (ContainsWord(word))
                return true;
        return false;
    }

    /// <summary>
    /// Counts the number of non-overlapping occurrences of the supplied substring.
    /// </summary>
    /// <param name="substring">The ASCII text to count.</param>
    /// <returns>The number of occurrences, or zero when the needle is absent.</returns>
    public int Count(string substring)
    {
        int count = 0;
        int start = 0;
        while (true)
        {
            int index = IndexOf(substring, start);
            if (index < 0) return count;
            count++;
            start = index + Math.Max(1, substring.Length);
        }
    }

    /// <summary>
    /// Determines whether a byte value is part of an ASCII identifier.
    /// </summary>
    /// <param name="value">The byte to test.</param>
    /// <returns><see langword="true"/> for <c>A-Z</c>, <c>a-z</c>, <c>0-9</c> and <c>_</c>.</returns>
    private static bool IsIdentifierByte(byte value) =>
        value is >= (byte)'a' and <= (byte)'z' or
        >= (byte)'0' and <= (byte)'9' or (byte)'_';

    /// <summary>
    /// Finds the next case-insensitive occurrence of a substring.
    /// </summary>
    /// <param name="substring">The ASCII text to search for.</param>
    /// <param name="start">The index at which to start searching.</param>
    /// <returns>The absolute index of the match, or <c>-1</c> when absent.</returns>
    private int IndexOf(string substring, int start)
    {
        byte[]? haystack = _lowercased;
        if (haystack is null || substring is null || substring.Length == 0 || start >= haystack.Length)
            return -1;

        Span<byte> needle = substring.Length <= StackBufferLimit
            ? stackalloc byte[substring.Length]
            : new byte[substring.Length];
        if (!TryNormalize(substring, needle)) return -1;

        if (start < 0) start = 0;
        int relative = haystack.AsSpan(start).IndexOf(needle);
        return relative < 0 ? -1 : start + relative;
    }

    /// <summary>
    /// Finds the next occurrence of an identifier bounded by non-identifier bytes.
    /// </summary>
    /// <param name="word">The ASCII identifier to search for.</param>
    /// <param name="start">The index at which to start searching.</param>
    /// <returns>The absolute index of the match, or <c>-1</c> when absent.</returns>
    private int IndexOfWord(string word, int start)
    {
        byte[]? haystack = _lowercased;
        if (haystack is null) return -1;

        int searchFrom = start;
        while (true)
        {
            int index = IndexOf(word, searchFrom);
            if (index < 0) return -1;

            int after = index + word.Length;
            bool openBoundary = index == 0 || !IsIdentifierByte(haystack[index - 1]);
            bool closeBoundary = after >= haystack.Length || !IsIdentifierByte(haystack[after]);
            if (openBoundary && closeBoundary) return index;

            searchFrom = index + 1;
        }
    }

    /// <summary>
    /// Copies an ASCII string into a destination buffer after lower-casing it.
    /// </summary>
    /// <param name="value">The string to normalize.</param>
    /// <param name="destination">The buffer that receives the lower-cased bytes.</param>
    /// <returns><see langword="true"/> when the whole string is ASCII; otherwise <see langword="false"/>.</returns>
    private static bool TryNormalize(string value, Span<byte> destination)
    {
        for (int index = 0; index < value.Length; index++)
        {
            char character = value[index];
            if (character > 0x7F) return false;
            destination[index] = character is >= 'A' and <= 'Z' ? (byte)(character + 32) : (byte)character;
        }
        return true;
    }
}

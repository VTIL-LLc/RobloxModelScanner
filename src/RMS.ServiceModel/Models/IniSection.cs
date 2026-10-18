using System.Globalization;
using System.Collections.ObjectModel;

namespace RMS.ServiceModel.Models;

/// <summary>
/// A named group of <see cref="IniEntry"/> values.
/// </summary>
/// <remarks>
/// <para>
/// Keys are compared without regard to case. That is deliberate: the plans this type carries are generated on
/// Windows, where a key that differs only in case is the same key to every tool that will read it, and
/// treating <c>Name</c> and <c>name</c> as different would let a deployment produce a service with one of them
/// silently ignored.
/// </para>
/// <para>
/// Insertion order is preserved so that a document can be written back out in the order it was read, which
/// keeps a regenerated file diffable against the one it replaced.
/// </para>
/// </remarks>
public sealed class IniSection
{
    private readonly List<IniEntry> _entries = [];

    private readonly Dictionary<string, int> _indexByName = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets the section name, or an empty string for the unnamed leading section.</summary>
    public string Name { get; }

    /// <summary>Gets every entry, in the order it appeared in the source text.</summary>
    public IReadOnlyList<IniEntry> Entries => _entries;

    /// <summary>Gets the number of entries in the section.</summary>
    public int Count => _entries.Count;

    /// <summary>
    /// Initializes a new instance of the <see cref="IniSection"/> class.
    /// </summary>
    /// <param name="name">The section name, or an empty string for content appearing before any header.</param>
    public IniSection(string name)
    {
        Name = name;
    }

    /// <summary>
    /// Adds an entry, replacing any entry already present under the same key.
    /// </summary>
    /// <param name="key">The key. An empty or blank key is rejected.</param>
    /// <param name="value">The value. May be empty.</param>
    /// <param name="lineNumber">The one-based source line, for diagnostics.</param>
    /// <exception cref="ArgumentException">Thrown if <paramref name="key"/> is blank.</exception>
    public void Add(string key, string value, int lineNumber = 0)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("An INI key may not be blank.", nameof(key));

        if (_indexByName.TryGetValue(key, out int existing))
        {
            _entries[existing] = new IniEntry(key, value, lineNumber);
            return;
        }

        _indexByName[key] = _entries.Count;
        _entries.Add(new IniEntry(key, value, lineNumber));
    }

    /// <summary>
    /// Gets a value, or an empty string when the key is absent.
    /// </summary>
    /// <param name="key">The key to look up.</param>
    /// <returns>The trimmed value, or an empty string.</returns>
    /// <remarks>
    /// An absent key and a key set to nothing both read back as an empty string. This format carries no value
    /// whose meaning depends on which of those it was -- the deployer writes a key explicitly when it wants
    /// the reader to know the field was considered -- and collapsing the two removes a null check from every
    /// caller, which is where a missed check would otherwise become a crash on an incomplete plan. Use
    /// <see cref="ContainsKey"/> when the distinction matters.
    /// </remarks>
    public string Get(string key)
    {
        return _indexByName.TryGetValue(key, out int index) ? _entries[index].Value : string.Empty;
    }

    /// <summary>
    /// Gets a value, or a caller-supplied fallback when the key is absent or blank.
    /// </summary>
    /// <param name="key">The key to look up.</param>
    /// <param name="fallback">The value to return when the key is absent or its value is empty.</param>
    /// <returns>The value, or <paramref name="fallback"/>.</returns>
    public string Get(string key, string fallback)
    {
        string value = Get(key);
        return value.Length == 0 ? fallback : value;
    }

    /// <summary>
    /// Reads an integer, falling back to a default when the key is absent or unparsable.
    /// </summary>
    /// <param name="key">The key to look up.</param>
    /// <param name="fallback">The value to return when the key is absent or not an integer.</param>
    /// <returns>The parsed value, or <paramref name="fallback"/>.</returns>
    public int GetInt32(string key, int fallback)
    {
        string value = Get(key);

        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
            ? parsed
            : fallback;
    }

    /// <summary>
    /// Reads a boolean, accepting the spellings the other RMS manifests use.
    /// </summary>
    /// <param name="key">The key to look up.</param>
    /// <param name="fallback">The value to return when the key is absent or unrecognised.</param>
    /// <returns>The parsed value, or <paramref name="fallback"/>.</returns>
    public bool GetBoolean(string key, bool fallback)
    {
        string value = Get(key);

        if (value.Length == 0)
            return fallback;

        return value.ToLowerInvariant() switch
        {
            "1" or "true" or "yes" or "on" => true,
            "0" or "false" or "no" or "off" => false,
            _ => fallback,
        };
    }

    /// <summary>
    /// Tests whether the key is present at all, regardless of its value.
    /// </summary>
    /// <param name="key">The key to test for.</param>
    /// <returns><see langword="true"/> when the key is present.</returns>
    public bool ContainsKey(string key)
    {
        return _indexByName.ContainsKey(key);
    }

    /// <summary>
    /// Removes an entry, if it is present.
    /// </summary>
    /// <param name="key">The key to remove.</param>
    /// <returns><see langword="true"/> when an entry was removed.</returns>
    public bool Remove(string key)
    {
        if (!_indexByName.TryGetValue(key, out int index))
            return false;

        _entries.RemoveAt(index);
        _indexByName.Remove(key);

        // Every index past the removed one shifts down by a single position.
        for (int position = index; position < _entries.Count; position++)
            _indexByName[_entries[position].Key] = position;

        return true;
    }

    /// <summary>
    /// Returns the entries whose keys begin with a prefix, ordered by the numeric suffix that follows it.
    /// </summary>
    /// <param name="prefix">The key prefix, including its trailing separator if it has one.</param>
    /// <returns>
    /// The matching values, ordered by the integer that follows the prefix. Entries whose suffix is not an
    /// integer are ignored, so a stray key with a similar name cannot displace the list.
    /// </returns>
    public List<string> GetIndexedValues(string prefix)
    {
        List<(int Index, string Value)> ordered = [];

        foreach (IniEntry entry in _entries)
        {
            if (!entry.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            string suffix = entry.Key[prefix.Length..];

            if (int.TryParse(suffix, NumberStyles.Integer, CultureInfo.InvariantCulture, out int position))
                ordered.Add((position, entry.Value));
        }

        ordered.Sort(static (left, right) => left.Index.CompareTo(right.Index));

        List<string> values = new(ordered.Count);

        foreach ((int _, string value) in ordered)
            values.Add(value);

        return values;
    }

    /// <summary>
    /// Returns the entries whose keys begin with a prefix, with the prefix removed.
    /// </summary>
    /// <param name="prefix">The key prefix, including its trailing separator if it has one.</param>
    /// <returns>A read-only map from the remaining key text to the value.</returns>
    /// <remarks>
    /// This is how the environment is carried: the keys are written as <c>environment.NAME</c>, which keeps a
    /// value containing an <c>=</c> intact and lets the variable names be recovered without any splitting.
    /// </remarks>
    public IReadOnlyDictionary<string, string> GetPrefixed(string prefix)
    {
        Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);

        foreach (IniEntry entry in _entries)
        {
            if (entry.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                values[entry.Key[prefix.Length..]] = entry.Value;
        }

        return new ReadOnlyDictionary<string, string>(values);
    }
}

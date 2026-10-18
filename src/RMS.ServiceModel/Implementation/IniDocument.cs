using System.Globalization;
using System.Text;

using RMS.ServiceModel.Models;

namespace RMS.ServiceModel.Implementation;

/// <summary>
/// A reader for the INI dialect the RMS deployer emits and every RMS service consumes.
/// </summary>
/// <remarks>
/// <para>
/// The dialect is deliberately the smallest one that can carry a service definition: <c>[section]</c> headers,
/// <c>key = value</c> pairs, and <c>#</c> or <c>;</c> comments. There are no includes, no line continuations
/// and no escape sequences, because the only writer is the deployer and the only readers are the services it
/// registers. A format with fewer moving parts is a format that cannot surprise an operator reading a
/// generated file by hand.
/// </para>
/// <para>
/// A line with no <c>=</c> is recorded as a key with an empty value rather than rejected, and the value keeps
/// every <c>=</c> after the first, so a connection string or a URL survives intact.
/// </para>
/// </remarks>
public sealed class IniDocument
{
    /// <summary>The character used to separate a key from its value.</summary>
    public const char KeyValueSeparator = '=';

    private readonly List<IniSection> _sections = [];

    private readonly Dictionary<string, int> _indexByName = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets every section, in the order it appeared in the source text.</summary>
    public IReadOnlyList<IniSection> Sections => _sections;

    /// <summary>
    /// Gets the unnamed section holding any pairs that appear before the first <c>[section]</c> header.
    /// </summary>
    public IniSection Leading { get; } = new(string.Empty);

    /// <summary>
    /// Initializes a new instance of the <see cref="IniDocument"/> class.
    /// </summary>
    public IniDocument()
    {
        _sections.Add(Leading);
        _indexByName[string.Empty] = 0;
    }

    /// <summary>
    /// Returns the named section, creating it when it does not exist yet.
    /// </summary>
    /// <param name="name">The section name.</param>
    /// <returns>The section.</returns>
    public IniSection GetOrAddSection(string name)
    {
        if (_indexByName.TryGetValue(name, out int index))
            return _sections[index];

        IniSection section = new(name);
        _indexByName[name] = _sections.Count;
        _sections.Add(section);

        return section;
    }

    /// <summary>
    /// Returns the named section, or <see langword="null"/> when it is absent.
    /// </summary>
    /// <param name="name">The section name.</param>
    /// <returns>The section, or <see langword="null"/>.</returns>
    public IniSection? FindSection(string name)
    {
        return _indexByName.TryGetValue(name, out int index) ? _sections[index] : null;
    }

    /// <summary>
    /// Parses an INI document from text.
    /// </summary>
    /// <param name="text">The text to parse.</param>
    /// <param name="origin">
    /// A path or other label used in exception messages. The reader does not open the file itself, so that the
    /// same code serves a file, an embedded resource and a test string.
    /// </param>
    /// <returns>The parsed document.</returns>
    /// <exception cref="InvalidDataException">
    /// Thrown when a section header is unterminated, because everything after it would then be attributed to a
    /// section the operator did not intend to write.
    /// </exception>
    public static IniDocument Parse(string text, string origin)
    {
        ArgumentNullException.ThrowIfNull(text);

        IniDocument document = new();
        IniSection section = document.Leading;

        // A byte order mark at the start of a generated file is invisible in an editor and would otherwise
        // become part of the first key, which is how the first line of a manifest ends up being silently
        // ignored.
        string body = text.Length > 0 && text[0] == '\uFEFF' ? text[1..] : text;

        using StringReader reader = new(body);
        int lineNumber = 0;

        while (reader.ReadLine() is { } rawLine)
        {
            lineNumber++;
            string line = rawLine.Trim();

            if (line.Length == 0 || line[0] == '#' || line[0] == ';')
                continue;

            if (line[0] == '[')
            {
                int closing = line.IndexOf(']');

                if (closing < 0)
                {
                    throw new InvalidDataException(
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "{0}({1}): a section header is missing its closing bracket.",
                            origin,
                            lineNumber));
                }

                section = document.GetOrAddSection(line[1..closing].Trim());
                continue;
            }

            int separator = line.IndexOf(KeyValueSeparator);

            if (separator < 0)
            {
                // A bare word is a key with an empty value rather than an error: the deployer uses that
                // spelling for the keys it wants present but unset, and rejecting it here would mean the two
                // sides disagreed about the same file.
                section.Add(line, string.Empty, lineNumber);
                continue;
            }

            string key = line[..separator].Trim();

            if (key.Length == 0)
            {
                // A line beginning with '=' carries no key, so there is nothing it could set.
                continue;
            }

            section.Add(key, line[(separator + 1)..].Trim(), lineNumber);
        }

        return document;
    }

    /// <summary>
    /// Reads and parses an INI file.
    /// </summary>
    /// <param name="path">The file to read.</param>
    /// <returns>The parsed document.</returns>
    /// <exception cref="FileNotFoundException">Thrown when the file does not exist.</exception>
    public static IniDocument Load(string path)
    {
        return Parse(File.ReadAllText(path, Encoding.UTF8), path);
    }

    /// <summary>
    /// Renders the document back to text, preserving section and entry order.
    /// </summary>
    /// <returns>The document as INI text, with a trailing newline.</returns>
    public string ToText()
    {
        StringBuilder builder = new();

        for (int index = 0; index < _sections.Count; index++)
        {
            IniSection section = _sections[index];

            if (section.Count == 0 && section.Name.Length == 0)
                continue;

            if (builder.Length > 0)
                builder.Append('\n');

            if (section.Name.Length > 0)
                builder.Append('[').Append(section.Name).Append("]\n");

            foreach (IniEntry entry in section.Entries)
                builder.Append(entry.Key).Append(" = ").Append(entry.Value).Append('\n');
        }

        return builder.ToString();
    }
}

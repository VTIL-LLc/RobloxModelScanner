using System.Globalization;

using RobloxModelScanner.Interfaces;

namespace RobloxModelScanner.Implementation;

/// <summary>
/// Reads and rewrites the scanner's environment file.
/// </summary>
/// <remarks>
/// <para>
/// The file is the conventional <c>KEY=VALUE</c> list with blank lines and <c>#</c> comments, which is
/// the format the previous revision used and therefore the format already on disk. It is parsed rather
/// than regenerated: every line the scanner did not change is written back as it was, in its original
/// position, with the file's own line ending. A settings file an operator edits by hand must not be
/// reformatted or reordered by the program that reads it, or the difference between what the operator
/// wrote and what is on disk becomes unreadable.
/// </para>
/// <para>
/// A value in the process environment always wins over the file, and is never written back. That is what
/// lets a service manager or a shell override one setting for a single run, and it is also what stops
/// the scanner from copying a credential out of the process environment and into a file on disk.
/// </para>
/// <para>
/// The one value the scanner writes is the sweep cursor. Writes are recorded and applied on the next
/// flush rather than immediately, because the sweep moves the cursor once per discovered model and
/// rewriting the file at that rate would dominate everything else the scanner does. A flush applies them
/// to the file as it stands at that moment rather than to the copy read when the scanner started, so a
/// setting changed by hand while the scanner runs is not quietly put back the way it was.
/// </para>
/// </remarks>
public sealed class EnvironmentFile : IEnvironmentFile
{
    /// <summary>
    /// The file's name.
    /// </summary>
    private const string FileName = ".env";

    /// <summary>
    /// The character that separates a setting name from its value.
    /// </summary>
    private const char Assignment = '=';

    /// <summary>
    /// The affirmative spellings accepted by <see cref="GetFlag"/>.
    /// </summary>
    private static readonly string[] Affirmative = ["1", "true", "yes", "on"];

    /// <summary>
    /// Guards the parsed map, the file's lines and the pending changes.
    /// </summary>
    private readonly object _gate = new();

    /// <summary>
    /// The path of the file, which may not exist.
    /// </summary>
    private readonly string _path;

    /// <summary>
    /// The file's lines as they were read, or <see langword="null"/> until the first read.
    /// </summary>
    private List<string>? _lines;

    /// <summary>
    /// The parsed settings, keyed without regard to case.
    /// </summary>
    private Dictionary<string, string>? _values;

    /// <summary>
    /// The values recorded but not yet written.
    /// </summary>
    private Dictionary<string, string>? _pending;

    /// <summary>
    /// The file's own line ending, discovered when it is read.
    /// </summary>
    private string _lineEnding = Environment.NewLine;

    /// <summary>
    /// Initializes a new instance of the <see cref="EnvironmentFile"/> class.
    /// </summary>
    /// <param name="path">The path of the file, which need not exist yet.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="path"/> is null or empty.</exception>
    public EnvironmentFile(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        _path = System.IO.Path.GetFullPath(path);
    }

    /// <summary>
    /// Gets the path this instance reads and writes.
    /// </summary>
    public string Path => _path;

    /// <summary>
    /// Chooses the file to read when none was configured.
    /// </summary>
    /// <returns>The first existing candidate, or the application directory's.</returns>
    /// <remarks>
    /// The working directory is preferred over the program's directory because that is the order the
    /// previous revision used, and because a developer who drops a file named <c>.env</c> beside their
    /// working directory means it. When neither exists, the application directory is returned, so that a
    /// first run writes its cursor somewhere predictable rather than into whichever directory happened
    /// to be current.
    /// </remarks>
    public static string ResolveDefaultPath()
    {
        string fromWorkingDirectory = System.IO.Path.Combine(Directory.GetCurrentDirectory(), FileName);
        return File.Exists(fromWorkingDirectory)
            ? fromWorkingDirectory
            : System.IO.Path.Combine(AppContext.BaseDirectory, FileName);
    }

    /// <inheritdoc />
    public string Get(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        string? fromProcess = Environment.GetEnvironmentVariable(key);
        if (fromProcess is not null) return fromProcess;

        lock (_gate)
        {
            EnsureLoaded();
            return _values!.TryGetValue(key, out string? value) ? value : string.Empty;
        }
    }

    /// <inheritdoc />
    public long GetLong(string key, long fallback)
    {
        string value = Get(key);
        return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed)
            ? parsed
            : fallback;
    }

    /// <inheritdoc />
    public int GetInt(string key, int fallback, int minimum, int maximum)
    {
        string value = Get(key);
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
        {
            return fallback;
        }

        return Math.Clamp(parsed, minimum, maximum);
    }

    /// <inheritdoc />
    public double GetDouble(string key, double fallback, double minimum, double maximum)
    {
        string value = Get(key);
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
        {
            return fallback;
        }

        return double.IsFinite(parsed) ? Math.Clamp(parsed, minimum, maximum) : fallback;
    }

    /// <inheritdoc />
    public bool GetFlag(string key, bool fallback)
    {
        string value = Get(key).Trim();
        if (value.Length == 0) return fallback;

        foreach (string affirmative in Affirmative)
        {
            if (value.Equals(affirmative, StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }

    /// <inheritdoc />
    public void Set(string key, string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(value);

        lock (_gate)
        {
            EnsureLoaded();
            _pending ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _pending[key] = value;
        }
    }

    /// <inheritdoc />
    public Task FlushAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Dictionary<string, string> writing;
        List<string> lines;

        /*
         * The lines that will be written are the file's lines as they stand now, not the copy taken when it
         * was loaded. The flush is the only writer this class knows about, but it is not the only writer an
         * operator knows about: a cookie or a webhook pasted into the file while the scanner is sweeping
         * would otherwise be overwritten by the copy in memory at the next flush, and the operator's edit
         * would vanish at a moment of the scanner's choosing rather than their own. That is a bad hour for
         * whoever pasted it, because every symptom of a missing cookie -- nothing downloads, every model
         * answers that its payload could not be fetched -- looks exactly the same as a cookie that is old.
         *
         * So the file is read again here and only the values this process actually set are applied to it.
         * An edit to any other setting survives, and so does the operator's own formatting and line endings.
         */
        List<string>? onDisk = null;

        try
        {
            onDisk = ReadCurrent();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Falls back to the copy taken at load, which is the behaviour this flush had before it read
            // the file again. A file that cannot be read at this moment is not a reason to lose the cursor:
            // the next flush a few seconds from now will try again, and the sweep is the thing being paid
            // for, not the tidiness of the file.
            onDisk = null;
        }

        lock (_gate)
        {
            EnsureLoaded();
            if (_pending is null || _pending.Count == 0) return Task.CompletedTask;

            // Copied rather than taken. The write happens outside the lock -- it may take milliseconds and
            // appends must not block behind it -- so the pending set can gain entries while it is running,
            // and those entries belong to the next flush rather than to this one.
            writing = new Dictionary<string, string>(_pending, StringComparer.OrdinalIgnoreCase);
            lines = onDisk ?? new List<string>(_lines!);
        }

        foreach ((string key, string value) in writing) Replace(lines, key, value);

        // Deliberately before the lock is retaken. A failed write must leave the changes pending, so that
        // the next flush tries again instead of the cursor being silently lost.
        Write(lines);

        lock (_gate)
        {
            foreach ((string key, string value) in writing)
            {
                _values![key] = value;

                // Removed only when the entry still holds what was just written. A value recorded while the
                // write was running must survive to be written by the next flush, and removing it here
                // would drop the operator's most recent cursor along with the one already on disk.
                if (_pending!.TryGetValue(key, out string? current) && current == value)
                {
                    _pending.Remove(key);
                }
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Reads the file if it has not been read yet.
    /// </summary>
    /// <remarks>
    /// The read is deferred until the first access so that constructing the instance cannot fail. A
    /// missing file is not an error: it is the state of a fresh installation, and the scanner's answer
    /// is to run on defaults and create the file when it first has something to persist.
    /// </remarks>
    private void EnsureLoaded()
    {
        if (_values is not null) return;

        _values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        _lines = ReadCurrent();

        foreach (string line in _lines) ParseLine(line);
    }

    /// <summary>
    /// Reads the file's lines as they stand now.
    /// </summary>
    /// <returns>The lines, in order, with blank lines and comments kept.</returns>
    /// <exception cref="IOException">Thrown when the file exists and cannot be read.</exception>
    /// <remarks>
    /// <para>
    /// One read serves both the first load and every flush, because both want the file rather than an
    /// opinion about it, and the flush in particular has to see an edit made while the scanner was running.
    /// </para>
    /// <para>
    /// A file that is not there is an empty list, which is the state a fresh installation is in. A file that
    /// is there and cannot be read throws instead, because a file this process cannot read must not be
    /// replaced by one derived from what it could not read.
    /// </para>
    /// </remarks>
    private List<string> ReadCurrent()
    {
        if (!File.Exists(_path)) return [];

        string text = File.ReadAllText(_path);
        _lineEnding = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

        List<string> lines = [];

        foreach (string line in text.Split('\n'))
        {
            lines.Add(line.TrimEnd('\r'));
        }

        // A file whose text ends with a terminator splits into a final empty element, which is an artefact
        // of splitting rather than a line. Dropping it stops the write below from appending a blank line
        // on every flush, which would grow the file without bound.
        if (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);

        return lines;
    }

    /// <summary>
    /// Parses one line into the value map when it assigns something.
    /// </summary>
    /// <param name="line">The line, without its terminator.</param>
    private void ParseLine(string line)
    {
        string trimmed = line.Trim();
        if (trimmed.Length == 0 || trimmed.StartsWith('#')) return;

        int separator = trimmed.IndexOf(Assignment);
        if (separator <= 0) return;

        string key = trimmed[..separator].Trim();
        if (key.Length == 0) return;

        _values![key] = trimmed[(separator + 1)..].Trim();
    }

    /// <summary>
    /// Replaces a setting's line in place, or appends one when it is not there.
    /// </summary>
    /// <param name="lines">The lines being prepared for writing.</param>
    /// <param name="key">The setting name.</param>
    /// <param name="value">The value to write.</param>
    /// <remarks>
    /// The comparison is case-insensitive, so that a setting written by a previous revision as
    /// <c>Start_Id</c> is updated rather than duplicated. Appending a second assignment for the same name
    /// would leave two lines disagreeing about its value, and which one wins would depend on the parser --
    /// the kind of ambiguity that stays invisible until it matters.
    /// </remarks>
    private static void Replace(List<string> lines, string key, string value)
    {
        for (int index = 0; index < lines.Count; index++)
        {
            string trimmed = lines[index].Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;

            int separator = trimmed.IndexOf(Assignment);
            if (separator <= 0) continue;
            if (!trimmed[..separator].Trim().Equals(key, StringComparison.OrdinalIgnoreCase)) continue;

            lines[index] = string.Concat(key, " = ", value);
            return;
        }

        lines.Add(string.Concat(key, " = ", value));
    }

    /// <summary>
    /// Writes the prepared lines over the file.
    /// </summary>
    /// <param name="lines">The lines to write.</param>
    /// <exception cref="IOException">Thrown when the write fails.</exception>
    /// <remarks>
    /// The write goes to a sibling file that is then moved into place. Replacing a file by truncating and
    /// rewriting it leaves a window in which the file is empty or half written, and a reader that arrives
    /// during that window sees a settings file with no credentials in it. A move within one directory is
    /// atomic on every platform this scanner runs on, so a reader sees either the old file or the new one
    /// and never a mixture.
    /// </remarks>
    private void Write(List<string> lines)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);

        string temporary = string.Concat(_path, ".writing");
        File.WriteAllText(temporary, string.Join(_lineEnding, lines) + _lineEnding);
        File.Move(temporary, _path, overwrite: true);
    }
}

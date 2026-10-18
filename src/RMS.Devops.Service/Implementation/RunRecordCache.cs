using RMS.ServiceModel.Models;

namespace RMS.Devops.Service.Implementation;

/// <summary>
/// Reuses parsed run records while the files they came from are unchanged.
/// </summary>
/// <remarks>
/// <para>
/// The history page lists every run of every service. A supervisor rewrites a run's record on every state
/// change, so on a busy machine most of those files are touched often and most of them say the same thing as
/// they did a second ago. Parsing them all on every poll would turn a page that refreshes every ten seconds
/// into a few hundred small file reads every ten seconds, for information that has not changed.
/// </para>
/// <para>
/// An entry is keyed by the file's path and validated against its last-write time and its length. Both must
/// match for the cached record to be reused, so a record that has been rewritten is always re-read; the
/// supervisor's own atomic replace guarantees a reader never sees a partial file, and this guarantees a reader
/// never sees a stale one.
/// </para>
/// <para>
/// The cache is bounded. A machine can accumulate directories that are no longer in the plan, and an
/// unbounded map keyed by path would grow with every service that was ever deployed. When the bound is
/// reached the map is dropped and rebuilt, which costs one pass and cannot leak.
/// </para>
/// </remarks>
internal sealed class RunRecordCache
{
    /// <summary>The number of records the cache holds before it is emptied.</summary>
    private const int Capacity = 4096;

    /// <summary>The signature a cached record is validated against.</summary>
    internal readonly record struct Stamp(DateTime LastWriteUtc, long Length);

    private readonly Dictionary<string, (Stamp Stamp, RunRecord Record)> _entries =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly object _gate = new();

    /// <summary>
    /// Reads a run record, reusing a cached parse when the file has not changed.
    /// </summary>
    /// <param name="path">The absolute path of the record file.</param>
    /// <param name="stamp">The file's identity, as read from the directory enumeration.</param>
    /// <returns>The record, or <see langword="null"/> when the file cannot be read as one.</returns>
    public RunRecord? Read(string path, Stamp stamp)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(path, out (Stamp Stamp, RunRecord Record) cached) &&
                cached.Stamp == stamp)
            {
                return cached.Record;
            }
        }

        RunRecord record;

        try
        {
            record = RunRecord.Load(path);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            // A record that cannot be read is not a reason to fail the page. The file stays on disk for an
            // operator to look at, and the rest of the history is still shown.
            return null;
        }

        lock (_gate)
        {
            if (_entries.Count >= Capacity)
                _entries.Clear();

            _entries[path] = (stamp, record);
        }

        return record;
    }
}

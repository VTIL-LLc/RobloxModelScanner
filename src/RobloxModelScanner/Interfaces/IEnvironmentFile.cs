namespace RobloxModelScanner.Interfaces;

/// <summary>
/// Reads and persists the scanner's environment file.
/// </summary>
/// <remarks>
/// <para>
/// The scanner is configured the way the rest of this stack is: a process environment that a service
/// manager or a shell supplies, layered over a file next to the program that an operator can edit. The
/// process environment wins, so a value can be overridden for one run without editing the file, and the
/// file carries the settings that are the same every run.
/// </para>
/// <para>
/// The interface also writes, for one value only: the sweep's cursor. It is batched rather than written
/// per call because the sweep advances the cursor for every model it finds, and rewriting a file on
/// every find is the kind of behaviour that makes a scanner look idle when it is actually working.
/// </para>
/// </remarks>
public interface IEnvironmentFile
{
    /// <summary>
    /// Reads a setting as text.
    /// </summary>
    /// <param name="key">The setting name, matched without regard to case in the file.</param>
    /// <returns>The value, or an empty string when the setting is absent.</returns>
    string Get(string key);

    /// <summary>
    /// Reads a setting as a whole number.
    /// </summary>
    /// <param name="key">The setting name.</param>
    /// <param name="fallback">The value to use when the setting is absent or unparsable.</param>
    /// <returns>The parsed value, or <paramref name="fallback"/>.</returns>
    long GetLong(string key, long fallback);

    /// <summary>
    /// Reads a setting as a whole number held within bounds.
    /// </summary>
    /// <param name="key">The setting name.</param>
    /// <param name="fallback">The value to use when the setting is absent or unparsable.</param>
    /// <param name="minimum">The smallest acceptable value.</param>
    /// <param name="maximum">The largest acceptable value.</param>
    /// <returns>The parsed value, clamped to the bounds, or <paramref name="fallback"/>.</returns>
    int GetInt(string key, int fallback, int minimum, int maximum);

    /// <summary>
    /// Reads a setting as a decimal number held within bounds.
    /// </summary>
    /// <param name="key">The setting name.</param>
    /// <param name="fallback">The value to use when the setting is absent or unparsable.</param>
    /// <param name="minimum">The smallest acceptable value.</param>
    /// <param name="maximum">The largest acceptable value.</param>
    /// <returns>The parsed value, clamped to the bounds, or <paramref name="fallback"/>.</returns>
    double GetDouble(string key, double fallback, double minimum, double maximum);

    /// <summary>
    /// Reads a setting as a switch.
    /// </summary>
    /// <param name="key">The setting name.</param>
    /// <param name="fallback">The value to use when the setting is absent.</param>
    /// <returns><see langword="true"/> for a configured affirmative, otherwise the parsed or fallback value.</returns>
    /// <remarks>
    /// The affirmative spellings are accepted liberally and the negative spellings are not enumerated at
    /// all: anything that is not affirmative is a refusal. Guessing that an unrecognised word meant "yes"
    /// is the failure mode that matters, and it cannot happen here.
    /// </remarks>
    bool GetFlag(string key, bool fallback);

    /// <summary>
    /// Records a value to be persisted.
    /// </summary>
    /// <param name="key">The setting name.</param>
    /// <param name="value">The value to write.</param>
    /// <remarks>
    /// The write is deferred until the next flush, so this is cheap enough to call in a hot loop. Two
    /// calls for the same key leave the later value, and the file's other lines and its ordering are
    /// preserved.
    /// </remarks>
    void Set(string key, string value);

    /// <summary>
    /// Persists any recorded value that has not yet been written.
    /// </summary>
    /// <param name="cancellationToken">A token used to abandon the write.</param>
    /// <returns>A task that completes when the file is up to date.</returns>
    Task FlushAsync(CancellationToken cancellationToken);
}

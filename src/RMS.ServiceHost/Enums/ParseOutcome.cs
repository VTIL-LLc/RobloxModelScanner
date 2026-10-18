namespace RMS.ServiceHost.Enums;

/// <summary>
/// What came of parsing the host's command line.
/// </summary>
/// <remarks>
/// Asking to see the usage text is not a failure, so it is a distinct outcome rather than a failure with an
/// empty message. A caller that could not tell the two apart would have to decide whether an absent message
/// meant "help" or "a bug", and the one time it guessed wrong would be the one time it printed nothing.
/// </remarks>
public enum ParseOutcome
{
    /// <summary>The command line was understood and describes work to do.</summary>
    Success,

    /// <summary>The caller asked for the usage text, which is not a failure.</summary>
    HelpRequested,

    /// <summary>The command line was not understood. The message says why.</summary>
    Failure,
}

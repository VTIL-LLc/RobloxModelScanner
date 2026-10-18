namespace RMS.ServiceInstaller.Enums;

/// <summary>
/// What came of parsing the installer's command line.
/// </summary>
public enum ParseOutcome
{
    /// <summary>The command line was understood and describes work to do.</summary>
    Success,

    /// <summary>The caller asked for the usage text, which is not a failure.</summary>
    HelpRequested,

    /// <summary>The command line was not understood. The message says why.</summary>
    Failure,
}

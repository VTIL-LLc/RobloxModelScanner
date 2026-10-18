using System.Text.Json;

using RobloxModelScanner.Models;

namespace RobloxModelScanner.Implementation;

/// <summary>
/// Reads the pause and stop request the administration API leaves on disk.
/// </summary>
/// <remarks>
/// <para>
/// The file is the whole of the communication between the API and the scanner, and it works in one
/// direction only: the API writes intent and the scanner reads it. There is no acknowledgement, and
/// consequently no way for the API to know whether the scanner is still alive -- which is why the API
/// reports a stop as requested rather than as performed.
/// </para>
/// <para>
/// The read is tolerant to the point of silence. A file that is absent, empty, mid-write or malformed
/// all mean the same thing to a running scanner: carry on. Treating any of them as a stop would let a
/// momentary glitch in the writer halt a scan that was doing its job.
/// </para>
/// </remarks>
internal static class ScannerControlFile
{
    /// <summary>
    /// The state assumed when no instruction can be read.
    /// </summary>
    private static readonly ScannerControlState NoInstruction = new();

    /// <summary>
    /// The settings used to read the file written by the API.
    /// </summary>
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Reads the current instruction.
    /// </summary>
    /// <param name="path">The control file's path.</param>
    /// <returns>The instruction, or a state that neither pauses nor stops when none can be read.</returns>
    internal static ScannerControlState Read(string path)
    {
        try
        {
            if (!File.Exists(path)) return NoInstruction;

            string text = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(text)) return NoInstruction;

            return JsonSerializer.Deserialize<ScannerControlState>(text, ReadOptions) ?? NoInstruction;
        }
        catch (JsonException)
        {
            return NoInstruction;
        }
        catch (IOException)
        {
            return NoInstruction;
        }
        catch (UnauthorizedAccessException)
        {
            return NoInstruction;
        }
    }
}

using System.Text.Json;

using Scanner.MicroApi.Interfaces;
using Scanner.MicroApi.Models;

namespace Scanner.MicroApi.Implementation;

/// <summary>
/// Reads and writes the scanner's JSON control file.
/// </summary>
/// <remarks>
/// <para>
/// Writes go to a sibling temporary file and are then moved over the target. On Windows that move
/// is an atomic replace, so a scanner that reads the control file concurrently observes either the
/// previous document or the new one, never a half-written mixture.
/// </para>
/// <para>
/// Reads are deliberately forgiving. A missing, locked or corrupt control file is reported as the
/// default running state, because the control file is an optimisation channel: losing it must not
/// take the API down or produce misleading status for operators.
/// </para>
/// </remarks>
public sealed class ScannerControlService : IScannerControlService
{
    /// <summary>
    /// The serializer policy shared by every control-file operation.
    /// </summary>
    /// <remarks>
    /// The control file is written with the declared member names, which is the shape the scanner has
    /// always produced and therefore the one on disk today. Reads additionally ignore case, so a scanner
    /// built with a different naming policy still reads a document written here.
    /// </remarks>
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly ScannerPaths _paths;

    /// <summary>
    /// Serializes concurrent writers so that two overlapping control requests cannot interleave
    /// their temporary files.
    /// </summary>
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    /// <summary>
    /// Initializes a new instance of the <see cref="ScannerControlService"/> class.
    /// </summary>
    /// <param name="paths">The resolved control-file location.</param>
    public ScannerControlService(ScannerPaths paths) => _paths = paths;

    /// <inheritdoc />
    public async Task<ControlState> ReadAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(_paths.ControlPath)) return new ControlState();

            await using FileStream stream = new(
                _paths.ControlPath,
                FileMode.Open,
                FileAccess.Read,
                // Allowing delete share means the scanner is free to replace the file while a status
                // request is reading it, instead of the read blocking the writer.
                FileShare.Read | FileShare.Delete,
                bufferSize: 4096,
                FileOptions.Asynchronous);

            return await JsonSerializer
                .DeserializeAsync<ControlState>(stream, SerializerOptions, cancellationToken)
                .ConfigureAwait(false)
                ?? new ControlState();
        }
        catch (IOException)
        {
            return new ControlState();
        }
        catch (JsonException)
        {
            return new ControlState();
        }
    }

    /// <inheritdoc />
    public async Task<ControlState> WriteAsync(ControlState state, CancellationToken cancellationToken)
    {
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string? directory = Path.GetDirectoryName(_paths.ControlPath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            string temporaryPath = _paths.ControlPath + ".tmp";

            await using (FileStream stream = new(
                temporaryPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.Asynchronous))
            {
                await JsonSerializer
                    .SerializeAsync(stream, state, SerializerOptions, cancellationToken)
                    .ConfigureAwait(false);
            }

            File.Move(temporaryPath, _paths.ControlPath, overwrite: true);
            return state;
        }
        finally
        {
            _writeGate.Release();
        }
    }
}

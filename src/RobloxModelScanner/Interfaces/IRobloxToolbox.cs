using RobloxModelScanner.Models;

namespace RobloxModelScanner.Interfaces;

/// <summary>
/// The catalogue the scanner searches for models.
/// </summary>
/// <remarks>
/// <para>
/// The scanner does not walk the identifier space blindly. Roblox allocates identifiers from one
/// sequence, so most of them belong to places, audio, images and other asset kinds that can never be a
/// model, and guessing at them one request at a time would spend almost all of its time discovering
/// that an identifier is not interesting. The details endpoint answers for a whole batch at once and
/// states each asset's kind, so asking about a hundred identifiers together is a hundred times less
/// wasteful than asking about them one at a time.
/// </para>
/// <para>
/// The implementation filters to models. The interface returns models for that reason: the caller is a
/// model scanner, and filtering here means the rest of the program never handles an asset kind it has
/// nothing to do with.
/// </para>
/// </remarks>
public interface IRobloxToolbox
{
    /// <summary>
    /// Asks the catalogue about a batch of identifiers.
    /// </summary>
    /// <param name="modelIds">The identifiers to ask about.</param>
    /// <param name="cancellationToken">A token used to abandon the request.</param>
    /// <returns>The models Roblox describes, or <see langword="null"/> when the batch could not be answered at all.</returns>
    /// <remarks>
    /// <para>
    /// Fewer results than identifiers is the normal case rather than an error. An identifier may not
    /// exist yet, may have been deleted, or may belong to another asset kind, and Roblox distinguishes
    /// none of those from the caller's point of view -- it simply omits the entry. Nothing should treat
    /// an omitted identifier as a failure, because doing so would turn the empty upper end of the
    /// identifier space into a stream of errors.
    /// </para>
    /// <para>
    /// An unanswered batch is a different outcome and is reported as one, because the caller must be able
    /// to tell it apart from an empty one. A sweep advances its cursor past a batch it has asked about,
    /// so treating a failed request as \"nothing here\" would step over a hundred identifiers that were
    /// never actually examined, and nothing would ever go back for them.
    /// </para>
    /// </remarks>
    Task<IReadOnlyList<ToolboxItem>?> GetModelsAsync(
        IReadOnlyList<long> modelIds,
        CancellationToken cancellationToken);
}

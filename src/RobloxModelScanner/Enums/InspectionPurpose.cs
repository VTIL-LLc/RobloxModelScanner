namespace RobloxModelScanner.Enums;

/// <summary>
/// Why an asset is being examined.
/// </summary>
/// <remarks>
/// <para>
/// The examination is identical either way, and so is the decision about whether the payload is worth
/// keeping. What differs is who is told about it. A model found by a sweep is news and is published to
/// the channels; a row revisited by a repair is one the operator already has in the catalogue and is
/// only being brought up to date. Announcing every repaired row would bury the day's real findings under
/// a re-run of the previous year's.
/// </para>
/// <para>
/// The value is carried explicitly rather than inferred from whether a row already exists, because the
/// two are not the same question: a sweep can find an asset that is already recorded -- two batches can
/// overlap a boundary -- and that find is still news.
/// </para>
/// </remarks>
public enum InspectionPurpose
{
    /// <summary>
    /// The asset was found by a sweep and its outcome is worth publishing.
    /// </summary>
    Discovery = 0,

    /// <summary>
    /// The asset was revisited to rebuild a catalogue row and its outcome replaces one already recorded.
    /// </summary>
    Repair = 1
}

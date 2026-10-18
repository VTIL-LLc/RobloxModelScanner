namespace RobloxModelScanner.Features.AssetAnalysis.Rules;

/// <summary>
/// Matches assets carrying the AMX1390 mark.
/// </summary>
/// <remarks>
/// <para>
/// AMX1390 is a creator, not a product. The token is the signature that creator leaves in their own
/// work, so a match is a record of their material rather than of something done to it -- there is no
/// obfuscator of this name, and a model that carries the mark has not been protected with anything.
/// </para>
/// <para>
/// Almost all of that material is gone. It was not taken from anyone: the place it was published
/// stopped serving it, and the mark survived in whatever copies were kept. That is what makes it worth
/// flagging, and it is the opposite of the reason this rule used to give -- a match means a model that
/// cannot be obtained any more has been found, so the useful thing to do with one is keep it, not treat
/// it as evidence that somebody stole script content.
/// </para>
/// <para>
/// A reviewer who reads this rule as a hazard would be misled twice: they would look for a protection
/// that is not present, and they would suspect the wrong party of taking something. See
/// <see cref="RuleCategory.Whitelist"/> and <see cref="RuleSeverity.Informational"/>, which are what the
/// vocabulary provides for a mark of this kind.
/// </para>
/// </remarks>
internal sealed class Amx1390Rule : IDetectionRule
{
    /// <summary>
    /// Gets the stable label written into catalogue records for this rule.
    /// </summary>
    /// <remarks>
    /// Unchanged, and deliberately so. A catalogue row, every published count and the analytics that
    /// aggregate them are all keyed on this text; correcting the sentence that explains the rule is a
    /// change of documentation, while changing the label would orphan the rows already written. The
    /// label this rule used before the spelling in it was tidied is mapped onto this one in the
    /// indexer's table of legacy aliases.
    /// </remarks>
    public string Tag => "AMX1390(RARE INCOMING)";

    /// <summary>
    /// Gets the score this rule contributes when it matches.
    /// </summary>
    /// <remarks>
    /// The highest score in the set, and not a statement about danger. Score ranks models, and what
    /// this mark ranks is how much of a find the model is: the whole point of carrying the rule is that
    /// a match is the rarest thing the scanner can report, and a score below the ones the obfuscators
    /// carry would file it under them. The field that describes risk is <see cref="Severity"/>, and it
    /// says the opposite.
    /// </remarks>
    public double Score => 99;

    /// <summary>
    /// Gets the behaviour group used for analytics aggregation.
    /// </summary>
    /// <remarks>
    /// A creator's watermark, which is the group the analytics counts it under and the group that
    /// exists for exactly this. It was filed as an obfuscator when the rule was written, on the reading
    /// that the mark was a paid protection product; counting it there put a model that carries no
    /// obfuscation at all into the obfuscation figures, and did so at the top of them.
    /// </remarks>
    public RuleCategory Category => RuleCategory.Whitelist;

    /// <summary>
    /// Gets the relative risk of a match.
    /// </summary>
    /// <remarks>
    /// Nothing about this mark is a hazard. It says who made the asset. Informational is the severity
    /// the vocabulary reserves for a trusted creator's watermark, and it keeps a match out of the
    /// alerting thresholds that severity drives, where a rarity does not belong.
    /// </remarks>
    public RuleSeverity Severity => RuleSeverity.Informational;

    /// <summary>
    /// Gets the operator-facing explanation of why the rule exists.
    /// </summary>
    public string Purpose => "Marks assets bearing the signature of AMX1390, a creator whose catalogue has almost entirely been lost. A match is a survivor of it, worth keeping, and not a sign of stolen or protected content.";

    /// <summary>
    /// Gets the technical description of the signature.
    /// </summary>
    public string Description => "Matches the literal token amx1390 as a whole word.";

    /// <summary>
    /// Tests a payload against this rule's signature.
    /// </summary>
    /// <param name="context">The lower-cased payload wrapper created once per asset.</param>
    /// <returns><see langword="true"/> when the signature is present.</returns>
    public bool IsMatch(in DetectionContext context) => context.ContainsWord("amx1390");
}

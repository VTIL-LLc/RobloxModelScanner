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
/// flagging, and it is the opposite of the reason this rule used to give -- a match is a pointer to a
/// model that cannot be obtained any more, so the useful thing to do with one is keep it, not treat it
/// as evidence that somebody stole script content.
/// </para>
/// <para>
/// A reviewer who reads this rule as a hazard would be misled twice: they would look for a protection
/// that is not present, and they would suspect the wrong party of taking something. See
/// <see cref="RuleCategory.Whitelist"/> and <see cref="RuleSeverity.Informational"/>, which are what the
/// vocabulary provides for a mark of this kind.
/// </para>
/// <para>
/// The token is not the only thing a copy of that material carries, so the rule does not match on the token
/// alone. A handful of instance names belong to the same catalogue and are matched the same way -- see
/// <see cref="CatalogueNames"/> -- because a copy that lost the mark still carries the name of what it
/// contains, and a name is what a reviewer recognises the material by.
/// </para>
/// <para>
/// A name only counts inside an asset that <em>is</em> a module, which is what the gate in
/// <see cref="IsMatch"/> decides by reading the file's structure. That is the difference between a copy and a
/// coincidence: the catalogue this rule points at is Lua delivered as a module, so an asset of that kind has
/// exactly one instance at its root -- a <c>ModuleScript</c> named <c>MainModule</c>, holding whatever the
/// module builds. Enter appears in every interface that asks a reader to press something, The Object appears
/// in titles, and R15 Rig appears in every rig anybody ever saved; a car whose boot script says "enter" is not
/// a copy of anything, and it is not a module asset either. The token is not gated: amx1390 in a payload is
/// the mark and nothing else, whatever the payload holds.
/// </para>
/// <para>
/// The gate used to be the presence of the word <c>modulescript</c> anywhere in the payload, which was a
/// mistake of the same kind as matching the names on their own: a thousand-instance vehicle kit contains a
/// <c>ModuleScript</c> for its chassis tune, so every word in it counted, and a model whose root was a
/// <c>Model</c> could be filed as this creator's material. Reading the class table before the name is what
/// makes the answer about the asset rather than about the text inside it.
/// </para>
/// </remarks>
internal sealed class Amx1390Rule : IDetectionRule
{
    /// <summary>
    /// The names that identify material from the same catalogue as the mark.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A copy of that material does not always carry the token. The mark survived in some copies and not in
    /// others, and what survived more often than the mark is the name an instance was given. These are the
    /// names that turned up on that material, lower-cased because the payload is lower-cased before any rule
    /// sees it and matched as words, so that a name inside a longer identifier is not a match.
    /// </para>
    /// <para>
    /// They are matched as text in the payload rather than from the catalogue's own record of the asset's
    /// name, deliberately. A model carries the names of its own instances inside it, so the name reaches this
    /// rule whether the asset was renamed at publication or not -- and it reaches it on a repair pass over a
    /// catalogue that was recorded before this rule existed, which a rule reading the stored name could not
    /// do.
    /// </para>
    /// </remarks>
    private static readonly string[] CatalogueNames =
    [
        "enter",
        "the object",
        "angel v2",
        "replicatearea",
        "r15 rig",
        "backuplocalcontrols277"
    ];

    /// <summary>
    /// Gets the stable label written into catalogue records for this rule.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A catalogue row, every published count and the analytics that aggregate them are all keyed on this
    /// text, so the label is not free to move casually: it moved once, and every spelling it has carried is
    /// mapped onto this one in the alias tables the indexer and the API read, so a row written under an
    /// older label still rolls up here instead of becoming an unattributable mark.
    /// </para>
    /// <para>
    /// The word after the token is the finding, and it states the strength of the signal honestly. A match
    /// says the mark is <em>mentioned</em> somewhere in the asset -- a script, a line of metadata, a credit
    /// -- and it stops short of claiming the asset came out of that creator's own catalogue. The token is a
    /// piece of text, and text can be quoted by someone who is not its author.
    /// </para>
    /// </remarks>
    public string Tag => "AMX1390(Mentioned)";

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
    public string Purpose => "Marks assets that mention AMX1390, a creator whose catalogue has almost entirely been lost, and script modules carrying one of that catalogue's instance names. A match is a pointer to that material, worth keeping, and not a sign of stolen or protected content.";

    /// <summary>
    /// Gets the technical description of the signature.
    /// </summary>
    public string Description => "Matches the literal token amx1390 as text bounded by non-identifier bytes, anywhere in a payload. Also matches one of the names that catalogue's material carries -- Enter, The Object, Angel v2, ReplicateArea, R15 Rig, BackupLocalControls277 -- but only when the asset is a module document: a single ModuleScript named MainModule at the root, which is the shape Roblox publishes a module in. A model that merely contains a module somewhere does not qualify. One occurrence is enough.";

    /// <summary>
    /// Tests a payload against this rule's signature.
    /// </summary>
    /// <param name="context">The lower-cased payload wrapper created once per asset.</param>
    /// <returns><see langword="true"/> when the signature is present.</returns>
    /// <remarks>
    /// <para>
    /// The token is tested first and on its own, because it is the mark: a payload carrying it is a pointer to
    /// this creator's material whether or not the module survived.
    /// </para>
    /// <para>
    /// The names are tested behind two gates. The asset must be a module document -- a root <c>ModuleScript</c>
    /// named <c>MainModule</c> -- and the name must be one an instance carries, read out of the file, not one
    /// the payload happens to contain as text. A script whose source says "enter" is not an instance named
    /// Enter, and that is the difference between a copy of this catalogue's material and every interface that
    /// asks a reader to press something.
    /// </para>
    /// <para>
    /// The text search in front of the parse is a substring search and only skips work: an instance named
    /// <c>Enter</c> necessarily writes those bytes, so a payload without them cannot carry the name. It is not
    /// the test itself, which is why it does not need word boundaries -- the one at the end of a name chunk
    /// runs straight into the letters of the chunk header after it.
    /// </para>
    /// </remarks>
    public bool IsMatch(in DetectionContext context)
    {
        if (context.ContainsWord("amx1390")) return true;
        if (!context.ContainsAny(CatalogueNames)) return false;

        ModelOutline? outline = ModelStructure.Read(context.Payload);

        if (outline is null || !outline.IsModuleRoot) return false;

        foreach (string name in outline.Names)
        {
            foreach (string catalogue in CatalogueNames)
            {
                if (string.Equals(name.Trim(), catalogue, StringComparison.OrdinalIgnoreCase)) return true;
            }
        }

        return false;
    }
}

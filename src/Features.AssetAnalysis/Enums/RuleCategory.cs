namespace RobloxModelScanner.Features.AssetAnalysis;

/// <summary>
/// Groups detection rules by the class of behaviour they describe.
/// </summary>
/// <remarks>
/// Categories exist so the analytics surface can report "how many models triggered
/// each kind of signal" instead of only raw per-tag counts. Keeping the grouping in
/// the shared detection assembly (rather than in the API or the website) guarantees
/// every consumer buckets the same rule the same way.
/// </remarks>
public enum RuleCategory
{
    /// <summary>Defensive default for a rule that has not been classified.</summary>
    Unknown = 0,

    /// <summary>Outbound network access performed through Roblox HttpService.</summary>
    HttpService = 1,

    /// <summary>Discord or similar webhook endpoints used to ship data off-platform.</summary>
    Webhook = 2,

    /// <summary>Parallel thread-scheduling markers (parallel Luau, Actors, task sync).</summary>
    Concurrency = 3,

    /// <summary>Custom virtual machines that execute hidden bytecode.</summary>
    VirtualMachine = 4,

    /// <summary>Lua/Luau compiler opcode or bytecode markers.</summary>
    Compiler = 5,

    /// <summary>Third-party obfuscators with verifiable signatures.</summary>
    Obfuscator = 6,

    /// <summary>Lower-confidence heuristic obfuscation indicators.</summary>
    Heuristic = 7,

    /// <summary>Trusted creator or content watermark markers.</summary>
    Whitelist = 8
}

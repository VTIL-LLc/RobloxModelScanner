namespace Scanner.MicroApi.Models;

/// <summary>
/// One entry of the creator leaderboard.
/// </summary>
/// <remarks>
/// Creators are ranked by indexed model count so operators can spot accounts that systematically
/// publish scripted or obfuscated content rather than triaging assets one at a time.
/// </remarks>
/// <param name="CreatorId">The creator's numeric identifier as text.</param>
/// <param name="CreatorName">The creator's display name.</param>
/// <param name="Models">The number of indexed models attributed to the creator.</param>
/// <param name="ModelsWithScripts">The number of those models that contain script data.</param>
/// <param name="Detections">The number of those models that triggered at least one detection rule.</param>
public sealed record CreatorStatistic(string CreatorId, string CreatorName, int Models, int ModelsWithScripts, int Detections);

namespace Scanner.MicroApi.Enums;

/// <summary>
/// Identifies the catalogue column a model query may be ordered by.
/// </summary>
/// <remarks>
/// The enum exists so that <c>sortBy</c> can be accepted from the query string without
/// ever being concatenated into SQL. It is mapped to a trusted column identifier by the
/// repository, which removes the injection surface an arbitrary sort key would create.
/// </remarks>
public enum ModelSortField
{
    /// <summary>Order by Roblox asset identifier.</summary>
    Model = 0,

    /// <summary>Order by the date the scanner first recorded the asset.</summary>
    Date = 1,

    /// <summary>Order by asset name.</summary>
    Name = 2,

    /// <summary>Order by creator display name.</summary>
    CreatorName = 3,

    /// <summary>Order by creator identifier.</summary>
    CreatorId = 4,

    /// <summary>Order by whether the asset contains script data.</summary>
    HasScripts = 5,

    /// <summary>Order by aggregate obfuscation score.</summary>
    Score = 6,

    /// <summary>Order by the flattened rule tag string.</summary>
    Tags = 7,

    /// <summary>Order by asset description.</summary>
    Description = 8
}

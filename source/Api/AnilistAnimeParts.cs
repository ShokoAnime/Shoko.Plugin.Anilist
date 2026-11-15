using System;

namespace Shoko.Plugin.Anilist.Api;

/// <summary>
/// The parts of an anime the full fetch may leave out. The media fields, the
/// schedule, the tags and the studios are always asked for.
/// </summary>
[Flags]
public enum AnilistAnimeParts
{
    /// <summary>
    /// Nothing besides what is always asked for.
    /// </summary>
    None = 0,

    /// <summary>
    /// The characters, with their voice actors, and the staff.
    /// </summary>
    CastAndCrew = 1,

    /// <summary>
    /// The relations to other anime and manga.
    /// </summary>
    Relations = 2,

    /// <summary>
    /// The recommendations.
    /// </summary>
    Recommendations = 4,

    /// <summary>
    /// Every part.
    /// </summary>
    All = CastAndCrew | Relations | Recommendations,
}

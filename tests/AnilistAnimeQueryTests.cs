using System.Text.RegularExpressions;
using Shoko.Plugin.Anilist.Api;
using Xunit;

namespace Shoko.Plugin.Anilist.Tests;

/// <summary>
/// The anime query of every combination of parts: GraphQL refuses a variable
/// or fragment nothing uses, and one used without being declared.
/// </summary>
public partial class AnilistAnimeQueryTests
{
    public static TheoryData<AnilistAnimeParts> EveryCombination()
        => [.. Enumerable.Range(0, (int)AnilistAnimeParts.All + 1).Select(parts => (AnilistAnimeParts)parts)];

    [Theory]
    [MemberData(nameof(EveryCombination))]
    public void TheQuery_DeclaresExactlyTheVariablesItUses_AndTheVariablesSentMatch(AnilistAnimeParts parts)
    {
        var query = AnilistApiClient.GetAnimeQuery(parts);
        var header = query[..query.IndexOf('{', StringComparison.Ordinal)];
        var body = query[header.Length..];

        var declared = Variables().Matches(header).Select(match => match.Groups[1].Value).ToHashSet();
        var used = Variables().Matches(body).Select(match => match.Groups[1].Value).ToHashSet();

        Assert.Equal(declared.Order(), used.Order());
        Assert.Equal(declared.Order(), AnilistApiClient.GetAnimeVariables(21, parts).Keys.Order());
    }

    [Theory]
    [MemberData(nameof(EveryCombination))]
    public void TheQuery_DefinesExactlyTheFragmentsItSpreads(AnilistAnimeParts parts)
    {
        var query = AnilistApiClient.GetAnimeQuery(parts);

        var defined = FragmentDefinitions().Matches(query).Select(match => match.Groups[1].Value).ToList();
        var spread = FragmentSpreads().Matches(query).Select(match => match.Groups[1].Value).ToHashSet();

        Assert.Equal(defined.Distinct().Count(), defined.Count);
        Assert.Equal(defined.Order(), spread.Order());
    }

    [Theory]
    [MemberData(nameof(EveryCombination))]
    public void TheQuery_AsksForEachPart_OnlyWhenItIsOn(AnilistAnimeParts parts)
    {
        var query = AnilistApiClient.GetAnimeQuery(parts);

        Assert.Equal(parts.HasFlag(AnilistAnimeParts.CastAndCrew), query.Contains("characters(", StringComparison.Ordinal));
        Assert.Equal(parts.HasFlag(AnilistAnimeParts.CastAndCrew), query.Contains("staff(", StringComparison.Ordinal));
        Assert.Equal(parts.HasFlag(AnilistAnimeParts.Relations), query.Contains("relations {", StringComparison.Ordinal));
        Assert.Equal(parts.HasFlag(AnilistAnimeParts.Recommendations), query.Contains("recommendations(", StringComparison.Ordinal));
        Assert.Contains("airingSchedule(", query, StringComparison.Ordinal);
        Assert.Contains("studios(", query, StringComparison.Ordinal);
        Assert.Contains("tags {", query, StringComparison.Ordinal);
    }

    [GeneratedRegex(@"\$(\w+)")]
    private static partial Regex Variables();

    [GeneratedRegex(@"fragment (\w+) on")]
    private static partial Regex FragmentDefinitions();

    [GeneratedRegex(@"\.\.\.(\w+)")]
    private static partial Regex FragmentSpreads();
}

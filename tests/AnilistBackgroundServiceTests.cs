using Moq;
using Shoko.Abstractions.Metadata;
using Shoko.Abstractions.Metadata.Storage;
using Shoko.Abstractions.Metadata.Providers;
using Shoko.Plugin.Anilist.Actions;
using Shoko.Plugin.Anilist.Mapping;
using Shoko.Plugin.Anilist.Metadata;
using Shoko.Plugin.Anilist.Services;
using Shoko.Plugin.Anilist.Storage;
using Xunit;

namespace Shoko.Plugin.Anilist.Tests;

/// <summary>
/// What the plugin asks the core for on its own schedule, and what its
/// actions ask the core for.
/// </summary>
public class AnilistBackgroundServiceTests
{
    [Fact]
    public async Task RefreshIfNothingStored_RefreshesEveryLinkedAnime_Once()
    {
        using var harness = new ServiceHarness();
        harness.Stores.CrossReferences.AddSeries(100, 21);
        var background = harness.Get<AnilistBackgroundService>();

        await background.RefreshIfNothingStored(TestContext.Current.CancellationToken);
        AnilistStoreTests.StoreAnime(harness.Stores, 21);
        await background.RefreshIfNothingStored(TestContext.Current.CancellationToken);

        // An expression tree can't read an extension property, so take it first.
        var anilist = MetadataSource.AniList;
        harness.RefreshService.Verify(service => service.RefreshAllLinked(anilist, false, null, MetadataEntityType.Series, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RefreshIfNothingStored_DoesNothing_WithNothingLinked()
    {
        using var harness = new ServiceHarness();
        harness.Stores.CrossReferences.AddEpisode(100, 100001, 0);

        Assert.Equal(0, await harness.Get<AnilistBackgroundService>().RefreshIfNothingStored(TestContext.Current.CancellationToken));

        harness.RefreshService.VerifyNoOtherCalls();
    }

    [Fact]
    public void RelabelCharacterNativeNames_LabelsUnlabelledHanNames_ByTheirAnime()
    {
        using var harness = new ServiceHarness();
        var stores = harness.Stores;
        var chinese = new AnilistMedia { ID = 30, MainTitle = "Chinese", OriginalLanguageCode = "zh" };
        stores.Series.SaveSeries(AnilistMediaMapper.ToSeriesData(chinese, []));
        // As stored before: the Han name without a language, the full name in romaji.
        var unlabelled = Character(1, "Li Bai", "李白", null);
        var uncredited = Character(2, "Du Fu", "杜甫", null);
        var labelled = Character(3, "Luffy Monkey", "モンキー・D・ルフィ", "ja");
        stores.People.SaveCharacters([unlabelled, uncredited, labelled]);
        stores.People.SetCast(AnilistUtility.SeriesGuid(30), [new() { CharacterID = unlabelled.ID, Name = "Li Bai" }, new() { CharacterID = labelled.ID, Name = "Luffy Monkey" }]);
        var background = harness.Get<AnilistBackgroundService>();

        Assert.Equal(1, background.RelabelCharacterNativeNames());
        Assert.Equal(0, background.RelabelCharacterNativeNames());

        var relabelled = stores.People.GetCharacter(unlabelled.ID)!;
        Assert.Equal("zh", relabelled.OriginalLanguageCode);
        Assert.Equal([("Li Bai", "x-zht"), ("李白", "zh")], relabelled.Titles.Select(title => (title.Value, title.LanguageCode)));
        Assert.Equal(unlabelled.Resources, relabelled.Resources);
        Assert.Null(stores.People.GetCharacter(uncredited.ID)!.OriginalLanguageCode);
    }

    private static MetadataCharacterData Character(int id, string name, string native, string? languageCode)
    {
        var people = new AnilistPeople();
        var node = System.Text.Json.Nodes.JsonNode.Parse($$"""{ "id": {{id}}, "name": { "full": "{{name}}", "native": "{{native}}" } }""");
        return AnilistMediaMapper.ReadCharacterNode(node, people, languageCode)!;
    }

    [Fact]
    public async Task PurgeUnused_GoesByTheSetting()
    {
        using var harness = new ServiceHarness();
        var background = harness.Get<AnilistBackgroundService>();

        await background.PurgeUnused(TestContext.Current.CancellationToken);
        harness.Stores.Configuration.AutoPurgeUnlinkedAfterDays = 0;
        await background.PurgeUnused(TestContext.Current.CancellationToken);

        // An expression tree can't read an extension property, so take it first.
        var anilist = MetadataSource.AniList;
        harness.PurgeService.Verify(service => service.PurgeUnused(anilist, It.Is<DateTime?>(cutoff => cutoff < DateTime.Now.AddDays(-13) && cutoff > DateTime.Now.AddDays(-15)), MetadataEntityType.Series, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TheLibraryActions_AskTheCore()
    {
        using var harness = new ServiceHarness();
        var progress = new Progress<decimal>();

        await new UpdateAllAnilistAnimeWithImagesAction(harness.RefreshService.Object).Execute(progress, TestContext.Current.CancellationToken);
        await new SearchForAnilistMatchesAction(harness.RefreshService.Object).Execute(progress, TestContext.Current.CancellationToken);
        await new PurgeAllUnusedAnilistAnimeAction(harness.PurgeService.Object).Execute(progress, TestContext.Current.CancellationToken);
        await new PurgeAllAnilistLinksAction(harness.LinkingService.Object).Execute(progress, TestContext.Current.CancellationToken);

        // An expression tree can't read an extension property, so take it first.
        var anilist = MetadataSource.AniList;
        harness.RefreshService.Verify(service => service.RefreshAllLinked(anilist, true, It.Is<MetadataRefreshOptions?>(options => options!.DownloadImages && options.Reason == MetadataRefreshReason.Requested), MetadataEntityType.Series, progress, It.IsAny<CancellationToken>()), Times.Once);
        harness.RefreshService.Verify(service => service.AutoSearchAll(anilist, false, progress, It.IsAny<CancellationToken>()), Times.Once);
        harness.PurgeService.Verify(service => service.PurgeUnused(anilist, null, MetadataEntityType.Series, progress, It.IsAny<CancellationToken>()), Times.Once);
        harness.LinkingService.Verify(service => service.RemoveAllLinks(anilist, true, true, false, progress, It.IsAny<CancellationToken>()), Times.Once);
        harness.LinkingService.Verify(service => service.ResetAutoLinkingState(It.IsAny<MetadataSource>(), It.IsAny<bool>()), Times.Never);
    }
}

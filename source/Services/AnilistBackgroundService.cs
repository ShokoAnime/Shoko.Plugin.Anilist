using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shoko.Abstractions.Config;
using Shoko.Abstractions.Core.Services;
using Shoko.Abstractions.Metadata;
using Shoko.Abstractions.Metadata.CrossReferences;
using Shoko.Abstractions.Metadata.Enums;
using Shoko.Abstractions.Metadata.Services;
using Shoko.Abstractions.Metadata.Storage;
using Shoko.Plugin.Anilist.Mapping;
using Shoko.Plugin.Anilist.Metadata;
using Shoko.Plugin.Anilist.Storage;

namespace Shoko.Plugin.Anilist.Services;

/// <summary>
/// The little the plugin does on its own schedule: registering the image
/// template, a one-time refresh of every linked anime when nothing is stored
/// yet, labelling the stored characters' native names, and the daily purge
/// of anime nothing has linked to for a while.
/// </summary>
/// <remarks>
/// The refreshes and purges are the core's jobs; this only asks for them,
/// once the server has started and its queue is up.
/// </remarks>
/// <param name="systemService">The core's system service, for when the server has started.</param>
/// <param name="imageService">Registers the image template.</param>
/// <param name="seriesStore">The core's series store, asked whether anything is stored.</param>
/// <param name="store">The stores, for the characters and the anime they appear in.</param>
/// <param name="textManager">The core's text manager, for the characters' stored titles and overviews.</param>
/// <param name="crossReferences">The core's store of links, asked whether anything is linked.</param>
/// <param name="refreshService">The core's refresh service.</param>
/// <param name="purgeService">The core's purge service.</param>
/// <param name="configurationProvider">The plugin's configuration.</param>
/// <param name="logger">The logger.</param>
public sealed class AnilistBackgroundService(
    ISystemService systemService,
    AnilistImageService imageService,
    IMetadataSeriesStore seriesStore,
    AnilistStore store,
    IMetadataTextManager textManager,
    IMetadataCrossReferenceStore crossReferences,
    IMetadataRefreshService refreshService,
    IMetadataPurgeService purgeService,
    ConfigurationProvider<AnilistConfiguration> configurationProvider,
    ILogger<AnilistBackgroundService> logger
) : BackgroundService
{
    /// <summary>
    /// How often the unused anime are purged.
    /// </summary>
    public static readonly TimeSpan PurgeInterval = TimeSpan.FromHours(24);

    /// <summary>
    /// How long after the server started the first purge runs.
    /// </summary>
    public static readonly TimeSpan FirstPurgeDelay = TimeSpan.FromHours(1);

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            imageService.RegisterTemplateUrl();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Unable to register the AniList image template URL.");
        }

        await WaitForStart(stoppingToken).ConfigureAwait(false);
        RelabelCharacterNativeNames();
        await RefreshIfNothingStored(stoppingToken).ConfigureAwait(false);

        await Task.Delay(FirstPurgeDelay, stoppingToken).ConfigureAwait(false);
        using var timer = new PeriodicTimer(PurgeInterval);
        do
        {
            await PurgeUnused(stoppingToken).ConfigureAwait(false);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    /// <summary>
    /// Asks the core to refresh every linked AniList anime when the series
    /// store holds none of them, which is the case after an upgrade from a
    /// version that kept the anime elsewhere.
    /// </summary>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>How many refreshes were queued.</returns>
    public async Task<int> RefreshIfNothingStored(CancellationToken cancellationToken = default)
    {
        try
        {
            if (seriesStore.GetAllSeries(MetadataSource.AniList).Count > 0)
                return 0;

            if (!crossReferences.GetAllSeriesLinks(MetadataSource.AniList).Any(link => link.ProviderID is not null))
                return 0;

            logger.LogInformation("No AniList anime is stored while some are linked. Refreshing every linked AniList anime.");
            return await refreshService.RefreshAllLinked(MetadataSource.AniList, entityType: MetadataEntityType.Series, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Unable to refresh the linked AniList anime.");
            return 0;
        }
    }

    /// <summary>
    /// Gives the stored characters' native names stored without a language
    /// the language of the anime they appear in, as a refresh now does, when
    /// their script does not tell it. Characters stored before that keep
    /// their name in Han characters unlabelled until then, which leaves them
    /// without an original title. Runs on every start, touching only those
    /// still unlabelled.
    /// </summary>
    /// <returns>How many characters were relabelled.</returns>
    public int RelabelCharacterNativeNames()
    {
        try
        {
            var anilist = MetadataSource.AniList;
            var candidates = store.People.GetAllCharacters(anilist)
                .Where(character => string.IsNullOrEmpty(character.OriginalLanguageCode))
                .Select(character => (Character: character, Titles: textManager.GetTitles(character.ID, new() { Source = anilist, IsEnabled = null, IncludeInlineDefault = false })))
                .Where(entry => entry.Titles.Any(title => title.Type is TitleType.Official && title.Language is TitleLanguage.Unknown))
                .ToList();
            if (candidates.Count is 0)
                return 0;

            // The languages of the anime each character appears in, one per credit.
            var mediaLanguages = new Dictionary<MetadataGuid, List<string?>>();
            foreach (var series in store.Series.GetAllSeries(anilist))
            {
                foreach (var characterID in store.People.GetCast(series.ID).Select(cast => cast.CharacterID).OfType<MetadataGuid>().Distinct())
                {
                    if (!mediaLanguages.TryGetValue(characterID, out var languages))
                        mediaLanguages[characterID] = languages = [];
                    languages.Add(series.OriginalLanguageCode);
                }
            }

            var relabelled = new List<MetadataCharacterData>();
            foreach (var (character, titles) in candidates)
            {
                var native = titles.First(title => title.Type is TitleType.Official && title.Language is TitleLanguage.Unknown).Value;
                var mediaLanguageCode = mediaLanguages.TryGetValue(character.ID, out var languages) ? AnilistUtility.MostCommonLanguage(languages) : null;
                if (AnilistUtility.GuessCharacterLanguage(native, mediaLanguageCode) is not { } languageCode)
                    continue;

                relabelled.Add(new()
                {
                    ID = character.ID,
                    Titles = AnilistMediaMapper.RelabelNativeName(titles, languageCode),
                    OriginalLanguageCode = languageCode,
                    OriginalCountryCode = character.OriginalCountryCode,
                    Overviews = textManager.GetOverviews(character.ID, new() { Source = anilist, IsEnabled = null, IncludeInlineDefault = false }),
                    Type = character.Type,
                    Gender = character.Gender,
                    BirthDay = character.BirthDay,
                    Resources = character.Resources,
                });
            }

            foreach (var chunk in relabelled.Chunk(500))
                store.People.SaveCharacters(chunk);
            if (relabelled.Count > 0)
                logger.LogInformation("Labelled the native names of {Count} stored AniList characters with the language of their anime.", relabelled.Count);
            return relabelled.Count;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Unable to label the native names of the stored AniList characters.");
            return 0;
        }
    }

    /// <summary>
    /// Asks the core to purge the stored anime nothing links to and nothing
    /// has refreshed for longer than the settings allow.
    /// </summary>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>How many purges were queued.</returns>
    public async Task<int> PurgeUnused(CancellationToken cancellationToken = default)
    {
        var threshold = configurationProvider.Load().AutoPurgeUnlinkedAfterDays;
        if (threshold <= 0)
        {
            logger.LogTrace("Purging unused AniList anime is off. Skipping.");
            return 0;
        }

        try
        {
            return await purgeService.PurgeUnused(MetadataSource.AniList, DateTime.Now.AddDays(-threshold), MetadataEntityType.Series, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Unable to purge the unused AniList anime.");
            return 0;
        }
    }

    private async Task WaitForStart(CancellationToken cancellationToken)
    {
        if (systemService.IsStarted)
            return;

        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnStarted(object? sender, EventArgs eventArgs) => started.TrySetResult();
        systemService.Started += OnStarted;
        try
        {
            if (!systemService.IsStarted)
                await started.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            systemService.Started -= OnStarted;
        }
    }
}

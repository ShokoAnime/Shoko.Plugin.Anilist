# Shoko AniList Metadata

Brings anime metadata from [AniList](https://anilist.co/) into Shoko Server:
each anime with its episodes, titles, descriptions and ratings, its tags,
studios, cast and crew, relations and recommendations, its images and its
broadcast times. Shoko links your anime to AniList on its own, and you can fix
or add links by hand. It replaces the AniList support that used to ship in the
server.

Install it from the Shoko Core Repository under Settings → Plugin Management →
Browse. Releases are also on the
[releases page](https://github.com/ShokoAnime/Shoko.Plugin.Anilist/releases).

Found a problem? Open an issue in the
[issue tracker](https://github.com/ShokoAnime/Shoko.Plugin.Anilist/issues) or
ask on [Discord](https://discord.gg/shokoanime).

## What you get

- **Anime and episodes.** Titles, the English description, ratings and vote
  counts, release status, source material, original language, popularity,
  air dates, and the adult flag as restricted. AniList has no seasons, so the
  episodes sit in none.
- **Tags and genres.** AniList's tags, with its genres as genre tags.
- **Studios, cast and crew.** With the people's genders, birthdays and other
  names, and the characters they voice.
- **Relations and recommendations.** Links to the related anime, and
  AniList's recommendations as suggestions.
- **Source material, told apart.** AniList files Korean and Chinese comics as
  manga, so the plugin works out manhwa and manhua from the comic's country,
  and an adult anime from a visual novel or game as an eroge.
- **Images.** Covers, banners and portraits. Which are downloaded is set in
  the server's image settings for AniList.
- **Broadcast times.** AniList's airing times become an airing schedule for
  each anime.
- **Links.** The AniList page, MyAnimeList, the trailer, and the sites AniList
  lists, plus the MyAnimeList ID.

When Shoko searches AniList for an anime, it searches by its titles, with and
without a sequel suffix or subtitle, and compares titles, start dates and
episode counts. AniList keeps every season as an anime of its own, so for the
closest candidates it also compares broadcast times to tell a split cour or a
remake apart. Episodes are then lined up with AniDB's, and again after each
full refresh.

AniList serves the cast and crew 25 at a time, so a long show costs many
requests. The first page comes with the anime for free. The rest is only read
while the provider's `character` and `creator` kinds are on under Settings →
Metadata, and both start off.

## Settings

You'll find the settings under Settings → Metadata, with the AniList provider.

| Setting | Default | What it does |
|---|---|---|
| Consider Existing Other Links | off | Leaves out AniList episodes another anime is already linked to when matching episodes. |
| Auto-Search Candidate Count | `5` | How many search results (`1` to `20`) to score when linking. |
| Purge Unlinked After (Days) | `14` | How long (`0` to `365` days) an anime nothing links to stays stored after its last refresh. `0` keeps it forever. |
| Download Cast and Crew | on | Downloads the characters, their voice actors and the staff. Off, a full refresh clears them. |
| Download Relations | on | Downloads the relations to other anime. Off, a full refresh clears them. |
| Download Recommendations | on | Downloads AniList's recommendations. Off, a full refresh clears them. |
| Recommendation Depth | While well rated | How far down the recommendations to read: the first page only, while a page still ends well rated, or everything. Only applies while recommendations are downloaded. |
| Image CDN URL | none | A base URL, or a template with `{0}`, for AniList's image CDN. |
| Max Requests Per Window | `1` | How many requests (`1` to `90`) may go out in one window. |
| Window Duration (ms) | `4000` | How long one window lasts (`1000` to `120000`). |

Some can also be set with environment variables, which win over the file:
`ANILIST_IMAGE_CDN_URL`, `ANILIST_RATE_LIMIT_MAX_REQUESTS_PER_WINDOW` and
`ANILIST_RATE_LIMIT_WINDOW_DURATION_MS`.

## Being gentle with AniList

All requests go through the plugin's own rate limiter, set by the two window
settings above. Up to four AniList jobs of each kind run at once. When AniList
asks us to slow down, or keeps answering with server errors, the plugin pauses
its jobs until AniList is ready again, and the server shows that AniList is
paused and why.

Once a day, the plugin has the server purge the stored anime nothing links to,
after the days set above. After an upgrade from a version that kept the anime
elsewhere, it has every linked anime refreshed once.

## For developers

```
dotnet build Shoko.Plugin.Anilist.slnx -c Release
dotnet test tests/Shoko.Plugin.Anilist.Tests.csproj
scripts/pack.sh --zip
```

The plugin builds against the published `Shoko.Abstractions` package, pinned
in `Directory.Build.props`, which the server supplies at runtime.

`AnilistMetadataProvider` is the plugin's metadata provider. The core runs the
refresh, search, link, image and purge jobs and calls into it; the plugin asks
AniList and writes what it gets into the core's shared stores, which the core
reads everything back from. `AnilistSuspensionProvider` reports the rate limit
and server error pauses. The plugin serves no endpoints of its own: the core's
`/api/v3/Metadata/anilist/...` routes cover it.

What only AniList has (the season, the licensed flag, the mean score, the
airing schedule's IDs and the image paths) is kept in the plugin's own
database, `AnilistDbContext`, at `data/<plugin-id>/anilist.db3`. The server
creates it, migrates it while it starts and removes it when the plugin is
uninstalled with its data. Entity Framework Core is referenced for compiling
only, since the server loads it. To add a migration, from the repository root:

```
dotnet tool install --global dotnet-ef --version 10.0.12
dotnet ef migrations add <Name> --project source --startup-project design --output-dir Storage/Migrations
```

`design/` only exists for that tool and is never shipped. Keep migrations free
of raw SQL, and never edit one a release has shipped.

To release, publish a GitHub release with a `v<version>` tag on `stable`. The
workflow builds the plugin, attaches the archive to the release and adds the
release to the manifest on the `metadata` branch.

## License

MIT. See [LICENSE](LICENSE).

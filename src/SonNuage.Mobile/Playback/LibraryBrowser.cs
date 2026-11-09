using AndroidX.Media3.Common;
using SonNuage.Contracts;
using SonNuage.Mobile.Services;
using SonNuage.UI.Services;

namespace SonNuage.Mobile.Playback;

/// <summary>Arborescence de navigation Android Auto et résolution des identifiants en pistes à jouer.</summary>
public sealed class LibraryBrowser(ApiClient api, AuthSession auth)
{
    private const int ListLimit = 200;

    public async Task<bool> EnsureSignedInAsync()
    {
        await auth.EnsureLoadedAsync();
        return auth.IsAuthenticated;
    }

    public MediaItem RootItem() => Folder(BrowseIds.Root, "SonNuage", MediaMetadata.MediaTypeFolderMixed);

    public MediaItem ResumeRootItem() => Folder(BrowseIds.ResumeRoot, "SonNuage", MediaMetadata.MediaTypeFolderMixed);

    /// <summary>La piste de la file sauvegardée, sous l'identifiant qui relance toute la file (voir OnSetMediaItems).</summary>
    public static MediaItem ResumeItem(MediaItem track) => track.BuildUpon()!.SetMediaId(BrowseIds.Resume)!.Build()!;

    public async Task<IList<MediaItem>> ChildrenAsync(string parentId)
    {
        if (!await EnsureSignedInAsync()) return [];
        var (kind, id) = BrowseIds.Parse(parentId);
        switch (kind)
        {
            case BrowseIds.Root:
                // Android Auto affiche les 4 premiers dossiers de la racine comme onglets.
                return
                [
                    Folder(BrowseIds.Playlists, "Playlists", MediaMetadata.MediaTypeFolderPlaylists),
                    Folder(BrowseIds.Albums, "Albums", MediaMetadata.MediaTypeFolderAlbums),
                    Folder(BrowseIds.Artists, "Artistes", MediaMetadata.MediaTypeFolderArtists),
                    Folder(BrowseIds.Recent, NativeText.Get("android.recent", "Récents"), MediaMetadata.MediaTypeFolderMixed),
                ];
            case BrowseIds.Playlists:
            {
                var items = new List<MediaItem>
                {
                    Playable(BrowseIds.ShuffleAll, NativeText.Get("player.shuffle", "Lecture aléatoire"), NativeText.Get("editor.wholeLibrary", "Toute la bibliothèque"), null, MediaMetadata.MediaTypePlaylist),
                    Container(BrowseIds.Favorites, NativeText.Get("nav.likedSongs", "Titres likés"), null, null, MediaMetadata.MediaTypePlaylist),
                };
                if (await StationsAsync() is { Count: > 0 })
                {
                    items.Add(Folder(BrowseIds.Radios, NativeText.Get("radio.title", "Radios"), MediaMetadata.MediaTypeFolderRadioStations));
                }
                foreach (var p in await api.PlaylistsAsync() ?? [])
                {
                    items.Add(Container(BrowseIds.Playlist(p.Id), p.Name, $"{p.TrackCount} titres", api.CoverUrl(p.CoverAlbumId, 300), MediaMetadata.MediaTypePlaylist));
                }
                return items;
            }
            case BrowseIds.Albums:
            {
                var page = await api.AlbumsAsync("recent", 0, ListLimit);
                return (page?.Items ?? []).Select(AlbumItem).ToList();
            }
            case BrowseIds.Artists:
            {
                var page = await api.ArtistsAsync(0, ListLimit);
                return (page?.Items ?? []).Select(a => Container(BrowseIds.Artist(a.Id), a.Name, $"{a.AlbumCount} albums",
                    api.CoverUrl(a.CoverAlbumId, 300), MediaMetadata.MediaTypeArtist)).ToList();
            }
            case BrowseIds.Radios:
                return (await StationsAsync() ?? []).Select(s => Playable(BrowseIds.Station(s.Id), s.Name, s.Description,
                    api.CoverUrl(s.CoverAlbumIds.Count > 0 ? s.CoverAlbumIds[0] : null, 300), MediaMetadata.MediaTypeRadioStation)).ToList();
            case BrowseIds.ArtistKind:
            {
                var artist = await api.ArtistAsync(id);
                if (artist is null) return [];
                var items = new List<MediaItem> { Playable(parentId, NativeText.Get("artist.playAll", "Tout lire"), artist.Artist.Name, null, MediaMetadata.MediaTypePlaylist) };
                items.AddRange(artist.Albums.Select(AlbumItem));
                return items;
            }
            default:
                return (await TracksAsync(parentId)).Select(t => TrackItem(t, parentId)).ToList();
        }
    }

    public async Task<IList<MediaItem>> SearchAsync(string query)
    {
        if (!await EnsureSignedInAsync() || string.IsNullOrWhiteSpace(query)) return [];
        var result = await api.SearchAsync(query, 30);
        if (result is null) return [];
        var items = new List<MediaItem>();
        items.AddRange(result.Tracks.Select(t => TrackItem(t, null)));
        items.AddRange(result.Albums.Select(AlbumItem));
        items.AddRange(result.Artists.Select(a => Container(BrowseIds.Artist(a.Id), a.Name, "Artiste", api.CoverUrl(a.CoverAlbumId, 300), MediaMetadata.MediaTypeArtist)));
        return items;
    }

    /// <summary>
    /// Transforme ce que demande un contrôleur (Android Auto, Assistant, l'appli) en file jouable.
    /// Les éléments venant de l'appli portent déjà la piste complète. Les autres sont des identifiants à charger.
    /// </summary>
    public async Task<(List<MediaItem> Items, int StartIndex)> ResolveAsync(IList<MediaItem> requested, int startIndex)
    {
        if (!await EnsureSignedInAsync()) return ([], 0);
        if (requested.Count == 1 && TrackItems.ToTrack(requested[0]) is null)
        {
            var mediaId = requested[0].MediaId ?? "";
            var search = TrackItems.SearchQuery(requested[0]);
            if (mediaId.Length == 0 && search is not null)
            {
                // « Ok Google, mets <requête> sur SonNuage »
                var found = await api.SearchAsync(search, 50);
                var tracks = found?.Tracks.ToList() ?? [];
                if (tracks.Count == 0 && found?.Albums.FirstOrDefault() is { } album) tracks = await TracksAsync(BrowseIds.Album(album.Id));
                return (tracks.Select(Item).ToList(), 0);
            }
            if (BrowseIds.StationId(mediaId) is { } station)
            {
                // Radio thématique (Android Auto ou l'appli) : un premier lot, que la lecture complète ensuite.
                var batch = await api.RadioStationNextAsync(station, new StationNextRequest([], QueueRefill.StationFirstBatch)) ?? [];
                return (batch.Select(t => StationItem(t, station)).ToList(), 0);
            }
            if (BrowseIds.SplitContext(mediaId) is { } inContext)
            {
                var context = await TracksAsync(inContext.Context);
                return (context.Select(Item).ToList(), Math.Max(0, context.FindIndex(t => t.Id == inContext.TrackId)));
            }
            if (!mediaId.StartsWith($"{BrowseIds.TrackKind}:", StringComparison.Ordinal))
            {
                var tracks = await TracksAsync(mediaId);
                return (tracks.Select(Item).ToList(), 0);
            }
        }

        var resolved = new List<MediaItem>(requested.Count);
        var missing = new List<Guid>();
        foreach (var item in requested)
        {
            // La radio d'origine reste attachée : la file restaurée ou ré-envoyée par l'appli continue de la suivre.
            if (TrackItems.ToTrack(item) is { } track) resolved.Add(StationItem(track, TrackItems.Station(item)));
            else if (BrowseIds.Parse(item.MediaId ?? "") is (BrowseIds.TrackKind, var id)) missing.Add(id);
        }
        if (missing.Count > 0)
        {
            resolved.AddRange((await api.LookupTracksAsync(missing) ?? []).Select(Item));
        }
        return (resolved, Math.Clamp(startIndex, 0, Math.Max(0, resolved.Count - 1)));
    }

    public MediaItem Item(TrackDto track) => StationItem(track, null);

    /// <summary>Piste venue de la radio <paramref name="station"/> (null : d'ailleurs).</summary>
    public MediaItem StationItem(TrackDto track, string? station) =>
        TrackItems.From(track, api.CoverUrl(track.HasCover ? track.AlbumId : null, 600), station);

    /// <summary>
    /// Radios du serveur ; aucune sur un serveur antérieur (404, lu comme null) ou injoignable : le reste du dossier
    /// s'affiche quand même.
    /// </summary>
    private async Task<List<RadioStationDto>?> StationsAsync()
    {
        try
        {
            return await api.RadioStationsAsync();
        }
        catch (Exception ex) when (ex is ApiException || ServerFailure.IsNetwork(ex))
        {
            return null;
        }
    }

    private async Task<List<TrackDto>> TracksAsync(string contextId)
    {
        var (kind, id) = BrowseIds.Parse(contextId);
        return kind switch
        {
            BrowseIds.AlbumKind => (await api.AlbumAsync(id))?.Tracks.ToList() ?? [],
            BrowseIds.ArtistKind => await api.ArtistTracksAsync(id) ?? [],
            BrowseIds.PlaylistKind => (await api.PlaylistAsync(id))?.Entries.Select(e => e.Track).ToList() ?? [],
            BrowseIds.Favorites => await api.FavoritesAsync() ?? [],
            BrowseIds.Recent => await api.RecentAsync(50) ?? [],
            BrowseIds.ShuffleAll => await api.RandomTracksAsync(100) ?? [],
            _ => [],
        };
    }

    private MediaItem AlbumItem(AlbumDto a) =>
        Container(BrowseIds.Album(a.Id), a.Title, a.ArtistName, api.CoverUrl(a.HasCover ? a.Id : null, 300), MediaMetadata.MediaTypeAlbum);

    private MediaItem TrackItem(TrackDto t, string? context)
    {
        var item = Item(t);
        return context is null ? item : item.BuildUpon()!.SetMediaId(BrowseIds.InContext(t.Id, context))!.Build()!;
    }

    private static MediaItem Folder(string id, string title, int mediaType) => Build(id, title, null, null, mediaType, browsable: true, playable: false);
    private static MediaItem Container(string id, string title, string? subtitle, string? artwork, int mediaType) => Build(id, title, subtitle, artwork, mediaType, browsable: true, playable: true);
    private static MediaItem Playable(string id, string title, string? subtitle, string? artwork, int mediaType) => Build(id, title, subtitle, artwork, mediaType, browsable: false, playable: true);

    private static MediaItem Build(string id, string title, string? subtitle, string? artwork, int mediaType, bool browsable, bool playable)
    {
        var metadata = new MediaMetadata.Builder()
            .SetTitle(title)!
            .SetSubtitle(subtitle)!
            .SetArtist(subtitle)!
            .SetArtworkUri(artwork is null ? null : Android.Net.Uri.Parse(artwork))!
            .SetIsBrowsable(Java.Lang.Boolean.ValueOf(browsable))!
            .SetIsPlayable(Java.Lang.Boolean.ValueOf(playable))!
            .SetMediaType(Java.Lang.Integer.ValueOf(mediaType))!
            .Build();
        return new MediaItem.Builder().SetMediaId(id)!.SetMediaMetadata(metadata)!.Build()!;
    }
}

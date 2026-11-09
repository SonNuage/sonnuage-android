namespace SonNuage.Mobile.Playback;

/// <summary>
/// Identifiants de l'arborescence Android Auto. Une piste listée dans un contexte porte ce contexte
/// (« track:{id}@album:{id} ») pour qu'un appui lance tout l'album à partir de cette piste.
/// Sans type Android : testé dans tests/SonNuage.Mobile.Tests.
/// </summary>
public static class BrowseIds
{
    public const string Root = "root";
    public const string Playlists = "playlists";
    public const string Albums = "albums";
    public const string Artists = "artists";
    public const string Recent = "recent";
    public const string Favorites = "favorites";
    /// <summary>Dossier des radios thématiques du serveur (absent si le serveur n'en propose pas).</summary>
    public const string Radios = "radios";
    public const string ShuffleAll = "shuffle";
    public const string ResumeRoot = "resume-root";
    public const string Resume = "resume";

    /// <summary>Préfixes des identifiants qui portent un Guid (« kind:{id} »).</summary>
    public const string TrackKind = "track";
    public const string AlbumKind = "album";
    public const string ArtistKind = "artist";
    public const string PlaylistKind = "playlist";
    /// <summary>Radio thématique : « station:{id} », l'identifiant du serveur n'est pas un Guid (« halloween »…).</summary>
    public const string StationKind = "station";

    /// <summary>Dossiers qu'Android Auto doit recharger quand le compte change.</summary>
    public static readonly IReadOnlyList<string> AccountFolders = [Root, Playlists, Albums, Artists, Recent, Radios];

    public static string Track(Guid id) => $"{TrackKind}:{id:N}";
    public static string Album(Guid id) => $"{AlbumKind}:{id:N}";
    public static string Artist(Guid id) => $"{ArtistKind}:{id:N}";
    public static string Playlist(Guid id) => $"{PlaylistKind}:{id:N}";
    public static string Station(string stationId) => $"{StationKind}:{stationId}";
    public static string InContext(Guid trackId, string context) => $"{Track(trackId)}@{context}";

    public static (string Kind, Guid Id) Parse(string id)
    {
        var colon = id.IndexOf(':');
        return colon < 0 ? (id, Guid.Empty) : (id[..colon], Guid.TryParse(id[(colon + 1)..], out var g) ? g : Guid.Empty);
    }

    /// <summary>Identifiant de piste valide (« track:{id} »), sinon null.</summary>
    public static Guid? TrackId(string? mediaId) =>
        Parse(mediaId ?? "") is (TrackKind, var id) && id != Guid.Empty ? id : null;

    /// <summary>Identifiant de radio d'un élément « station:{id} », sinon null.</summary>
    public static string? StationId(string? mediaId) =>
        mediaId is not null && mediaId.StartsWith($"{StationKind}:", StringComparison.Ordinal) && mediaId.Length > StationKind.Length + 1
            ? mediaId[(StationKind.Length + 1)..]
            : null;

    /// <summary>
    /// « track:{id}@{contexte} » : la piste (Guid.Empty si illisible, la lecture part alors du début du contexte) et
    /// son contexte. Null sans « @ ».
    /// </summary>
    public static (Guid TrackId, string Context)? SplitContext(string mediaId)
    {
        var at = mediaId.IndexOf('@');
        return at > 0 ? (Parse(mediaId[..at]).Id, mediaId[(at + 1)..]) : null;
    }
}

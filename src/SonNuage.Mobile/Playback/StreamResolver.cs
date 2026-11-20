using AndroidX.Media3.Common;
using AndroidX.Media3.DataSource;
using AndroidX.Media3.DataSource.Cache;
using SonNuage.Contracts;
using SonNuage.Mobile.Services;
using SonNuage.UI.Services;

namespace SonNuage.Mobile.Playback;

/// <summary>
/// Résout sonnuage://track/{id} en URL de flux au moment du chargement : qualité selon le réseau (Wi-Fi / mobile)
/// et jeton média renouvelé. La clé de cache ne dépend que de la piste et de la qualité, pas du jeton.
/// Si une autre qualité est déjà entièrement en cache (pré-chargée en Wi-Fi par exemple), elle est préférée :
/// aucun octet réseau, et l'adresse elle-même n'est pas indispensable.
/// Le cache est cloisonné par compte : la clé commence par l'identifiant du compte actif.
/// </summary>
public sealed class StreamResolver(ApiClient api, PlaybackSettings settings, AccountStore accounts, ICache cache, YoutubeDirect youtube)
    : Java.Lang.Object, ResolvingDataSource.IResolver
{
    public DataSpec? ResolveDataSpec(DataSpec? dataSpec)
    {
        if (dataSpec is not null && TrackItems.VideoIdFromUri(dataSpec.Uri) is { } video) return ResolveYoutube(dataSpec, video);
        if (dataSpec is null || TrackItems.TrackIdFromUri(dataSpec.Uri) is not { } id) return dataSpec;

        var wanted = MobilePlatform.IsMetered() ? settings.MobileQuality : settings.Quality;
        StreamQuality? cached = new[] { wanted, settings.Quality, settings.MobileQuality }
            .Where(q => IsFullyCached(Key(id, q)))
            .Select(q => (StreamQuality?)q)
            .FirstOrDefault();
        var quality = cached ?? wanted;
        var key = Key(id, quality);

        // Piste entière en cache : le cache la lit sans jamais ouvrir l'adresse. Elle se joue donc aussi sans réseau
        // (tunnel) ou quand le jeton média n'a pas pu être renouvelé.
        DataSpec FromCache() => dataSpec.BuildUpon()!.SetKey(key)!.Build()!;
        string? url;
        try
        {
            // Appelé sur le thread de chargement d'ExoPlayer : attente synchrone acceptable (pas de contexte UI).
            url = api.StreamUrlAsync(id, quality).GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ServerFailure.IsNetwork(ex))
        {
            if (cached is not null) return FromCache();
            // En IOException, ExoPlayer réessaie le chargement. Une exception .NET lui arrivait en RuntimeException :
            // erreur « inattendue », lecture arrêtée sans nouvel essai.
            throw new Java.IO.IOException(ex.Message);
        }
        if (url is not null) return dataSpec.BuildUpon()!.SetUri(Android.Net.Uri.Parse(url))!.SetKey(key)!.Build();
        if (cached is not null) return FromCache();
        throw new Java.IO.IOException(NativeText.Get("android.sessionExpired", "Session expirée : reconnectez-vous dans l'application."));
    }

    /// <summary>
    /// Titre YouTube direct : URL googlevideo (déjà en cache après la recherche du Jam, sinon ~0,2 s). Toujours avec un
    /// en-tête Range : sans lui, YouTube bride le débit (1 Mo en 30 s au lieu de 0,3 s).
    /// </summary>
    private DataSpec ResolveYoutube(DataSpec dataSpec, string video)
    {
        var key = $"yt/{video}";
        var cachedLength = cache.GetContentMetadata(key)?.Get("exo_len", -1) ?? -1;
        if (cachedLength > 0 && cache.IsCached(key, 0, cachedLength)) return dataSpec.BuildUpon()!.SetKey(key)!.Build()!;
        YoutubeAudio? audio;
        try
        {
            audio = youtube.AudioAsync(video).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            throw new Java.IO.IOException(ex.Message);
        }
        if (audio is null) throw new Java.IO.IOException($"YouTube : flux indisponible ({video})");
        var builder = dataSpec.BuildUpon()!.SetUri(Android.Net.Uri.Parse(audio.Url))!.SetKey(key)!;
        // Range explicite même pour une lecture depuis le début (ExoPlayer n'en met que pour un décalage).
        if (dataSpec.Position == 0 && dataSpec.Length == C.LengthUnset)
        {
            var headers = new Dictionary<string, string>();
            if (dataSpec.HttpRequestHeaders is { } existing)
                foreach (System.Collections.DictionaryEntry e in existing)
                    if (e.Key?.ToString() is { } k && e.Value?.ToString() is { } v) headers[k] = v;
            headers["Range"] = "bytes=0-";
            builder.SetHttpRequestHeaders(headers);
        }
        return builder.Build()!;
    }

    public string Key(Guid id, StreamQuality quality) => $"{accounts.Active?.Id:N}/{id:N}-{quality.ToWire()}";

    private bool IsFullyCached(string key)
    {
        var length = cache.GetContentMetadata(key)?.Get("exo_len", -1) ?? -1;
        return length > 0 && cache.IsCached(key, 0, length);
    }
}

using System.Text.Json;

namespace SonNuage.Mobile.Playback;

/// <summary>
/// Échecs attendus d'un appel au serveur fait en arrière-plan (lecture infinie, historique, reprise) : pas de réseau,
/// délai dépassé (HttpClient lève alors TaskCanceledException), réponse illisible. Ils ne sont pas des bugs : on les
/// tait. Le reste (NullReference…) doit remonter jusqu'au journal de plantage. ApiException (refus du serveur) vient
/// de SonNuage.UI et se teste à côté. Sans type Android : testé dans tests/SonNuage.Mobile.Tests.
/// </summary>
public static class ServerFailure
{
    public static bool IsNetwork(Exception ex) =>
        ex is HttpRequestException or TaskCanceledException or TimeoutException or IOException or JsonException;
}

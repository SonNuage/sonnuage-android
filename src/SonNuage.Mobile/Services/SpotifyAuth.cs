using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SonNuage.Contracts;
using SonNuage.UI.Services;

namespace SonNuage.Mobile.Services;

/// <summary>
/// Connexion à Spotify sur le téléphone (OAuth PKCE, redirection en boucle locale), comme l'appli de bureau Spotify :
/// ouvre la page de connexion dans un onglet système, récupère le code sur <c>http://127.0.0.1:&lt;port&gt;/login</c>, puis
/// l'échange contre des jetons. Les jetons sont renvoyés à l'appli, qui les confie au serveur. Aucun secret client
/// n'est nécessaire (PKCE).
/// </summary>
public sealed class SpotifyAuth : IMusicServiceAuth
{
    private const string Scopes = "streaming user-read-playback-state user-read-currently-playing";

    public bool CanConnect(string service) => service == Contracts.Jam.Spotify;

    public async Task<MusicServiceTokenRequest?> ConnectAsync(string service, CancellationToken ct)
    {
        if (service != Contracts.Jam.Spotify) return null;

        var verifier = Base64Url(RandomNumberGenerator.GetBytes(48));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var redirectUri = $"http://127.0.0.1:{port}/login";

        var authorizeUrl = "https://accounts.spotify.com/authorize?response_type=code" +
            $"&client_id={SpotifyClientId}" +
            $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
            "&code_challenge_method=S256" +
            $"&code_challenge={challenge}" +
            $"&scope={Uri.EscapeDataString(Scopes)}";

        // Fenêtre d'attente : l'utilisateur se connecte dans l'onglet, puis revient. Au-delà, on abandonne.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));

        await MainThread.InvokeOnMainThreadAsync(() => Browser.Default.OpenAsync(new Uri(authorizeUrl), BrowserLaunchMode.SystemPreferred));

        var code = await WaitForCodeAsync(listener, timeout.Token);
        listener.Stop();
        if (code is null) return null;

        return await ExchangeAsync(code, verifier, redirectUri, ct);
    }

    /// <summary>Attend la redirection de l'onglet sur la boucle locale et en extrait le paramètre <c>code</c>.</summary>
    private static async Task<string?> WaitForCodeAsync(TcpListener listener, CancellationToken ct)
    {
        try
        {
            using var client = await listener.AcceptTcpClientAsync(ct);
            using var stream = client.GetStream();
            var buffer = new byte[8192];
            var read = await stream.ReadAsync(buffer, ct);
            var request = Encoding.ASCII.GetString(buffer, 0, read);
            // Première ligne : « GET /login?code=…&… HTTP/1.1 ».
            var firstLine = request.Split('\n', 2)[0];
            var target = firstLine.Split(' ') is { Length: >= 2 } parts ? parts[1] : "";
            string? code = null;
            var q = target.IndexOf('?');
            if (q >= 0)
                foreach (var pair in target[(q + 1)..].Split('&'))
                {
                    var kv = pair.Split('=', 2);
                    if (kv.Length == 2 && kv[0] == "code") code = Uri.UnescapeDataString(kv[1]);
                }

            var body = code is null
                ? "<h2>Connexion annulée</h2><p>Vous pouvez revenir à SonNuage.</p>"
                : "<h2>Spotify connecté ✓</h2><p>Revenez à SonNuage, c'est prêt.</p>";
            var html = $"<!doctype html><meta charset=utf-8><title>SonNuage</title><body style=\"font-family:sans-serif;text-align:center;padding:3rem\">{body}</body>";
            var response = $"HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {Encoding.UTF8.GetByteCount(html)}\r\nConnection: close\r\n\r\n{html}";
            await stream.WriteAsync(Encoding.UTF8.GetBytes(response), ct);
            return code;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>Échange le code contre des jetons (PKCE), et lit le nom affiché du compte.</summary>
    private static async Task<MusicServiceTokenRequest?> ExchangeAsync(string code, string verifier, string redirectUri, CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        using var body = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["client_id"] = SpotifyClientId,
            ["code_verifier"] = verifier,
        });
        using var res = await http.PostAsync("https://accounts.spotify.com/api/token", body, ct);
        if (!res.IsSuccessStatusCode) return null;
        using var doc = JsonDocument.Parse(await res.Content.ReadAsByteArrayAsync(ct));
        var root = doc.RootElement;
        if (!root.TryGetProperty("access_token", out var at)) return null;
        var access = at.GetString()!;
        var refresh = root.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null;
        var expires = root.TryGetProperty("expires_in", out var ei) ? ei.GetInt32() : 3600;

        string? displayName = null;
        try
        {
            using var me = new HttpRequestMessage(HttpMethod.Get, "https://api.spotify.com/v1/me");
            me.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", access);
            using var meRes = await http.SendAsync(me, ct);
            if (meRes.IsSuccessStatusCode)
            {
                using var meDoc = JsonDocument.Parse(await meRes.Content.ReadAsByteArrayAsync(ct));
                if (meDoc.RootElement.TryGetProperty("display_name", out var dn)) displayName = dn.GetString();
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or OperationCanceledException)
        {
            // Le nom est facultatif.
        }

        return new MusicServiceTokenRequest(Contracts.Jam.Spotify, access, refresh, expires, displayName);
    }

    /// <summary>Client id public de l'appli de bureau Spotify, seul reconnu par les endpoints de Jam.</summary>
    private const string SpotifyClientId = "65b708073fc0480ea92a077233ca87bd";

    private static string Base64Url(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

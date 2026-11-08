using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Runtime;
using AndroidX.Media3.Common;
using AndroidX.Media3.DataSource;
using AndroidX.Media3.DataSource.Cache;
using AndroidX.Media3.Database;
using AndroidX.Media3.ExoPlayer;
using AndroidX.Media3.ExoPlayer.Source;
using AndroidX.Media3.Extractor;
using AndroidX.Media3.Session;
using Google.Common.Util.Concurrent;
using SonNuage.Contracts;
using SonNuage.UI.Services;
using static AndroidX.Media3.Session.MediaLibraryService;

namespace SonNuage.Mobile.Playback;

/// <summary>
/// Service de lecture natif : ExoPlayer + MediaLibrarySession. Il porte la notification, les contrôles écran verrouillé
/// et casque, et l'arborescence Android Auto. L'interface Blazor le pilote via un MediaController (<see cref="Services.AndroidPlayer"/>).
/// <list type="bullet">
/// <item>Cache disque LRU de 1 Go : une piste déjà écoutée ou pré-chargée ne repasse plus par le réseau.</item>
/// <item>Pré-chargement des N pistes suivantes en entier dans ce cache (tunnels, zones blanches en voiture).</item>
/// <item>Tampon de lecture de 5 minutes.</item>
/// </list>
/// </summary>
[Service(Exported = true, ForegroundServiceType = ForegroundService.TypeMediaPlayback)]
[IntentFilter(["androidx.media3.session.MediaLibraryService", "android.media.browse.MediaBrowserService"])]
public sealed class PlaybackService : MediaLibraryService
{
    private const long CacheBytes = 1024L * 1024 * 1024;
    private static readonly object CacheLock = new();
    private static SimpleCache? _cache;

    private MediaLibrarySession? _session;
    private Prefetcher? _prefetcher;
    private AccountStore? _accounts;
    private QueueStore? _queue;
    private Crossfader? _crossfader;

    /// <summary>Clé de sauvegarde de la file actuellement chargée (celle du compte à qui elle appartient).</summary>
    private string? _queueKey;

    public override void OnCreate()
    {
        base.OnCreate();
        var services = IPlatformApplication.Current!.Services;
        var api = services.GetRequiredService<ApiClient>();
        var settings = services.GetRequiredService<PlaybackSettings>();
        var browser = services.GetRequiredService<LibraryBrowser>();
        var queue = _queue = services.GetRequiredService<QueueStore>();
        var accounts = _accounts = services.GetRequiredService<AccountStore>();
        _queueKey = queue.CurrentKey;
        var cache = Cache(this);

        var http = new DefaultHttpDataSource.Factory()
            .SetUserAgent("SonNuage-Android")!
            .SetConnectTimeoutMs(15_000)!
            .SetReadTimeoutMs(30_000)!
            .SetAllowCrossProtocolRedirects(true)!;
        var cached = new CacheDataSource.Factory()
            .SetCache(cache)!
            .SetUpstreamDataSourceFactory(http)!
            .SetFlags(CacheDataSource.FlagIgnoreCacheOnError)!;
        var resolver = new StreamResolver(api, settings, accounts, cache);
        var extractors = new DefaultExtractorsFactory()
            .SetConstantBitrateSeekingEnabled(true)!
            .SetConstantBitrateSeekingAlwaysEnabled(true)!;
        var loadControl = new DefaultLoadControl.Builder()
            .SetBufferDurationsMs(30_000, 300_000, 1_500, 3_000)!
            .Build();
        var audio = new AudioAttributes.Builder()
            .SetUsage(C.UsageMedia)!
            .SetContentType(C.AudioContentTypeMusic)!
            .Build();

        var sources = new ResolvingDataSource.Factory(cached, resolver);
        var exo = new ExoPlayerBuilder(this)
            .SetMediaSourceFactory(new DefaultMediaSourceFactory(sources, extractors))!
            .SetLoadControl(loadControl)!
            .SetAudioAttributes(audio, true)!
            .SetHandleAudioBecomingNoisy(true)!
            .SetWakeMode(C.WakeModeNetwork)!
            .Build()!;
        exo.ShuffleOrder = new ShuffleOrderUnshuffledShuffleOrder(0);
        var player = new ShufflingPlayer(exo);
        // Second lecteur du fondu enchaîné (voir Crossfader) : mêmes sources et même cache, sans focus audio ni
        // gestion du casque débranché, que le principal porte pour les deux.
        var tail = new ExoPlayerBuilder(this)
            .SetMediaSourceFactory(new DefaultMediaSourceFactory(sources, extractors))!
            .SetAudioAttributes(audio, false)!
            .Build()!;
        var crossfader = _crossfader = new Crossfader(exo, player, tail, settings);

        _prefetcher = new Prefetcher(cached, resolver, settings);

        var activity = PendingIntent.GetActivity(this, 0, new Intent(this, typeof(MainActivity)), PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);
        _session = new MediaLibrarySession.Builder(this, player, new LibraryCallback(browser, queue))
            .SetSessionActivity(activity!)!
            .Build();
        // Après la session : à chaque changement de piste, elle publie le nouveau titre (notification, Android Auto)
        // avant nos traitements.
        player.AddListener(new PlayerEvents(player, _prefetcher, crossfader, queue, () => _queueKey, api, settings, browser));
        accounts.ActiveChanged += OnAccountChanged;
    }

    /// <summary>
    /// Changement de compte : la file du compte quitté est sauvée sous sa clé, la lecture s'arrête, puis la file du
    /// nouveau compte est chargée (sans lecture) et Android Auto est invité à recharger son arborescence.
    /// </summary>
    private void OnAccountChanged() => MainThread.BeginInvokeOnMainThread(async () =>
    {
        if (_session?.Player is not { } player || _queue is not { } queue) return;
        queue.Save(player, _queueKey);
        _queueKey = null;
        _prefetcher?.Cancel();
        // Stop() garde playWhenReady : sans ceci, la file du nouveau compte repartirait toute seule.
        player.PlayWhenReady = false;
        player.Stop();
        player.ClearMediaItems();
        _queueKey = queue.CurrentKey;
        foreach (var id in BrowseIds.AccountFolders)
        {
            _session.NotifyChildrenChanged(id, int.MaxValue, null);
        }
        var saved = await queue.LoadAsync();
        if (saved is null || _session?.Player is not { } current || current.MediaItemCount > 0) return;
        saved.LoadInto(current);
    });

    public override MediaLibrarySession? OnGetSessionFromMediaLibraryService(MediaSession.ControllerInfo? controllerInfo) => _session;

    // Le binding expose aussi la méthode de la classe parente comme abstraite.
    public override MediaSession? OnGetSession(MediaSession.ControllerInfo? controllerInfo) => _session;

    public override void OnDestroy()
    {
        if (_accounts is not null) _accounts.ActiveChanged -= OnAccountChanged;
        _prefetcher?.Cancel();
        _crossfader?.Release();
        _crossfader = null;
        if (_session is not null)
        {
            _session.Player?.Release();
            _session.Release();
            _session = null;
        }
        base.OnDestroy();
    }

    /// <summary>Un seul SimpleCache par dossier et par processus : le service peut être recréé, le cache non.</summary>
    private static SimpleCache Cache(Context context)
    {
        lock (CacheLock)
        {
            return _cache ??= new SimpleCache(
                new Java.IO.File(context.CacheDir, "audio"),
                new LeastRecentlyUsedCacheEvictor(CacheBytes),
                new StandaloneDatabaseProvider(context));
        }
    }

    /// <summary>Événements du lecteur : pré-chargement, historique d'écoute, sauvegarde de la file.</summary>
    private sealed class PlayerEvents(Media3IPlayer player, Prefetcher prefetcher, Crossfader crossfader, QueueStore queue, Func<string?> queueKey, ApiClient api, PlaybackSettings settings, LibraryBrowser browser) : Java.Lang.Object, IPlayerListener
    {
        // Un seul appel de lecture infinie à la fois : deux changements de piste rapprochés ne doivent pas doubler l'ajout.
        private bool _extending;

        public void OnTimelineChanged(Timeline? timeline, int reason) => prefetcher.Schedule(player);

        // Remis à la boucle suivante du thread : la session finit de publier le nouveau titre avant.
        private readonly Android.OS.Handler _handler = new(Android.OS.Looper.MainLooper!);

        public void OnMediaItemTransition(MediaItem? mediaItem, int reason)
        {
            // Clé lue maintenant, pas au passage du Post. Le changement de compte vide la file (clé nulle), puis
            // passe à la clé du compte suivant avant ce Post : relue plus tard, elle enregistrait la file vide sous le
            // compte suivant et effaçait sa file sauvegardée.
            var key = queueKey();
            _handler.Post(() =>
            {
                prefetcher.Schedule(player);
                if (key is null || key != queueKey()) return;
                queue.Save(player, key);
                Extend();
            });
        }

        /// <summary>
        /// Lecture infinie (même règle que le web, <see cref="InfinitePlay"/>) : garder l'avance réglée après la piste
        /// en cours en demandant au serveur des morceaux proches. Appelé sur le thread principal.
        /// </summary>
        private void Extend()
        {
            if (!settings.InfinitePlay || player.RepeatMode != PlayerConst.RepeatOff || _extending || queueKey() is not { } key) return;
            var count = player.MediaItemCount;
            var needed = InfinitePlay.Needed(count, player.CurrentMediaItemIndex, settings.InfiniteAhead);
            if (needed == 0) return;
            var ids = new List<Guid>(count);
            for (var i = 0; i < count; i++)
            {
                if (TrackItems.ToTrack(player.GetMediaItemAt(i)) is { } t) ids.Add(t.Id);
            }
            var request = new RadioRequest(InfinitePlay.Seeds(ids, player.CurrentMediaItemIndex), ids.TakeLast(RadioRequest.MaxIds).ToList(), needed);
            _extending = true;
            _ = Task.Run(async () =>
            {
                List<TrackDto>? tracks = null;
                try
                {
                    tracks = await api.RadioAsync(request);
                }
                catch (Exception ex) when (ex is ApiException || ServerFailure.IsNetwork(ex))
                {
                    // Sans réseau, la file s'arrêtera simplement à son dernier morceau.
                }
                finally
                {
                    // Toujours, même sur un échec inattendu : sans cela la lecture infinie restait bloquée jusqu'à la
                    // fin du service (un délai dépassé suffisait).
                    _handler.Post(() =>
                    {
                        _extending = false;
                        // La file a changé de compte entre-temps : ces morceaux ne la concernent plus.
                        if (tracks is not { Count: > 0 } || queueKey() != key) return;
                        player.AddMediaItems(tracks.Select(browser.Item).ToList());
                    });
                }
            });
        }

        public void OnRepeatModeChanged(int repeatMode) => prefetcher.Schedule(player);

        public void OnIsPlayingChanged(bool isPlaying)
        {
            if (!isPlaying) queue.Save(player, queueKey());
        }

        public void OnPositionDiscontinuity(PlayerPositionInfo? oldPosition, PlayerPositionInfo? newPosition, int reason)
        {
            if (oldPosition is null || newPosition is null) return;
            // Un fondu enchaîné quitte la piste par un saut (SEEK), mais elle a été écoutée jusqu'au bout. En répétition
            // d'une piste, il revient sur le même index : compté quand même, une fois.
            var completed = reason == PlayerConst.DiscontinuityAutoTransition || crossfader.TakeHandover(oldPosition.MediaItem);
            if (oldPosition.MediaItemIndex == newPosition.MediaItemIndex && !completed) return;
            Report(oldPosition.MediaItem, oldPosition.PositionMs / 1000.0, completed);
        }

        public void OnPlaybackStateChanged(int playbackState)
        {
            if (playbackState == PlayerConst.StateEnded) Report(player.CurrentMediaItem, player.CurrentPosition / 1000.0, completed: true);
        }

        /// <summary>Envoie l'écoute à l'historique si elle compte (<see cref="ListenRule"/>).</summary>
        private void Report(MediaItem? item, double seconds, bool completed)
        {
            // Pas de clé : la file est en cours de vidage pour un changement de compte, l'écoute n'est pas à imputer.
            if (queueKey() is null || TrackItems.ToTrack(item) is not { } track) return;
            if (ListenRule.Played(seconds, track.DurationSeconds, completed) is not { } played) return;
            _ = Task.Run(async () =>
            {
                try
                {
                    await api.ReportPlayAsync(new PlayEventRequest(track.Id, played, completed));
                }
                catch (Exception ex) when (ex is ApiException || ServerFailure.IsNetwork(ex))
                {
                    // Hors ligne ou refusé : cette écoute n'est pas comptée, comme sur le web.
                }
            });
        }
    }

    /// <summary>Arborescence Android Auto, recherche, résolution des éléments demandés et reprise de lecture.</summary>
    private sealed class LibraryCallback(LibraryBrowser browser, QueueStore queue) : Java.Lang.Object, MediaLibrarySession.ICallback
    {
        private const int ErrorNotSupported = -6;
        private readonly Dictionary<string, IList<MediaItem>> _searches = [];

        // Android Auto et le système demandent une racine « récents » (isRecent) pour proposer l'appli en suggestion et
        // reprendre la lecture : elle contient la piste de la file sauvegardée.
        public IListenableFuture? OnGetLibraryRoot(MediaLibrarySession? session, MediaSession.ControllerInfo? browserInfo, LibraryParams? @params) =>
            FutureExtensions.Completed(LibraryResult.OfItem(@params?.IsRecent == true ? browser.ResumeRootItem() : browser.RootItem(), @params));

        public IListenableFuture? OnGetItem(MediaLibrarySession? session, MediaSession.ControllerInfo? browserInfo, string? mediaId) =>
            FutureExtensions.Completed(LibraryResult.OfError(ErrorNotSupported));

        public IListenableFuture? OnGetChildren(MediaLibrarySession? session, MediaSession.ControllerInfo? browserInfo, string? parentId, int page, int pageSize, LibraryParams? @params) =>
            FutureExtensions.FromTask(async () =>
            {
                if (parentId == BrowseIds.ResumeRoot)
                {
                    var saved = await queue.LoadAsync();
                    IList<MediaItem> resume = saved is null ? [] : [LibraryBrowser.ResumeItem(saved.Current)];
                    return LibraryResult.OfItemList(resume, @params)!;
                }
                return LibraryResult.OfItemList(Page(await browser.ChildrenAsync(parentId ?? BrowseIds.Root), page, pageSize), @params)!;
            });

        public IListenableFuture? OnSearch(MediaLibrarySession? session, MediaSession.ControllerInfo? browserInfo, string? query, LibraryParams? @params) =>
            FutureExtensions.FromTask(async () =>
            {
                var results = await browser.SearchAsync(query ?? "");
                lock (_searches) _searches[query ?? ""] = results;
                MainThread.BeginInvokeOnMainThread(() => session?.NotifySearchResultChanged(browserInfo, query, results.Count, @params));
                return LibraryResult.OfVoid()!;
            });

        public IListenableFuture? OnGetSearchResult(MediaLibrarySession? session, MediaSession.ControllerInfo? browserInfo, string? query, int page, int pageSize, LibraryParams? @params) =>
            FutureExtensions.FromTask(async () =>
            {
                IList<MediaItem>? results;
                lock (_searches) _searches.TryGetValue(query ?? "", out results);
                results ??= await browser.SearchAsync(query ?? "");
                return LibraryResult.OfItemList(Page(results, page, pageSize), @params)!;
            });

        public IListenableFuture? OnSetMediaItems(MediaSession? mediaSession, MediaSession.ControllerInfo? controller, IList<MediaItem>? mediaItems, int startIndex, long startPositionMs) =>
            FutureExtensions.FromTask(async () =>
            {
                // Suggestion « reprendre » : toute la file sauvegardée, là où elle s'était arrêtée.
                if (mediaItems is [{ MediaId: BrowseIds.Resume }] && await queue.LoadAsync() is { } saved) return saved.StartPosition();
                var (items, index) = await browser.ResolveAsync(mediaItems ?? [], startIndex);
                return new MediaSession.MediaItemsWithStartPosition(items, index, startPositionMs);
            });

        public IListenableFuture? OnAddMediaItems(MediaSession? mediaSession, MediaSession.ControllerInfo? controller, IList<MediaItem>? mediaItems) =>
            FutureExtensions.FromTask(async () =>
            {
                var (items, _) = await browser.ResolveAsync(mediaItems ?? [], 0);
                return (Java.Lang.Object)new JavaList<MediaItem>(items);
            });

        public IListenableFuture? OnPlaybackResumption(MediaSession? mediaSession, MediaSession.ControllerInfo? controller) =>
            FutureExtensions.FromTask(async () =>
            {
                var saved = await queue.LoadAsync() ?? throw new InvalidOperationException("Aucune file à reprendre.");
                return saved.StartPosition();
            });

        private static List<MediaItem> Page(IList<MediaItem> items, int page, int pageSize) =>
            pageSize <= 0 ? items.ToList() : items.Skip(page * pageSize).Take(pageSize).ToList();
    }
}

/// <summary>
/// Télécharge entièrement dans le cache les N pistes qui suivent, l'une après l'autre.
/// Une coupure réseau en cours de piste ne s'entend donc pas tant que le cache tient.
/// </summary>
public sealed class Prefetcher(CacheDataSource.Factory cacheFactory, StreamResolver resolver, PlaybackSettings settings)
{
    /// <summary>
    /// Passage en cours. Chaque passage a son propre jeton et ses propres téléchargements : un ancien passage ne
    /// touche jamais à l'état du suivant (avant, un écrivain partagé pouvait être écrasé et échapper à l'annulation).
    /// </summary>
    private CancellationTokenSource? _run;

    /// <summary>À appeler sur le thread du lecteur.</summary>
    public void Schedule(Media3IPlayer player)
    {
        var uris = new List<Android.Net.Uri>();
        var repeatAll = player.RepeatMode == PlayerConst.RepeatAll;
        foreach (var next in PrefetchPlan.Indices(player.MediaItemCount, player.CurrentMediaItemIndex, settings.PrefetchCount, repeatAll))
        {
#pragma warning disable CS0618 // localConfiguration n'est exposé par le binding que sous son ancien nom.
            if (player.GetMediaItemAt(next)?.PlaybackProperties?.Uri is { } uri) uris.Add(uri);
#pragma warning restore CS0618
        }

        Cancel();
        if (uris.Count == 0) return;
        var run = _run = new CancellationTokenSource();
        var token = run.Token;
        _ = Task.Run(() =>
        {
            foreach (var uri in uris)
            {
                if (token.IsCancellationRequested) return;
                try
                {
                    var spec = resolver.ResolveDataSpec(new DataSpec(uri));
                    var writer = new CacheWriter(cacheFactory.CreateDataSourceForDownloading(), spec, null, null);
                    // Déjà annulé : Register appelle Cancel tout de suite, Cache s'arrête aussitôt.
                    using (token.Register(writer.Cancel)) writer.Cache();
                }
                catch (Java.IO.IOException)
                {
                    // Réseau absent, annulation, session expirée : la lecture normale prendra le relais.
                }
                catch (Exception ex)
                {
                    // Inattendu, mais le pré-chargement n'est qu'un confort : noté dans logcat, sans arrêter l'appli.
                    Android.Util.Log.Warn("SonNuage", $"Pré-chargement en échec : {ex}");
                }
            }
        });
    }

    public void Cancel()
    {
        _run?.Cancel();
        _run = null;
    }
}

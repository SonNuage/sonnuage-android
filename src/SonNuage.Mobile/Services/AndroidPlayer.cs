using System.Runtime.CompilerServices;
using Android.Content;
using Android.Runtime;
using AndroidX.Media3.Common;
using AndroidX.Media3.Session;
using SonNuage.Contracts;
using SonNuage.Mobile.Playback;
using SonNuage.UI.Services;
using IPlayer = SonNuage.UI.Services.IPlayer;

namespace SonNuage.Mobile.Services;

/// <summary>
/// Lecteur de l'interface Blazor sur Android : simple télécommande (MediaController) du <see cref="PlaybackService"/>.
/// La file vit dans le service, ce qui la partage avec la notification et Android Auto. L'état exposé ici est
/// relu depuis le contrôleur à chaque événement. Le MediaController n'est utilisable que depuis le thread principal.
/// </summary>
public sealed class AndroidPlayer(QueueStore queueStore, PlaybackSettings settings, LibraryBrowser browser, Breadcrumbs crumbs) : IPlayer, IRestorablePlayer, IAdoptablePlayer
{
    private MediaController? _controller;
    private SharedConnection<MediaController>? _connection;
    private PlayerState _state = PlayerState.Empty;
    private IReadOnlyList<TrackDto> _queue = [];
    private readonly Dictionary<Guid, TrackDto> _updated = [];
    private Ticker? _ticker;

    public PlayerState State => _state;

    /// <summary>Le service enchaîne en fondu avec un second ExoPlayer (<see cref="Crossfader"/>) : le réglage s'affiche.</summary>
    public bool SupportsCrossfade => true;
    public event Action? StateChanged;

    public Task RestoreAsync() => OnMain(async c =>
    {
        if (c.MediaItemCount > 0) return;
        if (await queueStore.LoadAsync() is { } saved) saved.LoadInto(c);
    });

    /// <summary>
    /// Se relie au service de lecture sans rien changer à sa file : l'état (et donc ce que voient les autres appareils
    /// du compte) suit dès que le service joue, même sans interface ouverte (Android Auto, notification).
    /// </summary>
    public Task AttachAsync() => OnMain(_ => { });

    /// <summary>Reprend ici la lecture d'un autre appareil du compte : sa file dans son ordre, à sa position.</summary>
    public Task AdoptAsync(PlayerState state, bool autoplay) => OnMain(c =>
    {
        if (state.Queue.Count == 0) return;
        c.ShuffleModeEnabled = false;
        c.RepeatMode = state.Repeat switch
        {
            RepeatMode.All => PlayerConst.RepeatAll,
            RepeatMode.One => PlayerConst.RepeatOne,
            _ => PlayerConst.RepeatOff,
        };
        var index = Math.Clamp(state.CurrentIndex, 0, state.Queue.Count - 1);
        c.SetMediaItems(state.Queue.Select(browser.Item).ToList(), index, (long)(state.PositionSeconds * 1000));
        c.Prepare();
        if (autoplay) c.Play();
    });

    /// <summary>Un autre appareil du compte joue : celui-ci se met en pause, sa file reste.</summary>
    public Task ReleaseAsync() => OnMain(c =>
    {
        if (c.IsPlaying) c.Pause();
    });

    public Task PlayAsync(IReadOnlyList<TrackDto> tracks, int startIndex = 0, bool? shuffle = null) => OnMain(c =>
    {
        if (tracks.Count == 0) return;
        var start = shuffle == true && startIndex < 0 ? Random.Shared.Next(tracks.Count) : Math.Clamp(startIndex, 0, tracks.Count - 1);
        // Le service mélange à la réception si le shuffle est actif : on fixe le mode avant d'envoyer la file.
        if (shuffle is { } s && c.ShuffleModeEnabled != s) c.ShuffleModeEnabled = s;
        c.SetMediaItems(tracks.Select(browser.Item).ToList(), start, 0);
        c.Prepare();
        c.Play();
    });

    /// <summary>
    /// Radio thématique : ses premiers morceaux, dans l'ordre, sans répétition. Chacun porte la radio, que le service
    /// suit pour compléter la file (<see cref="QueueRefill"/>), même après une reprise.
    /// </summary>
    public Task PlayStationAsync(string stationId, IReadOnlyList<TrackDto> tracks) => OnMain(c =>
    {
        if (tracks.Count == 0) return;
        c.ShuffleModeEnabled = false;
        c.RepeatMode = PlayerConst.RepeatOff;
        c.SetMediaItems(tracks.Select(t => browser.StationItem(t, stationId)).ToList(), 0, 0);
        c.Prepare();
        c.Play();
    });

    public Task TogglePlayAsync() => OnMain(c =>
    {
        if (c.IsPlaying)
        {
            c.Pause();
            return;
        }
        if (c.PlaybackState == PlayerConst.StateIdle) c.Prepare();
        if (c.PlaybackState == PlayerConst.StateEnded) c.SeekTo(0, 0);
        c.Play();
    });

    public Task NextAsync() => OnMain(c => c.SeekToNextMediaItem());

    /// <summary>Media3 applique la règle habituelle : au-delà de 3 s, retour au début de la piste.</summary>
    public Task PreviousAsync() => OnMain(c => c.SeekToPrevious());

    public Task SeekAsync(double seconds) => OnMain(c => c.SeekTo((long)(seconds * 1000)));

    public Task JumpToAsync(int queueIndex) => OnMain(c =>
    {
        c.SeekTo(queueIndex, 0);
        c.Play();
    });

    public Task SetShuffleAsync(bool shuffle) => OnMain(c => c.ShuffleModeEnabled = shuffle);

    public Task SetRepeatAsync(RepeatMode mode) => OnMain(c => c.RepeatMode = mode switch
    {
        RepeatMode.All => PlayerConst.RepeatAll,
        RepeatMode.One => PlayerConst.RepeatOne,
        _ => PlayerConst.RepeatOff,
    });

    public Task SetVolumeAsync(double volume) => OnMain(c => c.Volume = (float)Math.Clamp(volume, 0, 1));

    public Task EnqueueAsync(IReadOnlyList<TrackDto> tracks, bool playNext)
    {
        if (_state.Current is null) return PlayAsync(tracks);
        return OnMain(c => c.AddMediaItems(playNext ? c.CurrentMediaItemIndex + 1 : c.MediaItemCount, tracks.Select(browser.Item).ToList()));
    }

    public Task RemoveAtAsync(int queueIndex) => OnMain(c => c.RemoveMediaItem(queueIndex));

    /// <summary>Le service partage le même PlaybackSettings (même processus) : la nouvelle qualité vaut dès la piste suivante.</summary>
    public Task ReloadSettingsAsync() => settings.LoadAsync();

    public void UpdateTrack(TrackDto track)
    {
        _updated[track.Id] = track;
        _queue = _queue.Select(t => t.Id == track.Id ? track : t).ToList();
        _state = _state with { Queue = _queue };
        StateChanged?.Invoke();
    }

    private Task OnMain(Action<MediaController> action, [CallerMemberName] string command = "") => OnMain(c =>
    {
        action(c);
        return Task.CompletedTask;
    }, command);

    // Chaque commande rejoint les dernières actions jointes à un éventuel rapport de plantage.
    private Task OnMain(Func<MediaController, Task> action, [CallerMemberName] string command = "") => MainThread.InvokeOnMainThreadAsync(async () =>
    {
        crumbs.Add($"lecteur : {command}");
        try
        {
            await action(await ControllerAsync());
        }
        catch (Exception ex) when (ex is Java.Lang.Exception or InvalidOperationException)
        {
            crumbs.Add($"lecteur : {command} en échec ({ex.GetType().Name} : {ex.Message})");
            _state = _state with { Error = ex.Message };
            StateChanged?.Invoke();
        }
    });

    /// <summary>Connexion refaite après un échec, ou si le service l'a coupée (voir <see cref="SharedConnection{T}"/>).</summary>
    private Task<MediaController> ControllerAsync() =>
        (_connection ??= new SharedConnection<MediaController>(ConnectAsync, c => c.IsConnected, c => c.Release())).GetAsync();

    private async Task<MediaController> ConnectAsync()
    {
        var context = Android.App.Application.Context;
        var token = new SessionToken(context, new ComponentName(context, Java.Lang.Class.FromType(typeof(PlaybackService))));
        var result = await new MediaController.Builder(context, token).BuildAsync()!.AsTask();
        var controller = result.JavaCast<MediaController>()!;
        controller.AddListener(new Listener(this));
        _controller = controller;
        _ticker ??= new Ticker(TimeSpan.FromMilliseconds(500), () => Refresh(timelineChanged: false));
        Refresh(timelineChanged: true);
        return controller;
    }

    private void Refresh(bool timelineChanged)
    {
        var c = _controller;
        if (c is null) return;
        if (timelineChanged)
        {
            _queue = c.Items()
                .Select(TrackItems.ToTrack)
                .OfType<TrackDto>()
                .Select(t => _updated.GetValueOrDefault(t.Id, t))
                .ToList();
        }
        var index = c.CurrentMediaItemIndex;
        var current = index >= 0 && index < _queue.Count ? _queue[index] : null;
        _state = new PlayerState(
            _queue,
            current is null ? -1 : index,
            c.IsPlaying,
            c.PlayWhenReady && c.PlaybackState == PlayerConst.StateBuffering,
            Math.Max(0, c.CurrentPosition) / 1000.0,
            c.Duration > 0 ? c.Duration / 1000.0 : current?.DurationSeconds ?? 0,
            c.ShuffleModeEnabled,
            c.RepeatMode switch
            {
                PlayerConst.RepeatAll => RepeatMode.All,
                PlayerConst.RepeatOne => RepeatMode.One,
                _ => RepeatMode.Off,
            },
            c.Volume,
            c.PlayerError?.Message);

        if (_ticker is not null)
        {
            if (c.IsPlaying && !_ticker.IsRunning) _ticker.Start();
            else if (!c.IsPlaying && _ticker.IsRunning) _ticker.Stop();
        }
        StateChanged?.Invoke();
    }

    /// <summary>
    /// Relecture de la position pendant la lecture, sur le thread principal. Par un Handler Android et non par le
    /// Dispatcher de MAUI : le service peut démarrer sans interface (Android Auto), donc sans Application MAUI.
    /// </summary>
    private sealed class Ticker(TimeSpan interval, Action tick) : Java.Lang.Object, Java.Lang.IRunnable
    {
        private readonly Android.OS.Handler _handler = new(Android.OS.Looper.MainLooper!);

        public bool IsRunning { get; private set; }

        public void Start()
        {
            if (IsRunning) return;
            IsRunning = true;
            _handler.PostDelayed(this, (long)interval.TotalMilliseconds);
        }

        public void Stop()
        {
            IsRunning = false;
            _handler.RemoveCallbacks(this);
        }

        public void Run()
        {
            if (!IsRunning) return;
            tick();
            if (IsRunning) _handler.PostDelayed(this, (long)interval.TotalMilliseconds);
        }
    }

    private sealed class Listener(AndroidPlayer owner) : Java.Lang.Object, IPlayerListener
    {
        public void OnEvents(Media3IPlayer? player, PlayerEvents? events)
        {
            if (events is null) return;
            owner.Refresh(events.Contains(EventTimelineChanged));
        }

        // androidx.media3.common.Player.EVENT_TIMELINE_CHANGED
        private const int EventTimelineChanged = 0;
    }
}

using Android.OS;
using AndroidX.Media3.Common;
using AndroidX.Media3.ExoPlayer;
using SonNuage.UI.Services;

namespace SonNuage.Mobile.Playback;

/// <summary>
/// Fondu enchaîné sur Android. ExoPlayer ne joue qu'une piste à la fois : un second ExoPlayer, la « traîne », joue
/// la fin de la piste qui s'éteint pendant que le lecteur principal part déjà sur la suivante.
/// <para>Choix de conception :</para>
/// <list type="bullet">
/// <item>La session (notification, écran verrouillé, Android Auto, casque) ne voit qu'un lecteur, le principal, qui
/// porte toujours la file, la piste en cours et sa position. Au début du fondu il saute à la piste suivante : tout
/// suit le nouveau titre aussitôt. La traîne n'est connue de personne, elle n'a pas de file et rend le focus audio au
/// principal (handleAudioFocus = false).</item>
/// <item>Écarté : un lecteur enveloppe (SimpleBasePlayer / ForwardingPlayer) qui échangerait deux ExoPlayer à chaque
/// piste. Il faudrait recopier la file, le shuffle physique (<see cref="ShufflingPlayer"/>), la répétition et les
/// événements d'un lecteur à l'autre à chaque changement : beaucoup d'état dupliqué pour un fondu de 5 s.</item>
/// <item>Le volume du principal n'est jamais modifié : la rampe passe par un message direct à son rendu audio
/// (MSG_SET_VOLUME), sous la valeur Player.volume. La session et l'interface gardent ainsi le volume choisi par
/// l'utilisateur (pas de curseur qui bouge pendant le fondu). ExoPlayer 1.11 ne renvoie son propre volume au rendu
/// que sur setVolume ou un changement de focus : la rampe le réapplique à chaque pas de 50 ms.</item>
/// <item>La traîne est préparée quelques secondes avant (<see cref="CrossfadeTiming.ArmLeadMs"/>), en pause sur le
/// point de départ, depuis le cache disque où la piste en cours est déjà : au départ elle démarre sans chargement.</item>
/// </list>
/// Fondu réglé à 0 : rien ne change, l'enchaînement sans blanc d'ExoPlayer reste intact. Toute action de
/// l'utilisateur pendant le fondu (pause, saut, recherche, nouvelle file) l'arrête net : la traîne se tait et le
/// principal reprend son volume, comme le web. Tout se passe sur le thread principal (celui des lecteurs).
/// </summary>
public sealed class Crossfader : Java.Lang.Object, IPlayerListener
{
    // androidx.media3.exoplayer.Renderer.MSG_SET_VOLUME (non exposé par le binding).
    private const int MsgSetVolume = 2;

    private readonly IExoPlayer _exo;
    private readonly Media3IPlayer _player;
    private readonly IExoPlayer _tail;
    private readonly PlaybackSettings _settings;
    private readonly Handler _handler = new(Looper.MainLooper!);
    private readonly Action _tick;

    private Phase _phase;
    private MediaItem? _armed;
    private long _fadeStart;
    private int _fadeMs;
    /// <summary>Notre propre saut vers la piste suivante est en cours : ses événements ne sont pas une action de l'utilisateur.</summary>
    private bool _switching;
    /// <summary>Piste quittée par un fondu, en attente d'être comptée comme écoutée en entier (<see cref="TakeHandover"/>).</summary>
    private MediaItem? _handedOver;

    private enum Phase { Idle, Armed, Fading }

    /// <param name="exo">Le lecteur principal (pour atteindre son rendu audio).</param>
    /// <param name="player">Le même, tel que la session le voit (<see cref="ShufflingPlayer"/>).</param>
    /// <param name="tail">Second lecteur, même fabrique de sources et même cache que le principal.</param>
    public Crossfader(IExoPlayer exo, Media3IPlayer player, IExoPlayer tail, PlaybackSettings settings)
    {
        _exo = exo;
        _player = player;
        _tail = tail;
        _settings = settings;
        _tick = Tick;
        player.AddListener(this);
    }

    /// <summary>
    /// Vrai une seule fois pour la piste qu'un fondu vient de quitter : le saut est un SEEK pour Media3, mais la
    /// piste a été écoutée jusqu'au bout et doit compter comme telle dans l'historique.
    /// </summary>
    public bool TakeHandover(MediaItem? item)
    {
        if (_handedOver is null || item is null || !_handedOver.Equals(item)) return false;
        _handedOver = null;
        return true;
    }

    public void Release()
    {
        _handler.RemoveCallbacks(_tick);
        _player.RemoveListener(this);
        _tail.Release();
    }

    private void Schedule(int delayMs)
    {
        _handler.RemoveCallbacks(_tick);
        _handler.PostDelayed(_tick, delayMs);
    }

    private void Tick()
    {
        if (_phase == Phase.Fading)
        {
            Ramp();
            return;
        }
        // En pause ou sans rien à jouer : plus de vérification jusqu'à la reprise (OnPlayWhenReadyChanged,
        // OnPlaybackStateChanged).
        if (!_player.PlayWhenReady || _player.PlaybackState is PlayerConst.StateIdle or PlayerConst.StateEnded) return;
        var step = Step();
        switch (step.Action)
        {
            case CrossfadeAction.Arm when !IsArmedOnCurrent():
                Arm();
                break;
            case CrossfadeAction.Start:
                if (Start()) return;
                break;
            case CrossfadeAction.Wait when _phase == Phase.Armed:
                Stop();
                break;
        }
        Schedule(step.DelayMs);
    }

    private CrossfadeStep Step()
    {
        var fade = CrossfadeTiming.FadeMs(_settings.CrossfadeSeconds);
        if (fade == 0 || !_player.IsPlaying || TargetIndex() < 0) return new(CrossfadeAction.Wait, CrossfadeTiming.IdlePollMs);
        return CrossfadeTiming.Next(DurationMs(), _player.CurrentPosition, fade);
    }

    private int TargetIndex() =>
        CrossfadeTiming.Target(_player.CurrentMediaItemIndex, _player.NextMediaItemIndex, _player.RepeatMode == PlayerConst.RepeatOne);

    private long DurationMs() =>
        CrossfadeTiming.DurationMs(_player.Duration, TrackItems.ToTrack(_player.CurrentMediaItem)?.DurationSeconds ?? 0);

    private bool IsArmedOnCurrent() => _phase == Phase.Armed && _armed is not null && _armed.Equals(_player.CurrentMediaItem);

    /// <summary>La traîne se cale, en pause, sur le point de départ du fondu de la piste en cours.</summary>
    private void Arm()
    {
        if (_player.CurrentMediaItem is not { } item) return;
        var startAt = DurationMs() - CrossfadeTiming.FadeMs(_settings.CrossfadeSeconds);
        _tail.PlayWhenReady = false;
        _tail.SetMediaItem(item, Math.Max(0, startAt));
        _tail.Prepare();
        _armed = item;
        _phase = Phase.Armed;
    }

    /// <returns>Faux si le fondu n'a pas pu partir (aucune piste en cours).</returns>
    private bool Start()
    {
        if (!IsArmedOnCurrent()) Arm();
        if (_phase != Phase.Armed) return false;
        var target = TargetIndex();
        var position = _player.CurrentPosition;
        // Un pas en retard : la traîne rattrape la position réelle (quelques dizaines de ms au plus).
        if (Math.Abs(_tail.CurrentPosition - position) > CrossfadeTiming.TickMs * 2) _tail.SeekTo(position);
        _fadeMs = (int)Math.Clamp(DurationMs() - position, CrossfadeTiming.TickMs, CrossfadeTiming.MaxFadeMs);
        _tail.Volume = _player.Volume;
        _tail.Play();

        _handedOver = _player.CurrentMediaItem;
        SetMainGain(0);
        // Les écouteurs de Media3 sont appelés pendant SeekTo (ListenerSet.flushEvents, hors de tout rappel) :
        // le drapeau couvre bien les événements de ce saut, et seulement eux.
        _switching = true;
        try
        {
            _player.SeekTo(target, 0);
        }
        finally
        {
            _switching = false;
        }
        _armed = null;
        _phase = Phase.Fading;
        _fadeStart = SystemClock.ElapsedRealtime();
        Ramp();
        return true;
    }

    private void Ramp()
    {
        var (fadingOut, fadingIn) = CrossfadeTiming.Gains(CrossfadeTiming.Progress(SystemClock.ElapsedRealtime() - _fadeStart, _fadeMs));
        var volume = _player.Volume;
        _tail.Volume = (float)(volume * fadingOut);
        SetMainGain((float)(volume * fadingIn));
        if (fadingIn >= 1)
        {
            Stop();
            Schedule(CrossfadeTiming.IdlePollMs);
            return;
        }
        Schedule(CrossfadeTiming.TickMs);
    }

    /// <summary>Fin du fondu, ou interruption : la traîne se tait et le principal retrouve le volume de l'utilisateur.</summary>
    private void Stop()
    {
        if (_phase == Phase.Idle) return;
        var wasFading = _phase == Phase.Fading;
        _phase = Phase.Idle;
        _armed = null;
        _tail.Stop();
        _tail.ClearMediaItems();
        // Hors fondu, le rendu porte déjà le volume du lecteur. (Un atténuement de focus en cours, rare pendant
        // ces quelques secondes, reprendra à son prochain changement.)
        if (wasFading) SetMainGain(_player.Volume);
    }

    /// <summary>Volume du rendu audio du principal, sans toucher à Player.volume (voir la remarque de la classe).</summary>
    private void SetMainGain(float gain)
    {
        for (var i = 0; i < _exo.RendererCount; i++)
        {
            if (_exo.GetRendererType(i) != C.TrackTypeAudio || _exo.GetRenderer(i) is not { } renderer) continue;
            _exo.CreateMessage(renderer)!.SetType(MsgSetVolume)!.SetPayload(Java.Lang.Float.ValueOf(gain))!.Send();
        }
    }

    /// <summary>Changement venu d'ailleurs (utilisateur, fin de file, erreur) : on abandonne et on réévalue.</summary>
    private void Interrupt()
    {
        if (_switching) return;
        Stop();
        Schedule(0);
    }

    public void OnPlayWhenReadyChanged(bool playWhenReady, int reason)
    {
        if (playWhenReady) Schedule(0);
        else Interrupt();
    }

    // Focus audio perdu provisoirement (appel…) : le principal se tait, la traîne aussi.
    public void OnPlaybackSuppressionReasonChanged(int playbackSuppressionReason)
    {
        if (playbackSuppressionReason != 0) Interrupt();
    }

    public void OnPlaybackStateChanged(int playbackState)
    {
        if (playbackState is PlayerConst.StateIdle or PlayerConst.StateEnded) Interrupt();
        else if (_phase != Phase.Fading) Schedule(0);
    }

    public void OnPositionDiscontinuity(PlayerPositionInfo? oldPosition, PlayerPositionInfo? newPosition, int reason) => Interrupt();

    public void OnMediaItemTransition(MediaItem? mediaItem, int reason) => Interrupt();
}

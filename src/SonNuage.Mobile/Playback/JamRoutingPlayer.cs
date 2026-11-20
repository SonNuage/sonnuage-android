using AndroidX.Media3.Common;
using SonNuage.UI.Services;

namespace SonNuage.Mobile.Playback;

/// <summary>
/// Le lecteur tel que la session Media3 le voit. Dans un Jam (mode invité), une commande venue d'ailleurs que
/// l'interface (notification, écran verrouillé, casque, Android Auto) va à l'hôte, comme les boutons de l'interface
/// (voir <see cref="LinkedPlayer"/>) : sur le lecteur local, la synchro l'annulerait aussitôt.
/// <para>
/// L'interface passe aussi par la session (<see cref="Services.AndroidPlayer"/>) : ses commandes, déjà routées par
/// <see cref="LinkedPlayer"/> ou envoyées par la synchro elle-même, restent locales. La session dit qui commande via
/// <see cref="FromOutside"/>, posé juste avant chaque commande (voir <see cref="PlaybackService"/>). Le reste du service
/// (fondu enchaîné, pré-chargement…) utilise le lecteur enveloppé directement.
/// </para>
/// </summary>
public sealed class JamRoutingPlayer(Media3IPlayer player, Func<IPlayerRoute?> route) : ForwardingPlayer(player)
{
    /// <summary>Clé des connection hints que pose le contrôleur de l'interface.</summary>
    public const string AppControllerHint = "sonnuage.app";

    /// <summary>La commande en cours vient-elle d'un contrôleur extérieur à l'interface ?</summary>
    public bool FromOutside { get; set; }

    /// <summary>La route du Jam si la commande en cours doit y aller. Une commande ne passe qu'une fois.</summary>
    private IPlayerRoute? Target
    {
        get
        {
            if (!FromOutside || route() is not { Routes: true } r) return null;
            FromOutside = false;
            return r;
        }
    }

    public override void Play()
    {
        if (Target is { } r) { if (!IsPlaying) _ = r.TogglePlayAsync(); }
        else base.Play();
    }

    public override void Pause()
    {
        if (Target is { } r) { if (IsPlaying) _ = r.TogglePlayAsync(); }
        else base.Pause();
    }

    public override bool PlayWhenReady
    {
        get => base.PlayWhenReady;
        set
        {
            if (Target is { } r) { if (value != base.PlayWhenReady) _ = r.TogglePlayAsync(); }
            else base.PlayWhenReady = value;
        }
    }

    public override void Stop()
    {
        if (Target is { } r) { if (IsPlaying) _ = r.TogglePlayAsync(); }
        else base.Stop();
    }

    public override void SeekTo(long positionMs)
    {
        if (Target is { } r) _ = r.SeekAsync(positionMs / 1000.0);
        else base.SeekTo(positionMs);
    }

    public override void SeekToNext()
    {
        if (Target is { } r) _ = r.NextAsync();
        else base.SeekToNext();
    }

    public override void SeekToNextMediaItem()
    {
        if (Target is { } r) _ = r.NextAsync();
        else base.SeekToNextMediaItem();
    }

    public override void SeekToPrevious()
    {
        if (Target is { } r) _ = r.PreviousAsync();
        else base.SeekToPrevious();
    }

    public override void SeekToPreviousMediaItem()
    {
        if (Target is { } r) _ = r.PreviousAsync();
        else base.SeekToPreviousMediaItem();
    }
}

using AndroidX.Media3.Common;

namespace SonNuage.Mobile.Playback;

/// <summary>
/// Aléatoire « physique » : activer le shuffle réordonne réellement la file (piste en cours en tête, suite mélangée)
/// et le désactiver restaure l'ordre d'origine. Ainsi la file vue par l'appli, la notification et Android Auto
/// est toujours l'ordre de lecture réel, et le pré-chargement sait quelles pistes viennent ensuite.
/// L'ExoPlayer sous-jacent utilise un ordre de mélange neutre (UnshuffledShuffleOrder).
/// </summary>
public sealed class ShufflingPlayer(Media3IPlayer player) : ForwardingPlayer(player)
{
    private List<MediaItem>? _original;

    public override bool ShuffleModeEnabled
    {
        get => base.ShuffleModeEnabled;
        set
        {
            if (value == base.ShuffleModeEnabled) return;
            if (value) Shuffle();
            else Unshuffle();
            base.ShuffleModeEnabled = value;
        }
    }

    public override void SetMediaItems(IList<MediaItem>? mediaItems) => SetMediaItems(mediaItems, 0, 0);

    public override void SetMediaItems(IList<MediaItem>? mediaItems, bool resetPosition)
    {
        if (resetPosition || !base.ShuffleModeEnabled) SetMediaItems(mediaItems, 0, 0);
        else base.SetMediaItems(mediaItems, false);
    }

    public override void SetMediaItems(IList<MediaItem>? mediaItems, int startIndex, long startPositionMs)
    {
        if (!base.ShuffleModeEnabled || mediaItems is null || mediaItems.Count < 2)
        {
            _original = null;
            base.SetMediaItems(mediaItems, startIndex, startPositionMs);
            return;
        }
        var start = Math.Clamp(startIndex, 0, mediaItems.Count - 1);
        _original = mediaItems.ToList();
        base.SetMediaItems([mediaItems[start], .. ShuffleOrder.Others(_original, start, Random.Shared)], 0, startPositionMs);
    }

    private void Shuffle()
    {
        var items = this.Items();
        if (items.Count < 2) return;
        var current = Math.Max(0, CurrentMediaItemIndex);
        _original = items;
        ReplaceAround(current, [], ShuffleOrder.Others(items, current, Random.Shared));
    }

    private void Unshuffle()
    {
        if (_original is null) return;
        var items = this.Items();
        var current = Math.Max(0, CurrentMediaItemIndex);
        if (items.Count < 2 || current >= items.Count) return;

        var order = ShuffleOrder.Restore(_original, items, (a, b) => a.Equals(b));
        _original = null;

        var playing = items[current];
        var k = order.FindIndex(o => o.Equals(playing));
        if (k < 0) return;
        ReplaceAround(current, order[..k], order[(k + 1)..]);
    }

    /// <summary>Remplace tout sauf la piste en cours, sans interrompre la lecture.</summary>
    private void ReplaceAround(int current, List<MediaItem> before, IList<MediaItem> after)
    {
        var count = MediaItemCount;
        if (current + 1 < count) base.RemoveMediaItems(current + 1, count);
        if (current > 0) base.RemoveMediaItems(0, current);
        if (before.Count > 0) base.AddMediaItems(0, before);
        if (after.Count > 0) base.AddMediaItems(before.Count + 1, after);
    }
}

namespace SonNuage.Mobile.Playback;

/// <summary>Ce que <see cref="QueueStore"/> enregistre de la file (sans type Android, testé).</summary>
public static class QueueSnapshot
{
    /// <summary>Pistes enregistrées au plus : la lecture infinie fait grossir la file sans fin.</summary>
    public const int MaxTracks = 500;

    /// <summary>
    /// Identifiants à enregistrer et index de la piste en cours parmi eux. <paramref name="queue"/> donne
    /// l'identifiant de chaque élément de la file (null : pas une piste, ignoré). Au-delà de <paramref name="max"/>,
    /// on garde une fenêtre autour de la piste en cours, pour qu'elle soit toujours celle qui reprend.
    /// </summary>
    public static (List<Guid> Ids, int Index) Take(IReadOnlyList<Guid?> queue, int currentIndex, int max = MaxTracks)
    {
        var ids = new List<Guid>(queue.Count);
        var index = 0;
        for (var i = 0; i < queue.Count; i++)
        {
            if (i == currentIndex) index = ids.Count;
            if (queue[i] is { } id) ids.Add(id);
        }
        if (ids.Count == 0) return ([], 0);
        index = Math.Min(index, ids.Count - 1);
        if (ids.Count <= max) return (ids, index);
        var start = Math.Clamp(index - max / 2, 0, ids.Count - max);
        return (ids.GetRange(start, max), index - start);
    }
}

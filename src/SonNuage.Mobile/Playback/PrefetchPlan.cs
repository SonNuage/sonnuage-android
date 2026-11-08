namespace SonNuage.Mobile.Playback;

/// <summary>Quelles pistes le <see cref="Prefetcher"/> télécharge (sans type Android, testé).</summary>
public static class PrefetchPlan
{
    /// <summary>
    /// Index des <paramref name="ahead"/> pistes qui suivent <paramref name="current"/>, dans l'ordre. En répétition de
    /// toute la file, on repart du début, sans revenir à la piste en cours.
    /// </summary>
    public static IReadOnlyList<int> Indices(int count, int current, int ahead, bool repeatAll)
    {
        var indices = new List<int>();
        if (count <= 0 || current < 0) return indices;
        for (var step = 1; step <= ahead; step++)
        {
            var next = current + step;
            if (next >= count)
            {
                if (!repeatAll) break;
                next %= count;
            }
            if (next == current) break;
            indices.Add(next);
        }
        return indices;
    }
}

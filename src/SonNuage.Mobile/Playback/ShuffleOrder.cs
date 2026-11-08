namespace SonNuage.Mobile.Playback;

/// <summary>Règles de l'aléatoire « physique » de <see cref="ShufflingPlayer"/> (sans type Android, testées).</summary>
public static class ShuffleOrder
{
    /// <summary>Toutes les pistes sauf celle en cours, mélangées : elles viendront après elle.</summary>
    public static T[] Others<T>(IReadOnlyList<T> items, int current, Random random)
    {
        var rest = items.Where((_, i) => i != current).ToArray();
        random.Shuffle(rest);
        return rest;
    }

    /// <summary>
    /// L'ordre d'origine, limité aux éléments encore présents (multi-ensemble : une piste peut figurer deux fois),
    /// suivi, dans l'ordre de lecture, de ce qui a été ajouté pendant le shuffle.
    /// </summary>
    public static List<T> Restore<T>(IReadOnlyList<T> original, IReadOnlyList<T> items, Func<T, T, bool> same)
    {
        var remaining = items.ToList();
        var order = new List<T>(items.Count);
        foreach (var item in original)
        {
            var j = remaining.FindIndex(r => same(r, item));
            if (j < 0) continue;
            order.Add(remaining[j]);
            remaining.RemoveAt(j);
        }
        order.AddRange(remaining);
        return order;
    }
}

namespace SonNuage.Mobile.Glyph;

/// <summary>
/// Le logo SonNuage sur la matrice Glyph du Nothing Phone (3) : un nuage fait de sept barres d'égaliseur. En lecture,
/// les barres dansent (pleine luminosité) dans la silhouette du nuage, gardée en faible : le logo reste lisible.
/// Images en luminosités 0–255, ligne par ligne, <see cref="Size"/>×<see cref="Size"/>. Sans type Android : testé
/// hors téléphone.
/// </summary>
public static class CloudGlyph
{
    /// <summary>Côté de la matrice du Phone (3) : 25×25, dont 489 LED dans le cercle (<see cref="OnMatrix"/>).</summary>
    public const int Size = 25;

    public const byte Lit = 255;
    public const byte Ghost = 40;

    /// <summary>Hauteurs des barres du logo (viewBox 64, barres de 18 à 38 unités) ramenées à 16 pixels au plus.</summary>
    public static readonly IReadOnlyList<int> Heights = [8, 9, 15, 16, 16, 15, 8];

    /// <summary>Colonne gauche de chaque barre : 2 pixels de large, 1 d'écart, comme les fentes du logo.</summary>
    public static readonly IReadOnlyList<int> BarColumns = [3, 6, 9, 12, 15, 18, 21];

    private const int BarWidth = 2;

    /// <summary>Ligne du pied des barres : nuage de 16 pixels centré verticalement.</summary>
    private const int Baseline = 19;

    private const double Center = (Size - 1) / 2.0;

    public static bool OnMatrix(int x, int y)
    {
        double dx = x - Center, dy = y - Center;
        return dx * dx + dy * dy <= Size * Size / 4.0;
    }

    /// <summary>Le logo fixe, toutes barres pleines.</summary>
    public static byte[] Logo() => Draw(Heights);

    /// <summary>Image de l'animation au temps <paramref name="time"/> : chaque barre entre la moitié et toute sa hauteur.</summary>
    public static byte[] Frame(TimeSpan time)
    {
        var t = time.TotalSeconds;
        var heights = new int[Heights.Count];
        for (var i = 0; i < heights.Length; i++)
        {
            // Deux sinus de fréquences sans rapport simple, déphasés par barre : un mouvement d'égaliseur qui ne se
            // répète pas à l'œil. Les barres du milieu (les « basses ») vont un peu moins vite.
            var slow = 1.1 + 0.15 * Math.Abs(i - 3);
            var level = 0.5 + 0.25 * Math.Sin(2 * Math.PI * slow * t + i * 1.7) + 0.25 * Math.Sin(2 * Math.PI * 2.3 * t + i * 2.9);
            var min = (Heights[i] + 1) / 2;
            heights[i] = min + (int)Math.Round((Heights[i] - min) * level);
        }
        return Draw(heights);
    }

    private static byte[] Draw(IReadOnlyList<int> heights)
    {
        var frame = new byte[Size * Size];
        for (var i = 0; i < Heights.Count; i++)
        for (var dy = 0; dy < Heights[i]; dy++)
        for (var dx = 0; dx < BarWidth; dx++)
            frame[(Baseline - dy) * Size + BarColumns[i] + dx] = dy < heights[i] ? Lit : Ghost;
        return frame;
    }
}

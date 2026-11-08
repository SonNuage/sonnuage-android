namespace SonNuage.Mobile.Playback;

/// <summary>Ce que le <see cref="Crossfader"/> doit faire à cet instant de la piste.</summary>
public enum CrossfadeAction
{
    /// <summary>Rien pour l'instant (loin de la fin, fondu désactivé, piste trop courte ou fin déjà entamée).</summary>
    Wait,
    /// <summary>Le fondu approche : préparer le second lecteur sur la fin de la piste.</summary>
    Arm,
    /// <summary>Il reste la durée du fondu : lancer la piste suivante.</summary>
    Start,
}

/// <param name="DelayMs">Quand revenir vérifier.</param>
public readonly record struct CrossfadeStep(CrossfadeAction Action, int DelayMs);

/// <summary>
/// Règles du fondu enchaîné, sans type Android (testées dans tests/SonNuage.Mobile.Tests). Mêmes règles que le
/// lecteur web (player.js) : le morceau suivant part quand il reste la durée du fondu, seulement si la piste dure plus
/// de deux fois le fondu, avec une courbe à puissance constante.
/// </summary>
public static class CrossfadeTiming
{
    public const int MaxFadeMs = 5000;
    /// <summary>Pas de la rampe de volume, comme le web.</summary>
    public const int TickMs = 50;
    /// <summary>Vérification lente tant que la fin est loin.</summary>
    public const int IdlePollMs = 1000;
    /// <summary>Avance donnée au second lecteur pour charger la fin de piste (depuis le cache disque) avant le fondu.</summary>
    public const int ArmLeadMs = 3000;
    /// <summary>Retard toléré sur le point de départ ; au-delà (saut dans les dernières secondes), la piste finit sans fondu.</summary>
    public const int LateMs = 500;

    /// <summary>Le réglage (secondes, 0 = désactivé) en millisecondes, borné à 0–5 s.</summary>
    public static int FadeMs(double seconds) =>
        double.IsFinite(seconds) ? (int)Math.Round(Math.Clamp(seconds, 0, MaxFadeMs / 1000.0) * 1000) : 0;

    public static CrossfadeStep Next(long durationMs, long positionMs, int fadeMs)
    {
        if (fadeMs <= 0 || durationMs <= 2L * fadeMs) return new(CrossfadeAction.Wait, IdlePollMs);
        var startAt = durationMs - fadeMs;
        var armAt = startAt - ArmLeadMs;
        if (positionMs < armAt) return new(CrossfadeAction.Wait, (int)Math.Min(armAt - positionMs, IdlePollMs));
        if (positionMs < startAt) return new(CrossfadeAction.Arm, (int)Math.Min(startAt - positionMs, TickMs));
        if (positionMs < startAt + LateMs) return new(CrossfadeAction.Start, TickMs);
        return new(CrossfadeAction.Wait, IdlePollMs);
    }

    /// <summary>Courbe à puissance constante : Out² + In² = 1, pas de creux de volume au milieu du fondu.</summary>
    public static (double Out, double In) Gains(double progress)
    {
        var angle = Math.Clamp(progress, 0, 1) * Math.PI / 2;
        return (Math.Cos(angle), Math.Sin(angle));
    }

    public static double Progress(long elapsedMs, int fadeMs) =>
        fadeMs <= 0 ? 1 : Math.Clamp((double)elapsedMs / fadeMs, 0, 1);

    /// <summary>
    /// Piste vers laquelle fondre, -1 s'il n'y en a pas. <paramref name="next"/> est l'index suivant du lecteur
    /// (Media3 y traite la répétition d'une piste comme « pas de répétition ») : en répétition d'une piste, on fond
    /// vers elle-même, comme le web.
    /// </summary>
    public static int Target(int current, int next, bool repeatOne) => repeatOne ? current : next;

    /// <summary>Durée connue du lecteur, sinon celle des métadonnées de la piste (le flux n'est pas encore analysé).</summary>
    public static long DurationMs(long playerDurationMs, double metadataSeconds) =>
        playerDurationMs > 0 ? playerDurationMs : (long)Math.Round(Math.Max(0, metadataSeconds) * 1000);
}

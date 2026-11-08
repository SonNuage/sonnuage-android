namespace SonNuage.Mobile.Playback;

/// <summary>
/// Quand une écoute rejoint l'historique : même règle que le web, au-delà de 30 s (ou de la moitié d'une piste
/// courte), ou jouée jusqu'au bout. Sans type Android : testé dans tests/SonNuage.Mobile.Tests.
/// </summary>
public static class ListenRule
{
    public const double ThresholdSeconds = 30;

    /// <summary>Secondes à déclarer comme écoutées, null si l'écoute ne compte pas.</summary>
    public static double? Played(double seconds, double durationSeconds, bool completed)
    {
        if (completed) return durationSeconds;
        return seconds < Math.Min(ThresholdSeconds, durationSeconds * 0.5) ? null : seconds;
    }
}

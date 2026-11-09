namespace SonNuage.Mobile.Playback;

/// <summary>
/// Ce qui complète la file quand elle s'épuise : la radio thématique d'où elle vient, sinon la lecture infinie
/// (morceaux proches des derniers joués). Sans type Android : testé dans tests/SonNuage.Mobile.Tests.
/// </summary>
public static class QueueRefill
{
    /// <summary>Morceaux demandés à une radio thématique quand on la lance.</summary>
    public const int StationFirstBatch = 30;

    /// <summary>
    /// Radio que suit la file : celle de la piste en cours, sinon celle de la dernière piste. Un titre ajouté à la main
    /// (« à suivre » ou en fin de file) ne fait donc pas quitter la radio. Null : file ordinaire.
    /// </summary>
    public static string? Station(string? current, string? last) => current ?? last;

    /// <summary>
    /// Faut-il compléter la file ? Une radio ne s'arrête pas, même lecture infinie désactivée ; la répétition (de la
    /// file ou du titre) coupe tout, comme sur le web.
    /// </summary>
    public static bool Applies(bool fromStation, bool infinitePlay, bool repeatOff) => repeatOff && (fromStation || infinitePlay);
}

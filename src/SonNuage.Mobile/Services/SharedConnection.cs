namespace SonNuage.Mobile.Services;

/// <summary>
/// Une connexion partagée par tous les appelants (le MediaController de <see cref="AndroidPlayer"/>) : un seul essai
/// à la fois, refait après un échec, et remplacée quand elle est morte (<paramref name="drop"/> libère l'ancienne).
/// Avant, un échec restait en mémoire : plus aucune commande du lecteur ne passait jusqu'au redémarrage de l'appli.
/// Un seul thread (le principal) : pas de verrou. Sans type Android : testé dans tests/SonNuage.Mobile.Tests.
/// </summary>
public sealed class SharedConnection<T>(Func<Task<T>> connect, Func<T, bool> isAlive, Action<T> drop) where T : class
{
    private Task<T>? _pending;
    private T? _value;

    public Task<T> GetAsync()
    {
        if (_value is { } value)
        {
            if (isAlive(value)) return Task.FromResult(value);
            _value = null;
            drop(value);
        }
        // Un essai en cours est partagé ; un essai fini (réussi mais mort depuis, ou en échec) est refait.
        if (_pending is { IsCompleted: false } pending) return pending;
        return _pending = ConnectAsync();
    }

    private async Task<T> ConnectAsync()
    {
        var value = await connect();
        _value = value;
        return value;
    }
}

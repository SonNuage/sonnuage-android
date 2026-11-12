using Android.Content;
using Android.Graphics;
using Com.Nothing.Ketchum;

namespace SonNuage.Mobile.Glyph;

/// <summary>
/// Affiche <see cref="CloudGlyph"/> sur la matrice Glyph du Nothing Phone (3), par le SDK Glyph Matrix de Nothing
/// (téléchargé au build, voir le csproj). Deux usages : l'appli pendant la lecture (<see cref="ForApp"/>, cédant la
/// place à un Glyph Toy choisi par l'utilisateur) et le Glyph Toy SonNuage (<see cref="ForToy"/>).
/// Sur tout autre téléphone : rien (<see cref="Supported"/>).
/// </summary>
public sealed class GlyphCloud : Java.Lang.Object, GlyphMatrixManager.ICallback
{
    /// <summary>Environ 15 images par seconde : fluide sur 25×25 LED, sans réveiller le processeur pour rien.</summary>
    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(66);

    /// <summary>Musique en cours dans SonNuage : le Toy anime le logo, sinon il l'affiche fixe.</summary>
    public static volatile bool MusicPlaying;

    private readonly Context _context;
    private readonly bool _toy;
    private readonly object _lock = new();
    private GlyphMatrixManager? _manager;
    private bool _connected;
    private bool _wanted;
    private CancellationTokenSource? _loop;

    private GlyphCloud(Context context, bool toy)
    {
        _context = context.ApplicationContext ?? context;
        _toy = toy;
    }

    /// <summary>
    /// Nothing Phone (3) sous Android 13 ou plus (minimum du SDK). Le modèle est lu dans les propriétés système,
    /// sans appel au SDK : sur les autres téléphones, ses classes ne sont jamais chargées.
    /// </summary>
    public static bool Supported =>
        OperatingSystem.IsAndroidVersionAtLeast(33)
        && string.Equals(Android.OS.Build.Manufacturer, "Nothing", StringComparison.OrdinalIgnoreCase)
        && Common.Is23112();

    public static GlyphCloud? ForApp(Context context) => Supported ? new GlyphCloud(context, toy: false) : null;

    public static GlyphCloud ForToy(Context context) => new(context, toy: true);

    /// <summary>Allume (true) ou éteint (false) l'animation ; la connexion au service Glyph est ouverte au premier allumage.</summary>
    public void Show(bool on)
    {
        lock (_lock)
        {
            _wanted = on;
            if (on && _manager is null)
            {
                _manager = GlyphMatrixManager.GetInstance(_context);
                _manager!.Init(this);
                return; // OnServiceConnected démarre l'animation.
            }
            if (_connected) Apply();
        }
    }

    /// <summary>Éteint et ferme la connexion au service Glyph.</summary>
    public void Close()
    {
        lock (_lock)
        {
            _wanted = false;
            if (_connected) Apply();
            _manager?.UnInit();
            _manager = null;
            _connected = false;
        }
    }

    public void OnServiceConnected(ComponentName? name)
    {
        lock (_lock)
        {
            if (_manager is null) return;
            _connected = _manager.Register(Com.Nothing.Ketchum.Glyph.Device23112);
            if (_connected) Apply();
        }
    }

    public void OnServiceDisconnected(ComponentName? name)
    {
        lock (_lock)
        {
            _connected = false;
            StopLoop();
        }
    }

    /// <summary>Sous verrou : démarre ou arrête la boucle selon <see cref="_wanted"/>.</summary>
    private void Apply()
    {
        if (_wanted)
        {
            if (_loop is null) StartLoop();
            return;
        }
        StopLoop();
        try
        {
            if (_toy) _manager?.TurnOff();
            else _manager?.CloseAppMatrix();
        }
        catch (GlyphException)
        {
            // Système trop ancien pour l'affichage par une appli (avant 20250801) : rien n'était allumé.
        }
    }

    private void StartLoop()
    {
        var cts = _loop = new CancellationTokenSource();
        var manager = _manager!;
        _ = Task.Run(async () =>
        {
            var started = DateTime.UtcNow;
            using var timer = new PeriodicTimer(Tick);
            try
            {
                do
                {
                    var still = _toy && !MusicPlaying;
                    var frame = Render(still ? CloudGlyph.Logo() : CloudGlyph.Frame(DateTime.UtcNow - started));
                    if (_toy) manager.SetMatrixFrame(frame);
                    else manager.SetAppMatrixFrame(frame);
                }
                while (await timer.WaitForNextTickAsync(cts.Token));
            }
            catch (OperationCanceledException)
            {
            }
            catch (GlyphException)
            {
                // Affichage refusé (système trop ancien, service perdu) : l'animation s'arrête, la lecture continue.
            }
        });
    }

    private void StopLoop()
    {
        _loop?.Cancel();
        _loop = null;
    }

    /// <summary>
    /// Image du SDK à partir de nos luminosités : un bitmap 25×25 blanc dont l'opacité porte la luminosité, converti
    /// par le SDK lui-même (le format du tableau brut n'est pas documenté).
    /// </summary>
    private int[] Render(byte[] levels)
    {
        var pixels = new int[levels.Length];
        for (var i = 0; i < levels.Length; i++)
        {
            var v = levels[i];
            pixels[i] = Color.Argb(v, v, v, v).ToArgb();
        }
        using var bitmap = Bitmap.CreateBitmap(pixels, CloudGlyph.Size, CloudGlyph.Size, Bitmap.Config.Argb8888!)!;
        var image = new GlyphMatrixObject.Builder()
            .SetImageSource(bitmap)!
            .SetPosition(0, 0)!
            .SetScale(100)!
            .SetBrightness(255)!
            .Build()!;
        return new GlyphMatrixFrame.Builder().AddTop(image)!.Build(_context)!.Render()!;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) Close();
        base.Dispose(disposing);
    }
}

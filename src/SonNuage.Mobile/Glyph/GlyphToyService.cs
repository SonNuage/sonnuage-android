using Android.App;
using Android.Content;
using Android.OS;

namespace SonNuage.Mobile.Glyph;

/// <summary>
/// Glyph Toy « SonNuage » du Nothing Phone (3) : à choisir dans Réglages → Interface Glyph → Glyph Toys, puis à
/// afficher d'un appui sur le bouton Glyph. Le nuage danse pendant la lecture et reste fixe sinon. Le système lie le
/// service quand le Toy s'affiche et le délie quand on passe à un autre.
/// </summary>
[Service(Exported = true, Name = "org.pmjb.sonnuage.GlyphToyService")]
[IntentFilter(["com.nothing.glyph.TOY"])]
[MetaData("com.nothing.glyph.toy.name", Resource = "@string/glyph_toy_name")]
[MetaData("com.nothing.glyph.toy.image", Resource = "@drawable/glyph_toy")]
[MetaData("com.nothing.glyph.toy.summary", Resource = "@string/glyph_toy_summary")]
public sealed class GlyphToyService : Service
{
    private GlyphCloud? _cloud;

    public override IBinder? OnBind(Intent? intent)
    {
        _cloud ??= GlyphCloud.ForToy(this);
        _cloud.Show(true);
        // Le système envoie ses événements (appui long, AOD) par ce Messenger : ce Toy n'en utilise aucun.
        return new Messenger(new Handler(Looper.MainLooper!)).Binder;
    }

    public override bool OnUnbind(Intent? intent)
    {
        _cloud?.Close();
        _cloud = null;
        return false;
    }
}

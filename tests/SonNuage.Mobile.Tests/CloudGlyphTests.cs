using SonNuage.Mobile.Glyph;

namespace SonNuage.Mobile.Tests;

public sealed class CloudGlyphTests
{
    private const int N = CloudGlyph.Size;

    [Fact]
    public void The_matrix_is_the_round_489_led_screen_of_the_Phone_3()
    {
        var leds = 0;
        for (var y = 0; y < N; y++)
        for (var x = 0; x < N; x++)
            if (CloudGlyph.OnMatrix(x, y)) leds++;

        Assert.Equal(489, leds);
        Assert.False(CloudGlyph.OnMatrix(0, 0));
        Assert.True(CloudGlyph.OnMatrix(12, 0));
    }

    [Fact]
    public void The_logo_is_seven_bars_drawing_a_cloud()
    {
        var logo = CloudGlyph.Logo();

        Assert.Equal(N * N, logo.Length);
        Assert.Equal(CloudGlyph.Heights, BarHeights(logo, CloudGlyph.Lit));
        // Plus haut au milieu, plus bas aux bords : la silhouette du nuage.
        Assert.Equal(CloudGlyph.Heights.Max(), CloudGlyph.Heights[3]);
        Assert.True(CloudGlyph.Heights[0] < CloudGlyph.Heights[2]);
        Assert.True(CloudGlyph.Heights[6] < CloudGlyph.Heights[4]);
    }

    [Fact]
    public void Nothing_is_drawn_outside_the_round_screen()
    {
        foreach (var frame in new[] { CloudGlyph.Logo(), CloudGlyph.Frame(TimeSpan.FromSeconds(3.7)) })
        for (var y = 0; y < N; y++)
        for (var x = 0; x < N; x++)
            if (frame[y * N + x] > 0) Assert.True(CloudGlyph.OnMatrix(x, y), $"({x},{y})");
    }

    [Fact]
    public void While_playing_the_bars_dance_inside_the_dim_cloud()
    {
        for (var ms = 0; ms < 10_000; ms += 137)
        {
            var frame = CloudGlyph.Frame(TimeSpan.FromMilliseconds(ms));
            var bars = BarHeights(frame, CloudGlyph.Lit);
            var cloud = BarHeights(frame, CloudGlyph.Ghost, CloudGlyph.Lit);
            for (var i = 0; i < bars.Count; i++)
            {
                Assert.InRange(bars[i], (CloudGlyph.Heights[i] + 1) / 2, CloudGlyph.Heights[i]);
                // Le reste de la barre reste visible, en faible : le nuage garde sa forme.
                Assert.Equal(CloudGlyph.Heights[i], cloud[i]);
            }
        }
    }

    [Fact]
    public void The_animation_moves_and_is_the_same_at_the_same_time()
    {
        var a = CloudGlyph.Frame(TimeSpan.FromMilliseconds(1000));
        var b = CloudGlyph.Frame(TimeSpan.FromMilliseconds(1300));

        Assert.Equal(a, CloudGlyph.Frame(TimeSpan.FromMilliseconds(1000)));
        Assert.NotEqual(BarHeights(a, CloudGlyph.Lit), BarHeights(b, CloudGlyph.Lit));
    }

    /// <summary>Hauteur de chaque barre : pixels de la colonne gauche de la barre dont la luminosité est l'une de <paramref name="levels"/>.</summary>
    private static List<int> BarHeights(byte[] frame, params byte[] levels) =>
        CloudGlyph.BarColumns.Select(x => Enumerable.Range(0, N).Count(y => levels.Contains(frame[y * N + x]))).ToList();
}

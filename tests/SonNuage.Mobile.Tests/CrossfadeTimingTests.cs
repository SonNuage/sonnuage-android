using SonNuage.Mobile.Playback;

namespace SonNuage.Mobile.Tests;

public class CrossfadeTimingTests
{
    private const long Track = 200_000;
    private const int Fade = 3000;

    [Theory]
    [InlineData(0, 0)]
    [InlineData(2.5, 2500)]
    [InlineData(0.1, 100)]
    [InlineData(9, 5000)]
    [InlineData(-1, 0)]
    [InlineData(double.NaN, 0)]
    public void Fade_length_is_the_setting_clamped_to_zero_to_five_seconds(double seconds, int expectedMs) =>
        Assert.Equal(expectedMs, CrossfadeTiming.FadeMs(seconds));

    [Fact]
    public void Far_from_the_end_it_waits_at_most_a_second()
    {
        var step = CrossfadeTiming.Next(Track, 10_000, Fade);
        Assert.Equal(CrossfadeAction.Wait, step.Action);
        Assert.Equal(CrossfadeTiming.IdlePollMs, step.DelayMs);
    }

    [Fact]
    public void Just_before_the_arm_point_it_wakes_up_on_time()
    {
        var armAt = Track - Fade - CrossfadeTiming.ArmLeadMs;
        var step = CrossfadeTiming.Next(Track, armAt - 300, Fade);
        Assert.Equal(CrossfadeAction.Wait, step.Action);
        Assert.Equal(300, step.DelayMs);
    }

    [Fact]
    public void A_few_seconds_before_the_fade_the_second_player_gets_ready()
    {
        var step = CrossfadeTiming.Next(Track, Track - Fade - 1000, Fade);
        Assert.Equal(CrossfadeAction.Arm, step.Action);
        Assert.Equal(CrossfadeTiming.TickMs, step.DelayMs);
    }

    [Fact]
    public void Armed_close_to_the_start_it_wakes_up_exactly_at_the_start()
    {
        var step = CrossfadeTiming.Next(Track, Track - Fade - 20, Fade);
        Assert.Equal(CrossfadeAction.Arm, step.Action);
        Assert.Equal(20, step.DelayMs);
    }

    [Fact]
    public void When_the_remaining_time_drops_to_the_fade_length_the_next_track_starts() =>
        Assert.Equal(CrossfadeAction.Start, CrossfadeTiming.Next(Track, Track - Fade, Fade).Action);

    [Fact]
    public void A_late_tick_still_starts_the_fade() =>
        Assert.Equal(CrossfadeAction.Start, CrossfadeTiming.Next(Track, Track - Fade + CrossfadeTiming.LateMs - 1, Fade).Action);

    [Fact]
    public void After_a_seek_into_the_last_seconds_the_track_just_plays_out() =>
        Assert.Equal(new CrossfadeStep(CrossfadeAction.Wait, CrossfadeTiming.IdlePollMs), CrossfadeTiming.Next(Track, Track - 1000, Fade));

    [Theory]
    [InlineData(6000)]
    [InlineData(5000)]
    [InlineData(0)]
    public void A_track_not_longer_than_twice_the_fade_or_of_unknown_length_never_fades(long duration) =>
        Assert.Equal(new CrossfadeStep(CrossfadeAction.Wait, CrossfadeTiming.IdlePollMs), CrossfadeTiming.Next(duration, Math.Max(0, duration - Fade), Fade));

    [Fact]
    public void A_fade_of_zero_keeps_plain_gapless_playback() =>
        Assert.Equal(new CrossfadeStep(CrossfadeAction.Wait, CrossfadeTiming.IdlePollMs), CrossfadeTiming.Next(Track, Track - 10, 0));

    [Fact]
    public void Curve_starts_on_the_old_track_and_ends_on_the_new_one()
    {
        Assert.Equal((1.0, 0.0), Round(CrossfadeTiming.Gains(0)));
        Assert.Equal((0.0, 1.0), Round(CrossfadeTiming.Gains(1)));
    }

    [Theory]
    [InlineData(0.25)]
    [InlineData(0.5)]
    [InlineData(0.8)]
    public void Curve_keeps_the_power_constant_so_there_is_no_dip(double progress)
    {
        var (fadingOut, fadingIn) = CrossfadeTiming.Gains(progress);
        Assert.Equal(1.0, fadingOut * fadingOut + fadingIn * fadingIn, 6);
        Assert.True(fadingOut is > 0 and < 1 && fadingIn is > 0 and < 1);
    }

    [Fact]
    public void Curve_is_monotonic() =>
        Assert.True(CrossfadeTiming.Gains(0.3).In < CrossfadeTiming.Gains(0.6).In && CrossfadeTiming.Gains(0.3).Out > CrossfadeTiming.Gains(0.6).Out);

    [Theory]
    [InlineData(-50, 0)]
    [InlineData(0, 0)]
    [InlineData(1500, 0.5)]
    [InlineData(3000, 1)]
    [InlineData(9000, 1)]
    public void Progress_runs_from_zero_to_one_over_the_fade(long elapsedMs, double expected) =>
        Assert.Equal(expected, CrossfadeTiming.Progress(elapsedMs, Fade), 6);

    [Fact]
    public void Repeat_one_fades_into_the_same_track() =>
        Assert.Equal(4, CrossfadeTiming.Target(current: 4, next: 5, repeatOne: true));

    [Fact]
    public void Otherwise_it_fades_into_the_next_track_or_nowhere_at_the_end_of_the_queue()
    {
        Assert.Equal(5, CrossfadeTiming.Target(current: 4, next: 5, repeatOne: false));
        Assert.Equal(0, CrossfadeTiming.Target(current: 4, next: 0, repeatOne: false));
        Assert.Equal(-1, CrossfadeTiming.Target(current: 4, next: -1, repeatOne: false));
    }

    [Theory]
    [InlineData(180_000, 200, 180_000)]
    [InlineData(-9223372036854775807, 200, 200_000)]
    [InlineData(0, 200.5, 200_500)]
    [InlineData(-9223372036854775807, 0, 0)]
    public void Duration_comes_from_the_player_else_from_the_track_metadata(long playerMs, double metadataSeconds, long expected) =>
        Assert.Equal(expected, CrossfadeTiming.DurationMs(playerMs, metadataSeconds));

    private static (double, double) Round((double Out, double In) g) => (Math.Round(g.Out, 9), Math.Round(g.In, 9));
}

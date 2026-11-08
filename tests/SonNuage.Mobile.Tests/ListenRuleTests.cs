using SonNuage.Mobile.Playback;

namespace SonNuage.Mobile.Tests;

public class ListenRuleTests
{
    [Fact]
    public void A_track_played_to_the_end_counts_for_its_whole_length() =>
        Assert.Equal(240, ListenRule.Played(seconds: 239.6, durationSeconds: 240, completed: true));

    [Fact]
    public void Skipped_after_thirty_seconds_counts_for_the_time_heard() =>
        Assert.Equal(31, ListenRule.Played(seconds: 31, durationSeconds: 240, completed: false));

    [Fact]
    public void Skipped_before_thirty_seconds_does_not_count() =>
        Assert.Null(ListenRule.Played(seconds: 29, durationSeconds: 240, completed: false));

    [Fact]
    public void A_short_track_counts_after_half_of_it() =>
        Assert.Equal(21, ListenRule.Played(seconds: 21, durationSeconds: 40, completed: false));

    [Fact]
    public void A_short_track_skipped_before_half_does_not_count() =>
        Assert.Null(ListenRule.Played(seconds: 19, durationSeconds: 40, completed: false));
}

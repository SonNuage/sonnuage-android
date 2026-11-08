using SonNuage.Mobile.Playback;

namespace SonNuage.Mobile.Tests;

public class PrefetchPlanTests
{
    [Fact]
    public void The_tracks_after_the_current_one_are_fetched_in_order() =>
        Assert.Equal([3, 4, 5], PrefetchPlan.Indices(count: 10, current: 2, ahead: 3, repeatAll: false));

    [Fact]
    public void Without_repeat_it_stops_at_the_end_of_the_queue() =>
        Assert.Equal([9], PrefetchPlan.Indices(count: 10, current: 8, ahead: 3, repeatAll: false));

    [Fact]
    public void With_repeat_all_it_wraps_to_the_start() =>
        Assert.Equal([9, 0, 1], PrefetchPlan.Indices(count: 10, current: 8, ahead: 3, repeatAll: true));

    [Fact]
    public void With_repeat_all_it_never_comes_back_to_the_current_track() =>
        Assert.Equal([2, 0], PrefetchPlan.Indices(count: 3, current: 1, ahead: 5, repeatAll: true));

    [Theory]
    [InlineData(0, 0)]
    [InlineData(5, -1)]
    public void An_empty_queue_or_nothing_playing_fetches_nothing(int count, int current) =>
        Assert.Empty(PrefetchPlan.Indices(count, current, ahead: 3, repeatAll: true));

    [Fact]
    public void Prefetch_turned_off_fetches_nothing() =>
        Assert.Empty(PrefetchPlan.Indices(count: 10, current: 2, ahead: 0, repeatAll: false));
}

using SonNuage.Mobile.Playback;

namespace SonNuage.Mobile.Tests;

public class QueueSnapshotTests
{
    private static List<Guid?> Queue(int count) => Enumerable.Range(1, count).Select(i => (Guid?)Id(i)).ToList();

    private static Guid Id(int n) => new($"00000000-0000-0000-0000-{n:D12}");

    [Fact]
    public void A_short_queue_is_saved_whole_with_its_index()
    {
        var (ids, index) = QueueSnapshot.Take(Queue(5), currentIndex: 3, max: 500);
        Assert.Equal(Queue(5).Select(g => g!.Value), ids);
        Assert.Equal(3, index);
    }

    [Fact]
    public void In_a_long_queue_the_saved_index_still_points_at_the_playing_track()
    {
        var queue = Queue(800);
        var (ids, index) = QueueSnapshot.Take(queue, currentIndex: 650, max: 500);
        Assert.Equal(500, ids.Count);
        Assert.Equal(Id(651), ids[index]);
    }

    [Fact]
    public void A_long_queue_keeps_tracks_on_both_sides_of_the_playing_one()
    {
        var (ids, index) = QueueSnapshot.Take(Queue(800), currentIndex: 400, max: 10);
        Assert.Equal(Id(401), ids[index]);
        Assert.Equal(5, index);
        Assert.Equal(Id(396), ids[0]);
    }

    [Fact]
    public void Near_the_end_the_window_ends_with_the_queue()
    {
        var (ids, index) = QueueSnapshot.Take(Queue(800), currentIndex: 798, max: 10);
        Assert.Equal(Id(800), ids[^1]);
        Assert.Equal(Id(799), ids[index]);
    }

    [Fact]
    public void Items_without_a_track_are_skipped_and_the_index_follows()
    {
        var queue = new List<Guid?> { Id(1), null, Id(3), null, Id(5) };
        var (ids, index) = QueueSnapshot.Take(queue, currentIndex: 4, max: 500);
        Assert.Equal([Id(1), Id(3), Id(5)], ids);
        Assert.Equal(2, index);
    }

    [Fact]
    public void Nothing_playing_saves_from_the_start()
    {
        var (ids, index) = QueueSnapshot.Take(Queue(3), currentIndex: -1, max: 500);
        Assert.Equal(3, ids.Count);
        Assert.Equal(0, index);
    }

    [Fact]
    public void An_empty_queue_saves_nothing()
    {
        var (ids, index) = QueueSnapshot.Take([], currentIndex: 0, max: 500);
        Assert.Empty(ids);
        Assert.Equal(0, index);
    }
}

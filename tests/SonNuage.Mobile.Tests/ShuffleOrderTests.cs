using SonNuage.Mobile.Playback;

namespace SonNuage.Mobile.Tests;

public class ShuffleOrderTests
{
    private static bool Same(string a, string b) => a == b;

    [Fact]
    public void Shuffling_keeps_every_other_track_once_and_leaves_out_the_current_one()
    {
        string[] queue = ["a", "b", "c", "d", "e"];
        var rest = ShuffleOrder.Others(queue, current: 2, new Random(7));
        Assert.DoesNotContain("c", rest);
        Assert.Equal(["a", "b", "d", "e"], rest.Order());
    }

    [Fact]
    public void Shuffling_actually_changes_the_order()
    {
        var queue = Enumerable.Range(0, 50).Select(i => i.ToString()).ToArray();
        var rest = ShuffleOrder.Others(queue, current: 0, new Random(7));
        Assert.NotEqual(queue.Skip(1), rest);
    }

    [Fact]
    public void Unshuffling_brings_back_the_original_order()
    {
        Assert.Equal(["a", "b", "c", "d"], ShuffleOrder.Restore(["a", "b", "c", "d"], ["c", "a", "d", "b"], Same));
    }

    [Fact]
    public void Tracks_removed_while_shuffled_stay_removed()
    {
        Assert.Equal(["a", "c"], ShuffleOrder.Restore(["a", "b", "c"], ["c", "a"], Same));
    }

    [Fact]
    public void Tracks_added_while_shuffled_come_after_the_original_ones_in_play_order()
    {
        Assert.Equal(["a", "b", "y", "x"], ShuffleOrder.Restore(["a", "b"], ["b", "y", "a", "x"], Same));
    }

    [Fact]
    public void A_track_queued_twice_is_restored_twice()
    {
        Assert.Equal(["a", "b", "a"], ShuffleOrder.Restore(["a", "b", "a"], ["a", "a", "b"], Same));
    }
}

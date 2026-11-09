using SonNuage.Mobile.Playback;

namespace SonNuage.Mobile.Tests;

public class BrowseIdsTests
{
    private static readonly Guid Song = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Guid Disc = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    [Fact]
    public void An_album_id_parses_back_to_its_kind_and_guid() =>
        Assert.Equal((BrowseIds.AlbumKind, Disc), BrowseIds.Parse(BrowseIds.Album(Disc)));

    [Fact]
    public void A_fixed_folder_has_no_guid() =>
        Assert.Equal((BrowseIds.Favorites, Guid.Empty), BrowseIds.Parse(BrowseIds.Favorites));

    [Fact]
    public void A_garbled_guid_parses_as_empty() =>
        Assert.Equal((BrowseIds.PlaylistKind, Guid.Empty), BrowseIds.Parse("playlist:zzz"));

    [Fact]
    public void A_track_id_is_read_from_its_media_id() =>
        Assert.Equal(Song, BrowseIds.TrackId(BrowseIds.Track(Song)));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("album:aaaaaaaabbbbccccddddeeeeeeeeeeee")]
    [InlineData("track:not-a-guid")]
    [InlineData("track:00000000000000000000000000000000")]
    public void Anything_else_is_not_a_track(string? mediaId) => Assert.Null(BrowseIds.TrackId(mediaId));

    [Fact]
    public void A_track_listed_in_an_album_splits_into_the_track_and_the_album()
    {
        var split = BrowseIds.SplitContext(BrowseIds.InContext(Song, BrowseIds.Album(Disc)));
        Assert.Equal((Song, BrowseIds.Album(Disc)), split);
    }

    [Fact]
    public void A_plain_track_has_no_context() => Assert.Null(BrowseIds.SplitContext(BrowseIds.Track(Song)));

    [Fact]
    public void A_context_with_a_garbled_track_still_names_the_context() =>
        Assert.Equal((Guid.Empty, BrowseIds.Favorites), BrowseIds.SplitContext("track:x@favorites"));

    [Fact]
    public void A_station_id_is_read_back_from_its_media_id() =>
        Assert.Equal("good-mood-pop", BrowseIds.StationId(BrowseIds.Station("good-mood-pop")));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("station:")]
    [InlineData(BrowseIds.Radios)]
    [InlineData("track:11111111222233334444555555555555")]
    public void Anything_else_is_not_a_station(string? mediaId) => Assert.Null(BrowseIds.StationId(mediaId));

    [Fact]
    public void A_station_is_not_mistaken_for_a_track() => Assert.Null(BrowseIds.TrackId(BrowseIds.Station("halloween")));

    [Fact]
    public void The_radios_folder_reloads_when_the_account_changes() => Assert.Contains(BrowseIds.Radios, BrowseIds.AccountFolders);
}

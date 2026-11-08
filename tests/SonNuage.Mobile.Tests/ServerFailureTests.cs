using System.Text.Json;
using SonNuage.Mobile.Playback;

namespace SonNuage.Mobile.Tests;

public class ServerFailureTests
{
    public static TheoryData<Exception> Expected =>
    [
        new HttpRequestException("no route to host"),
        new TaskCanceledException("HttpClient.Timeout"),
        new TimeoutException(),
        new IOException("connection reset"),
        new JsonException("truncated body"),
    ];

    public static TheoryData<Exception> Bugs =>
    [
        new NullReferenceException(),
        new InvalidOperationException(),
        new ArgumentOutOfRangeException(),
    ];

    [Theory]
    [MemberData(nameof(Expected))]
    public void Network_trouble_and_unreadable_replies_are_expected_in_the_background(Exception ex) =>
        Assert.True(ServerFailure.IsNetwork(ex));

    [Theory]
    [MemberData(nameof(Bugs))]
    public void Programming_errors_are_not_swallowed(Exception ex) => Assert.False(ServerFailure.IsNetwork(ex));
}

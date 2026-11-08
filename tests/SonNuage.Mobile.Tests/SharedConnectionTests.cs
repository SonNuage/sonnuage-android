using SonNuage.Mobile.Services;

namespace SonNuage.Mobile.Tests;

public class SharedConnectionTests
{
    private sealed class Link
    {
        public bool Alive { get; set; } = true;
    }

    private sealed class Server
    {
        public int Connects { get; private set; }
        public TaskCompletionSource<Link>? Pending { get; private set; }
        public List<Link> Dropped { get; } = [];
        public bool Fail { get; set; }
        public bool Manual { get; set; }

        public Task<Link> ConnectAsync()
        {
            Connects++;
            if (Fail) return Task.FromException<Link>(new InvalidOperationException("service unavailable"));
            if (!Manual) return Task.FromResult(new Link());
            Pending = new TaskCompletionSource<Link>();
            return Pending.Task;
        }

        public SharedConnection<Link> Connection() => new(ConnectAsync, l => l.Alive, Dropped.Add);
    }

    [Fact]
    public async Task A_live_connection_is_reused()
    {
        var server = new Server();
        var connection = server.Connection();
        var first = await connection.GetAsync();
        Assert.Same(first, await connection.GetAsync());
        Assert.Equal(1, server.Connects);
    }

    [Fact]
    public async Task Callers_during_the_connection_share_it()
    {
        var server = new Server { Manual = true };
        var connection = server.Connection();
        var a = connection.GetAsync();
        var b = connection.GetAsync();
        server.Pending!.SetResult(new Link());
        Assert.Same(await a, await b);
        Assert.Equal(1, server.Connects);
    }

    [Fact]
    public async Task After_a_failed_connection_the_next_call_tries_again()
    {
        var server = new Server { Fail = true };
        var connection = server.Connection();
        await Assert.ThrowsAsync<InvalidOperationException>(connection.GetAsync);
        server.Fail = false;
        Assert.NotNull(await connection.GetAsync());
        Assert.Equal(2, server.Connects);
    }

    [Fact]
    public async Task A_dead_connection_is_released_and_replaced()
    {
        var server = new Server();
        var connection = server.Connection();
        var first = await connection.GetAsync();
        first.Alive = false;
        var second = await connection.GetAsync();
        Assert.NotSame(first, second);
        Assert.Equal([first], server.Dropped);
    }
}

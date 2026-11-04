using SolidWorksMcp.Infrastructure;

namespace SolidWorksMcp.Tests;

public class StaDispatcherTests
{
    [Fact]
    public async Task RunsAllWorkOnOneDedicatedThread()
    {
        using var sta = new StaDispatcher(new ServerOptions());
        var ids = await Task.WhenAll(Enumerable.Range(0, 50).Select(_ => Task.Run(() => sta.InvokeAsync(() => Environment.CurrentManagedThreadId))));
        Assert.All(ids, id => Assert.Equal(sta.ThreadId, id));
        Assert.NotEqual(Environment.CurrentManagedThreadId, sta.ThreadId);
    }

    [Fact]
    public async Task SerializesConcurrentCalls()
    {
        using var sta = new StaDispatcher(new ServerOptions());
        int active = 0, maxActive = 0;
        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => sta.InvokeAsync(() =>
        {
            int now = Interlocked.Increment(ref active);
            maxActive = Math.Max(maxActive, now);
            Thread.Sleep(2);
            Interlocked.Decrement(ref active);
        })));
        Assert.Equal(1, maxActive);
    }

    [Fact]
    public async Task UsesStaApartmentOnWindows()
    {
        using var sta = new StaDispatcher(new ServerOptions());
        var state = await sta.InvokeAsync(() => Thread.CurrentThread.GetApartmentState());
        if (OperatingSystem.IsWindows())
        {
            Assert.Equal(ApartmentState.STA, state);
        }
    }

    [Fact]
    public async Task PropagatesExceptions()
    {
        using var sta = new StaDispatcher(new ServerOptions());
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => sta.InvokeAsync<int>(() => throw new InvalidOperationException("boom")));
        Assert.Equal("boom", ex.Message);
        Assert.Equal(42, await sta.InvokeAsync(() => 42)); // thread survives the exception
    }

    [Fact]
    public async Task HonoursCancellationBeforeStart()
    {
        using var sta = new StaDispatcher(new ServerOptions());
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sta.InvokeAsync(() => 1, cts.Token));
    }
}

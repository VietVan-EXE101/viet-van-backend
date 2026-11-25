using VietVan.API.Services.Authentication;
using Xunit;

namespace VietVan.Payment.Tests;

public class GoogleChallengeStoreTests
{
    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public void Challenge_expires_after_five_minutes()
    {
        var clock = new TestClock(); var store = new GoogleChallengeStore(clock);
        var challenge = store.Create(); clock.Now = clock.Now.AddMinutes(5);
        Assert.Null(store.Consume(challenge.Key));
        Assert.Null(store.Consume(challenge.Key));
    }
    [Fact]
    public void Challenge_nonce_is_random_and_consumed_once()
    {
        var store = new GoogleChallengeStore();
        var first = store.Create();
        var second = store.Create();
        Assert.NotEqual(first.Key, second.Key);
        Assert.NotEqual(first.Nonce, second.Nonce);
        Assert.Equal(first.Nonce, store.Consume(first.Key));
        Assert.Null(store.Consume(first.Key));
        Assert.Equal(second.Nonce, store.Consume(second.Key));
    }

    [Fact]
    public async Task Concurrent_consumers_only_receive_nonce_once()
    {
        var store = new GoogleChallengeStore();
        var challenge = store.Create();
        var tasks = Enumerable.Range(0, 20).Select(_ => Task.Run(() => store.Consume(challenge.Key)));
        var results = await Task.WhenAll(tasks);
        Assert.Single(results, x => x == challenge.Nonce);
    }
}

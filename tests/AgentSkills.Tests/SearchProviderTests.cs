using AgentSkills.Sources;
using Xunit;

namespace AgentSkills.Tests;

public class SearchProviderTests
{
    [Theory]
    [InlineData(0, "")]
    [InlineData(1, "1 install")]
    [InlineData(42, "42 installs")]
    [InlineData(1_000, "1K installs")]
    [InlineData(1_500, "1.5K installs")]
    [InlineData(45_600, "45.6K installs")]
    [InlineData(1_000_000, "1M installs")]
    [InlineData(2_300_000, "2.3M installs")]
    public void FormatInstalls(long count, string expected)
    {
        Assert.Equal(expected, SearchFormatting.FormatInstalls(count));
    }

    [Fact]
    public async Task FakeProvider_ReturnsHits()
    {
        var provider = new FakeSearchProvider("test", enabled: true,
            new SearchHit("acme/sample", "alpha", "acme/sample", 100));

        var hits = await provider.SearchAsync("alpha", CancellationToken.None);
        Assert.Single(hits);
        Assert.Equal("alpha", hits[0].Name);
    }

    [Fact]
    public async Task FakeProvider_RespectsCancellation()
    {
        using var cts = new CancellationTokenSource();
        var provider = new SlowSearchProvider();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            provider.SearchAsync("anything", cts.Token));
    }

    private sealed class FakeSearchProvider : ISkillSearchProvider
    {
        private readonly SearchHit[] _hits;
        public FakeSearchProvider(string name, bool enabled, params SearchHit[] hits)
        {
            Name = name;
            IsEnabled = enabled;
            _hits = hits;
        }
        public string Name { get; }
        public bool IsEnabled { get; }
        public Task<IReadOnlyList<SearchHit>> SearchAsync(string query, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<SearchHit>>(_hits);
    }

    private sealed class SlowSearchProvider : ISkillSearchProvider
    {
        public string Name => "slow";
        public bool IsEnabled => true;
        public async Task<IReadOnlyList<SearchHit>> SearchAsync(string query, CancellationToken ct)
        {
            await Task.Delay(Timeout.Infinite, ct);
            return [];
        }
    }
}

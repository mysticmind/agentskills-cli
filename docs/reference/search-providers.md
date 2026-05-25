# Search providers (extension point)

[`find`](/commands/find) discovers skills through one or more **search providers**. AgentSkills CLI ships one out of the box (`skills.sh`); you can register more without forking.

## The contract

A provider implements `ISkillSearchProvider`:

```csharp
public interface ISkillSearchProvider
{
    string Name { get; }            // dedup + filter key, shown in the result table
    bool IsEnabled { get; }         // skip when false (auth missing, feature flag off, ...)
    Task<IReadOnlyList<SearchHit>> SearchAsync(string query, CancellationToken ct);
}
```

Implementations are stateless and thread-safe. The `IsEnabled` gate lets you skip a provider conditionally (e.g., when an API key env var is unset).

## Registration

Register after `AddAgentSkillsRuntime`:

```csharp
services
    .AddAgentSkillsRuntime()
    .AddSingleton<ISkillSearchProvider, ContosoInternalSearchProvider>();
```

Multiple providers can be registered; `find` will query all enabled ones in parallel.

## Behavior

`find` calls every enabled provider in parallel (10-second timeout each), merges results by `(source, name)`, sorts by installs, and tags each row with the provider it came from.

A failing or slow provider is dropped from the run; the rest still render. This means a custom provider experiencing an outage doesn't break the user's `find` workflow - they just see results from the providers that responded.

## Two extension points, two roles

| Extension point | Role | Built-in implementations |
|---|---|---|
| `ISkillSourceFactory` | *where you install from* | `LocalSourceFactory`, `GitSourceFactory`, `NuGetSourceFactory`, `NpmSourceFactory`, `WellKnownSourceFactory` |
| `ISkillSearchProvider` | *where you discover from* | `SkillsShSearchProvider` (built-in); add your own |

These are deliberately separate. "I want to install skills from npm" is a different concern from "I want skill search to query our internal registry too." A user can have both customized.

## Use cases for custom providers

- **Internal corporate registry** - "find" results should include team-owned skills published behind your VPN.
- **GitHub topic search** - rank repos tagged `agent-skills` directly from the GitHub API.
- **Offline / installed-only** - cheap local provider that searches your lock files when you're disconnected.
- **Multiple regional mirrors** - geo-distribute search load.

## Example: a minimal local-cache provider

```csharp
using AgentSkills.Sources;
using AgentSkills.Install;

public sealed class LocalCacheSearchProvider : ISkillSearchProvider
{
    public string Name => "local-cache";
    public bool IsEnabled => true;

    public Task<IReadOnlyList<SearchHit>> SearchAsync(string query, CancellationToken ct)
    {
        var lockDoc = SkillLock.Load();
        var matches = lockDoc.Skills
            .Where(kv => kv.Key.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Select(kv => new SearchHit(
                Slug: kv.Key,
                Name: kv.Key,
                Source: kv.Value.Source ?? string.Empty,
                Installs: 0))
            .ToList();
        return Task.FromResult<IReadOnlyList<SearchHit>>(matches);
    }
}
```

Register it; `agentskills-cli find` now also returns results matching your locally tracked skills, useful when offline.

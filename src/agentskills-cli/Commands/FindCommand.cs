using System.ComponentModel;
using AgentSkills.Agents;
using AgentSkills.Install;
using AgentSkills.Sources;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AgentSkills.Commands;

public sealed class FindCommand : AsyncCommand<FindCommand.Settings>
{
    /// <summary>Per-provider timeout. Beyond this, a provider is dropped from the fan-out.</summary>
    private static readonly TimeSpan ProviderTimeout = TimeSpan.FromSeconds(10);

    private readonly IInstallService _installer;
    private readonly IReadOnlyList<ISkillSearchProvider> _providers;
    private readonly IAnsiConsole _console;
    private readonly AgentSkills.Hosting.CommandCancellation _cancellation;

    public FindCommand(
        IInstallService installer,
        IEnumerable<ISkillSearchProvider> providers,
        IAnsiConsole console,
        AgentSkills.Hosting.CommandCancellation cancellation)
    {
        _installer = installer ?? throw new ArgumentNullException(nameof(installer));
        _providers = providers?.ToList() ?? throw new ArgumentNullException(nameof(providers));
        _console = console ?? throw new ArgumentNullException(nameof(console));
        _cancellation = cancellation ?? throw new ArgumentNullException(nameof(cancellation));
    }

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "[QUERY]")]
        [Description("Search query. When omitted, you'll be prompted interactively.")]
        public string[] QueryParts { get; init; } = [];

        [CommandOption("-y|--yes")]
        [Description("Skip the install confirmation when picking a result.")]
        public bool Yes { get; init; }

        [CommandOption("-g|--global")]
        [Description("Install the chosen skill globally instead of into the project.")]
        public bool Global { get; init; }

        [CommandOption("--provider <NAME>")]
        [Description("Restrict the search to specific providers (e.g. skills.sh). Repeatable. Omit to fan out across every enabled provider.")]
        public string[] Providers { get; init; } = [];

        [CommandOption("--list-providers")]
        [Description("Print the registered search providers and exit.")]
        public bool ListProviders { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (settings.ListProviders)
        {
            RenderProviderList();
            return 0;
        }

        var selectedProviders = ResolveProviders(settings.Providers);
        if (selectedProviders.Count == 0)
        {
            _console.MarkupLine("[yellow]No enabled search providers.[/]");
            return 1;
        }

        var query = string.Join(' ', settings.QueryParts).Trim();
        var interactive = _console.Profile.Capabilities.Interactive;

        if (string.IsNullOrEmpty(query))
        {
            if (!interactive)
            {
                _console.MarkupLine("[grey]Usage:[/] agentskills-cli find <query>");
                return 64;
            }
            query = _console.Prompt(new TextPrompt<string>("[bold]Search skills:[/]")
                .PromptStyle("cyan")
                .AllowEmpty());
            if (string.IsNullOrWhiteSpace(query))
            {
                _console.MarkupLine("[grey]Search cancelled.[/]");
                return 0;
            }
        }

        var providersLabel = selectedProviders.Count == 1
            ? selectedProviders[0].Name
            : $"{selectedProviders.Count} providers";

        var hits = await _console.Status()
            .Spinner(Spinner.Known.Dots)
            .StartAsync(
                $"Searching {providersLabel} for \"{Markup.Escape(query)}\"...",
                _ => RunFanOutAsync(query, selectedProviders, _cancellation.Token));

        if (hits.Count == 0)
        {
            _console.MarkupLine($"[grey]No skills found for[/] [yellow]{Markup.Escape(query)}[/]");
            return 0;
        }

        PrintResults(hits);

        // Non-interactive (script / CI / piped) or query-on-cmdline-and-not-tty: print and exit.
        if (!interactive)
        {
            return 0;
        }

        _console.WriteLine();

        var byLabel = hits
            .Select(r => new
            {
                Label = $"{Markup.Escape(r.Hit.Source)}@{Markup.Escape(r.Hit.Name)}  " +
                        $"[grey]{Markup.Escape(Truncate(r.Hit.Slug, 50))}" +
                        $"{(r.Hit.Installs > 0 ? "  " + SearchFormatting.FormatInstalls(r.Hit.Installs) : string.Empty)}" +
                        $"  [italic]({Markup.Escape(r.ProviderName)})[/][/]",
                Result = r,
            })
            .ToDictionary(x => x.Label, x => x.Result);

        const string cancelLabel = "[grey](cancel)[/]";
        byLabel[cancelLabel] = null!;

        var picked = _console.Prompt(new SelectionPrompt<string>()
            .Title("[bold]Install which skill?[/]")
            .PageSize(12)
            .AddChoices(byLabel.Keys));

        if (byLabel[picked] is not { } chosen)
        {
            _console.MarkupLine("[grey]Cancelled.[/]");
            return 0;
        }

        var installSource = string.IsNullOrEmpty(chosen.Hit.Source)
            ? chosen.Hit.Slug
            : $"{chosen.Hit.Source}@{chosen.Hit.Name}";

        _console.MarkupLine(
            $"[cyan]Installing {Markup.Escape(chosen.Hit.Name)} from {Markup.Escape(chosen.Hit.Source)}...[/]");

        var detectedAgents = AgentRegistry.DetectInstalled();
        var targetAgents = detectedAgents.Count > 0 ? detectedAgents : [AgentRegistry.Get("universal")];

        var request = new InstallRequest(
            SourceInput: installSource,
            SkillFilter: [chosen.Hit.Name],
            Agents: targetAgents,
            Global: settings.Global,
            Mode: InstallMode.Copy,
            ResolveOptions: ResolveOptions.Default,
            IncludeInternal: false);

        var summary = await _installer.InstallAsync(request, _cancellation.Token);
        var failures = summary.Outcomes.Count(o => !o.Result.Success);
        _console.MarkupLine(failures == 0
            ? $"[green]Installed {Markup.Escape(chosen.Hit.Name)}.[/]"
            : $"[yellow]Installed {Markup.Escape(chosen.Hit.Name)} with {failures} failure(s).[/]");
        return failures == 0 ? 0 : 1;
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private IReadOnlyList<ISkillSearchProvider> ResolveProviders(string[] requested)
    {
        var enabled = _providers.Where(p => p.IsEnabled).ToList();

        if (requested.Length == 0)
        {
            return enabled;
        }

        var requestedSet = new HashSet<string>(requested, StringComparer.OrdinalIgnoreCase);
        var matched = enabled.Where(p => requestedSet.Contains(p.Name)).ToList();

        var unknown = requested
            .Where(r => !enabled.Any(p => p.Name.Equals(r, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (unknown.Count > 0)
        {
            throw new UserInputException(
                $"Unknown search provider(s): {string.Join(", ", unknown)}. " +
                $"Available: {string.Join(", ", enabled.Select(p => p.Name))}.");
        }

        return matched;
    }

    private void RenderProviderList()
    {
        if (_providers.Count == 0)
        {
            _console.MarkupLine("[yellow]No search providers registered.[/]");
            return;
        }

        var table = new Table().Border(TableBorder.Rounded).LeftAligned();
        table.AddColumns("Provider", "Status", "Implementation");
        foreach (var provider in _providers)
        {
            table.AddRow(
                Markup.Escape(provider.Name),
                provider.IsEnabled ? "[green]enabled[/]" : "[grey]disabled[/]",
                $"[grey]{Markup.Escape(provider.GetType().FullName ?? provider.GetType().Name)}[/]");
        }
        _console.Write(table);
    }

    private static async Task<List<HitWithProvider>> RunFanOutAsync(
        string query,
        IReadOnlyList<ISkillSearchProvider> providers,
        CancellationToken cancellationToken)
    {
        var tasks = providers
            .Select(p => RunOneAsync(p, query, cancellationToken))
            .ToList();

        await Task.WhenAll(tasks).ConfigureAwait(false);

        // Dedupe by (Source, Name); first provider to mention a skill wins. Sort by installs.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var merged = new List<HitWithProvider>();
        foreach (var task in tasks)
        {
            foreach (var hit in task.Result.Hits)
            {
                var key = $"{hit.Source}{hit.Name}";
                if (seen.Add(key))
                {
                    merged.Add(new HitWithProvider(hit, task.Result.ProviderName));
                }
            }
        }
        return merged.OrderByDescending(x => x.Hit.Installs).ToList();
    }

    private static async Task<ProviderResult> RunOneAsync(
        ISkillSearchProvider provider, string query, CancellationToken outer)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(outer);
        cts.CancelAfter(ProviderTimeout);
        try
        {
            var hits = await provider.SearchAsync(query, cts.Token).ConfigureAwait(false);
            return new ProviderResult(provider.Name, hits);
        }
        catch
        {
            // A failing provider must not break the fan-out. The provider itself is expected
            // to log the cause; here we just drop it from this run.
            return new ProviderResult(provider.Name, []);
        }
    }

    private void PrintResults(IReadOnlyList<HitWithProvider> results)
    {
        var table = new Table().Border(TableBorder.Rounded).LeftAligned();
        table.AddColumns("Source", "Skill", "Installs", "Slug", "Provider");
        foreach (var row in results.Take(10))
        {
            table.AddRow(
                Markup.Escape(row.Hit.Source),
                Markup.Escape(row.Hit.Name),
                Markup.Escape(SearchFormatting.FormatInstalls(row.Hit.Installs)),
                $"[grey]{Markup.Escape(row.Hit.Slug)}[/]",
                $"[italic grey]{Markup.Escape(row.ProviderName)}[/]");
        }
        _console.Write(table);
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..(max - 1)] + "…";

    private sealed record ProviderResult(string ProviderName, IReadOnlyList<SearchHit> Hits);
    private sealed record HitWithProvider(SearchHit Hit, string ProviderName);
}

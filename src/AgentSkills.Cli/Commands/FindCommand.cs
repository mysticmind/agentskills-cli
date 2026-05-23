using System.ComponentModel;
using AgentSkills.Agents;
using AgentSkills.Install;
using AgentSkills.Sources;
using AgentSkills.Ui;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AgentSkills.Commands;

public sealed class FindCommand : AsyncCommand<FindCommand.Settings>
{
    private readonly IInstallService _installer;
    private readonly IAnsiConsole _console;
    private readonly AgentSkills.Hosting.CommandCancellation _cancellation;

    public FindCommand(IInstallService installer, IAnsiConsole console, AgentSkills.Hosting.CommandCancellation cancellation)
    {
        _installer = installer ?? throw new ArgumentNullException(nameof(installer));
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
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var query = string.Join(' ', settings.QueryParts).Trim();
        var interactive = AnsiConsole.Profile.Capabilities.Interactive;

        if (string.IsNullOrEmpty(query))
        {
            if (!interactive)
            {
                AnsiConsole.MarkupLine("[grey]Usage:[/] skills find <query>");
                return 64;
            }
            query = AnsiConsole.Prompt(new TextPrompt<string>("[bold]Search skills:[/]")
                .PromptStyle("cyan")
                .AllowEmpty());
            if (string.IsNullOrWhiteSpace(query))
            {
                AnsiConsole.MarkupLine("[grey]Search cancelled.[/]");
                return 0;
            }
        }

        var results = await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .StartAsync($"Searching skills.sh for \"{query}\"...",
                async _ => await SkillsApi.SearchAsync(query));

        if (results.Count == 0)
        {
            AnsiConsole.MarkupLine($"[grey]No skills found for[/] [yellow]{Markup.Escape(query)}[/]");
            return 0;
        }

        if (!interactive || settings.Yes && settings.QueryParts.Length == 0)
        {
            PrintResults(results);
            return 0;
        }

        if (settings.QueryParts.Length > 0 && !interactive)
        {
            PrintResults(results);
            return 0;
        }

        // Interactive: show printable list, then offer a selection prompt.
        PrintResults(results);
        AnsiConsole.WriteLine();

        var byLabel = results
            .Select(r => new
            {
                Label = $"{Markup.Escape(r.Source)}@{Markup.Escape(r.Name)}  [grey]{Markup.Escape(Truncate(r.Slug, 50))}{(r.Installs > 0 ? "  " + SkillsApi.FormatInstalls(r.Installs) : string.Empty)}[/]",
                Hit = r,
            })
            .ToDictionary(x => x.Label, x => x.Hit);

        byLabel[$"[grey](cancel)[/]"] = null!;

        var picked = AnsiConsole.Prompt(new SelectionPrompt<string>()
            .Title("[bold]Install which skill?[/]")
            .PageSize(12)
            .AddChoices(byLabel.Keys));

        if (byLabel[picked] is not { } chosen)
        {
            AnsiConsole.MarkupLine("[grey]Cancelled.[/]");
            return 0;
        }

        // Use the upstream install convention: owner/repo@skill-name.
        var installSource = string.IsNullOrEmpty(chosen.Source)
            ? chosen.Slug
            : $"{chosen.Source}@{chosen.Name}";

        AnsiConsole.MarkupLine($"[cyan]Installing {Markup.Escape(chosen.Name)} from {Markup.Escape(chosen.Source)}...[/]");

        var detectedAgents = AgentRegistry.DetectInstalled();
        var targetAgents = detectedAgents.Count > 0 ? detectedAgents : [AgentRegistry.Get("universal")];

        var request = new InstallRequest(
            SourceInput: installSource,
            SkillFilter: [chosen.Name],
            Agents: targetAgents,
            Global: settings.Global,
            Mode: InstallMode.Copy,
            ResolveOptions: ResolveOptions.Default,
            IncludeInternal: false);

        var summary = await _installer.InstallAsync(request, _cancellation.Token);
        var failures = summary.Outcomes.Count(o => !o.Result.Success);
        _console.MarkupLine(failures == 0
            ? $"[green]Installed {Markup.Escape(chosen.Name)}.[/]"
            : $"[yellow]Installed {Markup.Escape(chosen.Name)} with {failures} failure(s).[/]");
        return failures == 0 ? 0 : 1;
    }

    private static void PrintResults(IReadOnlyList<SearchHit> results)
    {
        var table = new Table().Border(TableBorder.Rounded).LeftAligned();
        table.AddColumns("Source", "Skill", "Installs", "Slug");
        foreach (var hit in results.Take(10))
        {
            table.AddRow(
                Markup.Escape(hit.Source),
                Markup.Escape(hit.Name),
                Markup.Escape(SkillsApi.FormatInstalls(hit.Installs)),
                $"[grey]{Markup.Escape(hit.Slug)}[/]");
        }
        AnsiConsole.Write(table);
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..(max - 1)] + "…";
}

using System.ComponentModel;
using AgentSkills.Agents;
using AgentSkills.Install;
using AgentSkills.SkillModel;
using AgentSkills.Sources;
using AgentSkills.UI;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AgentSkills.Commands;

/// <summary>
/// Thin CLI wrapper. Parses settings, picks agents and (optionally) skills via
/// interactive prompts, then delegates the actual install to
/// <see cref="IInstallService"/>. All install logic lives in the service so
/// <see cref="FindCommand"/> and <see cref="UpdateCommand"/> can reuse it without
/// instantiating commands.
/// </summary>
public sealed class AddCommand : AsyncCommand<AddCommand.Settings>
{
    private readonly IInstallService _installer;
    private readonly ISourceResolver _resolver;
    private readonly IAnsiConsole _console;
    private readonly AgentSkills.Hosting.CommandCancellation _cancellation;

    public AddCommand(IInstallService installer, ISourceResolver resolver, IAnsiConsole console, AgentSkills.Hosting.CommandCancellation cancellation)
    {
        _installer = installer ?? throw new ArgumentNullException(nameof(installer));
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _console = console ?? throw new ArgumentNullException(nameof(console));
        _cancellation = cancellation ?? throw new ArgumentNullException(nameof(cancellation));
    }

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<source>")]
        [Description("Local path, GitHub shorthand (owner/repo), URL, or NuGet package id.")]
        public string Source { get; init; } = string.Empty;

        [CommandOption("-g|--global")]
        [Description("Install to the user-wide directory instead of the project.")]
        public bool Global { get; init; }

        [CommandOption("-a|--agent <AGENTS>")]
        [Description("Target specific agents (claude-code, codex, cursor, opencode, universal).")]
        public string[] Agents { get; init; } = [];

        [CommandOption("-s|--skill <SKILLS>")]
        [Description("Install specific skills by name. Use '*' for all.")]
        public string[] Skills { get; init; } = [];

        [CommandOption("-y|--yes")]
        [Description("Skip interactive prompts and accept defaults.")]
        public bool Yes { get; init; }

        [CommandOption("--copy")]
        [Description("Copy files into each agent directory instead of symlinking.")]
        public bool Copy { get; init; }

        [CommandOption("--symlink")]
        [Description("Force symlink mode (default).")]
        public bool Symlink { get; init; }

        [CommandOption("--nuget-source <URL>")]
        [Description("Override NuGet feed (otherwise the user's NuGet.config feeds are used).")]
        public string? NuGetSource { get; init; }

        [CommandOption("--npm-registry <URL>")]
        [Description("Override the default npm registry (otherwise ~/.npmrc / project .npmrc + per-scope rules apply).")]
        public string? NpmRegistry { get; init; }

        [CommandOption("--path <PATH>")]
        [Description("Restrict discovery to a subdirectory of the source. Overrides any subpath the source string carried (e.g. a GitHub /tree/.../path URL). Use when a package puts its skills somewhere non-conventional.")]
        public string? Path { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var resolveOptions = new ResolveOptions(NuGetSource: settings.NuGetSource, NpmRegistry: settings.NpmRegistry);

        // Stage the source once to render the spinner + pick skills interactively;
        // the InstallService re-resolves below so this stays a pure preview path
        // when the user might cancel from the prompts.
        IReadOnlyList<Skill> skillsToInstall;
        using (var preview = await StageWithSpinner(settings.Source, resolveOptions, _cancellation.Token))
        {
            var includeInternal = settings.Skills.Length > 0 && !settings.Skills.Contains("*");
            var previewSubpath = string.IsNullOrWhiteSpace(settings.Path)
                ? preview.Subpath
                : AgentSkills.SkillModel.PathSafety.SanitizeSubpath(settings.Path.Trim().Replace('\\', '/'));

            if (!string.IsNullOrEmpty(previewSubpath) &&
                !Directory.Exists(System.IO.Path.Combine(preview.RootPath, previewSubpath)))
            {
                _console.MarkupLine(
                    $"[red]Path '{Markup.Escape(previewSubpath)}' does not exist inside {Markup.Escape(preview.DisplaySource)}.[/]");
                return 1;
            }

            var discovered = SkillDiscovery.Discover(
                preview.RootPath,
                previewSubpath,
                new SkillDiscovery.DiscoverOptions(IncludeInternal: includeInternal));

            if (discovered.Count == 0)
            {
                var where = string.IsNullOrEmpty(previewSubpath)
                    ? preview.DisplaySource
                    : $"{preview.DisplaySource} (under '{previewSubpath}')";
                _console.MarkupLine($"[yellow]No SKILL.md files found in {Markup.Escape(where)}[/]");
                return 1;
            }

            var filtered = settings.Skills.Length == 0 || settings.Skills.Contains("*")
                ? discovered
                : discovered.Where(s => settings.Skills.Contains(s.Name, StringComparer.OrdinalIgnoreCase)).ToList();

            if (filtered.Count == 0)
            {
                _console.MarkupLine("[yellow]No skills matched the provided --skill names.[/]");
                return 1;
            }

            skillsToInstall = Prompts.SelectSkills(filtered, settings.Yes);
            if (skillsToInstall.Count == 0)
            {
                _console.MarkupLine("[yellow]Nothing selected, skipping.[/]");
                return 0;
            }
        }

        var agents = ResolveAgents(settings);
        if (agents.Count == 0)
        {
            _console.MarkupLine("[yellow]No agents selected, nothing to install.[/]");
            return 0;
        }

        var request = new InstallRequest(
            SourceInput: settings.Source,
            SkillFilter: skillsToInstall.Select(s => s.Name).ToList(),
            Agents: agents,
            Global: settings.Global,
            Mode: settings.Copy ? InstallMode.Copy : InstallMode.Symlink,
            ResolveOptions: resolveOptions,
            IncludeInternal: settings.Skills.Length > 0 && !settings.Skills.Contains("*"),
            SubpathOverride: settings.Path);

        var summary = await _installer.InstallAsync(request, _cancellation.Token);
        Render(summary);
        return summary.Outcomes.Any(o => !o.Result.Success) ? 1 : 0;
    }

    private async Task<ISkillSource> StageWithSpinner(string input, ResolveOptions options, CancellationToken ct) =>
        await _console.Status()
            .Spinner(Spinner.Known.Dots)
            .StartAsync($"Resolving {input}...", async _ =>
                await _resolver.ResolveAsync(input, options, ct).ConfigureAwait(false))
            .ConfigureAwait(false);

    private List<AgentConfig> ResolveAgents(Settings settings)
    {
        if (settings.Agents.Length > 0)
        {
            return settings.Agents.Select(AgentRegistry.Get).ToList();
        }

        var detected = AgentRegistry.DetectInstalled();
        if (detected.Count == 0)
        {
            return [AgentRegistry.Get("universal")];
        }
        return Prompts.SelectAgents(detected, settings.Yes);
    }

    private void Render(InstallSummary summary)
    {
        var table = new Table().LeftAligned().Border(TableBorder.Rounded);
        table.AddColumn("Skill");
        table.AddColumn("Agent");
        table.AddColumn("Result");
        table.AddColumn("Path");

        foreach (var outcome in summary.Outcomes)
        {
            var status = outcome.Result switch
            {
                { Success: true, Skipped: true } => "[grey]canonical only[/]",
                { Success: true, SymlinkFailed: true } => "[yellow]copy (symlink fell back)[/]",
                { Success: true, Mode: InstallMode.Symlink } => "[green]symlinked[/]",
                { Success: true } => "[green]copied[/]",
                _ => $"[red]failed: {Markup.Escape(outcome.Result.Error ?? "unknown")}[/]",
            };

            var displayPath = outcome.Result.Success
                ? (outcome.Result.CanonicalPath is not null && outcome.Result.Skipped ? outcome.Result.CanonicalPath : outcome.Result.Path)
                : string.Empty;

            table.AddRow(
                Markup.Escape(outcome.Skill.Name),
                Markup.Escape(outcome.Agent.DisplayName),
                status,
                $"[grey]{Markup.Escape(displayPath)}[/]");
        }
        _console.Write(table);

        if (summary.InstallRoots.Count > 0)
        {
            _console.MarkupLine(summary.InstallRoots.Count == 1
                ? $"[grey]Installed under[/] {Markup.Escape(summary.InstallRoots.First())}"
                : $"[grey]Installed under[/] {string.Join(", ", summary.InstallRoots.Select(Markup.Escape))}");
        }
        _console.MarkupLine("[green]Done.[/]");
    }

    /// <summary>Retained for the upstream-equivalent test that asserts the filter contract.</summary>
    internal static List<Skill> FilterByName(List<Skill> skills, string[] names)
    {
        if (names.Length == 0 || names.Contains("*", StringComparer.Ordinal))
        {
            return skills;
        }
        var wanted = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
        return skills.Where(s => wanted.Contains(s.Name)).ToList();
    }
}

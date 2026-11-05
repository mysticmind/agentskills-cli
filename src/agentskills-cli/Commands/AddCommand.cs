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

        [CommandOption("--as <NAME>")]
        [Description("Install a skill under a different name, to avoid a name conflict: --as <skill>=<new-name>, " +
                     "or just --as <new-name> when installing one skill. Repeatable.")]
        public string[] As { get; init; } = [];

        [CommandOption("--replace")]
        [Description("Replace existing skills from other sources that have the same name, without asking.")]
        public bool Replace { get; init; }

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
        Dictionary<string, string> aliases;
        var replace = settings.Replace;
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

            skillsToInstall = Prompts.SelectSkills(_console, filtered, settings.Yes);
            if (skillsToInstall.Count == 0)
            {
                _console.MarkupLine("[yellow]Nothing selected, skipping.[/]");
                return 0;
            }

            var resolution = ResolveNameConflicts(settings, InstallService.TrackedSource(preview), skillsToInstall);
            if (resolution is null) return 2;
            (skillsToInstall, aliases, replace) = resolution.Value;
            if (skillsToInstall.Count == 0)
            {
                _console.MarkupLine("[yellow]Nothing left to install.[/]");
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
            SubpathOverride: settings.Path,
            Aliases: aliases,
            Replace: replace);

        var summary = await _installer.InstallAsync(request, _cancellation.Token);
        Render(summary);
        return summary.Outcomes.Any(o => !o.Result.Success) ? 1 : 0;
    }

    /// <summary>
    /// Applies <c>--as</c>, reuses aliases from an earlier install of the same source, and
    /// resolves name conflicts with skills from other sources: prompts when interactive,
    /// otherwise prints the conflicts with the commands that resolve them and returns null.
    /// </summary>
    private (IReadOnlyList<Skill> Skills, Dictionary<string, string> Aliases, bool Replace)? ResolveNameConflicts(
        Settings settings, string incomingSource, IReadOnlyList<Skill> skills)
    {
        var names = skills.Select(s => s.Name).ToList();
        var aliases = SkillConflicts.ParseAliases(settings.As, names);

        foreach (var (original, alias) in SkillConflicts.RememberedAliases(incomingSource, names, settings.Global))
        {
            if (aliases.TryAdd(original, alias))
            {
                _console.MarkupLine($"[grey]Installing {Markup.Escape(original)} as {Markup.Escape(alias)} (the name it was given last time).[/]");
            }
        }

        if (settings.Replace) return (skills, aliases, true);

        var conflicts = SkillConflicts.Find(
            incomingSource, names.Select(n => (n, aliases.GetValueOrDefault(n, n))), settings.Global);
        if (conflicts.Count == 0) return (skills, aliases, false);

        var interactive = _console.Profile.Capabilities.Interactive && !settings.Yes;
        if (!interactive)
        {
            ReportConflicts(settings, incomingSource, conflicts);
            return null;
        }

        var kept = skills.ToList();
        var replace = false;
        var scopeNames = SkillConflicts.NamesInScope(settings.Global);

        foreach (var conflict in conflicts)
        {
            bool Taken(string candidate) =>
                scopeNames.Contains(candidate) ||
                aliases.Where(kv => !string.Equals(kv.Key, conflict.SkillName, StringComparison.OrdinalIgnoreCase))
                       .Any(kv => string.Equals(kv.Value, candidate, StringComparison.Ordinal)) ||
                names.Any(n => !string.Equals(n, conflict.SkillName, StringComparison.OrdinalIgnoreCase) &&
                               !aliases.ContainsKey(n) &&
                               string.Equals(SkillNameSanitizer.Sanitize(n), candidate, StringComparison.Ordinal));

            var suggestion = SkillConflicts.SuggestAlias(conflict.SkillName, incomingSource, Taken);
            var decision = Prompts.ResolveConflict(_console, conflict, incomingSource, suggestion, candidate =>
                !SkillConflicts.IsValidAlias(candidate)
                    ? $"Use lowercase letters, digits and hyphens (for example '{SkillNameSanitizer.Sanitize(candidate)}')."
                    : Taken(candidate) ? $"'{candidate}' is already used in the {conflict.Scope} scope." : null);

            switch (decision.Choice)
            {
                case Prompts.ConflictChoice.Rename:
                    aliases[conflict.SkillName] = decision.Alias!;
                    _console.MarkupLine($"[grey]{Markup.Escape(conflict.SkillName)} will be installed as {Markup.Escape(decision.Alias!)}.[/]");
                    break;
                case Prompts.ConflictChoice.Replace:
                    replace = true;
                    break;
                default:
                    kept.RemoveAll(s => string.Equals(s.Name, conflict.SkillName, StringComparison.OrdinalIgnoreCase));
                    aliases.Remove(conflict.SkillName);
                    _console.MarkupLine($"[grey]Skipping {Markup.Escape(conflict.SkillName)}.[/]");
                    break;
            }
        }
        return (kept, aliases, replace);
    }

    private void ReportConflicts(Settings settings, string incomingSource, IReadOnlyList<SkillConflict> conflicts)
    {
        _console.MarkupLine("[red]Skill name conflict - nothing was installed.[/]");
        foreach (var c in conflicts)
        {
            _console.MarkupLine(
                $"  [yellow]{Markup.Escape(c.SkillName)}[/] from {Markup.Escape(incomingSource)} conflicts with " +
                $"{Markup.Escape(c.DescribeExisting())} in the {c.Scope} scope.");
        }

        var taken = SkillConflicts.NamesInScope(settings.Global);
        var asArgs = string.Join(" ", conflicts.Select(c =>
            $"--as {c.SkillName}={SkillConflicts.SuggestAlias(c.SkillName, incomingSource, taken.Contains)}"));
        var source = settings.Source.Contains(' ', StringComparison.Ordinal) ? $"\"{settings.Source}\"" : settings.Source;

        _console.MarkupLine("[grey]Keep both by installing under another name (add your other options as before):[/]");
        _console.MarkupLine($"  agentskills-cli add {Markup.Escape(source)} {Markup.Escape(asArgs)}");
        _console.MarkupLine("[grey]Or replace the existing skill(s):[/]");
        _console.MarkupLine($"  agentskills-cli add {Markup.Escape(source)} --replace");
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
            return AgentRegistry.GetMany(settings.Agents);
        }

        var detected = AgentRegistry.DetectInstalled();
        if (detected.Count == 0)
        {
            return [AgentRegistry.Get("universal")];
        }
        return Prompts.SelectAgents(_console, detected, settings.Yes);
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
                outcome.InstallName is null
                    ? Markup.Escape(outcome.Skill.Name)
                    : $"{Markup.Escape(outcome.InstallName)} [grey](as alias of {Markup.Escape(outcome.Skill.Name)})[/]",
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

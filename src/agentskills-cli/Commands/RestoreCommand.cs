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
/// <c>restore</c>: install every skill recorded in <c>./skills-lock.json</c> at the locked
/// version / ref, so a fresh checkout gets the same project skills (the way
/// <c>dotnet restore</c> or <c>npm ci</c> rebuild dependencies from a lock).
/// </summary>
public sealed class RestoreCommand : AsyncCommand<RestoreCommand.Settings>
{
    private readonly ISourceResolver _resolver;
    private readonly IInstallService _installer;
    private readonly IAnsiConsole _console;
    private readonly Hosting.CommandCancellation _cancellation;

    public RestoreCommand(
        ISourceResolver resolver,
        IInstallService installer,
        IAnsiConsole console,
        Hosting.CommandCancellation cancellation)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _installer = installer ?? throw new ArgumentNullException(nameof(installer));
        _console = console ?? throw new ArgumentNullException(nameof(console));
        _cancellation = cancellation ?? throw new ArgumentNullException(nameof(cancellation));
    }

    public sealed class Settings : CommandSettings
    {
        [CommandOption("-a|--agent <AGENTS>")]
        [Description("Install for these agents. Default: detected agents (prompted when interactive).")]
        public string[] Agents { get; init; } = [];

        [CommandOption("-y|--yes")]
        [Description("Don't prompt; use every detected agent.")]
        public bool Yes { get; init; }

        [CommandOption("--copy")]
        [Description("Copy files into each agent directory instead of symlinking.")]
        public bool Copy { get; init; }

        [CommandOption("--force")]
        [Description("Reinstall skills that are already present.")]
        public bool Force { get; init; }

        [CommandOption("--frozen")]
        [Description("Fail instead of installing when a source's content no longer matches the locked hash (CI).")]
        public bool Frozen { get; init; }

        [CommandOption("--nuget-source <URL>")]
        [Description("Restore NuGet packages from this feed only.")]
        public string? NuGetSource { get; init; }

        [CommandOption("--npm-registry <URL>")]
        [Description("Restore npm packages from this registry.")]
        public string? NpmRegistry { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var lockPath = LocalLock.GetLockPath();
        if (!File.Exists(lockPath))
        {
            _console.MarkupLine($"[yellow]No {LocalLock.FileName} in this directory - nothing to restore.[/]");
            return 0;
        }

        var lockDoc = LocalLock.Load();
        if (lockDoc.Skills.Count == 0)
        {
            _console.MarkupLine($"[grey]{LocalLock.FileName} tracks no skills - nothing to restore.[/]");
            return 0;
        }

        var present = Installer.List(global: false)
            .Select(s => s.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var todo = lockDoc.Skills
            .Where(kv => settings.Force || !present.Contains(kv.Key))
            .ToList();

        var alreadyPresent = lockDoc.Skills.Count - todo.Count;
        if (todo.Count == 0)
        {
            _console.MarkupLine($"[green]All {lockDoc.Skills.Count} skill(s) in {LocalLock.FileName} are already installed.[/] [grey](--force to reinstall)[/]");
            return 0;
        }

        var agents = ResolveAgents(settings);
        if (agents.Count == 0)
        {
            _console.MarkupLine("[yellow]No agents selected, nothing to restore.[/]");
            return 0;
        }

        var resolveOptions = new ResolveOptions(NuGetSource: settings.NuGetSource, NpmRegistry: settings.NpmRegistry);
        var mode = settings.Copy ? InstallMode.Copy : InstallMode.Symlink;

        var restored = 0;
        var failures = new List<(string Skill, string Reason)>();
        var drifted = new List<string>();

        var groups = todo.GroupBy(
            kv => UpdateService.BuildSpec(kv.Value.SourceType, kv.Value.Source, sourceUrl: null, kv.Value.Ref) ?? string.Empty,
            StringComparer.Ordinal);

        foreach (var group in groups)
        {
            if (group.Key.Length == 0)
            {
                failures.AddRange(group.Select(kv => (kv.Key, $"Unknown source type '{kv.Value.SourceType}'")));
                continue;
            }

            ISkillSource source;
            try
            {
                source = await _console.Status()
                    .Spinner(Spinner.Known.Dots)
                    .StartAsync($"Fetching {Markup.Escape(group.Key)}...",
                        _ => _resolver.ResolveAsync(group.Key, resolveOptions, _cancellation.Token))
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Source factories surface network/feed/git failures as several exception
                // types; record them per skill and keep restoring the other sources.
                failures.AddRange(group.Select(kv => (kv.Key, ex.Message)));
                continue;
            }

            using (source)
            {
                var toInstall = new List<Skill>();
                var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var (name, entry) in group)
                {
                    var skill = UpdateService.LocateSkill(source, entry.OriginalName ?? name, entry.SkillPath);
                    if (skill is null)
                    {
                        failures.Add((name, $"Not found in {source.DisplaySource}"));
                        continue;
                    }

                    if (!string.IsNullOrEmpty(entry.ComputedHash) &&
                        !string.Equals(LocalLock.ComputeFolderHash(skill.Path), entry.ComputedHash, StringComparison.OrdinalIgnoreCase))
                    {
                        drifted.Add(name);
                        if (settings.Frozen)
                        {
                            failures.Add((name, "Content differs from the locked hash (--frozen)"));
                            continue;
                        }
                    }
                    toInstall.Add(skill);
                    if (entry.OriginalName is not null) aliases[skill.Name] = name;
                }

                if (toInstall.Count == 0) continue;

                var request = new InstallRequest(
                    SourceInput: group.Key,
                    SkillFilter: [],
                    Agents: agents,
                    Global: false,
                    Mode: mode,
                    ResolveOptions: resolveOptions,
                    IncludeInternal: true,
                    Aliases: aliases,
                    // The lock is authoritative here; present skills were already skipped unless --force.
                    Replace: true);

                try
                {
                    var summary = await _installer.InstallSkillsAsync(source, toInstall, request, _cancellation.Token)
                        .ConfigureAwait(false);
                    foreach (var skill in toInstall)
                    {
                        var errors = summary.Outcomes
                            .Where(o => ReferenceEquals(o.Skill, skill) && !o.Result.Success)
                            .Select(o => $"{o.Agent.Name}: {o.Result.Error}")
                            .ToList();
                        if (errors.Count == 0)
                        {
                            restored++;
                            var shown = aliases.TryGetValue(skill.Name, out var alias) ? $"{alias} [grey](as alias of {Markup.Escape(skill.Name)})[/]" : Markup.Escape(skill.Name);
                            _console.MarkupLine($"[green]restored[/] {shown} [grey]({Markup.Escape(group.Key)})[/]");
                        }
                        else
                        {
                            failures.Add((aliases.GetValueOrDefault(skill.Name, skill.Name), string.Join("; ", errors)));
                        }
                    }
                }
                catch (SkillsException ex)
                {
                    failures.AddRange(toInstall.Select(s => (s.Name, ex.Message)));
                }
            }
        }

        if (drifted.Count > 0)
        {
            _console.MarkupLine(settings.Frozen
                ? $"[red]{drifted.Count} skill(s) no longer match the locked content:[/] {Markup.Escape(string.Join(", ", drifted))}"
                : $"[yellow]{drifted.Count} skill(s) changed upstream since the lock was written and were installed as-is (lock updated):[/] {Markup.Escape(string.Join(", ", drifted))}");
        }

        foreach (var (skill, reason) in failures)
        {
            _console.MarkupLine($"[red]failed[/] {Markup.Escape(skill)}: {Markup.Escape(reason)}");
        }

        var summaryLine = $"Restored {restored} skill(s)";
        if (alreadyPresent > 0) summaryLine += $", {alreadyPresent} already present";
        if (failures.Count > 0) summaryLine += $", {failures.Count} failed";
        _console.MarkupLine(failures.Count > 0 ? $"[red]{summaryLine}.[/]" : $"[green]{summaryLine}.[/]");

        return failures.Count > 0 ? 1 : 0;
    }

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
}

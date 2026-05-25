using System.ComponentModel;
using AgentSkills.Agents;
using AgentSkills.Install;
using AgentSkills.SkillModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AgentSkills.Commands;

public sealed class ListCommand : Command<ListCommand.Settings>
{
    private readonly IAnsiConsole _console;

    public ListCommand(IAnsiConsole console)
    {
        _console = console ?? throw new ArgumentNullException(nameof(console));
    }

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "[TARGETS]")]
        [Description("Only list skills matching these targets. Each argument is matched as a " +
                     "skill name first, then as a source (any shape `add` accepts). Omit to list everything.")]
        public string[] Targets { get; init; } = [];

        [CommandOption("-g|--global")]
        [Description("List globally installed skills.")]
        public bool Global { get; init; }

        [CommandOption("-a|--agent <AGENTS>")]
        [Description("Limit to specific agents.")]
        public string[] Agents { get; init; } = [];

        [CommandOption("--by <KEY>")]
        [Description("Group the output. Valid values: package, path, agent, scope. Default is a flat table.")]
        public string? GroupBy { get; init; }

        [CommandOption("--paths")]
        [Description("Include a column with each skill's on-disk install path.")]
        public bool ShowPaths { get; init; }
    }

    private enum Grouping { None, Package, Path, Agent, Scope }

    public override int Execute(CommandContext context, Settings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var filter = settings.Agents.Length > 0
            ? settings.Agents.Select(AgentRegistry.Get).ToList()
            : null;

        var installed = Installer.List(
            global: settings.Global ? true : (bool?)null,
            agentFilter: filter);

        if (installed.Count == 0)
        {
            _console.MarkupLine("[grey]No skills installed.[/]");
            return 0;
        }

        if (settings.Targets.Length > 0)
        {
            installed = PackageMatcher.ResolveTargets(settings.Targets, installed, s => s.Name);
            if (installed.Count == 0)
            {
                _console.MarkupLine(
                    $"[grey]No installed skills matched {Markup.Escape(string.Join(", ", settings.Targets))}.[/]");
                MaybePrintVersionMismatchHint(_console, settings.Targets);
                return 0;
            }
        }

        var rows = installed.Select(BuildRow).ToList();

        var grouping = ParseGrouping(settings.GroupBy);
        if (grouping is null)
        {
            _console.MarkupLine(
                $"[red]Invalid --by value '{Markup.Escape(settings.GroupBy!)}'. Valid: package, path, agent, scope.[/]");
            return 2;
        }

        if (grouping == Grouping.None)
        {
            RenderFlat(rows, settings.ShowPaths);
        }
        else
        {
            RenderGrouped(rows, grouping.Value, settings.ShowPaths);
        }

        return 0;
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private sealed record Row(
        InstalledSkill Skill,
        string? PackageBaseId,        // null when the skill isn't tracked in any lock
        string? PackageDisplay,       // "@scope/name@1.0.0 (npm)" - for grouping headers
        string PathDisplay);

    private static Row BuildRow(InstalledSkill skill)
    {
        var source = PackageMatcher.LookupSource(skill.Name);
        return new Row(
            Skill: skill,
            PackageBaseId: source?.BaseId,
            PackageDisplay: source is null
                ? null
                : $"{source.Source} [grey]({source.SourceType})[/]",
            PathDisplay: skill.Path);
    }

    private static Grouping? ParseGrouping(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return Grouping.None;
        return raw.ToLowerInvariant() switch
        {
            "package" or "pkg" or "source" => Grouping.Package,
            "path" or "dir" => Grouping.Path,
            "agent" or "agents" => Grouping.Agent,
            "scope" => Grouping.Scope,
            _ => null,
        };
    }

    private void RenderFlat(IReadOnlyList<Row> rows, bool showPaths)
    {
        var table = new Table().Border(TableBorder.Rounded).LeftAligned();
        if (showPaths)
        {
            table.AddColumns("Skill", "Scope", "Agents", "Source", "Path", "Description");
        }
        else
        {
            table.AddColumns("Skill", "Scope", "Agents", "Source", "Description");
        }

        foreach (var row in rows.OrderBy(r => r.Skill.Scope).ThenBy(r => r.Skill.Name, StringComparer.Ordinal))
        {
            var values = new List<string>
            {
                Markup.Escape(row.Skill.Name),
                row.Skill.Scope == "global" ? "[blue]global[/]" : "[green]project[/]",
                Markup.Escape(string.Join(", ", row.Skill.Agents)),
                row.PackageDisplay ?? "[grey]-[/]",
            };
            if (showPaths) values.Add($"[grey]{Markup.Escape(row.PathDisplay)}[/]");
            values.Add(Markup.Escape(Truncate(row.Skill.Description, 80)));

            table.AddRow(values.ToArray());
        }

        _console.Write(table);
    }

    private void RenderGrouped(IReadOnlyList<Row> rows, Grouping grouping, bool showPaths)
    {
        IEnumerable<IGrouping<string, Row>> groups = grouping switch
        {
            Grouping.Package => rows.GroupBy(r => r.PackageDisplay ?? "[grey](untracked)[/]", StringComparer.Ordinal),
            Grouping.Path => rows.GroupBy(r => GroupingPath(r.Skill.Path), StringComparer.Ordinal),
            Grouping.Agent => rows.SelectMany(r => r.Skill.Agents.DefaultIfEmpty("(no agent)")
                                                        .Select(a => (Agent: a, Row: r)))
                                  .GroupBy(t => t.Agent, t => t.Row, StringComparer.Ordinal),
            Grouping.Scope => rows.GroupBy(r => r.Skill.Scope, StringComparer.Ordinal),
            _ => rows.GroupBy(_ => string.Empty),
        };

        foreach (var group in groups.OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            _console.WriteLine();
            _console.MarkupLine($"[bold]{group.Key}[/] [grey]({group.Count()} skill(s))[/]");

            var table = new Table().Border(TableBorder.Minimal).LeftAligned();
            if (showPaths)
            {
                table.AddColumns("Skill", "Scope", "Agents", "Path", "Description");
            }
            else
            {
                table.AddColumns("Skill", "Scope", "Agents", "Description");
            }

            foreach (var row in group.OrderBy(r => r.Skill.Name, StringComparer.Ordinal))
            {
                var values = new List<string>
                {
                    Markup.Escape(row.Skill.Name),
                    row.Skill.Scope == "global" ? "[blue]global[/]" : "[green]project[/]",
                    Markup.Escape(string.Join(", ", row.Skill.Agents)),
                };
                if (showPaths) values.Add($"[grey]{Markup.Escape(row.PathDisplay)}[/]");
                values.Add(Markup.Escape(Truncate(row.Skill.Description, 80)));

                table.AddRow(values.ToArray());
            }
            _console.Write(table);
        }
    }

    /// <summary>Returns the parent directory of an install path, used as a grouping key.</summary>
    private static string GroupingPath(string installPath) =>
        Path.GetDirectoryName(installPath.TrimEnd(Path.DirectorySeparatorChar)) ?? installPath;

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..(max - 1)] + "…";

    /// <summary>
    /// When the user pins a version that doesn't match anything, but a different version
    /// of the same package is installed, print a "did you mean…" hint instead of leaving
    /// them guessing.
    /// </summary>
    internal static void MaybePrintVersionMismatchHint(IAnsiConsole console, IReadOnlyList<string> targets)
    {
        ArgumentNullException.ThrowIfNull(console);
        var pinned = targets
            .Select(PackageMatcher.NormalizeToLocator)
            .Where(l => l.Version is not null)
            .ToList();
        if (pinned.Count == 0) return;

        var installedVersions = PackageMatcher.InstalledVersionsByBaseId(targets);
        foreach (var locator in pinned)
        {
            if (installedVersions.TryGetValue(locator.BaseId, out var versions) && versions.Count > 0)
            {
                console.MarkupLine(
                    $"[grey]hint: {Markup.Escape(locator.BaseId)} is installed at version(s) " +
                    $"{Markup.Escape(string.Join(", ", versions.OrderBy(v => v, StringComparer.Ordinal)))}; " +
                    $"drop the @version to list anyway.[/]");
            }
        }
    }
}

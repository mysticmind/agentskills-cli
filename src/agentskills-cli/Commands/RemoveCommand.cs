using System.ComponentModel;
using AgentSkills.Agents;
using AgentSkills.Install;
using AgentSkills.SkillModel;
using AgentSkills.UI;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AgentSkills.Commands;

public sealed class RemoveCommand : Command<RemoveCommand.Settings>
{
    private readonly IAnsiConsole _console;

    public RemoveCommand(IAnsiConsole console)
    {
        _console = console ?? throw new ArgumentNullException(nameof(console));
    }

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "[TARGETS]")]
        [Description("Skills to remove. Each argument is matched as a skill name first, " +
                     "then as a source (any shape `add` accepts: GitHub owner/repo, GitHub/GitLab URL, " +
                     "npm package id, NuGet package id, local path). Omit to be prompted.")]
        public string[] Targets { get; init; } = [];

        [CommandOption("-g|--global")]
        [Description("Only remove globally installed skills.")]
        public bool Global { get; init; }

        [CommandOption("-p|--project")]
        [Description("Only remove project-installed skills.")]
        public bool Project { get; init; }

        [CommandOption("-a|--agent <AGENTS>")]
        [Description("Limit removal to specific agents.")]
        public string[] Agents { get; init; } = [];

        [CommandOption("-y|--yes")]
        [Description("Skip the confirmation prompt.")]
        public bool Yes { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var agentFilter = settings.Agents.Length > 0
            ? AgentRegistry.GetMany(settings.Agents)
            : null;

        bool? scope = settings.Global == settings.Project ? null : settings.Global;
        var installed = Installer.List(global: scope, agentFilter: agentFilter);
        if (agentFilter is not null)
        {
            installed = installed.Where(s => s.Agents.Any(a => agentFilter.Any(f => f.Name == a))).ToList();
        }
        if (installed.Count == 0)
        {
            _console.MarkupLine("[grey]Nothing installed to remove.[/]");
            return 0;
        }

        List<InstalledSkill> toRemove;
        if (settings.Targets.Length > 0)
        {
            toRemove = PackageMatcher.ResolveTargets(settings.Targets, installed, s => s.Name, s => s.Scope);

            if (toRemove.Count == 0)
            {
                _console.MarkupLine(
                    $"[yellow]No installed skills matched {Markup.Escape(string.Join(", ", settings.Targets))}.[/]");
                ListCommand.MaybePrintVersionMismatchHint(_console, settings.Targets);
                return 1;
            }

            if (toRemove.Count > 1 || !settings.Targets.Any(t => string.Equals(t, toRemove[0].Name, StringComparison.OrdinalIgnoreCase)))
            {
                _console.MarkupLine(
                    $"[grey]Matched {toRemove.Count} skill(s):[/] " +
                    string.Join(", ", toRemove.Select(s => $"{Markup.Escape(s.Name)} [grey]({s.Scope})[/]")));
            }
        }
        else if (!_console.Profile.Capabilities.Interactive)
        {
            _console.MarkupLine("[red]Refusing to remove all skills without explicit targets. Pass them as arguments.[/]");
            return 2;
        }
        else
        {
            var byLabel = installed.ToDictionary(
                s => $"{Markup.Escape(s.Name)} [grey]({Markup.Escape(s.Scope)}, {Markup.Escape(string.Join(",", s.Agents))})[/]",
                s => s);
            var picked = _console.Prompt(new MultiSelectionPrompt<string>()
                .Title("[bold]Select skills to remove[/]")
                .NotRequired()
                .InstructionsText("[grey](Press <space> to toggle, <enter> to confirm, Ctrl+C to cancel)[/]")
                .AddChoices(byLabel.Keys));
            toRemove = picked.Select(p => byLabel[p]).ToList();
            if (toRemove.Count == 0) return 0;
        }

        if (!settings.Yes && !Prompts.Confirm(_console,
                $"Remove {toRemove.Count} skill(s)?", defaultValue: false))
        {
            _console.MarkupLine("[grey]Aborted.[/]");
            return 0;
        }

        var globalLock = SkillLock.Load();
        var localLock = LocalLock.Load();
        var globalLockChanged = false;
        var localLockChanged = false;

        foreach (var skill in toRemove)
        {
            var isGlobal = skill.Scope == "global";
            var agentsForSkill = (agentFilter ?? skill.Agents.Select(a => AgentRegistry.All.GetValueOrDefault(a)).OfType<AgentConfig>())
                .ToList();

            // Agents can share one folder (every universal agent uses .agents/skills), so
            // remove each folder once and credit every agent that reads from it.
            var removedFrom = new List<string>();
            var byFolder = agentsForSkill
                .Where(a => !isGlobal || a.GlobalSkillsDir is not null || a.IsUniversal)
                .GroupBy(a => Path.GetFullPath(Path.Combine(
                    AgentRegistry.GetAgentBaseDir(a, isGlobal), SkillNameSanitizer.Sanitize(skill.Name))),
                    StringComparer.Ordinal);
            foreach (var folder in byFolder)
            {
                if (Installer.RemoveForAgent(skill.Name, folder.First(), global: isGlobal))
                {
                    removedFrom.AddRange(folder.Select(a => a.DisplayName));
                }
            }

            // With --agent, other agents may still use the skill (often through a symlink to
            // the canonical copy). Only drop the canonical copy and the lock entry when no
            // agent is left holding it.
            var remaining = Installer.List(global: isGlobal)
                .FirstOrDefault(s => string.Equals(s.Name, skill.Name, StringComparison.Ordinal) && s.Scope == skill.Scope);
            var stillUsed = agentFilter is not null && remaining is not null &&
                            remaining.Agents.Any(a => !agentFilter.Any(f => f.Name == a));

            if (!stillUsed)
            {
                var canonicalBase = AgentRegistry.GetCanonicalDir(global: isGlobal);
                var canonical = Path.Combine(canonicalBase, SkillNameSanitizer.Sanitize(skill.Name));
                if (Directory.Exists(canonical))
                {
                    try { Directory.Delete(canonical, recursive: true); } catch (IOException) { /* ignore */ }
                }

                if (isGlobal) globalLockChanged |= globalLock.Skills.Remove(skill.Name);
                else localLockChanged |= localLock.Skills.Remove(skill.Name);
            }

            var suffix = stillUsed ? " [grey](still installed for other agents)[/]" : string.Empty;
            _console.MarkupLine(removedFrom.Count > 0 || !stillUsed
                ? $"[green]removed[/] {Markup.Escape(skill.Name)} [grey]({skill.Scope}{(removedFrom.Count > 0 ? ": " + string.Join(", ", removedFrom.Select(Markup.Escape)) : string.Empty)})[/]{suffix}"
                : $"[grey]skipped[/] {Markup.Escape(skill.Name)} [grey](not present for any selected agent)[/]");
        }

        if (globalLockChanged) SkillLock.Save(globalLock);
        if (localLockChanged) LocalLock.Save(localLock);
        return 0;
    }
}

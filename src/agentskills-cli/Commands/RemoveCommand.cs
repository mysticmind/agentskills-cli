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
        [Description("Remove globally installed skills.")]
        public bool Global { get; init; }

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
            ? settings.Agents.Select(AgentRegistry.Get).ToList()
            : null;

        var installed = Installer.List(global: settings.Global ? true : (bool?)null, agentFilter: agentFilter);
        if (installed.Count == 0)
        {
            _console.MarkupLine("[grey]Nothing installed to remove.[/]");
            return 0;
        }

        List<InstalledSkill> toRemove;
        if (settings.Targets.Length > 0)
        {
            toRemove = PackageMatcher.ResolveTargets(settings.Targets, installed, s => s.Name);

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
                    string.Join(", ", toRemove.Select(s => Markup.Escape(s.Name))));
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

        var lockDoc = SkillLock.Load();
        foreach (var skill in toRemove)
        {
            var agentsForSkill = agentFilter
                ?? skill.Agents.Select(a => AgentRegistry.Get(a)).ToList();

            var removedFrom = new List<string>();
            foreach (var agent in agentsForSkill)
            {
                if (Installer.RemoveForAgent(skill.Name, agent, global: skill.Scope == "global"))
                {
                    removedFrom.Add(agent.DisplayName);
                }
            }

            _console.MarkupLine(removedFrom.Count > 0
                ? $"[green]removed[/] {Markup.Escape(skill.Name)} [grey]({string.Join(", ", removedFrom.Select(Markup.Escape))})[/]"
                : $"[grey]skipped[/] {Markup.Escape(skill.Name)} [grey](not present for any selected agent)[/]");

            // Also try canonical
            var canonicalBase = AgentRegistry.GetCanonicalDir(global: skill.Scope == "global");
            var canonical = Path.Combine(canonicalBase, SkillNameSanitizer.Sanitize(skill.Name));
            if (Directory.Exists(canonical))
            {
                try { Directory.Delete(canonical, recursive: true); } catch { /* ignore */ }
            }

            lockDoc.Skills.Remove(skill.Name);
            if (skill.Scope == "project")
            {
                LocalLock.Remove(skill.Name);
            }
        }

        SkillLock.Save(lockDoc);
        return 0;
    }
}

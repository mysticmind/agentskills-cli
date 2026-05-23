using System.ComponentModel;
using AgentSkills.Agents;
using AgentSkills.Install;
using AgentSkills.SkillModel;
using AgentSkills.Ui;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AgentSkills.Commands;

public sealed class RemoveCommand : Command<RemoveCommand.Settings>
{
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
        var agentFilter = settings.Agents.Length > 0
            ? settings.Agents.Select(AgentRegistry.Get).ToList()
            : null;

        var installed = Installer.List(global: settings.Global ? true : (bool?)null, agentFilter: agentFilter);
        if (installed.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]Nothing installed to remove.[/]");
            return 0;
        }

        List<InstalledSkill> toRemove;
        if (settings.Targets.Length > 0)
        {
            toRemove = PackageMatcher.ResolveTargets(settings.Targets, installed, s => s.Name);

            if (toRemove.Count == 0)
            {
                AnsiConsole.MarkupLine(
                    $"[yellow]No installed skills matched {Markup.Escape(string.Join(", ", settings.Targets))}.[/]");
                ListCommand.MaybePrintVersionMismatchHint(settings.Targets);
                return 1;
            }

            if (toRemove.Count > 1 || !settings.Targets.Any(t => string.Equals(t, toRemove[0].Name, StringComparison.OrdinalIgnoreCase)))
            {
                AnsiConsole.MarkupLine(
                    $"[grey]Matched {toRemove.Count} skill(s):[/] " +
                    string.Join(", ", toRemove.Select(s => Markup.Escape(s.Name))));
            }
        }
        else if (!AnsiConsole.Profile.Capabilities.Interactive)
        {
            AnsiConsole.MarkupLine("[red]Refusing to remove all skills without explicit targets. Pass them as arguments.[/]");
            return 2;
        }
        else
        {
            var byLabel = installed.ToDictionary(
                s => $"{Markup.Escape(s.Name)} [grey]({Markup.Escape(s.Scope)}, {Markup.Escape(string.Join(",", s.Agents))})[/]",
                s => s);
            var picked = AnsiConsole.Prompt(new MultiSelectionPrompt<string>()
                .Title("[bold]Select skills to remove[/]")
                .NotRequired()
                .AddChoices(byLabel.Keys));
            toRemove = picked.Select(p => byLabel[p]).ToList();
            if (toRemove.Count == 0) return 0;
        }

        if (!settings.Yes && !Prompts.Confirm(
                $"Remove {toRemove.Count} skill(s)?", defaultValue: false))
        {
            AnsiConsole.MarkupLine("[grey]Aborted.[/]");
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

            AnsiConsole.MarkupLine(removedFrom.Count > 0
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

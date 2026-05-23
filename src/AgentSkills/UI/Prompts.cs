using AgentSkills.Agents;
using AgentSkills.SkillModel;
using Spectre.Console;

namespace AgentSkills.UI;

public static class Prompts
{
    public static List<Skill> SelectSkills(IReadOnlyList<Skill> skills, bool yes)
    {
        if (skills.Count == 0)
        {
            return [];
        }
        if (skills.Count == 1 || yes || !AnsiConsole.Profile.Capabilities.Interactive)
        {
            return skills.ToList();
        }

        var byLabel = skills.ToDictionary(FormatSkillLabel, s => s);
        var prompt = new MultiSelectionPrompt<string>()
            .Title("[bold]Select skills to install[/]")
            .NotRequired()
            .PageSize(15)
            .InstructionsText("[grey](Press <space> to toggle, <enter> to confirm)[/]")
            .AddChoices(byLabel.Keys);

        var selected = AnsiConsole.Prompt(prompt);
        return selected.Count == 0 ? skills.ToList() : selected.Select(s => byLabel[s]).ToList();
    }

    /// <summary>
    /// Builds a Spectre-safe label for one skill. The skill's name and description come
    /// from third-party SKILL.md files and routinely contain <c>[brackets]</c> (e.g.
    /// references to <c>[Entity]</c> attributes), so both must be escaped before being
    /// interpolated into a Spectre markup string - otherwise Spectre parses them as colors.
    /// </summary>
    public static string FormatSkillLabel(Skill skill) =>
        $"{Markup.Escape(skill.Name)}  [grey]{Markup.Escape(Truncate(skill.Description, 60))}[/]";

    public static List<AgentConfig> SelectAgents(IReadOnlyList<AgentConfig> detected, bool yes)
    {
        var universal = detected.Where(a => a.IsUniversal).ToList();
        var others = detected.Where(a => !a.IsUniversal).ToList();

        if (yes || !AnsiConsole.Profile.Capabilities.Interactive)
        {
            return detected.ToList();
        }
        if (others.Count == 0)
        {
            return universal.Count > 0 ? universal : detected.ToList();
        }

        var byLabel = others.ToDictionary(
            a => $"{Markup.Escape(a.DisplayName)}  [grey]({Markup.Escape(a.Name)})[/]",
            a => a);
        var prompt = new MultiSelectionPrompt<string>()
            .Title("[bold]Install for which agents?[/]")
            .PageSize(15)
            .InstructionsText("[grey](Press <space> to toggle, <enter> to confirm. Universal agents are always included.)[/]")
            .AddChoices(byLabel.Keys);

        var selected = AnsiConsole.Prompt(prompt);
        var chosen = selected.Select(s => byLabel[s]).ToList();
        chosen.AddRange(universal);
        return chosen.Count == 0 ? detected.ToList() : chosen;
    }

    public static bool Confirm(string question, bool defaultValue = false)
    {
        if (!AnsiConsole.Profile.Capabilities.Interactive)
        {
            return defaultValue;
        }
        return AnsiConsole.Confirm(question, defaultValue);
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..(max - 1)] + "…";
}

using AgentSkills.Agents;
using AgentSkills.SkillModel;
using Spectre.Console;

namespace AgentSkills.UI;

/// <summary>
/// Stateless prompt helpers. Every method takes <see cref="IAnsiConsole"/> so it shares
/// the caller's console (testable via Spectre's <c>TestConsole</c>, respects whatever
/// rendering profile the host configured). No static <c>AnsiConsole.*</c> reaches.
/// </summary>
public static class Prompts
{
    public static List<Skill> SelectSkills(IAnsiConsole console, IReadOnlyList<Skill> skills, bool yes)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(skills);
        if (skills.Count == 0)
        {
            return [];
        }
        if (skills.Count == 1 || yes || !console.Profile.Capabilities.Interactive)
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

        var selected = console.Prompt(prompt);
        return selected.Count == 0 ? skills.ToList() : selected.Select(s => byLabel[s]).ToList();
    }

    /// <summary>
    /// Builds a Spectre-safe label for one skill. The skill's name and description come
    /// from third-party SKILL.md files and routinely contain <c>[brackets]</c> (e.g.
    /// references to <c>[Entity]</c> attributes), so both must be escaped before being
    /// interpolated into a Spectre markup string - otherwise Spectre parses them as colors.
    /// </summary>
    public static string FormatSkillLabel(Skill skill)
    {
        ArgumentNullException.ThrowIfNull(skill);
        return $"{Markup.Escape(skill.Name)}  [grey]{Markup.Escape(Truncate(skill.Description, 60))}[/]";
    }

    public static List<AgentConfig> SelectAgents(IAnsiConsole console, IReadOnlyList<AgentConfig> detected, bool yes)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(detected);
        var universal = detected.Where(a => a.IsUniversal).ToList();
        var others = detected.Where(a => !a.IsUniversal).ToList();

        if (yes || !console.Profile.Capabilities.Interactive)
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

        var selected = console.Prompt(prompt);
        var chosen = selected.Select(s => byLabel[s]).ToList();
        chosen.AddRange(universal);
        return chosen.Count == 0 ? detected.ToList() : chosen;
    }

    public static bool Confirm(IAnsiConsole console, string question, bool defaultValue = false)
    {
        ArgumentNullException.ThrowIfNull(console);
        if (!console.Profile.Capabilities.Interactive)
        {
            return defaultValue;
        }
        return console.Confirm(question, defaultValue);
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..(max - 1)] + "…";
}

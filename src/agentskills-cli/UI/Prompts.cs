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
            .InstructionsText("[grey](Press <space> to toggle, <enter> to confirm, Ctrl+C to cancel)[/]")
            .AddChoices(byLabel.Keys);

        var selected = console.Prompt(prompt);
        return selected.Select(s => byLabel[s]).ToList();
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
            .NotRequired()
            .PageSize(15)
            .InstructionsText("[grey](Press <space> to toggle, <enter> to confirm, Ctrl+C to cancel. Universal agents are always included.)[/]")
            .AddChoices(byLabel.Keys);

        var selected = console.Prompt(prompt);
        var chosen = selected.Select(s => byLabel[s]).ToList();
        chosen.AddRange(universal);
        return chosen;
    }

    public enum ConflictChoice { Rename, Replace, Skip }

    public sealed record ConflictDecision(ConflictChoice Choice, string? Alias = null);

    /// <summary>
    /// Explains one name conflict and asks what to do: install under another name (with a
    /// suggested default), replace the existing skill, or skip this skill.
    /// </summary>
    /// <param name="validateAlias">Returns an error message for an unusable alias, or null when it's fine.</param>
    public static ConflictDecision ResolveConflict(
        IAnsiConsole console,
        Install.SkillConflict conflict,
        string incomingSource,
        string suggestedAlias,
        Func<string, string?> validateAlias)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(conflict);
        ArgumentNullException.ThrowIfNull(validateAlias);

        const string rename = "Install it under a different name (keep both)";
        const string replace = "Replace the existing skill";
        const string skip = "Skip this skill";

        var choice = console.Prompt(new SelectionPrompt<string>()
            .Title($"[yellow]'{Markup.Escape(conflict.SkillName)}' from {Markup.Escape(incomingSource)} conflicts with " +
                   $"{Markup.Escape(conflict.DescribeExisting())} ({conflict.Scope}).[/] What should happen?")
            .AddChoices(rename, replace, skip));

        if (choice == skip) return new ConflictDecision(ConflictChoice.Skip);
        if (choice == replace) return new ConflictDecision(ConflictChoice.Replace);

        var alias = console.Prompt(new TextPrompt<string>($"Install '{Markup.Escape(conflict.SkillName)}' as:")
            .DefaultValue(suggestedAlias)
            .Validate(value =>
            {
                var error = validateAlias(value.Trim());
                return error is null ? ValidationResult.Success() : ValidationResult.Error($"[red]{Markup.Escape(error)}[/]");
            }));
        return new ConflictDecision(ConflictChoice.Rename, alias.Trim());
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

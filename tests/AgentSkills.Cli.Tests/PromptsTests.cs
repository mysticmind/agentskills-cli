using AgentSkills.SkillModel;
using AgentSkills.Ui;
using Spectre.Console;
using Xunit;

namespace AgentSkills.Tests;

public class PromptsTests
{
    /// <summary>
    /// Regression: a SKILL.md description containing literal square brackets
    /// (e.g. <c>[Entity]</c>) was being interpolated raw into a Spectre markup
    /// string, which then tried to parse the bracketed token as a color name
    /// and threw <c>Could not find color or style 'Entity'</c>.
    /// </summary>
    [Theory]
    [InlineData("Use [Entity] to auto-load entities before handlers run.")]
    [InlineData("Brackets [in description] should not crash Spectre.")]
    [InlineData("Use [red]color-looking text[/] safely.")]
    [InlineData("Mismatched [ and ] all over [the place")]
    public void FormatSkillLabel_RendersSpectreSafeMarkup(string description)
    {
        var skill = new Skill(
            Name: "wolverine-handlers-declarative-persistence",
            Description: description,
            Path: "/tmp/skill",
            RawContent: string.Empty,
            Metadata: null);

        var label = Prompts.FormatSkillLabel(skill);

        // Round-tripping through Markup.Remove asserts the markup is valid - it
        // re-runs Spectre's parser, which is exactly what blew up in the bug report.
        var stripped = Markup.Remove(label);
        Assert.Contains(skill.Name, stripped);
        Assert.Contains(description[..Math.Min(10, description.Length)], stripped);
    }

    [Fact]
    public void FormatSkillLabel_EscapesBracketsInName()
    {
        // SKILL.md sanitization usually prevents this, but defense in depth: a
        // bracket in the name shouldn't crash the prompt either.
        var skill = new Skill(
            Name: "[weird-name]",
            Description: "ok",
            Path: "/tmp",
            RawContent: string.Empty,
            Metadata: null);

        var label = Prompts.FormatSkillLabel(skill);
        var stripped = Markup.Remove(label);
        Assert.Contains("[weird-name]", stripped);
    }
}

using System.ComponentModel;
using Skills.Ui;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Skills.Commands;

public sealed class InitCommand : Command<InitCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "[PATH]")]
        [Description("Directory to scaffold a SKILL.md in. Defaults to current directory.")]
        public string? Path { get; init; }

        [CommandOption("-y|--yes")]
        [Description("Overwrite an existing SKILL.md without asking.")]
        public bool Yes { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var dir = settings.Path is null
            ? Directory.GetCurrentDirectory()
            : System.IO.Path.GetFullPath(settings.Path);
        Directory.CreateDirectory(dir);

        var skillMd = System.IO.Path.Combine(dir, "SKILL.md");
        if (File.Exists(skillMd) && !settings.Yes)
        {
            if (!Prompts.Confirm($"{skillMd} exists. Overwrite?", false))
            {
                AnsiConsole.MarkupLine("[grey]Aborted.[/]");
                return 0;
            }
        }

        var name = SkillModel.SkillNameSanitizer.Sanitize(System.IO.Path.GetFileName(dir));
        var template = $$"""
            ---
            name: {{name}}
            description: One-line description that helps an agent decide when to use this skill.
            ---
            # {{name}}

            What this skill does and how it should be used by an agent.

            ## When to invoke

            - Bullet trigger conditions

            ## Notes

            - Anything else the agent should know.
            """;

        File.WriteAllText(skillMd, template);
        AnsiConsole.MarkupLine($"[green]Created[/] {Markup.Escape(skillMd)}");
        return 0;
    }
}

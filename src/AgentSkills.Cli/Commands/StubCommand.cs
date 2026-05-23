using Spectre.Console;
using Spectre.Console.Cli;

namespace AgentSkills.Commands;

public sealed class StubCommand : Command<StubCommand.Settings>
{
    public sealed class Settings : CommandSettings { }

    public override int Execute(CommandContext context, Settings settings)
    {
        var name = context.Data as string ?? "this command";
        AnsiConsole.MarkupLine(
            $"[yellow]`{name}` is not implemented yet in skills-net v1.[/] " +
            "Track progress at https://github.com/skills-net/skills-net.");
        return 64;
    }
}

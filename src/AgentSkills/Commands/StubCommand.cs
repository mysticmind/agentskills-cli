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
            $"[yellow]`{name}` is not implemented yet in AgentSkills v1.[/] " +
            "Track progress at https://github.com/mysticmind/agent-skills.");
        return 64;
    }
}

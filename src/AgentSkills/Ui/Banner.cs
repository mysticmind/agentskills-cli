using Spectre.Console;

namespace AgentSkills.Ui;

public static class Banner
{
    public static void Show()
    {
        if (!AnsiConsole.Profile.Capabilities.Interactive)
        {
            return;
        }

        AnsiConsole.Write(new FigletText("skills").LeftJustified().Color(Color.Aqua));
        AnsiConsole.MarkupLine("[grey]The open agent skills CLI for .NET[/]");
        AnsiConsole.WriteLine();
    }
}

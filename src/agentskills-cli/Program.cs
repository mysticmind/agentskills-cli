using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using AgentSkills;
using AgentSkills.Commands;
using AgentSkills.Hosting;
using Spectre.Console;
using Spectre.Console.Cli;

// Resolve verbosity early so the logger pipeline reflects -v / -q before any
// command runs. Spectre.Console.Cli's settings binding happens too late for this.
// The flags also have to be stripped from args before Spectre sees them, since
// Spectre would otherwise reject them as 'Unexpected option ...'.
var (verbosity, runArgs) = ParseVerbosity(args);

var services = new ServiceCollection()
    .AddSkillsRuntime(minimumLogLevel: verbosity);

var registrar = new TypeRegistrar(services);
var app = new CommandApp(registrar);

app.Configure(config =>
{
    config.SetApplicationName("agentskills-cli");
    config.PropagateExceptions();

    config.AddCommand<AddCommand>("add")
        .WithAlias("a").WithAlias("i").WithAlias("install")
        .WithDescription("Install one or more skills from a local folder, GitHub repo, git URL, or NuGet/npm package.");

    config.AddCommand<ListCommand>("list")
        .WithAlias("ls")
        .WithDescription("List installed skills grouped by agent or package.");

    config.AddCommand<RemoveCommand>("remove")
        .WithAlias("rm").WithAlias("r")
        .WithDescription("Remove installed skills.");

    config.AddCommand<InitCommand>("init")
        .WithDescription("Scaffold a new SKILL.md.");

    config.AddCommand<FindCommand>("find")
        .WithAlias("search").WithAlias("f")
        .WithDescription("Search registered providers (skills.sh and any you register) and install a result.");

    config.AddCommand<UpdateCommand>("update")
        .WithAlias("upgrade").WithAlias("check")
        .WithDescription("Check tracked skills for upstream updates and (optionally) reinstall.");
});

try
{
    return await app.RunAsync(runArgs);
}
catch (SkillsException ex)
{
    AnsiConsole.MarkupLine($"[red]error[/] {Markup.Escape(ex.Message)}");
    if (verbosity <= LogLevel.Debug && ex.InnerException is not null)
    {
        AnsiConsole.WriteException(ex.InnerException, ExceptionFormats.ShortenEverything);
    }
    return ex.ExitCode;
}
catch (CommandRuntimeException ex)
{
    AnsiConsole.MarkupLine($"[red]error[/] {Markup.Escape(ex.Message)}");
    return 2;
}
catch (Exception ex)
{
    AnsiConsole.MarkupLine("[red]unexpected error[/]");
    AnsiConsole.WriteException(ex, ExceptionFormats.ShortenEverything);
    return 1;
}

static (LogLevel Level, string[] StrippedArgs) ParseVerbosity(string[] cli)
{
    var level = LogLevel.Information;
    var remaining = new List<string>(cli.Length);
    foreach (var a in cli)
    {
        switch (a)
        {
            case "--quiet" or "-q":
                level = LogLevel.Warning;
                continue;
            case "--verbose" or "-v":
                level = LogLevel.Debug;
                continue;
            case "--trace":
                level = LogLevel.Trace;
                continue;
            default:
                remaining.Add(a);
                break;
        }
    }
    return (level, remaining.ToArray());
}

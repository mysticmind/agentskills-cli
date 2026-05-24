using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using AgentSkills;
using AgentSkills.Commands;
using AgentSkills.Hosting;
using Spectre.Console;
using Spectre.Console.Cli;

// Resolve verbosity early so the logger pipeline reflects -v / -q before any
// command runs. Spectre.Console.Cli's settings binding happens too late for this.
var verbosity = ParseVerbosity(args);

var services = new ServiceCollection()
    .AddSkillsRuntime(minimumLogLevel: verbosity);

var registrar = new TypeRegistrar(services);
var app = new CommandApp(registrar);

app.Configure(config =>
{
    config.SetApplicationName("agentskills");
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
    return await app.RunAsync(args);
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

static LogLevel ParseVerbosity(string[] cli)
{
    for (var i = 0; i < cli.Length; i++)
    {
        var a = cli[i];
        if (a is "--quiet" or "-q") return LogLevel.Warning;
        if (a is "--verbose" or "-v") return LogLevel.Debug;
        if (a == "--trace") return LogLevel.Trace;
    }
    return LogLevel.Information;
}

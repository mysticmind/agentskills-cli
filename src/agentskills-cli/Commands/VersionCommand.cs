using System.ComponentModel;
using System.Runtime.InteropServices;
using AgentSkills.Hosting;
using AgentSkills.Install;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AgentSkills.Commands;

/// <summary>
/// <c>version</c>: the tool version plus the runtime and file locations, which is what a
/// bug report needs. <c>--version</c> (handled by Spectre) prints only the version number.
/// </summary>
public sealed class VersionCommand : Command<VersionCommand.Settings>
{
    private readonly IAnsiConsole _console;

    public VersionCommand(IAnsiConsole console)
    {
        _console = console ?? throw new ArgumentNullException(nameof(console));
    }

    public sealed class Settings : CommandSettings
    {
        [CommandOption("--json")]
        [Description("Write the details as JSON to stdout.")]
        public bool Json { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var details = new
        {
            version = VersionInfo.Version,
            commit = VersionInfo.Commit,
            runtime = VersionInfo.Runtime,
            targetFramework = VersionInfo.TargetFramework,
            os = RuntimeInformation.OSDescription,
            architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            globalLock = SkillLock.GetLockPath(),
            projectLock = LocalLock.GetLockPath(),
        };

        if (settings.Json)
        {
            JsonOutput.Write(_console, details);
            return 0;
        }

        _console.MarkupLine($"[bold]agentskills-cli[/] {Markup.Escape(details.version)}" +
                            (details.commit is null ? string.Empty : $" [grey]({Markup.Escape(details.commit)})[/]"));

        var grid = new Grid().AddColumn().AddColumn();
        grid.AddRow("[grey]Runtime[/]", Markup.Escape(details.runtime));
        grid.AddRow("[grey]Built for[/]", Markup.Escape(details.targetFramework));
        grid.AddRow("[grey]OS[/]", Markup.Escape($"{details.os} ({details.architecture})"));
        grid.AddRow("[grey]Global lock[/]", Markup.Escape(details.globalLock));
        grid.AddRow("[grey]Project lock[/]", Markup.Escape(details.projectLock));
        _console.Write(grid);
        return 0;
    }
}

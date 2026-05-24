using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using AgentSkills.Commands;
using AgentSkills.Install;
using AgentSkills.Sources;
using AgentSkills.Sources.Factories;
using Spectre.Console;

namespace AgentSkills.Hosting;

/// <summary>
/// Single composition root for the CLI. Registers the shared runtime services
/// (<see cref="IAnsiConsole"/>, logging, HttpClient) and gives extension points
/// for tests or downstream consumers to swap any of them.
/// </summary>
public static class SkillsServices
{
    public static IServiceCollection AddSkillsRuntime(
        this IServiceCollection services,
        LogLevel minimumLogLevel = LogLevel.Information,
        IAnsiConsole? consoleOverride = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var console = consoleOverride ?? AnsiConsole.Console;
        services.AddSingleton(console);

        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.SetMinimumLevel(minimumLogLevel);
            builder.AddProvider(new SpectreLoggerProvider(console, minimumLogLevel));
        });

        // One HttpClient instance for the process lifetime is fine for a CLI - keeps
        // sockets warm across the few HTTP calls we make.
        services.AddSingleton(_ =>
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("agentskills");
            return client;
        });

        services.AddSingleton<CommandCancellation>();

        // Source factories: the v1 extension point. To add a new source type, write
        // an ISkillSourceFactory and AddSingleton it after this call.
        services.AddSingleton<ISkillSourceFactory, LocalSourceFactory>();
        services.AddSingleton<ISkillSourceFactory, GitSourceFactory>();
        services.AddSingleton<ISkillSourceFactory, NuGetSourceFactory>();
        services.AddSingleton<ISkillSourceFactory, NpmSourceFactory>();
        services.AddSingleton<ISkillSourceFactory, WellKnownSourceFactory>();

        services.AddSingleton<ISourceResolver, SourceResolver>();
        services.AddSingleton<IInstallService, InstallService>();

        // Search providers: the extension point for `find`. Register additional
        // ones with services.AddSingleton<ISkillSearchProvider, MyProvider>().
        services.AddSingleton<ISkillSearchProvider, SkillsShSearchProvider>();

        // Commands. Spectre.Console.Cli resolves these via the TypeRegistrar.
        services.AddTransient<AddCommand>();
        services.AddTransient<ListCommand>();
        services.AddTransient<RemoveCommand>();
        services.AddTransient<InitCommand>();
        services.AddTransient<FindCommand>();
        services.AddTransient<UpdateCommand>();

        return services;
    }
}

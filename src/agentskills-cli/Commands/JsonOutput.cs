using System.Text.Json;
using System.Text.Json.Serialization;
using Spectre.Console;

namespace AgentSkills.Commands;

/// <summary>Shared <c>--json</c> writer. Output goes to the console's raw stdout writer, never through markup.</summary>
internal static class JsonOutput
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static void Write<T>(IAnsiConsole console, T value)
    {
        ArgumentNullException.ThrowIfNull(console);
        var writer = console.Profile.Out.Writer;
        writer.WriteLine(JsonSerializer.Serialize(value, Options));
        writer.Flush();
    }
}

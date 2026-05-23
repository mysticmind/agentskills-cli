namespace AgentSkills;

/// <summary>
/// Base type for every exception thrown intentionally by skills-net. The top-level
/// handler in <c>Program.cs</c> recognizes this type and renders a clean one-line
/// error to the console; anything else escapes as a stack trace.
/// </summary>
public abstract class SkillsException : Exception
{
    /// <summary>Suggested process exit code for this failure mode.</summary>
    public int ExitCode { get; }

    protected SkillsException(string message, int exitCode = 1, Exception? innerException = null)
        : base(message, innerException)
    {
        ExitCode = exitCode;
    }
}

/// <summary>The user passed an input the CLI cannot parse or honor (bad source string, unknown agent, etc.).</summary>
public sealed class UserInputException : SkillsException
{
    public UserInputException(string message, Exception? inner = null) : base(message, exitCode: 2, inner) { }
}

/// <summary>The source string parsed correctly but could not be fetched (clone failure, 404, auth refused, ...).</summary>
public sealed class SkillSourceException : SkillsException
{
    public string SourceDescription { get; }

    public SkillSourceException(string sourceDescription, string message, Exception? inner = null)
        : base(message, exitCode: 1, inner)
    {
        SourceDescription = sourceDescription;
    }
}

/// <summary>npm registry or tarball download failure.</summary>
public sealed class NpmRegistryException : SkillsException
{
    public string PackageId { get; }
    public string? Registry { get; }

    public NpmRegistryException(string packageId, string? registry, string message, Exception? inner = null)
        : base(message, exitCode: 1, inner)
    {
        PackageId = packageId;
        Registry = registry;
    }
}

/// <summary>NuGet feed or package download failure.</summary>
public sealed class NuGetFeedException : SkillsException
{
    public string PackageId { get; }

    public NuGetFeedException(string packageId, string message, Exception? inner = null)
        : base(message, exitCode: 1, inner)
    {
        PackageId = packageId;
    }
}

/// <summary>git binary missing, clone timed out, or returned a non-zero exit code.</summary>
public sealed class GitException : SkillsException
{
    public string? RepoUrl { get; }

    public GitException(string? repoUrl, string message, Exception? inner = null)
        : base(message, exitCode: 1, inner)
    {
        RepoUrl = repoUrl;
    }
}

/// <summary>Failure parsing or writing the global/project skill-lock file.</summary>
public sealed class LockFileException : SkillsException
{
    public string Path { get; }

    public LockFileException(string path, string message, Exception? inner = null)
        : base(message, exitCode: 1, inner)
    {
        Path = path;
    }
}

/// <summary>An archive (zip, tar.gz) failed validation: too large, too many files, unsafe path, bad digest.</summary>
public sealed class ArchiveValidationException : SkillsException
{
    public ArchiveValidationException(string message, Exception? inner = null)
        : base(message, exitCode: 1, inner) { }
}

/// <summary>The well-known endpoint returned no usable index or returned an unknown schema.</summary>
public sealed class WellKnownEndpointException : SkillsException
{
    public string Endpoint { get; }

    public WellKnownEndpointException(string endpoint, string message, Exception? inner = null)
        : base(message, exitCode: 1, inner)
    {
        Endpoint = endpoint;
    }
}

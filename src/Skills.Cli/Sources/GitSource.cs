using System.Diagnostics;
using System.Text;

namespace Skills.Sources;

public sealed class GitSource : ISkillSource
{
    private readonly string _stagingRoot;

    public ParsedSource Parsed { get; }
    public string RootPath => _stagingRoot;
    public string? Subpath => Parsed.Subpath;
    public string DisplaySource { get; }
    public string SourceTypeLabel { get; }
    public string? SourceUrl => Parsed.Url;
    public string? Reference => Parsed.Ref;

    private GitSource(ParsedSource parsed, string stagingRoot, string sourceTypeLabel, string displaySource)
    {
        Parsed = parsed;
        _stagingRoot = stagingRoot;
        SourceTypeLabel = sourceTypeLabel;
        DisplaySource = displaySource;
    }

    public static GitSource Clone(ParsedSource parsed, IProgress<string>? progress = null)
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"skills-net-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            var psi = new ProcessStartInfo("git")
            {
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("clone");
            psi.ArgumentList.Add("--depth");
            psi.ArgumentList.Add("1");

            if (!string.IsNullOrEmpty(parsed.Ref))
            {
                psi.ArgumentList.Add("--branch");
                psi.ArgumentList.Add(parsed.Ref);
            }

            psi.ArgumentList.Add(parsed.Url);
            psi.ArgumentList.Add(tempRoot);

            psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
            psi.Environment["GIT_LFS_SKIP_SMUDGE"] = "1";

            using var proc = Process.Start(psi)
                ?? throw new InvalidOperationException("Unable to launch git. Is it installed and on PATH?");

            var stderr = new StringBuilder();
            proc.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };
            proc.BeginErrorReadLine();
            proc.BeginOutputReadLine();

            var timeoutMs = ResolveTimeoutMs();
            if (!proc.WaitForExit(timeoutMs))
            {
                try { proc.Kill(true); } catch { /* ignore */ }
                throw new TimeoutException($"git clone exceeded {timeoutMs}ms");
            }

            if (proc.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"git clone failed (exit {proc.ExitCode}) for {parsed.Url}:\n{stderr}");
            }

            var label = parsed.Type switch
            {
                SourceType.GitHub => "github",
                SourceType.GitLab => "gitlab",
                _ => "git",
            };

            return new GitSource(parsed, tempRoot, label, parsed.Url);
        }
        catch
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { /* swallow */ }
            throw;
        }
    }

    private static int ResolveTimeoutMs()
    {
        var raw = Environment.GetEnvironmentVariable("SKILLS_CLONE_TIMEOUT_MS");
        return int.TryParse(raw, out var v) && v > 0 ? v : 300_000;
    }

    /// <summary>
    /// Returns the tree SHA for <paramref name="relativePath"/> within the cloned repo, or
    /// <c>null</c> if the path doesn't resolve. Used to record <c>skillFolderHash</c> in
    /// the lock so <c>update</c> has something to compare against the live GitHub tree.
    /// </summary>
    public string? ReadFolderTreeSha(string? relativePath)
    {
        var spec = string.IsNullOrEmpty(relativePath) ? "HEAD^{tree}" : $"HEAD:{relativePath.Replace('\\', '/').Trim('/')}";
        var psi = new ProcessStartInfo("git", $"rev-parse {spec}")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = _stagingRoot,
        };

        try
        {
            using var proc = Process.Start(psi);
            if (proc is null) return null;
            if (!proc.WaitForExit(5000)) { try { proc.Kill(); } catch { /* ignore */ } return null; }
            if (proc.ExitCode != 0) return null;
            var output = proc.StandardOutput.ReadToEnd().Trim();
            return string.IsNullOrEmpty(output) ? null : output;
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        try { Directory.Delete(_stagingRoot, recursive: true); } catch { /* best effort */ }
    }
}

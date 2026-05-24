using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace AgentSkills.Sources;

public sealed record GitTreeEntry(string Path, string Type, string Sha);
public sealed record RepoTree(string Sha, string Branch, IReadOnlyList<GitTreeEntry> Entries);

/// <summary>
/// Minimal GitHub helper used by <c>update</c> and the lock-writing path of <c>add</c>.
/// Token resolution is lazy and mirrors upstream: try unauthenticated first, fall back
/// to GITHUB_TOKEN / GH_TOKEN / <c>gh auth token</c> only after a rate-limit 403.
/// </summary>
public static class GitHubApi
{
    private const string TreeUrlTemplate = "https://api.github.com/repos/{0}/git/trees/{1}?recursive=1";
    private static readonly TimeSpan FetchTimeout = TimeSpan.FromSeconds(10);
    private static bool _rateLimitedThisProcess;
    private static bool _ghCliWarned;

    public static async Task<RepoTree?> FetchTreeAsync(
        string ownerRepo,
        string? @ref,
        Func<string?>? getToken = null,
        HttpClient? httpClient = null,
        CancellationToken cancellationToken = default)
    {
        var owned = httpClient is null;
        httpClient ??= CreateClient();
        try
        {
            var branches = @ref is null
                ? new[] { "HEAD", "main", "master" }
                : new[] { @ref };

            if (_rateLimitedThisProcess && getToken is not null)
            {
                var token = getToken();
                if (token is null) return null;
                foreach (var branch in branches)
                {
                    var tree = await FetchSingleAsync(httpClient, ownerRepo, branch, token, cancellationToken)
                        .ConfigureAwait(false);
                    if (tree is not null) return tree;
                }
                return null;
            }

            var rateLimited = false;
            foreach (var branch in branches)
            {
                var (tree, hitLimit) = await FetchSingleWithStatusAsync(
                    httpClient, ownerRepo, branch, token: null, cancellationToken).ConfigureAwait(false);
                if (tree is not null) return tree;
                if (hitLimit)
                {
                    rateLimited = true;
                    break;
                }
            }

            if (!rateLimited || getToken is null) return null;

            _rateLimitedThisProcess = true;
            var tokenAfter = getToken();
            if (tokenAfter is null) return null;

            foreach (var branch in branches)
            {
                var tree = await FetchSingleAsync(httpClient, ownerRepo, branch, tokenAfter, cancellationToken)
                    .ConfigureAwait(false);
                if (tree is not null) return tree;
            }

            return null;
        }
        finally
        {
            if (owned) httpClient.Dispose();
        }
    }

    public static string? GetFolderHashFromTree(RepoTree tree, string skillPath)
    {
        var folderPath = skillPath.Replace('\\', '/');
        const string suffix = "skill.md";
        if (folderPath.EndsWith("/" + suffix, StringComparison.OrdinalIgnoreCase))
        {
            folderPath = folderPath[..^("/" + suffix).Length];
        }
        else if (folderPath.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            folderPath = folderPath[..^suffix.Length];
        }
        folderPath = folderPath.TrimEnd('/');

        if (folderPath.Length == 0)
        {
            return tree.Sha;
        }

        foreach (var entry in tree.Entries)
        {
            if (entry.Type == "tree" && string.Equals(entry.Path, folderPath, StringComparison.Ordinal))
            {
                return entry.Sha;
            }
        }
        return null;
    }

    public static string? ResolveToken(Microsoft.Extensions.Logging.ILogger? logger = null)
    {
        var envToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN")
            ?? Environment.GetEnvironmentVariable("GH_TOKEN");
        if (!string.IsNullOrWhiteSpace(envToken)) return envToken;

        try
        {
            var psi = new ProcessStartInfo("gh", "auth token")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var proc = Process.Start(psi);
            if (proc is null) return null;
            if (!proc.WaitForExit(3000)) { try { proc.Kill(); } catch { /* ignore */ } return null; }
            if (proc.ExitCode != 0) return null;

            var token = proc.StandardOutput.ReadToEnd().Trim();
            if (!_ghCliWarned)
            {
                _ghCliWarned = true;
                logger?.LogInformation(
                    "using GitHub token from `gh auth token` (set GITHUB_TOKEN or GH_TOKEN to silence)");
            }
            return string.IsNullOrEmpty(token) ? null : token;
        }
        catch
        {
            return null;
        }
    }

    // ─── Internals ───────────────────────────────────────────────────────────

    private static async Task<RepoTree?> FetchSingleAsync(
        HttpClient client, string ownerRepo, string branch, string? token, CancellationToken cancellationToken)
    {
        var (tree, _) = await FetchSingleWithStatusAsync(client, ownerRepo, branch, token, cancellationToken)
            .ConfigureAwait(false);
        return tree;
    }

    private static async Task<(RepoTree? Tree, bool RateLimited)> FetchSingleWithStatusAsync(
        HttpClient client, string ownerRepo, string branch, string? token, CancellationToken cancellationToken)
    {
        var url = string.Format(TreeUrlTemplate, ownerRepo, Uri.EscapeDataString(branch));
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));
        if (!string.IsNullOrEmpty(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(FetchTimeout);

        try
        {
            using var response = await client.SendAsync(request, cts.Token).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                var stream = await response.Content.ReadAsStreamAsync(cts.Token).ConfigureAwait(false);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cts.Token).ConfigureAwait(false);

                if (!doc.RootElement.TryGetProperty("sha", out var shaEl) ||
                    !doc.RootElement.TryGetProperty("tree", out var treeEl) ||
                    treeEl.ValueKind != JsonValueKind.Array)
                {
                    return (null, false);
                }

                var entries = new List<GitTreeEntry>(treeEl.GetArrayLength());
                foreach (var entry in treeEl.EnumerateArray())
                {
                    if (!entry.TryGetProperty("path", out var pathEl) ||
                        !entry.TryGetProperty("type", out var typeEl) ||
                        !entry.TryGetProperty("sha", out var entrySha)) continue;
                    entries.Add(new GitTreeEntry(
                        pathEl.GetString() ?? string.Empty,
                        typeEl.GetString() ?? string.Empty,
                        entrySha.GetString() ?? string.Empty));
                }

                return (new RepoTree(shaEl.GetString() ?? string.Empty, branch, entries), false);
            }

            var hitLimit = response.StatusCode == HttpStatusCode.Forbidden &&
                response.Headers.TryGetValues("x-ratelimit-remaining", out var values) &&
                values.FirstOrDefault() == "0";
            return (null, hitLimit);
        }
        catch
        {
            return (null, false);
        }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("agentskills/0.1");
        return client;
    }
}

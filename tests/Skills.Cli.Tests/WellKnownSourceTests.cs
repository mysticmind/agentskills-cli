using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using Skills.Sources;
using Xunit;

namespace Skills.Tests;

public class WellKnownSourceTests : IDisposable
{
    private readonly HttpListener _listener;
    private readonly string _baseUrl;
    private readonly CancellationTokenSource _cts = new();
    private readonly Dictionary<string, (byte[] Body, string ContentType)> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly Task _serveLoop;

    public WellKnownSourceTests()
    {
        var port = GetFreeTcpPort();
        _baseUrl = $"http://127.0.0.1:{port}";
        _listener = new HttpListener();
        _listener.Prefixes.Add($"{_baseUrl}/");
        _listener.Start();
        _serveLoop = Task.Run(ServeAsync);
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _listener.Stop(); } catch { /* ignore */ }
        try { _serveLoop.Wait(TimeSpan.FromSeconds(1)); } catch { /* ignore */ }
    }

    private async Task ServeAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try
            {
                ctx = await _listener.GetContextAsync();
            }
            catch
            {
                return;
            }

            var path = ctx.Request.Url!.AbsolutePath;
            if (_files.TryGetValue(path, out var resource))
            {
                ctx.Response.StatusCode = 200;
                ctx.Response.ContentType = resource.ContentType;
                ctx.Response.OutputStream.Write(resource.Body);
            }
            else
            {
                ctx.Response.StatusCode = 404;
            }
            ctx.Response.Close();
        }
    }

    private void Serve(string path, string body, string contentType = "application/json")
        => _files[path] = (Encoding.UTF8.GetBytes(body), contentType);

    private void Serve(string path, byte[] body, string contentType)
        => _files[path] = (body, contentType);

    [Fact]
    public async Task LegacyV1_Index_Materializes()
    {
        var skillMd = "---\nname: hello\ndescription: hi\n---\n# hello\n";
        Serve("/.well-known/agent-skills/index.json",
            "{\"skills\":[{\"name\":\"hello\",\"description\":\"hi\",\"files\":[\"SKILL.md\"]}]}");
        Serve("/.well-known/agent-skills/hello/SKILL.md", skillMd, "text/markdown");

        using var http = new HttpClient();
        using var source = await WellKnownSource.FetchAsync(
            SourceParser.Parse(_baseUrl), http);

        var skillFile = Path.Combine(source.RootPath, "hello", "SKILL.md");
        Assert.True(File.Exists(skillFile));
        Assert.Contains("name: hello", await File.ReadAllTextAsync(skillFile));
    }

    [Fact]
    public async Task V2_SkillMd_VerifiesDigest()
    {
        var skillMd = "---\nname: greeter\ndescription: g\n---\nbody\n";
        var bytes = Encoding.UTF8.GetBytes(skillMd);
        var digest = "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        Serve("/.well-known/agent-skills/index.json", $$"""
            {
              "$schema": "https://schemas.agentskills.io/discovery/0.2.0/schema.json",
              "skills": [
                {
                  "name": "greeter",
                  "description": "g",
                  "type": "skill-md",
                  "url": "greeter.md",
                  "digest": "{{digest}}"
                }
              ]
            }
            """);
        Serve("/.well-known/agent-skills/greeter.md", bytes, "text/markdown");

        using var http = new HttpClient();
        using var source = await WellKnownSource.FetchAsync(
            SourceParser.Parse(_baseUrl), http);
        Assert.True(File.Exists(Path.Combine(source.RootPath, "greeter", "SKILL.md")));
    }

    [Fact]
    public async Task V2_BadDigest_Throws()
    {
        Serve("/.well-known/agent-skills/index.json", $$"""
            {
              "$schema": "https://schemas.agentskills.io/discovery/0.2.0/schema.json",
              "skills": [
                {
                  "name": "tampered",
                  "description": "x",
                  "type": "skill-md",
                  "url": "tampered.md",
                  "digest": "sha256:0000000000000000000000000000000000000000000000000000000000000000"
                }
              ]
            }
            """);
        Serve("/.well-known/agent-skills/tampered.md",
            Encoding.UTF8.GetBytes("---\nname: x\ndescription: y\n---\n"), "text/markdown");

        using var http = new HttpClient();
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => WellKnownSource.FetchAsync(SourceParser.Parse(_baseUrl), http));
    }

    [Fact]
    public async Task V2_ZipArchive_ExtractsAndKeepsSkillMd()
    {
        // Build a zip containing SKILL.md and ref/notes.md
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var sw = new StreamWriter(zip.CreateEntry("SKILL.md").Open()))
                sw.Write("---\nname: archived\ndescription: arc\n---\nbody\n");
            using (var sw = new StreamWriter(zip.CreateEntry("ref/notes.md").Open()))
                sw.Write("notes");
        }
        var zipBytes = ms.ToArray();
        var digest = "sha256:" + Convert.ToHexString(SHA256.HashData(zipBytes)).ToLowerInvariant();

        Serve("/.well-known/agent-skills/index.json", $$"""
            {
              "$schema": "https://schemas.agentskills.io/discovery/0.2.0/schema.json",
              "skills": [
                {
                  "name": "archived",
                  "description": "arc",
                  "type": "archive",
                  "url": "archived.zip",
                  "digest": "{{digest}}"
                }
              ]
            }
            """);
        Serve("/.well-known/agent-skills/archived.zip", zipBytes, "application/zip");

        using var http = new HttpClient();
        using var source = await WellKnownSource.FetchAsync(
            SourceParser.Parse(_baseUrl), http);

        Assert.True(File.Exists(Path.Combine(source.RootPath, "archived", "SKILL.md")));
        Assert.True(File.Exists(Path.Combine(source.RootPath, "archived", "ref", "notes.md")));
    }

    [Fact]
    public async Task FallsBackToLegacyPath()
    {
        // No /.well-known/agent-skills index - only legacy /.well-known/skills works.
        Serve("/.well-known/skills/index.json",
            "{\"skills\":[{\"name\":\"legacy\",\"description\":\"l\",\"files\":[\"SKILL.md\"]}]}");
        Serve("/.well-known/skills/legacy/SKILL.md",
            "---\nname: legacy\ndescription: l\n---\n", "text/markdown");

        using var http = new HttpClient();
        using var source = await WellKnownSource.FetchAsync(
            SourceParser.Parse(_baseUrl), http);
        Assert.True(File.Exists(Path.Combine(source.RootPath, "legacy", "SKILL.md")));
    }

    private static int GetFreeTcpPort()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}

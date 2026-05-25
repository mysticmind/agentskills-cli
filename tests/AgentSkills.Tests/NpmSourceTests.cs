using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using AgentSkills.Sources;
using Xunit;

namespace AgentSkills.Tests;

public class NpmSourceParserTests
{
    [Theory]
    [InlineData("npm:sample-pkg", "sample-pkg", null)]
    [InlineData("npm:sample-pkg@1.3.0", "sample-pkg", "1.3.0")]
    [InlineData("npm:@scope/name", "@scope/name", null)]
    [InlineData("npm:@scope/name@2.0.0-beta.1", "@scope/name", "2.0.0-beta.1")]
    [InlineData("@my-org/skills", "@my-org/skills", null)]
    [InlineData("@my-org/skills@1.0.0", "@my-org/skills", "1.0.0")]
    public void DetectsNpm(string input, string expectedId, string? expectedVersion)
    {
        var parsed = SourceParser.Parse(input);
        Assert.Equal(SourceType.Npm, parsed.Type);
        Assert.Equal(expectedId, parsed.PackageId);
        Assert.Equal(expectedVersion, parsed.PackageVersion);
    }

    [Fact]
    public void BareUnscopedWithDotStillNuGet()
    {
        // We document this: unscoped npm requires the `npm:` prefix because a bare
        // `foo.bar` defaults to NuGet (since this is a .NET tool).
        var parsed = SourceParser.Parse("lodash.merge");
        Assert.Equal(SourceType.NuGet, parsed.Type);
    }
}

public class NpmRegistryTests : IDisposable
{
    private readonly string _home;
    private readonly string _origHome;
    private readonly string _rcPath;

    public NpmRegistryTests()
    {
        _home = Path.Combine(Path.GetTempPath(), "agentskills-cli-npmrc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_home);
        _rcPath = Path.Combine(_home, ".npmrc");
        _origHome = Environment.GetEnvironmentVariable("HOME") ?? string.Empty;
        Environment.SetEnvironmentVariable("HOME", _home);
        Environment.SetEnvironmentVariable("USERPROFILE", _home);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("HOME", _origHome);
        Environment.SetEnvironmentVariable("USERPROFILE", _origHome);
        try { Directory.Delete(_home, recursive: true); } catch { /* ignore */ }
    }

    [Fact]
    public void DefaultRegistryFromRc()
    {
        File.WriteAllText(_rcPath, "registry=https://custom.example.com/npm/\n");
        var reg = NpmRegistry.Load();
        Assert.Equal("https://custom.example.com/npm/", reg.DefaultRegistryUrl);
    }

    [Fact]
    public void ScopedRegistryOverridesDefault()
    {
        File.WriteAllText(_rcPath,
            "registry=https://registry.npmjs.org/\n@my-org:registry=https://npm.contoso.com/team/\n");
        var reg = NpmRegistry.Load();
        Assert.Equal("https://npm.contoso.com/team/", reg.ResolveRegistry("@my-org/foo"));
        Assert.Equal("https://registry.npmjs.org/", reg.ResolveRegistry("@other/bar"));
        Assert.Equal("https://registry.npmjs.org/", reg.ResolveRegistry("plain"));
    }

    [Fact]
    public void AuthTokenAttachedForMatchingHostAndPathPrefix()
    {
        File.WriteAllText(_rcPath, """
            registry=https://npm.contoso.com/team/
            //npm.contoso.com/team/:_authToken=secret-xyz
            """);
        var reg = NpmRegistry.Load();
        var auth = reg.GetAuthHeader("https://npm.contoso.com/team/");
        Assert.NotNull(auth);
        Assert.Equal("Bearer", auth!.Scheme);
        Assert.Equal("secret-xyz", auth.Parameter);
    }

    [Fact]
    public void NoAuthForUnrelatedHost()
    {
        File.WriteAllText(_rcPath,
            "//npm.contoso.com/:_authToken=secret\n");
        var reg = NpmRegistry.Load();
        Assert.Null(reg.GetAuthHeader("https://registry.npmjs.org/"));
    }

    [Fact]
    public void EnvVarSubstitution()
    {
        Environment.SetEnvironmentVariable("SKILLS_TEST_NPM_TOKEN", "from-env");
        try
        {
            File.WriteAllText(_rcPath,
                "//npm.contoso.com/:_authToken=${SKILLS_TEST_NPM_TOKEN}\n");
            var reg = NpmRegistry.Load();
            var auth = reg.GetAuthHeader("https://npm.contoso.com/");
            Assert.Equal("from-env", auth?.Parameter);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SKILLS_TEST_NPM_TOKEN", null);
        }
    }
}

public class NpmSourceIntegrationTests : IDisposable
{
    private readonly HttpListener _listener;
    private readonly string _baseUrl;
    private readonly CancellationTokenSource _cts = new();
    private readonly Dictionary<string, (byte[] Body, string ContentType)> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly Task _serveLoop;

    public NpmSourceIntegrationTests()
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
            try { ctx = await _listener.GetContextAsync(); } catch { return; }

            // Match by exact path; npm encodes @ and / as %40 / %2F.
            var path = Uri.UnescapeDataString(ctx.Request.Url!.AbsolutePath);
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

    [Fact]
    public async Task DownloadsTarballFromCustomRegistry()
    {
        var packageId = "@my-org/test-skills";
        var version = "1.0.0";
        var tarball = BuildTarGz([
            ("package/package.json", "{\"name\":\"@my-org/test-skills\",\"version\":\"1.0.0\"}"),
            ("package/skills/sample/SKILL.md", "---\nname: sample\ndescription: s\n---\n# sample\n"),
        ]);
        var tarballUrl = $"{_baseUrl}/@my-org/test-skills/-/test-skills-1.0.0.tgz";

        _files["/@my-org/test-skills"] = (Encoding.UTF8.GetBytes($$"""
            {
              "name": "@my-org/test-skills",
              "dist-tags": { "latest": "1.0.0" },
              "versions": {
                "1.0.0": {
                  "dist": { "tarball": "{{tarballUrl}}" }
                }
              }
            }
            """), "application/json");
        _files[$"/@my-org/test-skills/-/test-skills-1.0.0.tgz"] = (tarball, "application/octet-stream");

        var parsed = SourceParser.Parse(packageId);
        Assert.Equal(SourceType.Npm, parsed.Type);

        using var source = await NpmSource.DownloadAsync(parsed, overrideRegistry: _baseUrl + "/");
        Assert.Equal(version, source.Reference);
        var skillMd = Path.Combine(source.RootPath, "package", "skills", "sample", "SKILL.md");
        Assert.True(File.Exists(skillMd), "expected SKILL.md at " + skillMd);

        // Subpath should point to the skills root, not the package root.
        Assert.Equal("package/skills", source.Subpath);
    }

    private static byte[] BuildTarGz(IEnumerable<(string Path, string Content)> entries)
    {
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Fastest, leaveOpen: true))
        using (var writer = new TarWriter(gz, TarEntryFormat.Pax, leaveOpen: false))
        {
            foreach (var (path, content) in entries)
            {
                var bytes = Encoding.UTF8.GetBytes(content);
                var entry = new PaxTarEntry(TarEntryType.RegularFile, path)
                {
                    DataStream = new MemoryStream(bytes),
                    Mode = UnixFileMode.UserRead | UnixFileMode.UserWrite,
                };
                writer.WriteEntry(entry);
            }
        }
        return ms.ToArray();
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

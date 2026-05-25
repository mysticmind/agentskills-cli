using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using AgentSkills.Sources;
using Xunit;

namespace AgentSkills.Tests;

/// <summary>
/// Verifies the local file-as-source paths through NuGetSource and NpmSource:
/// when a user passes a .nupkg or .tgz file directly (instead of a package id),
/// the source should extract the archive and populate package metadata from the
/// archive's own manifest (.nuspec / package.json) - so the lock entry looks
/// identical to a feed-resolved install.
/// </summary>
public sealed class LocalArchiveSourceTests : IDisposable
{
    private readonly string _workDir;

    public LocalArchiveSourceTests()
    {
        _workDir = Path.Combine(Path.GetTempPath(), $"agentskills-cli-local-archive-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_workDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_workDir, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task ExtractsLocalNuPkgAndPopulatesIdentityFromNuspec()
    {
        var nupkgPath = BuildNupkg(
            id: "Contoso.SampleSkills",
            version: "1.4.0",
            files: new[]
            {
                ("contentFiles/any/any/skills/sample/SKILL.md", "---\nname: sample\ndescription: s\n---\n# sample\n"),
            });

        var parsed = SourceParser.Parse(nupkgPath);
        Assert.Equal(SourceType.NuGet, parsed.Type);
        Assert.Equal(nupkgPath, parsed.LocalPath);

        using var source = await NuGetSource.DownloadAsync(parsed);

        // Package id / version come from the .nuspec inside the archive.
        Assert.Equal("Contoso.SampleSkills", source.Parsed.PackageId);
        Assert.Equal("1.4.0", source.Parsed.PackageVersion);
        Assert.Equal("1.4.0", source.Reference);

        // DisplaySource matches the feed-resolved form so list/remove behave identically.
        Assert.Equal("nuget:Contoso.SampleSkills@1.4.0", source.DisplaySource);

        // SourceUrl is a file:// URI so the lock entry records where it came from.
        Assert.StartsWith("file://", source.SourceUrl);

        // Conventional NuGet skills path detected as subpath.
        Assert.Equal(Path.Combine("contentFiles", "any", "any", "skills"), source.Subpath);

        var skillMd = Path.Combine(source.RootPath, source.Subpath!, "sample", "SKILL.md");
        Assert.True(File.Exists(skillMd), $"expected SKILL.md at {skillMd}");
    }

    [Fact]
    public async Task ExtractsLocalNpmTarballAndPopulatesIdentityFromPackageJson()
    {
        var tarPath = Path.Combine(_workDir, "contoso-sample-skills-2.1.0.tgz");
        File.WriteAllBytes(tarPath, BuildTarGz(new[]
        {
            ("package/package.json", "{\"name\":\"@contoso/sample-skills\",\"version\":\"2.1.0\"}"),
            ("package/skills/sample/SKILL.md", "---\nname: sample\ndescription: s\n---\n# sample\n"),
        }));

        var parsed = SourceParser.Parse(tarPath);
        Assert.Equal(SourceType.Npm, parsed.Type);
        Assert.Equal(tarPath, parsed.LocalPath);

        using var source = await NpmSource.DownloadAsync(parsed);

        Assert.Equal("@contoso/sample-skills", source.Parsed.PackageId);
        Assert.Equal("2.1.0", source.Parsed.PackageVersion);
        Assert.Equal("npm:@contoso/sample-skills@2.1.0", source.DisplaySource);
        Assert.StartsWith("file://", source.SourceUrl);
        Assert.Equal("package/skills", source.Subpath);

        var skillMd = Path.Combine(source.RootPath, "package", "skills", "sample", "SKILL.md");
        Assert.True(File.Exists(skillMd), $"expected SKILL.md at {skillMd}");
    }

    [Fact]
    public async Task LocalNuPkgWithoutConventionalSkillsPath_SetsEmptySubpath()
    {
        var nupkgPath = BuildNupkg(
            id: "NoSkills.Pkg",
            version: "0.1.0",
            files: new[]
            {
                ("lib/net8.0/NoSkills.Pkg.dll", "fake content"),
            });

        var parsed = SourceParser.Parse(nupkgPath);
        using var source = await NuGetSource.DownloadAsync(parsed);

        Assert.Equal(string.Empty, source.Subpath);
    }

    [Fact]
    public async Task LocalTarballMissingPackageJson_FallsBackToFilename()
    {
        var tarPath = Path.Combine(_workDir, "weird-tarball.tgz");
        File.WriteAllBytes(tarPath, BuildTarGz(new[]
        {
            ("package/skills/x/SKILL.md", "---\nname: x\ndescription: s\n---\n# x\n"),
            // intentionally no package.json
        }));

        var parsed = SourceParser.Parse(tarPath);
        using var source = await NpmSource.DownloadAsync(parsed);

        // No package.json - falls back to the filename stem as the package id,
        // version stays null, but extraction still succeeds.
        Assert.Equal("weird-tarball", source.Parsed.PackageId);
        Assert.Null(source.Parsed.PackageVersion);
        Assert.Equal("package/skills", source.Subpath);
    }

    /// <summary>
    /// Builds a minimal valid .nupkg directly as a zip - a .nuspec at the root
    /// plus the content files at their literal paths. Avoids
    /// <c>PackageBuilder.AddFiles</c> which interprets the destination as a
    /// directory and reshapes the layout.
    /// </summary>
    private string BuildNupkg(string id, string version, IEnumerable<(string PackagePath, string Content)> files)
    {
        var nupkgPath = Path.Combine(_workDir, $"{id}.{version}.nupkg");

        using var fs = File.Create(nupkgPath);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);

        // .nuspec at the root - NuGet.Packaging.PackageArchiveReader.GetIdentity()
        // reads this to populate Id + Version.
        var nuspec = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
              <metadata>
                <id>{id}</id>
                <version>{version}</version>
                <authors>test</authors>
                <description>Test fixture</description>
              </metadata>
            </package>
            """;
        var nuspecEntry = zip.CreateEntry($"{id}.nuspec");
        using (var w = new StreamWriter(nuspecEntry.Open(), Encoding.UTF8))
        {
            w.Write(nuspec);
        }

        foreach (var (packagePath, content) in files)
        {
            var entry = zip.CreateEntry(packagePath);
            using var w = new StreamWriter(entry.Open(), Encoding.UTF8);
            w.Write(content);
        }

        return nupkgPath;
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
}

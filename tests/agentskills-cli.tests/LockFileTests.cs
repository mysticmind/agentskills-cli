using AgentSkills.Install;
using Xunit;

namespace AgentSkills.Tests;

[Collection(SerialTests.Name)]
public sealed class LockFileTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "agentskills-cli-lock-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> _warnings = [];
    private readonly Action<string> _previousWarn;

    public LockFileTests()
    {
        Directory.CreateDirectory(_dir);
        _previousWarn = LockFileIO.Warn;
        LockFileIO.Warn = _warnings.Add;
        LockFileIO.ResetBackupTracking();
    }

    public void Dispose()
    {
        LockFileIO.Warn = _previousWarn;
        try { Directory.Delete(_dir, recursive: true); } catch { /* ignore */ }
    }

    [Fact]
    public void CorruptProjectLock_IsBackedUpOnce_AndWarnsOnce()
    {
        var path = LocalLock.GetLockPath(_dir);
        File.WriteAllText(path, "{ not json");

        var first = LocalLock.Load(_dir);
        var second = LocalLock.Load(_dir);

        Assert.Empty(first.Skills);
        Assert.Empty(second.Skills);
        Assert.Single(_warnings);
        var backups = Directory.GetFiles(_dir, "skills-lock.json.corrupt-*");
        Assert.Single(backups);
        Assert.Equal("{ not json", File.ReadAllText(backups[0]));
    }

    [Fact]
    public void NewerProjectLock_IsReadOnly()
    {
        File.WriteAllText(LocalLock.GetLockPath(_dir), """{"version":9,"skills":{"a":{"source":"x","sourceType":"local","computedHash":"h"}}}""");

        var doc = LocalLock.Load(_dir);

        Assert.True(doc.Skills.ContainsKey("a"));
        Assert.NotNull(doc.ReadOnlyReason);
        Assert.Throws<LockFileException>(() => LocalLock.Save(doc, _dir));
        Assert.Contains("\"version\":9", File.ReadAllText(LocalLock.GetLockPath(_dir)), StringComparison.Ordinal);
    }

    [Fact]
    public void NewerGlobalLock_IsReadOnly()
    {
        var path = Path.Combine(_dir, ".skill-lock.json");
        File.WriteAllText(path, """{"version":4,"skills":{}}""");

        var doc = SkillLock.Load(path);

        Assert.NotNull(doc.ReadOnlyReason);
        Assert.Throws<LockFileException>(() => SkillLock.Save(doc, path));
    }

    [Fact]
    public void OlderGlobalLock_IsResetWithBackup()
    {
        var path = Path.Combine(_dir, ".skill-lock.json");
        File.WriteAllText(path, """{"version":2,"skills":{"old":{"source":"a/b","sourceType":"github"}}}""");

        var doc = SkillLock.Load(path);

        Assert.Empty(doc.Skills);
        Assert.Null(doc.ReadOnlyReason);
        Assert.Single(Directory.GetFiles(_dir, ".skill-lock.json.v2-*"));
        Assert.Single(_warnings);
    }

    [Fact]
    public void Save_RoundTrips_AndLeavesNoTempFiles()
    {
        var doc = new LocalLockDocument();
        doc.Skills["s"] = new LocalLockEntry { Source = "acme/x", SourceType = "github", ComputedHash = "abc" };

        LocalLock.Save(doc, _dir);
        var loaded = LocalLock.Load(_dir);

        Assert.Equal("acme/x", loaded.Skills["s"].Source);
        Assert.Single(Directory.GetFiles(_dir));
        Assert.Empty(_warnings);
    }
}

/// <summary>Tests that touch process-wide state (env vars, lock-file warning sink) run serially.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SerialTests
{
    public const string Name = "serial";
}

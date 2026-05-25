using AgentSkills.Sources;
using Xunit;

namespace AgentSkills.Tests;

public class GitHubApiTests
{
    [Fact]
    public void GetFolderHash_RootSkill_ReturnsTreeSha()
    {
        var tree = new RepoTree("rootsha", "main", []);
        Assert.Equal("rootsha", GitHubApi.GetFolderHashFromTree(tree, "SKILL.md"));
    }

    [Fact]
    public void GetFolderHash_NestedSkill_FindsFolderEntry()
    {
        var tree = new RepoTree("rootsha", "main",
        [
            new GitTreeEntry("skills", "tree", "abc"),
            new GitTreeEntry("skills/foo", "tree", "deadbeef"),
            new GitTreeEntry("skills/foo/SKILL.md", "blob", "ignored"),
        ]);

        Assert.Equal("deadbeef", GitHubApi.GetFolderHashFromTree(tree, "skills/foo/SKILL.md"));
    }

    [Fact]
    public void GetFolderHash_UnknownPath_ReturnsNull()
    {
        var tree = new RepoTree("rootsha", "main",
        [
            new GitTreeEntry("skills", "tree", "abc"),
        ]);

        Assert.Null(GitHubApi.GetFolderHashFromTree(tree, "missing/SKILL.md"));
    }
}

# Sample.SkillPackage

A trivial NuGet package showing the recommended layout for skills in a NuGet package: `skills/<name>/SKILL.md` at the package root. AgentSkills CLI and NuGet's `dotnet-package-skills` both read this layout. Pack with:

```bash
dotnet pack samples/sample-nuget-package -o ./local-feed
agentskills-cli add Sample.SkillPackage --nuget-source ./local-feed -a claude-code -y
```

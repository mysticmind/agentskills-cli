# Sample.SkillPackage

A trivial NuGet package showing the AgentSkills CLI NuGet layout. Pack with:

```bash
dotnet pack samples/sample-nuget-package -o ./local-feed
agentskills-cli add Sample.SkillPackage --nuget-source ./local-feed -a claude-code -y
```

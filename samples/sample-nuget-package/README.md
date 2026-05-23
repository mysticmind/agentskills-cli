# Sample.SkillPackage

A trivial NuGet package showing the skills-net NuGet layout. Pack with:

```bash
dotnet pack samples/sample-nuget-package -o ./local-feed
skills add Sample.SkillPackage --nuget-source ./local-feed -a claude-code -y
```

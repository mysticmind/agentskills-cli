# `version`

Show which version of AgentSkills CLI is running, plus the details a bug report needs.

## Synopsis

```
agentskills-cli version [--json]
agentskills-cli --version
```

`--version` prints just the version number (for scripts). `version` adds the runtime, OS, and where the lock files live.

## Example

```
$ agentskills-cli --version
1.1.0

$ agentskills-cli version
agentskills-cli 1.1.0 (1c6acad)
Runtime       .NET 10.0.1
Built for     .NETCoreApp,Version=v10.0
OS            macOS 26.6.2 (Arm64)
Global lock   /Users/you/.agents/.skill-lock.json
Project lock  /Users/you/src/app/skills-lock.json
```

- **Runtime** is the .NET runtime actually running the tool. It can be newer than **Built for**: the tool rolls forward to the newest installed major version, so on a machine with .NET 11 the `net10.0` build runs on .NET 11.
- The value in parentheses is the commit the package was built from.

`--json` prints the same fields as a JSON object.

`-v` is short for `--verbose`, not `--version`.

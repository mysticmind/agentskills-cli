# Local folder

Install skills from a directory on disk. The simplest source type - no network, no auth.

## Input shapes

```bash
agentskills-cli add .                       # current directory
agentskills-cli add ./my-skill              # relative
agentskills-cli add ../shared/skill         # parent-relative
agentskills-cli add /abs/path/to/skill      # absolute (POSIX)
agentskills-cli add C:\skills\my-skill      # absolute (Windows)
agentskills-cli add C:/skills/my-skill      # also Windows
```

## Detection rule

A source is treated as local if it:

- starts with `./`, `../`, `.\` or `..\`, OR
- is exactly `.` or `..`, OR
- is absolute (POSIX `/...` or Windows drive `C:\` / `C:/`)

## Layout

Skills are discovered using the same multi-pass strategy described in the [overview](/sources/). If your local source has skills directly at the root (`./my-skill/SKILL.md`), one folder under `./skills/`, or under any of the priority directories, no extra configuration is needed.

## When to use

- **During development** of a skill, before publishing
- **For project-private skills** that don't belong in a public registry
- **CI bootstrap** when the skill ships in the same repo as the code

For the published-distribution path, prefer [NuGet](/sources/nuget) or [npm](/sources/npm).

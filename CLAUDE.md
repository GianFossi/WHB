# CLAUDE.md

Claude Code entry point for this repository.

The canonical AI assistant memory, project conventions and modification
rules live in [`AGENTS.md`](./AGENTS.md). Read that file before making
any changes and follow it as authoritative.

Quick pointers:

- Solution: `WhbDesign.sln` (F# / .NET 8).
- Build & test: `.\build.ps1` (PowerShell) or `dotnet build` +
  `dotnet test` on the solution.
- CLI entry: `dotnet run --project src/Whb.Cli`.
- Self-test: `dotnet run --project src/Whb.Cli -- --selftest`.

When you make a non-obvious modification decision, append a short entry
to the "Modification Memory Log" section of `AGENTS.md` so future
sessions inherit the context.

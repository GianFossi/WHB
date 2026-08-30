# GitHub Copilot Instructions

These instructions are automatically loaded by GitHub Copilot for this
repository.

The canonical AI assistant memory, project conventions and modification
rules are maintained in [`.ai/AGENTS.md`](../.ai/AGENTS.md). Treat that
file as authoritative and follow it when suggesting or applying changes.
Every AI instruction file for this repository is in `.ai/`.

Key points:

- F# on .NET 10, solution `WhbDesign.sln`.
- Projects: `src/Whb.Core` (library), `src/Whb.Cli` (CLI),
  `tests/Whb.Tests` (xUnit).
- Build & test with `.\macro\build.ps1`; use `macro/test.ps1` as the official
  test frontend over raw `dotnet test`.
- Preserve engineering correlations, units, defaults and the reference
  case unless explicitly asked to change them.
- Keep edits minimal and surgical; do not refactor unrelated code, do
  not remove tests, do not drop safety disclaimers.
- F# file order matters: register any new `.fs` file in the correct
  position of its `.fsproj`.

Record notable modification decisions in the "Modification Memory Log"
section of `.ai/AGENTS.md`.

# Project Profile - WHB

Companion to `AI_ENGINEERING_PROJECT_STANDARD.md` (same folder). The standard
holds the rules; this file holds only what is specific to *this* project.

**Maintenance rule:** if a line here merely repeats a rule of the standard,
delete it. If this project cannot satisfy a rule, do not edit the standard -
add a row to "Declared exceptions" below.

**Applied standard:** AI Engineering Project Standard **v2.0**.

---

## 1. Identity

| | |
|---|---|
| Product | WHB - Waste Heat Boiler / Process Gas Cooler |
| Purpose | thermal, hydraulic and diagnostic calculations for fire-tube WHB / PGC units |
| Repository | GianFossi/WHB |
| Solution | `WhbDesign.sln` |
| Entry point | `dotnet run --project src/Whb.Cli` (self-test: `-- --selftest`) |

## 2. Stack decisions (fills `ARCH-1`)

| Concern | Decision |
|---|---|
| Calculation core | F# |
| Target framework | **.NET 10 LTS**, single target, SDK pinned in `global.json` |
| Application shell | CLI (`src/Whb.Cli`) - JSON input, text/CSV reports |
| Desktop UI (future) | C# + WPF + MVVM + CommunityToolkit.Mvvm |
| Serialization | `System.Text.Json` |
| Tests | xUnit |
| Package versions | pinned per project in `.fsproj` (see `REL-3` exception) |

### Declared packages

| Package | Version | Where | Role |
|---|---|---|---|
| `Ganfoss.ROP` | 1.0.2 | `Whb.Core` | Railway-Oriented Programming helpers for the typed error path (`EXEC-2`) |
| `NumericInput.Core` | 1.3.0 | future UI application layer | numeric entry model, unit-agnostic |
| `NumericInput.WPF` | 1.3.0 | future `Whb.Desktop` | WPF numeric entry control |

Note on `PRIN-3`: `NumericInput.*` handles entry, formatting and format
validation only. No thermal correlation, acceptance limit or unit-system
business rule goes into it - those stay in `Whb.Core`.

## 3. Modules (fills `ARCH-4`)

`Whb.Core` is a single assembly whose folders carry the standard's module
boundaries:

| Standard module | Here |
|---|---|
| Domain / Calculations | `Components/`, `Process/`, `Loads/`, `Solvers/` |
| Materials | `Materials/` (gas properties, IF97 steam) |
| Sizing / Optimization | `Designers/`, `Optimizer/` |
| Configuration | `Options/` (`whb.options.json` merged onto defaults) |
| Reporting | `Reports/` |
| Application / Infrastructure | `src/Whb.Cli` (JSON loading, preflight, progress, logging) |

Not yet created: `Desktop`, `Validation` as a separate module, `registry/`.

## 4. Engineering scope

Fire-tube waste heat boiler / process gas cooler: gas-side and water-side
thermal-hydraulics, circulation, bypass control, steam drum and riser/downcomer
behaviour, IF97 steam properties.

Scope, theory, assumptions and limitations are documented in `docs/SCOPE.md`,
`docs/THEORY.md`, `docs/ASSUMPTIONS.md`, `docs/LIMITATIONS.md` and
`docs/VALIDATION.md`. Vendor datasheets and drawings backing the reference case
are held under `docs/PDS/`, `docs/WHB/`, `docs/STEAM DRUM/` and
`docs/RISERS & DOWNCOMERS/`.

## 5. Architecture baseline

No formal freeze declared yet. Until one is recorded, `ARCH-5` applies only to
its ADR requirement for structural changes; existing structural conventions are
those in `.ai/AGENTS.md` under "Repository Layout (canonical)".

## 6. Declared exceptions to the standard

Format: rule id, what is done instead, why, and when it will be resolved.

| Rule | Exception | Reason | Review |
|---|---|---|---|
| `ARCH-4` | one `Whb.Core` assembly instead of separate module projects | small codebase; boundaries kept as folders (see section 3) | if the UI or a second consumer is added |
| `REL-3` | package versions inline in `.fsproj`, no `Directory.Packages.props` | only five packages, all pinned to exact versions | when a UI project adds a second consumer |
| `PERS-2` | project input JSON carries no schema version | current inputs are hand-written engineering cases, not a persisted project format | before a UI or a saved project format exists |
| `DOC-1` | documentation tree is `docs/`, not `doc/` | pre-existing convention, also holds vendor PDFs | not planned |
| `DOC-2` | no `registry/`; rule metadata lives in prose under `docs/` | no normative code rules implemented yet, only engineering correlations | when a normative standard is implemented |
| `DOC-3` | the `AI.md` role is played by the "Modification Memory Log" section of `.ai/AGENTS.md` | single-file memory already established and referenced by the root pointers | not planned |

## 7. File map (fills `DOC-3`)

All AI instruction files live in `.ai/`. Everything outside it is either a
tool-discovery pointer or engineering documentation.

| File | Role here |
|---|---|
| `.ai/AGENTS.md` | canonical: repository conventions, build/test/run, layout, modification rules, and the Modification Memory Log (the `AI.md` role) |
| `.ai/AI_ENGINEERING_PROJECT_STANDARD.md` | the rules |
| `.ai/AI_STARTER_INSTRUCTIONS.md` | this file: project-specific choices and exceptions |
| `.ai/README.md` | index of the folder |
| `AGENTS.md`, `CLAUDE.md`, `.github/copilot-instructions.md` | root pointers to `.ai/AGENTS.md`, no independent rule text |
| `README.md` | capabilities, CLI usage, options |
| `TODO.md` | pending work |
| `docs/` | engineering scope, theory, assumptions, validation, vendor data |

## 8. Reading order for a new session

1. `.ai/AGENTS.md`
2. this profile
3. the Modification Memory Log at the end of `.ai/AGENTS.md`
4. the standard, for any rule referenced by id
5. `docs/` for the engineering topic being changed

## Changelog

- **2026-08-28** - Profile created when the AI Engineering Project Standard v2.0
  was adopted in this repository. Same day: solution retargeted from `net8.0` to
  `net10.0`, SDK pinned in `global.json`, CI moved to `10.0.x`. 66/66 tests pass
  and `--selftest` output is byte-identical to the `net8.0` build of the same
  source.
- **2026-08-28** - All AI instruction files consolidated under `.ai/`; this
  file moved from `docs/`. Stale BismarckGame copies removed from `.ai/`.

# AI Engineering Project Standard

**Version:** 2.3
**Status:** normative baseline for engineering / scientific software projects.
**Scope:** project-independent. Every project-specific choice (framework version,
packages, vendor, repository, current phase) lives in the project profile
(`AI_STARTER_INSTRUCTIONS.md`), never in this document.

---

## How to maintain this document

- **One concept, one place.** A rule is stated once and referenced elsewhere by
  its id (for example `PERS-3`). Never restate a rule in another section, in the
  project profile, in `AGENTS.md`, or in `AI.md`.
- **Stable ids.** Rule ids are permanent. A removed rule is marked
  `(withdrawn in vX.Y)` and its id is never reused.
- **Shared shapes are centralised.** Data shapes and status enumerations are
  defined once in Appendix A; reference layouts once in Appendix B.
- **No project data here.** No product name, framework version, package,
  license, repository or schedule.
- **Deviations are declared, not forked.** A project that cannot satisfy a rule
  records it under "Declared exceptions" in its profile, citing the rule id.
- **Editing.** Adding or changing a rule requires a version bump and a line in
  the changelog at the end of this file.

Rule id prefixes: `PRIN` principles, `ARCH` architecture, `RULE` engineering
rules, `DATA` configuration and data sources, `PERS` persistence, `EXEC`
execution, `OUT` output, `VER` verification, `REL` delivery, `DOC`
documentation.

---

## Part 0 - Principles (PRIN)

**PRIN-0 (guiding principle).** A calculation result must be explainable,
testable, reproducible, traceable to its inputs and rules, and insulated from
unrelated changes in the UI or the application environment. Every other rule in
this document exists to serve this one.

**PRIN-1 (separation of concerns).** Keep these seven concerns in separate
modules: engineering/domain logic, application orchestration, persistence and
external infrastructure, user interface, reporting, validation and
qualification, release and reproducibility.

**PRIN-2 (purity and statelessness).** The calculation core is pure by default:
each operation receives its input explicitly and returns a new output without
mutating external state, hidden shared state, or the arguments it was passed.
Any unavoidable side effect stays at an application or infrastructure boundary
and is never mixed into domain calculations.

**PRIN-3 (calculations stay in the core).** Engineering formulas, normative
checks and acceptance limits exist only in the domain/calculation modules. UI,
ViewModels, numeric input controls, persistence, reporting and the optimizer
consume results; they never compute or re-compute them.

**PRIN-4 (cohesion).** Split code by responsibility and engineering concept.
Avoid catch-all files such as `Calculations.*`, `Utils.*`, `Models.*`. About
150-300 lines per file is a useful target, but cohesion outranks line count.

**PRIN-5 (explicitness).** Prefer explicit states, typed data and deterministic
behaviour over implicit conventions.

**PRIN-6 (no invention).** Never invent normative formulas, clauses, limits or
interpretations. When implementing a standards-based rule, verify the source and
record it per `RULE-2`.

**PRIN-7 (isolation of defaults).** Changing an application or global default
must never alter the result of an existing project. See `DATA-2`.

---

## Part 1 - Architecture (ARCH)

**ARCH-1 (stack roles).** The calculation core is a strongly typed functional
core: units of measure for physical quantities where useful, domain types,
discriminated unions for technical states and alternatives, `Result`-style typed
errors, deterministic calculation paths. The desktop UI is MVVM, built on the
standard dependency injection, configuration and logging abstractions of the
platform. Concrete languages, framework versions and packages are declared in
the project profile, not here.

**ARCH-2 (boundary).** Never expose core implementation types directly to the UI
or to an external API. Cross boundaries through DTOs:

```
UI  ->  Application DTO  ->  mapping  ->  Domain core
                                             |
UI  <-  Result DTO       <-  mapping  <-  Calculation result
```

**ARCH-3 (distinct DTO families).** UI/API DTOs and persistence DTOs are
separate types and evolve independently.

**ARCH-4 (module layout).** Follow the reference solution layout in Appendix B.
Not every project needs every module; the boundaries survive even when modules
are omitted. Modules present and omitted are listed in the project profile.

**ARCH-5 (baseline and freeze).** Define an architecture baseline before major
implementation. After the freeze, implementation details evolve normally, but
structural changes require an explicit ADR under `doc/decisions/`, schema
changes require a migration design (`PERS-2`), normative changes require impact
analysis (`VER-5`), and public API breaks require a versioning review. The
baseline exists to prevent accidental drift, not improvement.

**ARCH-6 (top-down decomposition).** Design and implement from macro to atomic
level. First define the overall pipeline and the typed input/output contracts of
each stage. Then split each stage into isolated single-responsibility
subcomponents. Only then implement small, composable, internally consistent
operations. Domain calculation stages and I/O or orchestration stages remain
separate even when they live in the same assembly.

---

## Part 2 - Engineering rules and values (RULE)

**RULE-1 (location).** Engineering formulas live only in domain/calculation
modules; see `PRIN-3`.

**RULE-2 (rule identity).** Every important technical rule carries stable
metadata: `EngineeringRule` (Appendix A). Engineering interpretations are never
hidden in code comments.

**RULE-3 (structured checks).** A technical check returns `CheckResult`
(Appendix A), not a boolean.

**RULE-4 (interpretation registry).** Non-trivial interpretations of a standard
are recorded in a versioned interpretation registry (`DOC-2`), with the rule ids
they affect.

**RULE-5 (units).** SI is the canonical internal system unless the domain
requires otherwise. The UI may offer other unit systems. Persist canonical
value, original user input, original unit and provenance: `EngineeringValue`
(Appendix A).

**RULE-6 (rounding).** Presentation rounding never alters the value used in a
calculation. Rounding required by a standard is an explicit engineering rule
under `RULE-2`, not a formatting concern.

---

## Part 3 - Configuration and data sources (DATA)

**DATA-1 (three tiers).** These are distinct and must not be merged:

| Tier | Answers | Examples |
|---|---|---|
| Software settings | how the application behaves | language, theme, window state, folders, logging, autosave, recent files, database locations |
| Calculation defaults | what a *new* project starts from | default standards, solver defaults, optimization defaults, acceptance defaults, manufacturing defaults, material policies, reporting defaults |
| Project engineering data | what *this* calculation used | geometry, loads, materials, options, selected standards and editions, solver settings, acceptance criteria, source snapshots, audit data |

Software settings must never change an existing engineering result.

**DATA-2 (defaults are copied).** On project creation the relevant calculation
defaults are copied into the project. Later edits to global defaults do not
propagate (`PRIN-7`).

**DATA-3 (configuration module).** Configuration is its own module, organised by
the areas in Appendix B, not scattered across the application.

**DATA-4 (recent files).** Keep at most 20 non-pinned recent files; pinned
entries are exempt from automatic eviction. Shape: `RecentFileEntry`
(Appendix A).

**DATA-5 (database locations).** External database locations are configurable
and never hard-coded in calculation modules. Shape: `DatabaseLocation`
(Appendix A). Typical locations: materials, fasteners, threads, gaskets, tools,
standards-derived datasets, manufacturer data, reference cases, project and
custom databases.

**DATA-6 (snapshot and fingerprint).** External data consumed by an official
result must be snapshot-able and fingerprinted (`PERS-4`), and referenced from
the release manifest (`REL-5`).

---

## Part 4 - Persistence and data integrity (PERS)

**PERS-1 (format and mapping).** JSON via the standard serializer of the
platform is the default. Persistence DTOs are separate from domain types
(`ARCH-3`):

```
write:  domain model -> persistence DTO -> JSON file
read:   JSON file -> persistence DTO -> migration -> current DTO -> validation -> domain model
```

**PERS-2 (schema version and migration).** Every persisted technical format
carries a schema version. Migrations are explicit and tested, one step at a time
(`schema N -> schema N+1`). Never best-effort convert an unknown future schema.

**PERS-3 (atomic save).** Important files are written atomically:

```
write temporary file -> flush -> validate -> atomic replace -> final file
```

with a documented backup retention policy. This applies to project files,
results, settings, registries, snapshots, manifests and audit data. Autosave
writes a separate file and never overwrites the official saved project.

**PERS-4 (canonical form for fingerprints).** Fingerprint serialization is a
separate concern from normal serialization: deterministic field ordering,
deterministic collection ordering where semantics allow, invariant numeric
representation, canonical SI values, no presentation rounding, SHA-256 by
default.

**PERS-5 (scoped invalidation).** A change to an input or dependency invalidates
only the affected calculation nodes. Use an explicit dependency graph rather
than global invalidation.

---

## Part 5 - Execution (EXEC)

**EXEC-1 (status families).** Keep calculation definition, execution state,
engineering assessment and UI progress separate. Never collapse
`ExecutionStatus`, `AssessmentStatus`, `QualificationStatus` and `Severity`
(Appendix A) into a single status.

**EXEC-2 (errors).** Expected technical failures are returned as typed errors.
Exceptions are reserved for unexpected, programming or infrastructure failures.

**EXEC-3 (cancellation and partial output).** Cancellation is cooperative.
Partial output must never be presentable as a final accepted assessment.

**EXEC-4 (solver configuration).** Numerical solver settings that can affect a
result are explicit and persisted with the project: relative tolerance, absolute
tolerances, maximum iterations, damping, subdivision limits, deterministic math
mode. Solver defaults are numerical implementation defaults, not normative
engineering limits. A deterministic strict mode must exist for validation and
qualified release.

**EXEC-5 (optimization).** The optimizer never duplicates engineering formulas
(`PRIN-3`); it drives the check engine:

```
candidate generator -> check engine -> feasibility -> objective / ranking -> final full recheck
```

It supports fixed, bounded continuous, bounded discrete and standard-series
variables. Manufacturing rounding is applied *before* the final full
verification: never accept a geometry because an unrounded mathematical
candidate passed.

**EXEC-6 (messages).** The core emits stable `MessageCode` identifiers with
typed parameters; human-readable text lives in a message catalog. Fallback
order: requested language, English, generic `MessageCode`. CI must detect a
missing required message in a qualified build.

---

## Part 6 - Output: reporting and audit (OUT)

**OUT-1 (reporting is downstream).** The report layer consumes the structured
result model and never recalculates engineering values (`PRIN-3`).

**OUT-2 (report levels).** Provide at least a summary report and a detailed
report. A detailed engineering report shows, where appropriate: formula,
symbols, substitutions, result, allowable/limit, utilization, pass/fail,
governing case, source standard, edition, clause.

**OUT-3 (audit trail).** Important project changes are traceable through
append-oriented records: actor, timestamp, previous value, new value, reason
where relevant, provenance. High-assurance projects use a hash-chained trail; an
official release freezes the audit state and references its final hash
(`REL-5`).

---

## Part 7 - Verification (VER)

**VER-1 (test layers).** Every project runs a test campaign from the start:

1. **Unit** - pure mathematical and domain behaviour.
2. **Clause/rule** - one test set per normative or engineering rule id.
3. **Reference** - independent published or reference examples.
4. **Regression** - protects previously accepted results.
5. **Integration** - persistence, repositories, application boundaries, UI
   services, external providers.
6. **Benchmarks** - critical calculation and optimization workloads.

Research public references where legally and technically appropriate
(`PRIN-6`).

**VER-2 (reference cases).** A reference case is described by `ReferenceCase`
(Appendix A) and stored as data, not as ad-hoc test code.

**VER-3 (qualification granularity).** Qualification is granular by module,
standard, edition and engine version. A new standard edition never inherits the
qualification of a previous edition. Lifecycle states: `QualificationStatus`
(Appendix A).

**VER-4 (evidence).** Validation evidence is immutable and versioned. Passing
unit tests never justifies marking a module `Qualified`.

**VER-5 (change impact).** Classify every change and map it to the required
activity:

| Class | Required |
|---|---|
| `DocumentationOnly` | documentation review |
| `UIOnly` | UI and integration tests |
| `NonNormativeCode` | unit + regression |
| `NumericalImplementation` | unit + regression + reference, deterministic mode |
| `NormativeLogic` | clause + reference + regression, revalidation, `VER-3` |
| `DataSourceChange` | snapshot and fingerprint diff (`DATA-6`), regression |

The last three trigger the stronger CI gates of `REL-2`.

---

## Part 8 - Delivery (REL)

**REL-1 (source control).** Git from the first implementation commit. Protected
`main`, feature branches, pull requests, focused logical commits. Architectural
changes carry an ADR (`ARCH-5`).

**REL-2 (build profiles and gates).** Promotion is sequential:

| Transition | Gate |
|---|---|
| Development -> Test | build + unit tests |
| Test -> Validation | clause + regression + reference tests |
| Validation -> Release | packaging + SBOM + security + license checks |
| Release -> QualifiedRelease | qualification evidence + deterministic mode (`EXEC-4`) + release verification |

**REL-3 (dependencies).** Centralise package versions in a single versions file.
No floating or wildcard versions. Prerelease dependencies only for development
unless explicitly approved. Maintain a dependency policy and a license policy;
record the resolved graph in the release SBOM.

**REL-4 (versioning).** Semantic Versioning. Keep software version, release
channel and technical qualification separate (`ReleaseChannel`, Appendix A).
Normative changes that may affect results are documented explicitly; breaking
engineering changes require a version increment and revalidation.

**REL-5 (release bundle).** An official release is reproducible and auditable
and ships the bundle in Appendix B, indexed by `ReleaseManifest` (Appendix A),
with a CycloneDX JSON SBOM and SHA-256 checksums. Architect for artifact signing
even if signing is not mandatory in the first version.

**REL-6 (reproducibility).** An official result must be reproducible from
project input, software version, calculation configuration, standard editions,
external-data snapshots, dependency graph, feature state and numerical mode.
Verification result: `ReproducibilityStatus` (Appendix A).

**REL-7 (qualified scope).** A qualified release must not contain unqualified
functionality inside its declared qualified scope.

**REL-8 (feature lifecycle).** Keep feature implementation, feature activation,
project technical option and qualification separate. A feature flag means a
capability exists; a technical option means the user selected an engineering
configuration. A feature flag must never silently select a normative
interpretation. Features are versioned in the feature registry (`DOC-2`) with
explicit dependencies.

---

## Part 9 - Documentation and AI usage (DOC)

**DOC-1 (documentation is implementation).** Maintain `README.md` and a `doc/`
tree (Appendix B). Documentation is updated together with the change that
motivates it, not postponed to the end of the project.

**DOC-2 (registries).** JSON registries are the authoritative machine-readable
source; Markdown documentation is generated from them (Appendix B). CI detects
inconsistency between registries, code, tests and generated documentation.

**DOC-3 (instruction files, no overlap).**

| File | Contains | Never contains |
|---|---|---|
| this standard | project-independent rules | project names, versions, packages, status |
| project profile (`AI_STARTER_INSTRUCTIONS.md`) | stack choices, scope, declared exceptions, file map | copies of standard rules |
| `AGENTS.md` | entry point: what to read and in what order | rule text that already exists in the standard |
| `AI.md` | append-only project memory: state, decisions, pending work | rules, stack tables, anything stable |

`CLAUDE.md`, `GEMINI.md`, `.github/copilot-instructions.md` and equivalents are
one-screen pointers to `AGENTS.md`. They never carry independent rule text.

**DOC-4 (AI working loop).** An assistant working on a project under this
standard:

1. reads `AGENTS.md`, then the project profile, `AI.md`, and the ADRs and
   registries relevant to the change;
2. respects module boundaries (`PRIN-1`, `PRIN-3`) and does not invent normative
   content (`PRIN-6`), while preserving purity (`PRIN-2`) and top-down
   decomposition (`ARCH-6`);
3. adds or updates tests with every calculation change (`VER-1`);
4. updates `README.md`, `doc/` and the registries the change touches;
5. appends material changes and pending work to `AI.md`;
6. records any conflict with this standard as a declared exception in the
   profile rather than silently deviating.

**DOC-5 (code documentation, English, XML doc).** Public APIs and important
code elements carry English documentation comments using the platform's native
documentation format (for example XML documentation comments in .NET code).
The goal is technical clarity, not boilerplate repetition: explain purpose,
inputs, outputs, units, assumptions, invariants, boundary conditions and any
non-obvious engineering meaning. Do not generate trivial comments that merely
restates the code line by line.

**DOC-6 (editor tasks).** Provide reproducible command-line tasks from project
start: restore, build, clean, run, debug, unit tests, full suite, validation
tests, benchmarks where relevant, formatting, packaging, release verification.
Prefer command-line tasks over IDE-only operations.

**DOC-7 (architecture-first task solving).** When an assistant is asked to
design or implement a technical change, it follows this hierarchy:

1. macro level - define the end-to-end pipeline, data contracts, types and
   function signatures;
2. sub-task level - decompose each macro block into isolated,
   single-responsibility components;
3. atomic level - implement only simple, composable, self-consistent
   operations.

The assistant closes with a critical self-review that checks, at minimum:

- absence of hidden mutation or implicit side effects;
- correct handling of edge cases and error propagation;
- type conformity across the pipeline;
- clarity and composability of the atomic functions.

**DOC-8 (structure before implementation).** For any non-trivial design or
implementation task, first define only the task hierarchy and the typed
pipeline contracts before writing the internal algorithm.

Required first pass:

- task hierarchy only;
- pipeline signature and data flow (for example
  `Input -> StepA -> StepB -> Result`);
- function signatures and intermediate types;
- explicit separation between pure/domain stages and orchestration or I/O
  stages.

Do not implement the internal logic until that structure has been reviewed or
established.

Required second pass:

- implement the atomic functions;
- compose them into the approved pipeline;
- perform the final validation checklist (`DOC-7`).

**DOC-9 (inline explanation of non-obvious code).** Inside functions,
subroutines and calculation pipelines, add concise English inline comments
where they materially improve understanding. Use them to explain non-obvious
steps, engineering intent, unit-sensitive transformations, numerical guards,
algorithmic choices, invariants and error-propagation decisions. Do not turn
the source into prose and do not comment every obvious assignment.

---

## Appendix A - Canonical shapes and enumerations

Defined once here; referenced by name from the rules above.

### Status families (`EXEC-1`)

```
ExecutionStatus        NotStarted | Running | Cancelled | Failed | Completed
AssessmentStatus       NotAssessed | Pass | Warning | Fail | Inconclusive
QualificationStatus    Implemented | PartiallyImplemented | Validated |
                       Qualified | Deprecated
Severity               Info | Notice | Warning | Error | Critical
ReleaseChannel         Development | Preview | Validated | Qualified | Deprecated
ReproducibilityStatus  Reproducible | ReproducibleWithWarnings |
                       NotReproducible | NotVerifiable
ChangeImpactClass      DocumentationOnly | UIOnly | NonNormativeCode |
                       NumericalImplementation | NormativeLogic |
                       DataSourceChange
```

### Engineering shapes

```
EngineeringRule    RuleId, Name, Module, Source, Standard, Edition, Clause,
                   FormulaReference, QualificationStatus, ValidationEvidence[]

CheckResult        CheckId, Status, Severity, Actual, Limit, Utilization,
                   GoverningCase, MessageCode, Standard, Edition, Clause,
                   Inputs, IntermediateValues

EngineeringValue   EngineeringValueId, CanonicalValue, OriginalInput, Unit,
                   Source, Provenance

ReferenceCase      Id, Description, Standard, Edition, Clause, Source,
                   Inputs, ExpectedResults, Tolerances, Notes
```

### Application shapes

```
RecentFileEntry    Path, DisplayName, LastOpenedAt, LastSavedAt?, FileExists,
                   SchemaVersion?, Pinned

DatabaseLocation   Id, Name, Path, Enabled, ReadOnly, Priority,
                   LastAccessedAt, Fingerprint?

ReleaseManifest    ReleaseId, Version, EngineVersion, SchemaVersion,
                   StandardEditions[], QualificationStatus, ProjectFingerprint,
                   ResultsFingerprint, AuditChainFinalHash,
                   ExternalDataSnapshots[], ValidationEvidenceRefs[],
                   FeatureSnapshot, SecurityScan, LicensePolicyVersion,
                   SbomFingerprint, ReleaseSignature?, CreatedAt
```

`ReleaseManifest` is the authoritative index of an official release.

---

## Appendix B - Reference layouts

### Solution (`ARCH-4`)

```
src/    <Project>.Domain, .Geometry, .Loads, .Materials, .Calculations,
        .Validation, .Optimization, .Sizing, .Results, .Reporting,
        .Configuration, .Application, .Infrastructure, .Desktop
tests/  .UnitTests, .ClauseTests, .ReferenceTests, .RegressionTests,
        .IntegrationTests, .Benchmarks
doc/    architecture, calculations, validation, standards, decisions, generated
registry/
.vscode/   .github/
```

### Configuration module (`DATA-3`)

```
Application, UserPreferences, Calculation, Optimization, Standards, Paths,
RecentFiles, Validation, Serialization
```

Application settings hierarchy: `General, Window, Files, Databases,
ExternalRepositories, RecentFiles, Logging, Updates`.

### Registries and generated documentation (`DOC-2`)

```
registry/       engineering-values.json, engineering-rules.json, features.json,
                normative-interpretations.json, qualification.json,
                standards-support.json,
                policies/license-policy.json, policies/dependency-policy.json
doc/generated/  engineering-values.md, engineering-rules.md, features.md,
                standards-support-matrix.md, normative-interpretations.md
```

### Release bundle (`REL-5`)

```
release/  manifest.json, qualification.json, sbom.cdx.json,
          release-notes.json, RELEASE_NOTES.md, checksums.sha256,
          validation-evidence/, external-data/,
          project/ and results/ and report.pdf where applicable
```

### Bootstrap checklist

A new project starts with `README.md`, `AGENTS.md`, `AI.md`,
`AI_STARTER_INSTRUCTIONS.md` (profile), `CHANGELOG.md`, a central package
versions file, an SDK pin, `.editorconfig`, and the
`src/ tests/ doc/ registry/ .vscode/ .github/` trees.

Before the first functional release, verify: clean-checkout build; all tests
pass; persistence round-trip and migration tests pass; reports generate;
external-data provenance preserved; SBOM generated; security and license gates
run; release manifest and checksums generated; the release reproduces
(`REL-6`).

---

## Changelog

- **2.3** - Expanded `DOC-5` to require English documentation comments in the
  native code documentation format with emphasis on technical meaning rather
  than boilerplate, and added `DOC-9` to require concise inline comments for
  non-obvious code paths, assumptions, units, invariants and numerical guards.
- **2.2** - Added `DOC-8` to require a structure-first pass for non-trivial
  tasks: task hierarchy, typed pipeline, intermediate contracts and boundary
  separation before internal algorithm implementation.
- **2.1** - Strengthened `PRIN-2` from a preference into an explicit
  pure/stateless default for the calculation core, added `ARCH-6` for top-down
  decomposition with explicit contracts, and added `DOC-7` to require an
  architecture-first task-solving flow with a final validation checklist.
- **2.0** - Restructured into ten numbered parts with stable rule ids. Merged
  the duplicated configuration sections (old 5-7), persistence (8-10), release
  (27-33) and documentation/AI (23, 24, 35, 36). Status enumerations and data
  shapes, previously repeated across several sections, centralised in
  Appendix A; directory layouts in Appendix B. Removed project-specific content
  (framework version, vendor packages) in favour of the project profile, and
  added the declared-exception mechanism so a project never forks this document.
- **1.0** - Initial standard, distilled from the OptimizedFlange architecture.

# `.ai/`

Every AI instruction file for this repository lives here. The files at the
repository root (`AGENTS.md`, `CLAUDE.md`) and in `.github/` are pointers
kept where each tool discovers them; they contain no rules.

| File | Role |
|---|---|
| `AGENTS.md` | canonical project memory: conventions, build/test/run, repository layout, modification rules, Modification Memory Log |
| `AI_STARTER_INSTRUCTIONS.md` | WHB project profile: stack decisions, module map, declared exceptions to the standard |
| `AI_ENGINEERING_PROJECT_STANDARD.md` | project-independent engineering software standard v2.0, cited by stable rule id |

Reading order for a new session: `AGENTS.md`, then the profile, then the
Modification Memory Log, then the standard for any rule referenced by id,
then `docs/` for the engineering topic being changed.

Engineering documentation (scope, theory, assumptions, validation, vendor
datasheets) stays in `docs/` and is not duplicated here.

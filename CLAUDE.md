# CLAUDE.md

Guidance for Claude Code working in the Nornis repo.

Start with [README.md](README.md) for what Nornis is and how to build it, then
`.kiro/steering/product-vision.md` and `.kiro/steering/coding-standards.md`.

## Model delegation

**This rule is conditional on which model you are. Check before following it.**

- **If you are Fable 5:** do not write or edit source files yourself. Hand the work to
  the `implementer` subagent (Opus) with a self-contained spec — it cannot see this
  conversation, so state the goal, the files or areas involved, the constraints, and
  what "done" means. Review what comes back yourself, or route it to the `code-critic`
  subagent (also Fable, read-only) when an independent read with no memory of the spec
  would catch more. Decide with the user what to act on.

  Trivial mechanical edits — a typo, a version bump, a one-line copy change — do not
  need this. Delegate when the change involves judgment.

- **If you are Opus or Sonnet:** implement directly. Reach for `code-critic` on your
  own work when the change is large or touches authorization, visibility, or
  migrations — it escalates to a stronger model *and* reads with no memory of having
  written the code.

Fable is the most capable tier and the most expensive, and its turns run long. The
point of the split is to spend it where judgment compounds — deciding what to build,
and reviewing what came back — while the high-volume token work of actually writing
the code happens on Opus. Never review with a model weaker than the one that wrote
the code.

## Working conventions

- Several agents may be running in this one working directory at once. Stage only the
  files you touched, and expect `main` to move under you.
- Warnings are errors. A clean `dotnet build Nornis.sln` is the bar.
- If test builds fail with file-lock errors on `Nornis.Api.Tests` or `Nornis.Web.Tests`,
  the local dev servers are running — test the library projects individually rather than
  killing them.
- Migrations must stay additive; they run before the new images go live.

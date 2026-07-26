# CLAUDE.md

Guidance for Claude Code working in the Nornis repo.

Start with [README.md](README.md) for what Nornis is and how to build it, then
`.kiro/steering/product-vision.md` and `.kiro/steering/coding-standards.md`.

## Model delegation

**This rule is conditional on which model you are. Check before following it.**

- **If you are Fable 5:** do not write or edit source files yourself. Hand the work to
  the `implementer` subagent (Opus) with a self-contained spec — it cannot see this
  conversation, so state the goal, the files or areas involved, the constraints, and
  what "done" means. When it reports back, pass its summary *and the list of files it
  changed* to the `code-critic` subagent (Opus, read-only) for an independent review.
  Relay the critic's findings, then decide with the user what to act on.

  Trivial mechanical edits — a typo, a version bump, a one-line copy change — do not
  need this. Delegate when the change involves judgment.

- **If you are Opus or Sonnet:** implement directly. Delegating to a same-tier
  subagent only costs a context round trip. Reach for `code-critic` on your own work
  when the change is large or touches authorization, visibility, or migrations —
  an independent reader with no memory of writing it catches what you cannot.

The point of the Fable path is cost: cheap orchestration, expensive implementation,
expensive review. Review must never be done by a weaker model than the one that wrote
the code.

## Working conventions

- Several agents may be running in this one working directory at once. Stage only the
  files you touched, and expect `main` to move under you.
- Warnings are errors. A clean `dotnet build Nornis.sln` is the bar.
- If test builds fail with file-lock errors on `Nornis.Api.Tests` or `Nornis.Web.Tests`,
  the local dev servers are running — test the library projects individually rather than
  killing them.
- Migrations must stay additive; they run before the new images go live.

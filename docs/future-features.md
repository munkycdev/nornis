# Execution order

It would be nice to be able to mark a Character article as a PC, even if the actual player doesn't use Nornis for whatever reason.

## The spec files

Each plan lives whole in its own file. They are specs, not authorization — a session
implements only what this file's sequence assigns it, and does not browse sibling
plans for inspiration:

- [plans/scrub-plan.md](plans/scrub-plan.md) — the nine-reviewer audit sweep (tiers 1-2 done; 3-5 open)
- [plans/test-quality.md](plans/test-quality.md) — coverage, CRAP, floors, authorization suite, audits
- [plans/system-status.md](plans/system-status.md) — /status endpoint, checks, worker heartbeat, status page
- [plans/operational-hardening.md](plans/operational-hardening.md) — O1-O6
- [plans/defect-remediation.md](plans/defect-remediation.md) — D1-D4 and the verified-sound record
- [plans/loremaster-wiki.md](plans/loremaster-wiki.md) — W1-W4 (all currently Fable-held)

# Test quality baseline

2026-08-03. First qualitative audit of the suite, run after scrub tiers 1–5 so that nothing
here grades a test that was already scheduled for deletion.

This is a **baseline**, which means its job is to be diffed against later, not to be acted on
all at once. Nothing in it is CI-enforced and nothing here should become a dashboard —
[test-quality.md](plans/test-quality.md) is explicit that this layer exists to surface
judgment.

**Scope.** 2,556 test methods across 378 files, six projects. Findings are scoped to the
seven priority areas in `.kiro/steering/testing-strategy.md`; everything outside them was
scanned for the Critical patterns and otherwise left alone.

**Method.** Pattern scans across the whole suite, then reading every hit. That distinction
matters, because most of the hits were wrong — see *False positives* below, which is
recorded so the next audit does not re-raise them.

---

## Verdict

**No Critical findings.** Every automated Critical-pattern hit was a false positive on
inspection. The suite does not contain assertion-free tests, swallowed exceptions,
always-true assertions outside the deliberate sanity tests, or broad `Assert.Throws<Exception>`.

| Severity | Count | Summary | State |
| -------- | ----- | ------- | ----- |
| Critical | 0     | — | — |
| High     | 1     | Four constructor tests assert only that an object was produced | **fixed 2026-08-03** |
| Medium   | 2     | Two background-service tests assert a negative through a wall-clock delay; assertion vocabulary is narrow | accepted, watch |
| Low      | 2     | 103 long tests, nearly all property tests; `Assert.Multiple` adoption is uneven | accepted |

Everything not marked fixed is **consciously accepted**, with the reasoning below. A baseline
whose findings are all "to do" is a backlog, not a baseline.

---

## High

### H1 — Four tests verify a constructor by asserting it returned an object

`tests/Nornis.Worker.Tests/Messaging/ServiceBusExtractionProcessorTests.cs:82, 99, 114, 131`

`Constructor_ConfiguresPeekLockMode_ProcessorCreatedSuccessfully`,
`Constructor_AcceptsMaxConcurrentCalls_FromOptions`,
`Constructor_AcceptsMaxAutoLockRenewalDuration_FromOptions`,
`Constructor_AcceptsDefaultWorkerOptions_Configuration`

Each constructs `ServiceBusExtractionProcessor` with a different parameter and asserts
`Assert.That(processor, Is.Not.Null)`. That is the whole assertion.
`Constructor_AcceptsMaxConcurrentCalls_FromOptions` passes identically whether the
implementation forwards `maxConcurrentCalls` to `ServiceBusProcessorOptions` or discards it,
which is the one thing its name claims to establish.

To the code's credit it says so plainly — *"The SDK does not expose the receive mode after
construction"* — so this is a known limit rather than an oversight, and the surrounding file
does contain real tests (the `ArgumentException` guard above them asserts a specific type).

**Fixed 2026-08-03**, since the remedy was smaller than the write-up. The four are now one
`[TestCase]`-driven test over the option sets the worker actually constructs. Same four cases
run, so nothing was lost — what went is four names claiming four verified options over one
unverifiable fact. The test says what it can prove: these argument sets are accepted.

The alternative considered and rejected: asserting a rejection instead, by constructing with
an invalid `maxConcurrentCalls`. That is real behaviour and the SDK does surface it — but the
file already has five `ArgumentException` guards above these, so it would have added a sixth
of the same shape rather than covering anything new.

---

## Medium

### M1 — Two background-service tests assert a negative across a wall-clock delay

`tests/Nornis.Api.Tests/BackgroundServices/ContinuityAuditBackgroundServiceTests.cs:72, 88`

Both start the service, `await Task.Delay(200)` / `Task.Delay(300)`, stop it, and assert that
nothing happened. They cost half a second of wall clock and can only ever prove "nothing
happened *within 200 ms*".

They are not wrong: each was written against a specific regression named in its own comment
(the `Math.Max(0.0, …)` floor that turned a configured `0` into a delay-free loop, and the
tick-before-first-delay that swept on every deploy), and both regressions would in fact be
caught. Rated Medium rather than Low only because the shape scales badly — a third such test
is another 300 ms, and the real fix is injecting a time abstraction into the service, which
is a change to production code and out of scope for a test audit.

### M2 — Assertion vocabulary is narrow at the top

Across 6,000-odd assertions, `Is.EqualTo` is 3,136 of them and `Is.True`/`Is.False` another
1,219. That is not wrong — most assertions really are equality — but `Is.True` in particular
hides what was compared: a failed `Assert.That(x.IsValid, Is.True)` reports "expected True,
was False" and nothing about why.

Not a finding to sweep. It is the thing to watch in the *next* audit: if `Is.True` grows
faster than the suite, diagnostics are degrading.

---

## Low

### L1 — 103 tests exceed 45 lines

Almost all are FsCheck property tests, where the length is generator setup rather than
branching logic, and the longest (`ProposalAcceptanceProperties.cs:473`,
`MergeArtifact_reassigns_and_archives`, 166 lines) seeds a merge with facts, relationships
and a self-referencing edge because the property genuinely needs all of it.

Recorded as a number to watch, not a backlog. Worth noting that a naive brace-counting
measure reports far worse — JSON in raw string literals (`{"name": null}`) breaks it — so any
future re-measure has to discount that or it will invent a regression.

### L2 — `Assert.Multiple` adoption is uneven

61 files use it; the rest assert serially, so a test with four assertions reports only the
first failure. Opportunistic, not a sweep.

---

## False positives — recorded so the next audit stops here

| Pattern flagged | Files | Why it is not a finding |
| --- | --- | --- |
| Assertion-free test | `EnumDefinitionTests.cs` ×15 | Delegate to `AssertEnumHasExactValues<TEnum>`, which holds the `Assert.That`. |
| Assertion-free test | `OptimisticConcurrencyTests.cs` ×6 | Body is `Task.Run(async …)` — but every one ends `.GetAwaiter().GetResult()`, so it blocks and propagates, and the assertion is inside `AssertConcurrencyConflict<T>`. |
| Wall-clock in assertion | 7 sites | All defensively bounded: `.Within(5s)`, `Is.LessThanOrEqualTo(UtcNow)`, `Is.LessThan(2s)` against a much longer backoff. None can flap on a slow runner. |
| Sleep for synchronisation | `AzureOpenAiEmbeddingClientTests.cs:34` | `Task.Delay(Timeout.Infinite, ct)` is the simulated hang the timeout test exists to trigger. |
| Always-true assertion | `SanityTests.cs` ×5 | One per project, deliberate: proves the test host and discovery work before anything else is believed. |
| Giant test | `ProposalValidatorTests.cs:63` | 7 lines. The measure was wrong, not the test. |

---

## Gaps against the priority areas

Coverage of the seven priorities is real; these are the edges.

| # | Priority area | State |
| - | ------------- | ----- |
| 1 | Authorization and visibility | Strongest area — 320 tagged cases, a dedicated suite, and a named CI step. **But nothing exercises real sign-in**: the dev-auth bypass means every one of them starts from an identity the test handed itself. Recorded as a rider in `future-features.md`. |
| 2 | Review proposal application | Well covered, including the property fixtures. The provenance stamp became structural on 2026-08-03, so the old eight-site duplication is no longer a drift risk. |
| 3 | AI parsing and validation | Good after the extraction-client rework — every parse failure now asserts the exception *names* the offending field. |
| 4 | Source state transitions | Covered, including the stale-Queued gate. |
| 5 | Artifact/fact/relationship mutations | Heaviest area by volume. |
| 6 | Cost ledger | Covered on both success and failure paths; the $0-pricing bypass has its own test. |
| 7 | World membership | Covered. The invite→selection persistence bug found on 2026-08-03 was a *Web* gap, not a membership one — worth remembering that the seams between these areas are where the last three real defects came from. |

**The pattern worth carrying forward**: the defects actually found this month were not
missing tests inside an area. They were rules living in two places (the Web recomputing the
continuity score, the merge batch and the unique index disagreeing about null), and behaviour
nothing exercised at all (real sign-in, a non-GUID route, the empty-rationale branch). Line
coverage cannot see any of those, which is exactly why this layer is not a percentage.

---

## What this baseline does not cover

Stated so its silence is not read as a clean bill:

- No per-test letter grades. `grade-tests` is designed for a curated set — the changed test
  files in a PR — and grading 2,556 methods would produce a number nobody would act on. The
  plan's per-PR use of it stands.
- No mutation testing. `test-gap-analysis` reasons about whether a test would catch a change;
  running it across the suite is a separate exercise.
- No `test-smell-detection` pass. That skill is the testsmells.org academic catalogue and is
  meant to be asked for by name; this audit used the pragmatic anti-pattern set instead.

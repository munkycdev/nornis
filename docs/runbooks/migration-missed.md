# Migration missed

## Symptom

The deploy run fails at **Verify the rollout**, naming `pending-migrations`. Or `/health`
returns **503** and names it, immediately after a deploy.

```bash
curl -s -w " [%{http_code}]\n" https://api.nornis.app/health
# {"status":"Unhealthy","failing":["pending-migrations"]} [503]
```

`nornis-availability` fires within about fifteen minutes: `ping-nornis-api-health` requests
this endpoint and expects 200, and a pending migration answers 503. The deploy run catches it
first if the rollout is what caused it; the alert is what catches it if anything else did.

This is the failure the pending-migrations check exists to catch. Without it, a deploy
whose migration step was skipped comes up looking fine and then 500s on the first request
that touches a missing table — usually found by a user, not by us.

## Why it happens

Migrations are applied **by hand, before** the deploy that needs them. Nothing in
`deploy.yml` runs them. A push that carries a migration and is not preceded by that step
produces exactly this.

## Diagnose

`/health` names the failing check but not the migrations — it is anonymous, so it names no
schema. Ask the database instead:

```bash
CONN=$(dotnet user-secrets list --project src/Nornis.Api \
  | grep '^ConnectionStrings:DefaultConnection' | cut -d' ' -f3-)

dotnet ef migrations list \
  --project src/Nornis.Infrastructure --startup-project src/Nornis.Api \
  --connection "$CONN"
```

Anything marked `(Pending)` is the answer. Never echo `$CONN` — it carries the SQL
password.

If nothing is pending, this is not your problem: `/health` is only ever Unhealthy for
pending migrations, so a 503 with a clean migration list means the check itself could not
reach the database. Go to [database-pressure.md](database-pressure.md).

## Remedy

Apply the migration. The old revision keeps serving throughout — migrations are additive
by policy, so the running code tolerates the new schema.

```bash
dotnet ef database update \
  --project src/Nornis.Infrastructure --startup-project src/Nornis.Api \
  --connection "$CONN"
```

The window between deploy and migration is genuinely an outage for anything touching the
new schema, and the readiness probe is what contains it: the new revision never goes ready,
so traffic stays on the old one.

## Verify

```bash
curl -s -w " [%{http_code}]\n" https://api.nornis.app/health
# {"status":"Healthy","failing":[]} [200]
```

Then re-run the failed deploy job — the rollout is not finished until its verify step
passes.

## Prevention

Apply migrations **before** pushing, not after. The sequence that avoids this entirely:

1. `dotnet ef migrations list --connection "$CONN"` — confirm what is pending
2. `dotnet ef database update --connection "$CONN"` — apply it
3. `curl https://api.nornis.app/health` — old code, new schema, still Healthy
4. Push

Step 3 is the one people skip and the one that proves the migration was additive.

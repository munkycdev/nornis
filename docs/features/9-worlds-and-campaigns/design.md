# Design Document

## Overview

Two phases, one branch:

1. **Mechanical rename** — `Campaign*` → `World*` across domain, persistence, API,
   worker, web, and tests, with a data-preserving rename migration.
2. **New modeling** — thin `Campaign` child entity, `Character` + `CampaignCharacter`,
   nullable `Source.CampaignId`, campaign context in extraction prompts, and management
   UI.

## Phase 1: Rename

### Identifier mapping (case-sensitive, applied in order)

```text
CampaignMember  → WorldMember      campaignMember → worldMember
CampaignRole    → WorldRole        campaignRole   → worldRole
CampaignId      → WorldId          campaignId     → worldId
Campaign        → World            campaign       → world
```

Applied to `src/`, `tests/`, and steering docs — but **not** to
`src/Nornis.Infrastructure/Migrations/` (applied migrations are history) and not to
historical feature folders `docs/features/1..8`.

Routes move from `/api/campaigns/...` to `/api/worlds/...`. The config key
`Ai:DailyCampaignBudgetUsd` becomes `Ai:DailyWorldBudgetUsd`.

### Rename migration

EF cannot diff a rename; the scaffolded migration would drop/create. Hand-write:

- `RenameTable`: `Campaigns` → `Worlds`, `CampaignMembers` → `WorldMembers`.
- `RenameColumn`: every `CampaignId` FK column → `WorldId` (Sources, Artifacts,
  ArtifactRelationships, ReviewBatches, AiUsageRecords, HealthAssessments,
  ContinuityFindings — verify against the model snapshot).
- `RenameIndex` + `sp_rename` for PK/FK constraint names so future scaffolded
  migrations reference the names EF expects.
- `Down` mirrors everything.

**Deploy note:** this migration is *not* additive. The deploy workflow applies
migrations before new containers go live, so the old revision errors briefly during
rollout. Accepted for the current single-user deployment; called out in the deploy
workflow comment.

## Phase 2: New Entities

```csharp
Campaign          // thin play-context, child of World
- Id, WorldId, Name, Description?, Status: CampaignStatus,
  StartedAt?, EndedAt?, CreatedAt, UpdatedAt, CreatedByUserId

CampaignStatus { Active, Completed, Archived }

Character
- Id, WorldId, WorldMemberId, Name, Description?, CreatedAt, UpdatedAt

CampaignCharacter
- Id, CampaignId, CharacterId, CreatedAt   // unique (CampaignId, CharacterId)

Source
+ CampaignId: Guid?    // FK → Campaigns, ON DELETE SET NULL
```

Delete behavior:

- World delete cascades to campaigns and characters (consistent with existing cascade
  posture from the world root).
- Campaign delete: `SET NULL` on `Source.CampaignId`; cascade `CampaignCharacter` rows.
- Character delete: cascade `CampaignCharacter` rows.
- WorldMember delete: cascades to their characters, which cascades their campaign
  assignments. (MVP posture: member removal is rare and GM-driven.)

`WorldMember.CharacterName` is dropped. Migration backfills: for each member with a
non-empty CharacterName, insert one Character (owned by that member) before dropping the
column.

Cross-world integrity (campaign of another world, character of another world) is
enforced in the application service layer, not by database constraints.

### API surface

```text
GET/POST           /api/worlds/{worldId}/campaigns
GET/PUT/DELETE     /api/worlds/{worldId}/campaigns/{campaignId}
GET/POST           /api/worlds/{worldId}/characters
GET/PUT/DELETE     /api/worlds/{worldId}/characters/{characterId}
PUT                /api/worlds/{worldId}/campaigns/{campaignId}/characters   // replace assignment set
```

- All endpoints require world membership (existing member filter).
- Mutations require GM, except characters: a member may create/update/delete their own;
  GM may manage all. Observers read-only.
- Sources create/update requests gain optional `campaignId`; responses include
  `campaignId` and `campaignName`.
- Sources list accepts `?campaignId=` filter (sentinel `none` for unassigned).

### Extraction context

`ExtractionWorker` loads the source's campaign (if any) and prepends a context line to
the prompt, e.g. `This source describes events from the campaign "Rise of Tiamat"
(Active, started 2026-01-10).` No schema change to the structured output.

### UI

- Global rename: World everywhere the root container is meant.
- World settings/home: campaign list (create/edit/archive/delete) and character
  management (per member; GM sees all).
- Capture/Source detail: optional campaign select, campaign chip on source cards and
  ledger, campaign filter on Sources view.

## Testing

- Existing suites follow the rename mechanically.
- New unit tests: campaign service (CRUD, cross-world rejection, delete semantics),
  character service (ownership rules, assignment uniqueness, cross-world rejection),
  source campaign validation, extraction prompt context.
- Authorization tests: non-member 403/404, observer mutation rejection, player managing
  own vs others' characters.
- Migration test coverage is not practical in unit tests; verified by applying to a
  local database.

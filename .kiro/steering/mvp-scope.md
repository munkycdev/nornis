# MVP Scope

## MVP Goal

Build the smallest useful version of Nornis that proves the core loop:

```text
Source → AI extraction → Review proposals → Artifacts/facts/relationships → Ask
```

## In Scope

### Worlds and Members

- Create world.
- Add world members.
- Roles: GM, Player, Observer.
- Auth0 authentication with Discord provider.
- Server-side world authorization.

### Campaigns and Characters

- Campaign CRUD within a world (thin: name, description, status, play dates).
- Characters owned by world members; any number per member.
- Assign characters to campaigns (many-to-many).
- Sources may declare an optional campaign.
- Campaigns add no permissions; world membership governs access.

### Sources

- Create text source.
- Source types at minimum:
  - SessionNote
  - JournalEntry
  - GMNote
  - WebLink placeholder
  - Image placeholder
  - HandwrittenNotes placeholder
- Store OccurredAt and CreatedAt.
- Optional campaign assignment.
- Visibility: Private, GMOnly, PartyVisible.

### Async Extraction

- Store source.
- Enqueue extraction job.
- Worker processes extraction.
- AI returns structured proposal output.
- Create review batch and proposals.

### Review Proposals

- Show review queue.
- Accept proposal.
- Reject proposal.
- Edit proposal if feasible for MVP; otherwise defer editing but design for it.
- One proposal per reviewable change.

### Artifacts

- Create/update artifacts through accepted proposals.
- Artifact types:
  - Character
  - Location
  - Item
  - Faction
  - Event
  - Storyline
  - Concept
- Browse artifacts.
- View artifact details.

### Storylines

- Storylines are artifacts with `Artifact.Type == Storyline`.
- Storylines represent narrative arcs, mysteries, quests, investigations, rivalries, prophecies, unresolved questions, and emerging threats.
- Browse active, dormant, resolved, and archived storylines.
- View storyline detail with summary, connected artifacts, open questions, source references, and status.

### Facts and Relationships

- Add facts through accepted proposals.
- Add relationships through accepted proposals.
- Show source references.
- Display truth state and visibility.

### Ask Loremaster

- Ask questions against accepted artifacts, facts, relationships, and source references.
- Respect visibility and world membership.
- Cite sources where possible.
- Say when information is unknown or unsupported.

### Cost Tracking

- Record AI usage for every AI operation.
- Show cost detail page.
- Show usage by date range, world, user, operation type, and model.

### Observability

- DataDog logs, traces, and metrics.
- AI metrics and extraction job metrics.

## Out of Scope

- Public anonymous sharing.
- Audio transcription.
- Sophisticated OCR pipeline.
- Full document parsing pipeline.
- Real-time collaborative editing.
- Advanced vector search unless needed for Ask.
- Complex canon conflict resolution.
- Autonomous canon updates.
- Mobile app.
- Native desktop app.
- RPG rules automation.
- Deep import from Chronicis.

## MVP UX Screens

Required:

- Login
- World list/home
- World home
- Capture source
- Source detail
- Review queue
- Artifact list
- Artifact detail
- Storylines view
- Ask Loremaster
- Costs
- Settings/basic member management

Optional:

- Canon view as read-only truth-state browser.
- Source ledger if not already covered by Source list/detail.

## MVP Success Criteria

A user can:

1. Sign in with Discord via Auth0.
2. Create or join a world.
3. Create a session note source.
4. See extraction status.
5. Review multiple proposals from that source.
6. Accept proposals to create/update artifacts, facts, and relationships.
7. Ask what is known about an artifact.
8. See supporting source references.
9. Review token and estimated dollar usage.

## MVP Sample Scenario

Input source:

```text
We questioned Captain Voss in Black Harbor. He denied knowing about the missing caravan, but Tavrin found the Silver Key in his quarters.
```

Expected proposals:

- Create/update artifact: Captain Voss.
- Create/update artifact: Black Harbor.
- Create/update artifact: Silver Key.
- Create/update storyline: Missing Caravan.
- Add relationship: Captain Voss connected to Black Harbor.
- Add relationship: Captain Voss connected to Missing Caravan.
- Add fact: Captain Voss denied knowing about Missing Caravan.
- Add fact: Silver Key found in Voss's quarters.

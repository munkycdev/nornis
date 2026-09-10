# Domain Model

> **Amendment (2026-08-09): the Character↔Artifact link is built, not future.**
> The Character section below closes by calling the link between a member's `Character` and the
> AI-extracted `Artifact` of type `Character` "a future feature, not MVP". It shipped:
> `Character.ArtifactId` is a nullable FK, `CharacterService.ValidateArtifactLinkAsync` guards
> it (same-world, `Type == Character`, and non-GMs refused `GMOnly` targets behind a single
> undifferentiated error so probing ids reveals nothing), and `ArtifactDetail.PlayedBy` reads it
> in the artifact→character direction.
>
> Null still means "not linked" and nothing more. The link is optional in both directions and
> carries no authorization of its own — owning a character that links to an artifact does not
> widen what its owner may see of that artifact.

> **Amendment (2026-09-10): characters belong to players, not memberships.** Feature 25
> adds `Player` — a person who plays in a world, with a name and an optional link to one
> `WorldMember` — and moves `Character.WorldMemberId` to `Character.PlayerId`. A player linked
> to a membership is that member; an unlinked player is someone at the table who is not on
> Nornis (Henry, who plays every week and chose not to sign up), and their characters exist
> anyway. Every membership has exactly one linked player from creation (`WorldMembership.Create`
> is the one place a membership is built; `MemberPlayerScanTests` holds it), and removing a
> member unlinks the player rather than deleting the characters. A player confers no rights:
> the member its player is linked to is a character's steward, and while there is none the GM
> stands in (`CharacterService.IsSteward`, the one predicate). The `WorldMember` and
> `Character` sections below predate this; read `WorldMemberId` there as `PlayerId`.
>
> ```csharp
> Player
> - Id: Guid
> - WorldId: Guid
> - WorldMemberId: Guid?     // null = not on Nornis, nothing else; unique per world when set
> - Name: string             // the GM's name for an unlinked player; unread while linked
> - CreatedAt, UpdatedAt
> ```

> **Amendment (2026-08-09): a world names the campaign it is playing now.**
> `World.CurrentCampaignId` — nullable, pointing at one of the world's own campaigns. The
> capture form defaults a new source to it, which is the whole reason it exists: the form
> used to infer "the only active campaign", which was correct for one campaign and gave up
> entirely for two — and giving up meant filing a session under no campaign at all.
>
> It lives on **World**, not as a flag on Campaign: "exactly one current per world" is then
> structural rather than an invariant a uniqueness filter has to defend. And it is a fact
> about the table rather than a per-user preference — a player filing session notes should
> file them into the run of play the GM is running — which is why it sits beside the world's
> other settings and not in anyone's localStorage.
>
> Two rules keep it honest, both owned by `CampaignService`, its only writer:
>
> - **Whatever it names is Active.** Setting a Completed or Archived campaign is refused, and
>   a current campaign that leaves Active clears the pointer. Readers therefore never have to
>   re-check the status of the campaign they are handed.
> - **A world adopts its first active campaign** without being asked, and a second campaign
>   does not steal the position. Worlds with several active campaigns and no choice made stay
>   null, which is exactly the case nothing could have inferred.
>
> Null means "no campaign is current", never "unknown": the capture form reads it as *No
> campaign* and does not guess, because a wrong campaign on a source is worse than none.
> Deleting the named campaign collapses into the same null — the foreign key is **Restrict**
> rather than SetNull, because Campaigns already cascade from Worlds and SQL Server refuses a
> second path between the same two tables, so `CampaignRepository.DeleteAsync` detaches the
> pointer alongside the sources and characters it already detaches.

> **Amendment (2026-08-09): campaigns are thin in data, first-tier in presentation.**
> The Campaign section below still reads as if a campaign were only a label on a source,
> and that framing produced a real gap: campaigns had no page, so nothing showed a GM what
> a run of play actually contained. A campaign is now somewhere to go — `/campaigns/{id}`,
> readable by every world member, editable only by a GM.
>
> Nothing in the *data* model loosened, and the "do not stamp campaign IDs onto artifacts
> or facts" rule below is the reason the page works the way it does: its cast, places and
> artifacts are derived from provenance at read time, so they cannot go stale. Two
> additions to the schema below:
>
> - `Campaign.SortOrder` — a GM-chosen display position. Governs the settings list, the
>   campaign index and the sources filter; deliberately **not** the storyline timeline,
>   whose campaign bands stay chronological. Zero means "this world has never been
>   reordered".
> - `CampaignRecap` — the generated "story so far", one replaceable row per campaign,
>   sibling to `WorldDigest` and a read-model for the same reason. The GM's *written*
>   intro stays on `Campaign.Description`; the two answer different questions. Two
>   renderings (GM and party) from separately-scoped generation passes, because the
>   campaign page is one a player opens directly.
>
> **`StorylineCampaign` is deleted.** A storyline's campaigns are now derived from the
> campaigns its dated sessions fall in, full stop — the timeline already unioned derived
> memberships with declared ones, so cross-campaign arcs were never relying on the
> declaration. What is genuinely gone: a GM can no longer place a storyline in a campaign
> *before* a session there references it, and a storyline touched only by undated sources
> now falls to the timeline's "no dated activity yet" list instead of being rescued into a
> band by a declaration. If forward-declaration is wanted back, it returns as an explicit
> *planned* concept, not as a membership that can contradict the sessions.

> **Amendment (2026-08-04):** two shapes below have drifted from the tree; the tree is
> the authority on both.
>
> - `ReviewBatch` carries a `Kind: string?` the schema below omits. Null means "the
>   source's own extraction batch" — the filtered unique index enforcing
>   one-extraction-batch-per-source keys on it — and every synthetic batch names its
>   producer with a constant from `ReviewBatchKinds` (SessionWrapUp, ArtifactMerge,
>   Reveal, ContinuityFix, StorylineRetrospective, RelationshipBackfill). Sweeps also use
>   it as their per-source idempotency key. Synthetic batches are written only through
>   `SyntheticBatchWriter`.
> - `ReviewChangeType` gained `AddPlacemark` (and `ReviewTargetType` resolution for it)
>   with map extraction; the enum list below predates maps.

> **Amendment (2026-09-09): `SourceType` has grown past the list below.** The tree adds
> `SessionAudio` (a pasted session transcript — no audio is stored or transcribed),
> `FanFiction`, `Map` (a map image whose extraction reads place names and positions into
> placemarks — the source of the `AddPlacemark` change type the 2026-08-04 note records) and
> `Reveal` (a synthetic, party-visible provenance record minted by a GM reveal — never captured,
> never extracted). `JournalEntry` and `Transcript` remain in the enum as legacy: retired from
> the capture UI, kept for the sources that carry them. `Nornis.Domain/Enums/SourceType.cs` is
> the authority.

## Core Mental Model

Nornis is built around three layers:

```text
Sources
    ↓
Artifacts
    ↓
Views
```

Sources are raw inputs. Artifacts are structured world knowledge derived from sources. Views are projections of artifacts for different user needs.

Preferred product language:

```text
Sources create the raw record.
Artifacts represent what Nornis understands.
Storylines organize what matters.
Canon records what endures.
```

Use **Storyline** instead of **Thread** in the domain model and UI. Thread/weaving language belonged to an earlier brand direction and should not be used as a primary product term.

### Worlds and Campaigns

A **World** is the root container: one body of knowledge, one membership list, one canon. A **Campaign** is a play-context within a world — a particular run of sessions with a particular cast. Long-living worlds accumulate multiple campaigns over time (the original campaign, the sequel five in-fiction years later, a side game with a different party).

Division of responsibility:

- The World owns authorization, membership, artifacts, facts, relationships, canon, and cost tracking.
- A Campaign is a label and a timeline, **not** a second authorization boundary. There are no per-campaign permissions.
- Sources may declare which campaign they happened in. Which campaign a fact "happened in" is derivable through provenance (`ArtifactFact → SourceReference → Source → CampaignId`); do not stamp campaign IDs onto artifacts or facts.

## User

A lightweight record for identifying and authenticating users within Nornis.

```csharp
User
- Id: Guid
- Auth0SubjectId: string
- Username: string
- Email: string
- CreatedAt: DateTimeOffset
- UpdatedAt: DateTimeOffset
```

Notes:

- Store only the minimum required for authentication and identification.
- Username is user-editable.
- Email is stored for contact/display purposes.
- Auth0SubjectId links to the external identity provider.
- Do not store passwords, tokens, or other auth secrets.

## World

A world is the root collaboration and authorization boundary.

```csharp
World
- Id: Guid
- Name: string
- Description: string?
- GameSystem: string?
- CreatedAt: DateTimeOffset
- UpdatedAt: DateTimeOffset
- CreatedByUserId: Guid
```

## WorldMember

Worlds are multiplayer by default.

```csharp
WorldMember
- Id: Guid
- WorldId: Guid
- UserId: Guid
- Role: WorldRole
- DisplayName: string?
- JoinedAt: DateTimeOffset
```

```csharp
WorldRole
- GM
- Player
- Observer
```

Notes:

- `Observer` may be rendered in the UI as "Fly on the wall".
- Authorization must always be enforced server-side using world membership.
- Auth0 authenticates identity. Nornis owns authorization.
- Member characters live on the `Character` entity, not on the membership record — a member may play many characters.

## Campaign

A campaign is a play-context within a world. Deliberately thin.

```csharp
Campaign
- Id: Guid
- WorldId: Guid
- Name: string
- Description: string?
- Status: CampaignStatus
- StartedAt: DateTimeOffset?
- EndedAt: DateTimeOffset?
- CreatedAt: DateTimeOffset
- UpdatedAt: DateTimeOffset
- CreatedByUserId: Guid
```

```csharp
CampaignStatus
- Active
- Completed
- Archived
```

Notes:

- Campaigns carry no membership and no permissions; world membership governs access.
- `StartedAt`/`EndedAt` are real-world dates describing when the campaign was played.
- Deleting a campaign must not delete knowledge: sources fall back to "no campaign" (`SET NULL`),
  and a world naming it as current falls back to none (see the 2026-08-09 amendment).
- Which campaign a world is *playing* is on `World.CurrentCampaignId`, not here.

## Character

A player character, owned by a world member. A member may have any number of characters in a world, and a character may participate in any number of campaigns.

```csharp
Character
- Id: Guid
- WorldId: Guid
- WorldMemberId: Guid
- Name: string
- Description: string?
- CreatedAt: DateTimeOffset
- UpdatedAt: DateTimeOffset
```

```csharp
CampaignCharacter
- Id: Guid
- CampaignId: Guid
- CharacterId: Guid
- CreatedAt: DateTimeOffset
```

Notes:

- `CampaignCharacter` is a pure join: which characters are (or were) part of which campaign. `(CampaignId, CharacterId)` is unique.
- A `Character` here is the member's playable identity, not the AI-extracted `Artifact` of type `Character`. The two may describe the same fictional person; linking them is a future feature, not MVP.

## Source

A source is raw information entered or uploaded by a user.

```csharp
Source
- Id: Guid
- WorldId: Guid
- CampaignId: Guid?
- Type: SourceType
- Title: string
- Body: string?
- Uri: string?
- OccurredAt: DateTimeOffset?
- CreatedAt: DateTimeOffset
- CreatedByUserId: Guid
- Visibility: VisibilityScope
- ProcessingStatus: SourceProcessingStatus
```

```csharp
SourceType
- SessionNote
- JournalEntry
- Transcript
- Upload
- Image
- HandwrittenNotes
- WebLink
- GMNote
- ImportedNote
```

```csharp
SourceProcessingStatus
- Draft
- Ready
- Queued
- Processing
- Processed
- Failed
```

Date semantics:

- `OccurredAt` is when the described events happened, if known.
- `CreatedAt` is when the source was created in Nornis.

Campaign semantics:

- `CampaignId` says which campaign the source's events happened in. It is nullable on purpose: worldbuilding lore, GM prep, and setting documents belong to no campaign.
- The extraction pipeline should pass the source's campaign (when present) into the prompt as context, so the AI can disambiguate recurring names across campaign eras.
- A `ReviewBatch` inherits its campaign context from its source; it does not store a campaign ID of its own.

Brand/product note:

- Sources are the many inputs that feed the enduring world record.
- In UI copy, sources may occasionally be described as "layers" beneath the epic, but the domain term remains `Source`.

## SourceExtraction

A source may have extracted text or interpretation derived from a non-text input.

```csharp
SourceExtraction
- Id: Guid
- SourceId: Guid
- ExtractionType: SourceExtractionType
- Text: string
- Confidence: decimal?
- CreatedAt: DateTimeOffset
```

```csharp
SourceExtractionType
- Manual
- OCR
- VisionSummary
- Transcription
- WebPageText
```

For MVP, this can be minimal. Do not build a sophisticated OCR or document ingestion pipeline unless explicitly scoped.

## Artifact

An artifact is something Nornis knows about. Artifacts are world-level: Captain Voss is one artifact no matter how many campaigns he appears in.

```csharp
Artifact
- Id: Guid
- WorldId: Guid
- Type: ArtifactType
- Name: string
- Summary: string?
- Visibility: VisibilityScope
- Confidence: decimal?
- Status: ArtifactStatus
- CreatedAt: DateTimeOffset
- UpdatedAt: DateTimeOffset
```

```csharp
ArtifactType
- Character
- Location
- Item
- Faction
- Event
- Storyline
- Concept
- Document
```

```csharp
ArtifactStatus
- Active
- Dormant
- Resolved
- Archived
```

Important design choices:

- Storyline is an artifact type, not a separate root entity.
- Storylines can have facts, relationships, source references, confidence, and visibility like any other artifact.
- Storyline lifecycle transitions (Active → Dormant → Resolved, etc.) are triggered by AI suggestions via review proposals. The AI may propose status changes based on source content, and users accept or reject those proposals like any other change.
- Artifacts do not carry a campaign ID. Campaign association is derived from source provenance.

## ArtifactFact

Facts are atomic statements about artifacts.

```csharp
ArtifactFact
- Id: Guid
- ArtifactId: Guid
- Predicate: string
- Value: string
- Confidence: decimal?
- TruthState: TruthState
- Visibility: VisibilityScope
- CreatedAt: DateTimeOffset
- UpdatedAt: DateTimeOffset
```

Examples:

```text
Artifact: Captain Voss
Predicate: location
Value: Black Harbor
TruthState: Confirmed
```

```text
Artifact: Silver Key
Predicate: current owner
Value: Tavrin
TruthState: Likely
```

## ArtifactRelationship

Relationships are bidirectional typed edges between artifacts.

```csharp
ArtifactRelationship
- Id: Guid
- WorldId: Guid
- ArtifactAId: Guid
- ArtifactBId: Guid
- Type: string
- Description: string?
- Confidence: decimal?
- TruthState: TruthState
- Visibility: VisibilityScope
- CreatedAt: DateTimeOffset
- UpdatedAt: DateTimeOffset
```

Notes:

- Relationships are bidirectional. There is no inherent directionality between ArtifactA and ArtifactB.
- The relationship type describes the connection (e.g., "LocatedIn", "SuspectedIn", "AlliedWith").
- Queries should match on either ArtifactAId or ArtifactBId when looking up relationships for a given artifact.

Examples:

```text
Captain Voss <-> Black Harbor
Type: LocatedIn
```

```text
Captain Voss <-> Missing Caravan
Type: SuspectedIn
```

## TruthState

Truth state allows Nornis to distinguish facts from rumors and hidden reality.

```csharp
TruthState
- Confirmed
- Likely
- Rumor
- Disputed
- False
- Hidden
```

Notes:

- Player-visible truth and GM truth must be separable.
- `Hidden` should be visible only to GMs.
- `False` may represent known misinformation, not bad data.

## VisibilityScope

```csharp
VisibilityScope
- Private
- GMOnly
- PartyVisible
```

Rules:

- `Private` is visible only to the creating user unless future sharing rules are added.
- `GMOnly` is visible only to world GMs.
- `PartyVisible` is visible to all world members.
- Visibility is world-scoped. Campaigns add no visibility rules.

## SourceReference

Facts and relationships must cite their supporting sources.

```csharp
SourceReference
- Id: Guid
- SourceId: Guid
- TargetType: SourceReferenceTargetType
- TargetId: Guid
- Quote: string?
- Notes: string?
- CreatedAt: DateTimeOffset
```

```csharp
SourceReferenceTargetType
- Artifact
- ArtifactFact
- ArtifactRelationship
- ReviewProposal
```

## ReviewBatch

A source extraction run creates a review batch.

```csharp
ReviewBatch
- Id: Guid
- WorldId: Guid
- SourceId: Guid
- Status: ReviewBatchStatus
- CreatedAt: DateTimeOffset
- CompletedAt: DateTimeOffset?
```

```csharp
ReviewBatchStatus
- Pending
- InReview
- Completed
- Canceled
- Failed
```

## ReviewProposal

A review proposal is one reviewable change. Avoid one giant JSON blob containing all changes.

```csharp
ReviewProposal
- Id: Guid
- ReviewBatchId: Guid
- ChangeType: ReviewChangeType
- TargetType: ReviewTargetType
- TargetId: Guid?
- ProposedValueJson: string
- Rationale: string?
- Confidence: decimal?
- Status: ReviewProposalStatus
- CreatedAt: DateTimeOffset
- ReviewedAt: DateTimeOffset?
- ReviewedByUserId: Guid?
```

```csharp
ReviewChangeType
- CreateArtifact
- UpdateArtifact
- MergeArtifact
- AddFact
- UpdateFact
- AddRelationship
- UpdateRelationship
```

```csharp
ReviewTargetType
- Artifact
- ArtifactFact
- ArtifactRelationship
```

```csharp
ReviewProposalStatus
- Pending
- Accepted
- Rejected
- Edited
```

`ProposedValueJson` should contain a schema-specific payload for the proposed artifact, fact, or relationship mutation.

Notes:

- Editing a proposal mutates the original ReviewProposal record in place.
- The `Edited` status indicates a proposal was modified by the reviewer before acceptance.
- `MergeArtifact` proposals should specify the source artifact and target artifact to merge into.

## Conversation

Conversation is optional for MVP.

Conversation means saved chat history with the Loremaster.

```csharp
Conversation
- Id: Guid
- WorldId: Guid
- UserId: Guid
- Title: string
- CreatedAt: DateTimeOffset
- UpdatedAt: DateTimeOffset
```

```csharp
ConversationMessage
- Id: Guid
- ConversationId: Guid
- Role: ConversationRole
- Content: string
- CreatedAt: DateTimeOffset
```

For MVP, defer persistence of conversations unless Ask history is explicitly required.

## Views

Views are projections over artifacts and sources.

### Artifacts View

A browseable collection of artifacts by type, recency, importance, or relationship.

### Storylines View

A filtered artifact view where `Artifact.Type == Storyline`.

### Canon View

A truth-state view over artifacts, facts, and relationships.

### Sources View

A source ledger. This is where users inspect raw inputs. Filterable by campaign.

### Ask View

A conversational interface over artifacts, relationships, facts, and cited sources.

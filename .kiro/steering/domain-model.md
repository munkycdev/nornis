# Domain Model

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
- Deleting a campaign must not delete knowledge: sources fall back to "no campaign" (`SET NULL`).

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

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

Sources are raw inputs. Artifacts are structured campaign knowledge derived from sources. Views are projections of artifacts for different user needs.

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

## Campaign

A campaign is the root collaboration and authorization boundary for MVP.

```csharp
Campaign
- Id: Guid
- Name: string
- Description: string?
- GameSystem: string?
- CreatedAt: DateTimeOffset
- UpdatedAt: DateTimeOffset
- CreatedByUserId: Guid
```

## CampaignMember

Campaigns are multiplayer by default.

```csharp
CampaignMember
- Id: Guid
- CampaignId: Guid
- UserId: Guid
- Role: CampaignRole
- DisplayName: string?
- CharacterName: string?
- JoinedAt: DateTimeOffset
```

```csharp
CampaignRole
- GM
- Player
- Observer
```

Notes:

- `Observer` may be rendered in the UI as "Fly on the wall".
- Authorization must always be enforced server-side using campaign membership.
- Auth0 authenticates identity. Nornis owns authorization.

## Source

A source is raw information entered or uploaded by a user.

```csharp
Source
- Id: Guid
- CampaignId: Guid
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

- `OccurredAt` is when the described campaign event happened, if known.
- `CreatedAt` is when the source was created in Nornis.

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

An artifact is something Nornis knows about.

```csharp
Artifact
- Id: Guid
- CampaignId: Guid
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
- Thread
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

- Thread is an artifact type, not a separate root entity.
- Threads can have facts, relationships, evidence, confidence, and visibility like any other artifact.
- Thread lifecycle transitions (Active → Dormant → Resolved, etc.) are triggered by AI suggestions via review proposals. The AI may propose status changes based on source content, and users accept or reject those proposals like any other change.

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
- CampaignId: Guid
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
- `GMOnly` is visible only to campaign GMs.
- `PartyVisible` is visible to all campaign members.

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
- CampaignId: Guid
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
- CampaignId: Guid
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

### Threads View

A filtered artifact view where `Artifact.Type == Thread`.

### Canon View

A truth-state view over artifacts, facts, and relationships.

### Sources View

A source/evidence ledger. This is where users inspect raw inputs.

### Ask View

A conversational interface over artifacts, relationships, facts, and cited sources.

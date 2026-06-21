# UI Design System

## Product Vibe

Nornis should feel like a calm, modern campaign Loremaster.

Not:

- Generic admin dashboard
- Dense enterprise CRUD app
- Heavy fantasy parchment UI
- Folder-based wiki with a chatbot stapled on

Aim for:

- Light theme first
- Soft neutral background
- Rounded cards
- Deep blue-grey text and navigation
- Warm gold accents
- Subtle multi-color thread motif
- Clean typography
- Low-friction review workflows

## Brand Metaphor

Nornis weaves campaign threads into coherent memory.

Logo concept:

- Multiple colored threads entering from one side.
- Threads loosely weave together.
- End state becomes a unified rope that still preserves individual colors.

The UI can reuse this metaphor in subtle ways:

- Processing states: "Weaving source..."
- Review state: "Threads found"
- Connectedness indicator on thread cards
- Small woven glyphs for artifact relationship density

## UI Framework

Use MudBlazor plus custom CSS/design tokens.

Do not rely on default MudBlazor styling alone. Defaults are acceptable for scaffolding but final UI should have a distinct Nornis feel.

## Navigation

Recommended primary navigation:

```text
Ask
Capture
Artifacts
Threads
Canon
Sources
Costs
Settings
```

### Ask

The AI Loremaster interface.

### Capture

Create sources: session notes, journal entries, GM notes, uploads, web links, handwritten notes.

### Artifacts

Browse structured campaign knowledge.

### Threads

Narrative threads. This is a filtered artifact view where `Artifact.Type == Thread`.

### Canon

Truth-state view over accepted facts and relationships.

### Sources

Raw input ledger. Formerly "Evidence" in earlier thinking.

### Costs

Token and dollar usage.

### Settings

Campaign and user configuration.

## Core Screens

### Campaign Home

Should answer:

- What campaign am I in?
- What should I review?
- What changed recently?
- What can I ask?
- What sources need processing?

Primary CTA:

```text
Tell Nornis what happened
```

### Capture Source

A simple input experience.

Fields:

- Source type
- Title
- OccurredAt
- Body / upload / URL
- Visibility

On submit:

- Store source
- Enqueue extraction
- Show processing status

### Review Queue

Review proposals should be easy to accept one-by-one.

Each proposal card should show:

- Change type
- Target artifact/fact/relationship
- Proposed value
- Rationale
- Confidence
- Source citation
- Accept / Edit / Reject

Include batch actions only when safe.

### Artifacts

Cards or list view of artifacts.

Artifact card:

- Name
- Type
- Summary
- Status
- Confidence
- Recently updated date
- Connected artifacts

Artifact detail:

- Summary
- Facts
- Relationships
- Threads
- Source references
- Change history

### Threads

Thread card:

- Name
- Status
- Last advanced
- Summary
- Connected artifacts
- Open questions
- Player-visible vs GM-only indicators

### Canon

Canon should show accepted truth state.

Sections:

- Recent canon changes
- Disputed facts
- Hidden GM facts
- Rumors
- Confirmed world state

### Sources

Source ledger.

Source card:

- Title
- Type
- OccurredAt
- CreatedAt
- CreatedBy
- Processing status
- Number of proposals generated

### Costs

Cost dashboard.

Display:

- Today
- This week
- This month
- By campaign
- By user
- By operation type
- By model
- Token totals
- Estimated dollar totals

## Accessibility

- Use semantic HTML.
- Ensure keyboard navigation for proposal review.
- Ensure color is not the only indicator for confidence, truth state, or visibility.
- Keep contrast high enough in light theme.

## Tone

The product should be warm, confident, and slightly magical, but not twee.

Avoid excessive fantasy jargon in operational areas.

Good:

```text
Review proposed updates
Source processed
Ask the Loremaster
```

Too much:

```text
The Oracle has divined sixteen fragments of destiny from thine parchment
```

We are building a useful app, not a dinner theater incident.

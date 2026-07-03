# UI Design System

## Product Vibe

Nornis should feel like a calm, modern campaign Loremaster with a durable, archival identity.

Not:

- Generic admin dashboard
- Dense enterprise CRUD app
- Heavy fantasy parchment UI
- Folder-based wiki with a chatbot stapled on
- LARP-styled rune-and-stone interface
- Medieval game UI

Aim for:

- Light theme first
- Warm off-white body background
- Clean cards with slightly stronger contrast than the body
- Deep blue sidebar/navigation
- Deep blue-grey text
- Restrained aged-gold accents
- Subtle strata/topographic/geologic cues used sparingly
- Strong rune/stone-inspired logo as the primary mythic anchor
- Clean typography
- Low-friction review workflows

The product should feel like modern premium software with a mythic archival identity: durable, calm, intelligent, and trustworthy.

## Brand Metaphor

Preferred metaphor:

```text
Many sources feed one enduring epic.
```

The rune/tree mark represents many sources feeding a single trunk. In product terms, notes, journals, transcripts, handouts, images, maps, links, and ideas feed the durable campaign record.

The UI should no longer lean on the thread/weaving/rope motif as its primary metaphor.

Use supporting language such as:

- Every source leaves a mark.
- Your campaign, remembered.
- Built from every source.
- Shaped into story.
- What is remembered, endures.

Use strata/geologic cues sparingly:

- Subtle layered line patterns in backgrounds or empty states.
- Source pages may refer to source material as layers beneath the campaign record.
- Canon pages may lightly evoke bedrock, durability, and accepted truth.

Avoid turning the app into geology software with swords. The metaphor should support the UI, not become homework.

## Logo Direction

The preferred logo direction is the stonemark / rune-like N:

- A strong N-shaped glyph.
- A small branching rune/tree mark that symbolizes many sources feeding one trunk.
- Deep blue field or text.
- Ivory/stone glyph treatment where appropriate.
- Minimal aged-gold accent.

Use the stone/rune identity as a brand anchor, not as permission to texture every surface.

Do not use:

- Literal stone textures across the whole UI.
- Parchment panels.
- Heavy fantasy ornamentation.
- Medieval borders.
- Ropes, woven threads, spinning wheels, looms, axes, ravens, helmets, or Celtic knots.

## UI Framework

Use MudBlazor plus custom CSS/design tokens.

Do not rely on default MudBlazor styling alone. Defaults are acceptable for scaffolding but final UI should have a distinct Nornis feel.

## Navigation

Recommended primary navigation:

```text
Ask
Capture
Artifacts
Storylines
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

### Storylines

Narrative arcs, mysteries, quests, investigations, rivalries, prophecies, and unresolved developments. This is a filtered artifact view where `Artifact.Type == Storyline`.

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

Supporting copy:

```text
Add notes, journals, transcripts, images, maps, or links. Nornis will shape them into your campaign record.
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

Processing copy should avoid weaving language.

Good:

```text
Reading source...
Shaping the record...
Source processed
Review proposed updates
```

Avoid:

```text
Weaving source...
Threads found
```

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
- Storylines
- Source references
- Change history

### Storylines

Storyline card:

- Name
- Status
- Last advanced
- Summary
- Connected artifacts
- Open questions
- Player-visible vs GM-only indicators

Suggested page copy:

```text
The stories in motion.
```

### Canon

Canon should show accepted truth state.

Sections:

- Recent canon changes
- Disputed facts
- Hidden GM facts
- Rumors
- Confirmed world state

Suggested page copy:

```text
What is remembered, endures.
```

Use bedrock/strata language only where it clarifies the concept. Do not overuse it in labels.

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

Suggested page copy:

```text
The layers beneath your epic.
```

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

## Visual Tokens

Suggested starting palette:

```text
Body background: #F8F5EF
Card background: #FFFDF8
Primary text: #172A36
Sidebar/nav: #0F1F2D
Aged gold accent: #C4A15A
Slate secondary: #6D7A80
Success: muted green
Warning: muted amber
Danger: restrained coral/red
```

Cards should have enough contrast to stand apart from the body background. Use subtle border, shadow, or tonal difference; avoid heavy texture.

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
Every source leaves a mark
```

Too much:

```text
The Oracle has divined sixteen fragments of destiny from thine parchment
```

We are building a useful app, not a dinner theater incident.

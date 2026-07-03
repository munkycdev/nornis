# Product Vision: Nornis

## Purpose

Nornis is a campaign memory engine for tabletop roleplaying games. It helps players and GMs capture what happened, transform those sources into structured campaign knowledge, and consult that knowledge through an AI Loremaster.

Nornis is not a wiki-first application. It is not primarily a folder tree, notebook, or document archive. Those may exist as supporting views, but the product is centered on the transformation of raw campaign sources into reliable artifacts, relationships, storylines, and canon.

## Core Promise

Paste or upload campaign information. Nornis extracts important artifacts, proposes changes, and lets users ask the Loremaster what matters.

## Tagline Direction

```text
It's your epic. Every source leaves a mark.
```

The tagline should reinforce that the campaign belongs to the players and GM. Nornis preserves, connects, and clarifies the record; it does not author the epic for them.

## Product Metaphor

A campaign grows from many sources: session notes, player journals, GM ideas, transcripts, images, maps, handouts, links, rumors, choices, and consequences. Nornis gathers these sources and shapes them into an enduring campaign record.

The brand metaphor is not primarily weaving or threads. The preferred metaphor is:

```text
Many sources feed one enduring epic.
```

The rune/tree mark represents many branches feeding a single trunk. In product terms, many source inputs feed the durable shape of the campaign's memory.

A secondary geologic metaphor may be used sparingly: sources accumulate as layers, and accepted canon becomes the durable bedrock of the campaign. Avoid overusing geology language in UI labels.

## Brand Tone

Nornis should feel:

- Solid
- Calm
- Durable
- Intelligent
- Slightly mythic
- Modern and useful

Nornis should not feel like:

- A fantasy game UI
- A parchment-and-runes LARP dashboard
- A generic admin application
- A chatbot bolted onto a wiki

The rune/stone visual identity should provide weight and permanence, while the application UI remains clean, modern, and restrained.

## Primary Users

### Player

A player uses Nornis to remember things their brain refuses to store because it is apparently busy keeping useless song lyrics from 1998.

Player needs:

- Catch up before a session.
- Remember NPCs, places, clues, and promises.
- Ask what their character knows.
- Track active storylines and unresolved mysteries.
- Maintain private player notes.

### Game Master

A GM uses Nornis to plan, track, and preserve campaign continuity.

GM needs:

- Capture session notes and prep notes.
- Review AI-generated updates before they become accepted knowledge.
- Maintain hidden canon and player-visible truth separately.
- Track storylines, unresolved questions, factions, NPC states, and continuity risks.
- Ask for prep assistance grounded in campaign state.

### Observer

An observer can view campaign material according to permissions but should not mutate campaign state. In the UI this role may be called "Fly on the wall," but the internal role name should be `Observer`.

## MVP Product Loop

```text
Create Source
    ↓
Async Extraction
    ↓
Review Proposals
    ↓
Accept / Edit / Reject
    ↓
Artifacts, Facts, and Relationships update
    ↓
Ask the Loremaster
```

## MVP Wow Moment

A user enters this source:

```text
We questioned Captain Voss in Black Harbor. He denied knowing about the missing caravan, but Tavrin found the Silver Key in his quarters.
```

Nornis proposes:

- Create or update `Captain Voss`.
- Connect `Captain Voss` to `Black Harbor`.
- Add claim: `Captain Voss denied knowing about the missing caravan`.
- Create or update `Silver Key`.
- Add fact: `Silver Key was found in Voss's quarters`.
- Create or update storyline `Missing Caravan`.
- Connect `Missing Caravan` to `Captain Voss`, `Black Harbor`, and `Silver Key`.

The user can accept, edit, or reject each proposal individually.

## Product Principles

1. Sources are raw material. Artifacts are memory.
2. AI proposes. Users decide what becomes accepted campaign knowledge.
3. The UI should make the AI feel like a Loremaster, not a chatbot bolted to a CRUD app.
4. Sessions are historical records. Artifacts are persistent knowledge.
5. Storylines are narrative projections over artifacts.
6. Canon is truth-state applied to artifacts, facts, and relationships.
7. The user should be able to ask questions without remembering where anything was written.
8. The user should always be able to inspect the source material behind an answer.
9. Many sources feed one enduring epic.
10. Every source should be able to leave a traceable mark on accepted knowledge.

## Core Product Language

Use these primary terms consistently:

- **Sources**: raw inputs and original material.
- **Artifacts**: structured things Nornis understands.
- **Storylines**: narrative arcs, mysteries, quests, investigations, rivalries, and unresolved developments.
- **Canon**: accepted truth-state and durable campaign reality.
- **Ask the Loremaster**: conversational access to campaign memory.

Avoid using **Threads** as a top-level product term. It belonged to an earlier weaving metaphor and should not be used for navigation or primary domain language.

## Non-Goals for MVP

- No wiki tree as primary navigation.
- No audio transcription.
- No autonomous mutation of canon.
- No complex canon engine beyond reviewable facts and relationships.
- No real-time multiplayer editing.
- No RPG rules engine.
- No multi-system rules automation.
- No advanced document OCR pipeline beyond a simple source extraction placeholder.
- No public anonymous campaign browsing.

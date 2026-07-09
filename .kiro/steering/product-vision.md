# Product Vision: Nornis

## Purpose

Nornis is a memory engine for tabletop roleplaying worlds. It helps players and GMs capture what happened, transform those sources into structured world knowledge, and consult that knowledge through an AI Loremaster.

A **World** is the container: one body of knowledge that endures. A **Campaign** is a run of play within a world. Long-living worlds outlast any single campaign — the same setting, NPCs, and canon carry across the original campaign, the sequel years later, and the side game with a different party. Sources declare which campaign their events happened in; knowledge belongs to the world.

Nornis is not a wiki-first application. It is not primarily a folder tree, notebook, or document archive. Those may exist as supporting views, but the product is centered on the transformation of raw sources into reliable artifacts, relationships, storylines, and canon.

## Core Promise

Paste or upload campaign information. Nornis extracts important artifacts, proposes changes, and lets users ask the Loremaster what matters.

## Tagline Direction

```text
It's your epic. Every source leaves a mark.
```

The tagline should reinforce that the world belongs to the players and GM. Nornis preserves, connects, and clarifies the record; it does not author the epic for them.

## Product Metaphor

A world grows from many sources: session notes, player journals, GM ideas, transcripts, images, maps, handouts, links, rumors, choices, and consequences. Nornis gathers these sources and shapes them into an enduring world record.

The brand metaphor is not primarily weaving or threads. The preferred metaphor is:

```text
Many sources feed one enduring epic.
```

The rune/tree mark represents many branches feeding a single trunk. In product terms, many source inputs feed the durable shape of the world's memory.

A secondary geologic metaphor may be used sparingly: sources accumulate as layers, and accepted canon becomes the durable bedrock of the world. Avoid overusing geology language in UI labels.

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
- Play multiple characters, in one campaign or across several.

### Game Master

A GM uses Nornis to plan, track, and preserve continuity across campaigns in a world.

GM needs:

- Capture session notes and prep notes.
- Review AI-generated updates before they become accepted knowledge.
- Maintain hidden canon and player-visible truth separately.
- Track storylines, unresolved questions, factions, NPC states, and continuity risks.
- Ask for prep assistance grounded in world state.
- Run successive or parallel campaigns against one enduring world.

### Observer

An observer can view world material according to permissions but should not mutate world state. In the UI this role may be called "Fly on the wall," but the internal role name should be `Observer`.

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
2. AI proposes. Users decide what becomes accepted world knowledge.
3. The UI should make the AI feel like a Loremaster, not a chatbot bolted to a CRUD app.
4. Sessions are historical records. Artifacts are persistent knowledge.
5. Storylines are narrative projections over artifacts.
6. Canon is truth-state applied to artifacts, facts, and relationships.
7. The user should be able to ask questions without remembering where anything was written.
8. The user should always be able to inspect the source material behind an answer.
9. Many sources feed one enduring epic.
10. Every source should be able to leave a traceable mark on accepted knowledge.
11. Knowledge belongs to the world. Campaigns contextualize when and with whom it happened.

## Core Product Language

Use these primary terms consistently:

- **World**: the root container — one setting, one membership, one body of knowledge.
- **Campaign**: a run of play within a world; sources may belong to one.
- **Characters**: a member's playable identities; many per world, many per campaign, spanning campaigns.
- **Sources**: raw inputs and original material.
- **Artifacts**: structured things Nornis understands.
- **Storylines**: narrative arcs, mysteries, quests, investigations, rivalries, and unresolved developments.
- **Canon**: accepted truth-state and durable world reality.
- **Ask the Loremaster**: conversational access to world memory.

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
- No public anonymous world browsing.

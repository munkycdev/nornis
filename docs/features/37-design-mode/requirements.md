# Requirements Document

## Introduction

On 2026-11-03 Dave shared a transcript of how "Everybody Dies at the Slurping Spa" was written
in ChatGPT. The work went back and forth over one growing document, and was split into a
booklet only at the end. Prep (features 29 and 30) can't work that way today. Reviewing the
transcript found these gaps:

1. **Prep sees no source books.** Drafting and the side conversation read the world record
   through `KeywordKnowledgeRetriever`, but never the world's Library or Bibliotaph books. The
   transcript leaned on a published adventure throughout.
2. **No standing table brief.** The party, their level, the session's length and the player
   to watch were restated in almost every message.
3. **One section at a time.** There is no way to iterate on the whole adventure as one text.
   Restructuring means copying between sections by hand.

Dave's direction, 2026-11-03:

- "Let's tie all prep work to information already known about the world, including the
  system." Feature 35 already writes for the adventure's own system, edition and levels. This
  feature adds the world's books and a table brief.
- "An interactive design mode where the user and AI could go back and forth, iterating on a
  single markdown document. Once it's ready, the user can tell the Prep feature to decompose
  it into the sections for final review."
- On structuring someone's existing text: when a GM pastes a whole adventure in and has Prep
  only structure it, "it's reasonable to say AI wasn't involved". So the split must provably
  not change the words.

Ships as Nornis 3.8. It needs one migration, which Dave applies.

## Glossary

- **Design draft**: one Markdown document per Adventure, separate from its sections, where
  the GM and the Loremaster iterate.
- **Design mode**: the Adventure page's Design tab: the draft's editor beside the side
  conversation.
- **Suggested edit**: a change the Loremaster proposes to the draft in a conversation answer,
  as a passage to find and its replacement. The GM applies it or doesn't.
- **Split**: turning the draft into sections. It is a **plan** (where each section starts, its
  title and its kind) and a **cut** (code slicing the draft at those lines).
- **Gather**: joining the sections back into the draft.
- **Table brief**: a short note about the table (party, levels, session length, player
  quirks) that goes into every Prep prompt.
- **Source book**: a Library book (Bibliotaph or an indexed upload) the Loremaster may read
  while it prepares this adventure.

## Requirements

### Requirement 1 — The design draft

**User Story:** As a GM, I want to write and rework the whole adventure as one document before
I break it into sections.

#### Acceptance Criteria

1. Every Adventure SHALL have an optional design draft. An Adventure created today has none.
2. The Adventure page SHALL have two tabs: **Design** (the draft) and **Sections** (today's
   outline and editor). Sections SHALL be the default once the Adventure has any written
   section; otherwise Design.
3. The draft SHALL use the section editor with every Prep tool (read-aloud, prop, ask, GM note,
   stat block, item), a local backup, and a save based on the version opened.
4. The draft SHALL be at most `PrepOptions.MaxDraftChars` (60,000) characters.
5. The draft SHALL keep revisions like a section's, so Undo steps back through every change the
   Loremaster made.
6. The draft SHALL never print and never export. The booklet and Export to Nornis read
   sections only.
7. **Import Markdown** (feature 32) SHALL offer to import into the draft instead of into
   sections.

### Requirement 2 — Iterating with the Loremaster

**User Story:** As a GM, I want to talk the adventure through and have the Loremaster change
the draft only where I agree.

#### Acceptance Criteria

1. In Design mode, the side conversation SHALL see the whole draft (up to its limit) instead of
   one section.
2. When the Loremaster proposes a change, it SHALL write it as one or more **suggested edits**.
   Each names an exact passage of the draft and its replacement. A suggestion for an empty draft
   is the whole text.
3. Each suggested edit SHALL show as a card with the passage before and after, and **Apply**.
   Apply SHALL change only that passage, keep a revision, and refuse (409) when the passage is
   no longer in the draft exactly once.
4. Nothing the Loremaster writes SHALL reach the draft without the GM's Apply.
5. **Put in draft** SHALL replace or append the whole answer, as Put in section does today.

### Requirement 3 — Splitting into sections

**User Story:** As a GM, I want the finished draft broken into the Adventure's sections, with
none of my words changed.

#### Acceptance Criteria

1. **Split into sections** SHALL first show a plan: each section's title, kind and opening
   words. The GM SHALL be able to rename, re-kind, merge or drop a split point before applying.
2. The default plan SHALL come from the draft's own headings: a `#` heading starts a Chapter,
   and a `##` heading starts a section whose kind is read from its title (Overview, Session
   clock, Quick reference, Notes, otherwise Scene). This uses no AI.
3. When the draft has no headings, or the GM asks for it, the Loremaster SHALL propose the plan.
   It SHALL return only line numbers, titles and kinds, never text.
4. The cut SHALL be made by code. Each section's body is the draft's lines from its start up to
   the next start, less a heading line that became the title. The cut SHALL be checked so the
   sections' titles and bodies contain exactly the draft's words in the same order. If the check
   fails, nothing is applied.
5. Applying SHALL ask **Replace the sections** or **Add after the sections** when the Adventure
   already has written sections. Replace SHALL keep each old section's text as a revision of the
   draft before it goes, so nothing is lost.
6. A split SHALL count as structuring, not writing, for feature 38's AI-use flag.

### Requirement 4 — Gathering back

#### Acceptance Criteria

1. **Gather sections into the draft** SHALL join the sections in order. A Chapter becomes a `#`
   heading and any other section a `##` heading, each followed by its text.
2. It SHALL replace the draft, keeping the old draft as a revision.

### Requirement 5 — Table brief

**User Story:** As a GM, I want to tell the Loremaster about my table once.

#### Acceptance Criteria

1. Every Adventure SHALL have an optional table brief of at most 2,000 characters, edited on a
   card beside Book details.
2. The brief SHALL be in every Prep prompt: drafting a section, the side conversation, design
   mode and a Loremaster split plan.
3. The brief SHALL never print and never export.

### Requirement 6 — Source books

**User Story:** As a GM, I want the Loremaster to use the published adventure I'm adapting.

#### Acceptance Criteria

1. Prep's prompts SHALL include reference passages from the world's books, searched at the GM's
   scope (GMOnly and PartyVisible), as the Loremaster's Ask does.
2. The GM SHALL be able to choose up to five **source books** on the Adventure. When any are
   chosen, the reference search SHALL search only those.
3. Passages SHALL be labelled as published reference, not world canon, and the Loremaster SHALL
   cite them by book and page.
4. The prompt conventions SHALL keep feature 35's rule: name the book and page rather than copy a
   published stat block or long passage.
5. When the books can't be reached (Bibliotaph down), drafting SHALL go on from the world
   record and the answer SHALL say the books were unavailable, as Ask does.
6. Reference passages SHALL be charged to the Prep pool like the rest of the call.

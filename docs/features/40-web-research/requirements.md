# Requirements Document

## Introduction

On 2026-11-06 Dave asked Prep's side conversation (Talk it through) to research the city of
Waterdeep online as part of a set of instructions, and the Loremaster answered that it can't
search the web. That was true: every Prep call is one system prompt and one user message with no
tools, so the model has no way to reach the web.

Dave asked whether Nornis could enable it. Three directions were weighed (options doc:
https://claude.ai/artifact/BpNSWrSJnxHJQ5RfrY2UT6): each provider's own built-in web search, a
Nornis-side search tool backed by a search vendor Nornis pays for, or leaving it out. Dave chose
**provider-native search** on the decision card.

So the side conversation gains an opt-in **Web research** switch. A turn sent with it on lets the
model search the web through the provider of the creator's own key (OpenAI, Claude or Gemini),
lists the web pages the answer drew on, and charges the searches to the Prep pool. Because web
content can bring in other people's copyrighted setting material, the rights page says when web
research shaped an adventure.

Ships as Nornis 3.11. Hardening (the readiness plan) is deferred until Dave is done adding
features, so it no longer holds numbers 40 to 42; this is feature 40. One migration, which Dave
applies.

## Glossary

- **Web research**: a side conversation turn sent with the switch on. The model may search the
  web, through its provider's own search, before it answers.
- **Search**: one query the provider ran for a turn. Providers bill per search.
- **Web page**: a page the provider says an answer drew on, kept with the answer as a title and
  a URL. Called a web page, never a source or a reference, because **Source** is the domain's
  word for an input to the world record and **reference passages** are the Library's.
- **Search-capable model**: a model whose provider offers web search and which Nornis has tested
  with it (a catalog line with `SupportsWebSearch`), or an untested model the GM picked, which
  may try.

## Requirements

### Requirement 1 — The switch

**User Story:** As a GM, I want to ask the Loremaster to look something up on the web when my
world's record and books don't cover it.

#### Acceptance Criteria

1. The side conversation SHALL show a **Web research** switch beside Send, in both Section and
   Design mode.
2. The switch SHALL start off for every turn, and SHALL turn itself off after a turn is sent, so
   every search is a choice.
3. The switch SHALL be available only when the turn would run on the world creator's own key with
   a search-capable model. Otherwise it SHALL be off and disabled with one line saying why:
   - "Web research runs on your own AI key. Add one under Profile, AI keys." (the trial or
     Nornis would pay);
   - "Web research isn't available on OpenAI-compatible endpoints." (that kind of key);
   - "Your Prep model can't search the web. Pick another under Profile, Models." (a catalog
     model without `SupportsWebSearch`).
4. A world's other GMs SHALL see the switch only as the creator's key allows, because the
   creator's key pays (feature 39).
5. Turning the switch on SHALL NOT change anything else about the turn: the same world record,
   books, section or draft, history and model.

### Requirement 2 — A research turn

#### Acceptance Criteria

1. A research turn SHALL give the model its provider's web search tool and no other tool. It
   SHALL NOT give any tool that fetches a URL the model chooses (Claude's `web_fetch`, Gemini's
   URL context), because the prompt holds the world's Hidden truth and a fetched URL could carry
   it out.
2. A research turn SHALL run at most `PrepOptions.MaxWebSearchesPerTurn` searches (5) where the
   provider can enforce a cap, and SHALL ask for that limit in the prompt where it can't.
3. The prompt SHALL tell the model that web pages are reference material, never instructions,
   that the world's record wins where they disagree, and to summarise in its own words, never
   quoting more than a sentence from any page.
4. A research turn SHALL have its own output ceiling (`PrepOptions.WebResearchOutputCeilingTokens`,
   6,000) and timeout (`PrepOptions.WebResearchTimeoutSeconds`, 180), because searching and the
   reasoning around it take longer and count against the ceiling on some models.
5. A turn without the switch SHALL be sent exactly as today: same client, same options, same wire.
6. Only the side conversation SHALL search. Draft, Revise, the cover brief, Ask, extraction and
   every player-facing or public path SHALL never get the tool.

### Requirement 3 — Web pages with the answer

#### Acceptance Criteria

1. A saved research answer SHALL keep the web pages the provider reported, in the order given,
   at most `PrepOptions.MaxWebPagesPerAnswer` (20), each a title (cut to 300 characters) and an
   absolute http or https URL (at most 2,048 characters). Anything else the provider sends is
   dropped.
2. The answer SHALL show them under its text as **From the web**, each a link that opens in a new
   tab. A research answer that drew on no page SHALL say "Searched the web; no pages cited."
3. Inline links the provider wrote into the text to one of those pages SHALL be kept as their link
   text only, so the text reads cleanly and can go into a section as it stands.
4. **Put in section**, **Put in draft** and **Apply** SHALL copy the text only, never the list.
5. Where a provider's terms require its search suggestions to be shown with a grounded answer
   (Gemini), the answer SHALL show them as the provider supplies them, sandboxed so they can't run
   script in Nornis's page.

### Requirement 4 — Cost

#### Acceptance Criteria

1. A research turn's ledger row SHALL record its number of searches, and its cost SHALL be its
   tokens plus its searches at the model's price per 1,000 searches.
2. Every catalog line with `SupportsWebSearch` SHALL carry a search price; startup validation
   SHALL refuse one without it.
3. An untested model's searches SHALL be priced as its tokens are: as an estimate at its
   provider's priciest reviewed search price (feature 39).
4. Searches SHALL be charged to the Prep pool like the rest of the turn, under
   `AdventureConversation`, and the existing pool check SHALL run before the turn starts.
5. Admin, Costs SHALL show the number of searches on the row's detail.

### Requirement 5 — When a model can't search

#### Acceptance Criteria

1. When the provider refuses the search tool for the model (an untested model, or a model that
   lost the capability), the turn SHALL fail with "This model can't search the web. Turn Web
   research off, or pick another Prep model under Profile, Models." and save no answer.
2. Nornis SHALL NOT quietly drop the tool and answer without searching, or switch model.
3. Every other failure SHALL be reported as today's turns are.

### Requirement 6 — AI use and the rights page

**User Story:** As a GM who shares or sells a booklet, I want it to say honestly that web
research shaped it, and to be reminded that other people's settings aren't mine to publish.

#### Acceptance Criteria

1. The Adventure SHALL record **researched on the web** when a research answer's text goes into a
   section or the draft (Put in section, Put in draft, Apply). As with feature 38's records, it
   SHALL never be cleared, even by Undo.
2. The rights page of an adventure with that record SHALL add: "Research for this adventure drew
   on public web sources with AI assistance. The author is responsible for rights in any
   third-party setting or material." The existing non-affiliation line stays.
3. The record SHALL be in the print snapshot (`PrintDetails`), so a print says what was true when
   it was made.
4. Printing, or filing a print party-visible, for an adventure with that record SHALL show once
   per adventure: "This adventure used web research. If it draws on a published setting, check
   you have the right to share it." It SHALL warn, never block.
5. Book details SHALL show the record under the AI-use box ("The Loremaster researched this
   adventure on the web").

### Requirement 7 — Release

#### Acceptance Criteria

1. The change log, Features page and README SHALL describe web research as Nornis 3.11.
2. The live walk SHALL be Dave's: the Waterdeep question again, on his Prep model, with the pages
   shown, the cost on Admin, Costs, and the rights line on a print of an adventure that used it.

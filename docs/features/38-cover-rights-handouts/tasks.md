# Tasks

Ships as Nornis 3.9, after features 36 and 37. One migration (`AddCoverRightsHandouts`, with the
AI-use backfill), which Dave applies before the merge. The image deployment and its settings
are provisioned by Dave with targeted `az` commands, not the bootstrap script.

**Status: built 2026-11-03 (Phases B to E1); E2, the live walk, is Dave's. See the design's
amendment for what building it changed: covers run only on the world creator's own OpenAI key
(feature 39), so there is no Azure image deployment to provision.**

**Specified 2026-11-03. Dave chose AI art plus upload for covers, asked for a rights page
with an AI statement and a locking AI-use checkbox (locking only when AI wrote words or made
art), and suggested a Library picker for handouts.**

## Phase A — Spec

- [x] A1. `requirements.md`, `design.md` and this file; row 38 in the features README.

## Phase B — AI use and the rights page

- [x] B1. `AdventureAiUse`, `AiUseRecorder` and its callers; the declared endpoint; the
      migration with the backfill; `PrintDetails.AiUse` and the Bibliotaph field. Tests,
      including the list of AI writer paths.
- [x] B2. `RightsStatement`, `BookletRights`, and page 2 in the renderer; the cover loses its
      credits line. PdfPig tests.
- [x] B3. Web: the checkbox in Book details, locked with its reason. bUnit tests.

## Phase C — Cover art

- [x] C1. Spike: the image deployment, two options, the cost, and no lettering in the output.
      Dave provisions the deployment.
- [x] C2. `ICoverArtGenerator` and its Azure implementation; `CoverArtPrompt`; `CoverService`
      with the cap, the pricing and the pending blobs. Tests with a fake generator.
- [x] C3. The three layouts in the renderer. PdfPig tests for type placement on each.
- [x] C4. Web: the Cover card (brief, generate, choose, upload, layout). bUnit tests.

## Phase D — Handouts

- [x] D1. Spike: QuestPDF `DocumentOperation` merge under the Community licence.
- [x] D2. `AdventureHandout`; the endpoints; the printer's appendix (images, merged PDFs); Print
      props. Tests.
- [x] D3. Web: the Handouts card and picker (feature 36's chips and search); upload from the card.
      bUnit tests.

## Phase E — Ship 3.9

- [x] E1. `MinorVersion` 9; the change log, the Features page and the README; row 38; ticks; the
      steering re-check. Validate: a clean build with 0 warnings, the full test run,
      `dotnet format`, sabotage, and a local Slurping Spa render with a cover, rights page and
      two handouts.
- [ ] E2. Live walk (Dave): generate a cover for the Slurping Spa, pick one, print, check the
      rights page says AI wrote text and made art, add a map from the Library, and print props.

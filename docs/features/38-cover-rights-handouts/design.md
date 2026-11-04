# Design Document

> **Amendment (2026-11-03, after release):** the image model is `openai/gpt-image-2`. OpenAI shut
> `gpt-image-1` down on 2026-10-23, so the first live Generate failed with "The model
> 'gpt-image-1' does not exist", and the card called it a refusal. A 400 from the image endpoint
> is now read from OpenAI's error body: its safety system's codes (`moderation_blocked`,
> `content_policy_violation`) say "reword"; anything else shows OpenAI's own reason
> (`cover_rejected`); `billing_hard_limit_reached` is the key's account out of credit.

> **Amendment (2026-11-03, Phases B to D):** what building it changed. The text below is left
> as written.
>
> - **Covers run on the creator's own OpenAI key (feature 39).** The westus Azure OpenAI
>   resource has no image model, so there is no `CoverArt__*` deployment and no Nornis-paid or
>   trial path. `AiTask.CoverArt` is key-only: `OpenAiCoverArtGenerator` binds through
>   `IAiImageBinder` (the funding rule picks `openai/gpt-image-1` on the world creator's key) and
>   reports key failures like any call. With no key, Generate is off with "Add an OpenAI key to
>   generate covers. You can still upload your own art."; upload still works.
> - **Token pricing, not a per-image price.** The image model's catalog line carries per-million
>   rates and the cost comes from the response's usage, recorded to the Prep pool as
>   `CoverArt`. There is no `PrepOptions.CoverImagePriceUsd`. The cap is
>   `PrepOptions.MaxCoverImagesPerAdventure` (12), counted atomically before the call
>   (`TryCountCoverImagesAsync`); a failure or a short delivery gives the count back, a cancel
>   doesn't.
> - **No `AiUseRecorder`.** The flags are set by the repository in the same save as the text or
>   cover they record: accepting a section revision (draft and Put in section), a draft change
>   whose `AdventureDraftChange.IsLoremasterWriting()` is true (Edit, Put), and
>   `SetCoverAsync(generated: true)`. `PrepBoundaryTests` pins `generated: true` to
>   `AdventureCoverService`, and `IsLoremasterWriting` throws for an unclassified change.
> - **The rights block sits at the foot of page 2**, centred, rather than filling the page.
> - **Options persist; nothing is pending.** Generated options stay under
>   `worlds/{w}/covers/{a}/options/` (`Adventure.CoverOptionIds`, with the working
>   `CoverBrief`) until the Adventure is deleted, so the GM can go back to one. Choose points the
>   cover at the option's blob without copying it. Uploads get unique names under `uploads/`,
>   and a replaced upload's blob is deleted. Deleting the Adventure deletes its covers folder.
> - **Upload is the raw request body** (`PUT cover/image`, bounded read), not a SAS handshake:
>   10 MB is small enough to go through the API, which decodes it to check it's an image.
> - **A cover endpoint of its own.** `GET prep/{id}/cover` returns the card's whole state
>   (cover, brief, options, cap, whether Generate can run and why not); `GET prep/{id}` does not
>   carry it. The images reach the browser through the Web's `/prep-covers/...` proxies.
> - **Handouts are one `PUT prep/{id}/handouts`** with the whole ordered list: add, remove and
>   reorder are all that call. `BookletHandout` is `Image`, `Pdf` or `Missing(reason)` with the
>   bytes loaded by the printer, bounded at 100 MB for the lot; a handout that can't be read
>   prints a page saying so and where to find it. The card reorders with up and down arrows
>   rather than drag.
> - **Print props** is offered only when a section holds a `[!PROP]` box (the Web looks for the
>   marker; the API's `no_props` 404 stays the rule) and downloads through `/prep-props/...`.

## Overview

```
Adventure
  AiUse: WroteText (bool), MadeArt (bool), Declared (bool)      (new, owned)
  Cover: BlobPath, Source (Generated|Uploaded), Brief, Layout   (new, owned)
  CoverImagesGenerated (int)                                    (new)
AdventureHandout: AdventureId, LibraryDocumentId, SortOrder     (new)

AiUseRecorder.RecordTextAsync / RecordArtAsync   ← the only writers of WroteText/MadeArt
ICoverArtGenerator (Application) ← AzureOpenAiCoverArtGenerator (Infrastructure)
BookletModel + Cover + RightsPage + Handouts → QuestPdfBookletRenderer
                                                └─ PDF handouts appended after render
```

## AI use

- `AdventureAiUse` is an owned type on `Adventure` with columns `AiWroteText`, `AiMadeArt` and
  `AiDeclared`. Its `IsLocked` is `WroteText || MadeArt`, and `Any` is `IsLocked || Declared`.
- `AiUseRecorder` (Application/Prep) is the only code that sets `WroteText` or `MadeArt`. It is
  called by:
  - `AdventureDraftingService` when a draft completes;
  - `AdventureConversationService.PutInSectionAsync`;
  - feature 37's Put in draft and Apply edit;
  - `CoverService.ChooseAsync` for a generated image.
  
  A boundary scan pins it, and a test lists those callers so a new AI writer has to be added
  deliberately.
- Nothing clears them. Undo restores text, not innocence.
- `PUT prep/{id}/ai-declared` sets `Declared`. When locked it is a no-op that returns the
  current state.
- Migration backfill: `UPDATE Adventures SET AiWroteText = 1 WHERE EXISTS (a revision of one of
  its sections)`.
- `PrintDetails` gains `AiUse` (`None | Declared | Text | Art | TextAndArt`). It is resolved in
  `PrintDetails.Resolve`, snapshotted on filing, and sent in the Bibliotaph metadata block as
  `aiUse`. Bibliotaph treats unknown fields as optional (feature 32's contract).
- `RightsStatement.For(AiUse)` is the one place the sentence is chosen.

## Rights page

- `BookletModel` gains `BookletRights(Title, Publisher, Copyright, System, Levels, Printed,
  MadeWith, AiStatement, Notice)`.
- The renderer draws it as page 2: centred, in small Serif, with a hairline between the credits
  and the notices.
- `MadeWith` reads the assembly's informational version, so it is the deployed version.
- The cover drops its credits line.

## Cover art

- `ICoverArtGenerator.GenerateAsync(brief, count, ct)` returns `IReadOnlyList<GeneratedImage>`
  (PNG bytes and the model's revised prompt). It lives in Application/Prep/Covers.
- The Infrastructure implementation calls an Azure OpenAI image deployment
  (`CoverArt__Deployment`, `CoverArt__Endpoint`, managed identity). It goes through feature 39's
  per-call AI resolver once that ships, so a world's own key pays for its covers. The interface
  doesn't change.
- `CoverArtPrompt.BriefFor(adventure, context)` is the Loremaster call that writes the brief. It
  takes the Overview and pitch only, never the world's Hidden truth. Its rules:
  - no text or letters in the image;
  - no living artists' names;
  - no publisher trade dress;
  - a portrait composition with empty space at the top for the title.
- `CoverService`:
  - `GenerateAsync` checks the Prep budget and the per-Adventure cap, stores both options as
    pending blobs (24h, under `covers/pending/`), records the cost as `AiOperationType.CoverArt`,
    and returns them;
  - `ChooseAsync` moves the pick to `covers/{adventureId}.png`, sets `Cover`, and records MadeArt;
  - `UploadAsync` validates PNG or JPEG, at most 10 MB and at least 1,000 px on the short side
    (a warning, not a refusal), stores the image, and sets `Source = Uploaded`;
  - `RemoveAsync` deletes the blob and clears `Cover`. MadeArt stays recorded.
- Pricing: `PrepOptions.CoverImagePriceUsd` (configured per image, default 0.08) is charged to
  the Prep pool through the existing usage recorder.
- Layouts: `CoverLayout` is `FullBleed`, `Framed` or `ClassicModule`. Each is a renderer
  function over the same `BookletCover` plus an image. Text on art gets a solid band (FullBleed)
  or sits off the art (the other two), so no layout depends on the art's colours.

## Handouts

- `AdventureHandout` rows are ordered and unique per document. Adding one checks the document is
  in the world and is a Map or Handout with an image or PDF content type.
- `BookletModel` gains `BookletHandouts(IReadOnlyList<BookletHandout>)`, where `BookletHandout`
  is `(Title, ContentType, Func<Task<Stream>> Open)`. The printer loads the blobs. The model stays
  pure, and the printer is the only one that opens storage (`PrepBoundaryTests`).
- Images are drawn one per page with `FitArea`; a wider-than-tall image gets a landscape page.
- PDF handouts are appended after rendering with QuestPDF's `DocumentOperation`
  (merge). This is verified in a spike before the build. If it can't be used under the Community
  licence, PDF handouts fall back to a page naming the document with a link to it in the
  Library.
- **Print props:** `GET prep/{id}/props` renders `[!PROP]` boxes one per page through the same
  printer (`AdventureBookletPrinter.PrintPropsAsync`). Nothing is filed; the GM downloads it.
- The Upload button reuses the Library's upload dialog with feature 36's adventure link preset,
  then adds the new document as a handout.

## API

- `GET prep/{id}` adds `aiUse`, `cover` (`{ url, source, layout }`) and `handouts`.
- `PUT prep/{id}/ai-declared`.
- `POST prep/{id}/cover/brief`, `POST prep/{id}/cover/generate`, `POST prep/{id}/cover/choose`,
  `PUT prep/{id}/cover` (upload) and `DELETE prep/{id}/cover`.
- `PUT prep/{id}/cover/layout`.
- `POST prep/{id}/handouts`, `DELETE prep/{id}/handouts/{documentId}` and
  `PUT prep/{id}/handouts/order`.
- `GET prep/{id}/props`.

## Pre-implementation checks

1. **Where each rule lives.**
   - Who records AI use: `AiUseRecorder`.
   - Its sentence: `RightsStatement`.
   - The art rules: `CoverArtPrompt`.
   - Who opens blobs for a print: the printer.
2. **Second caller.** Every AI writer path calls the recorder, and a test enumerates them. The
   booklet and the props print share `AdventureBookletPrinter`.
3. **Null, empty, zero.**
   - No cover means today's typeset cover.
   - No handouts means no appendix.
   - No props means "Print props" is disabled with a reason.
   - A deleted handout document drops out of the list (cascade).
4. **Unbounded.**
   - Images per Adventure are capped.
   - Uploads are capped at 10 MB.
   - Handouts are capped at 20 per Adventure.
   - The rendered PDF is logged with its size; the filing path already bounds an upload's size.
5. **Show each guard failing.**
   - The recorder's lock survives an Undo.
   - A conversation without Put leaves AI use clear.
   - Uploaded art leaves MadeArt clear.
   - The cap refuses the 13th image.
   - The brief never contains a Hidden-truth sentence from the fixture.

## Out of scope

- Pregenerated characters as cards. Feature 35's stat blocks print in the booklet. A
  card-sized print of named stat blocks can follow if Dave wants one.
- Interior illustrations.
- Choosing a different image model or style per world.

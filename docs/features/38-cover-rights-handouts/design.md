# Design Document

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

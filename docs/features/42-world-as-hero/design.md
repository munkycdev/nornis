# Design Document

> **Amendment, 2026-11-08 (Phases C and D as built, Nornis 3.16).** Where the build differs:
>
> - **Uploads are PNG or JPEG**, not WebP (requirement 1.1): the upload reuses the cover's header
>   reader (`ImageFile`, moved to `Application/Art` with the generator), which reads the format
>   and size from the bytes without a decoder. WebP can follow if a GM asks for it.
> - **The hero's shape** is `{ worldId, name, gameSystem, sessionCount, lastSessionAt, entryCount,
>   picture: { kind, keyArtStamp, mapUrl, focusY } }`. Key art carries its stamp rather than a URL,
>   because the address is the Web's own image route (`/world-art/{w}?v=`, or `/w/{slug}/art?v=`
>   in public); `HeroDisplay.PictureUrl` is the one place it's spelled. `KeyArtResponse` carries
>   `focusY` too, so the card's preview crops as the hero does.
> - **The band is an `<img>`** with `object-fit: cover` and `object-position: center {focusY}%`
>   in a frame of fixed height, faded in by CSS rather than a JS `onload` helper. The fixed frame is
>   what keeps a slow image from moving the page, and a CSS fade can't miss a load event that
>   fired before the circuit attached. The name, facts and Ask box sit beside and below the
>   picture, on the faded paper. A map fallback is shown softer, so it reads as the world's
>   shape rather than as art.
> - **The switcher thumbnail is key art only** (requirement 3.1 said "or fallback art"): at 34px
>   a map is noise, and its SAS address changes on every read, so the browser could never keep it.
> - **The public page opens on its picture** when it has key art or a map the party has seen (the
>   Observer's journey map, as the public Locations page shows); requirement 3.2 asked for key art
>   only, and the map is no more than that page already shows anonymously.
> - **Cover options and key-art options share `CoverSource`** (Uploaded or Generated); no new enum.
> - **"Cover art" on Profile's Models card reads "Pictures"**, since the one image task now paints
>   covers and key art.
> - Writes go through `IWorldKeyArtRepository`, targeted at the key-art columns, so a key-art
>   change never races a settings save on the world's row version.

> **Amendment, 2026-11-07 23:40Z (Phase B as built).** The artifact graph had its own brighter
> palette for types (orange characters, blue places), kept in two places: `ArtifactGraph.Legend`
> and `typeColors` in `nornis-graph.js`. With type colours in the theme, that was a third copy
> of the rule saying something different. The graph now reads the `--nornis-type-*` tokens off
> its canvas (so dark mode takes the dark set) and its legend dots take `TypeClass`. The guard
> test scans the Web's scripts as well as its components. Character's slate equals the palette's
> Info role, which status marks use in their own right, so the guard skips palette colours.
> The Prep shelf needed the cover's stamp on the list: `AdventureSummary.CoverUpdatedAt`, read
> in the same query. `ContextRail.RailLink` now carries the entry's type instead of an icon.
> Phase B shipped as 3.15; key art and the hero follow as 3.16.

## Overview

```
World
  KeyArt: BlobPath, Source (Uploaded|Generated), Brief, FocusY, UpdatedAt        (new, owned)
  KeyArtOptionIds (json list, ≤ 2), KeyArtBrief (working brief)                  (new)
  KeyArtImagesMonth (yyyy-MM), KeyArtImagesGenerated (int)                       (new)

IImageArtGenerator (Application)  ← OpenAiImageArtGenerator (Infrastructure)
    replaces ICoverArtGenerator; takes an ImageShape (Portrait for covers, Landscape for key art)
WorldKeyArtService (Application)  upload · generate · choose · focus · remove · state
WorldHeroService   (Application)  one read: facts + which picture (key art | map | none)

API   GET/PUT/DELETE /api/worlds/{w}/key-art …        (GM writes, members read)
      GET            /api/worlds/{w}/hero             (members)
      GET            /api/public/worlds/{slug}/hero   (anonymous, public worlds only)
Web   /world-art/{w}/…  and  /w/{slug}/art  image proxies; HeroBand component; type tiles
```

One migration, `AddWorldKeyArt`: the owned `KeyArt` columns and the four working columns on
`Worlds`. Dave applies it before the merge, as with every migration.

## Key decisions

### 1. One image generator, two shapes

Covers already generate through `ICoverArtGenerator` → `OpenAiCoverArtGenerator`, which binds the
creator's image key (`IAiImageBinder`), maps OpenAI's 400 bodies to refusal, rejection or billing,
and prices from usage. Key art needs all of that and a different size (1536×1024 instead of
1024×1536). A second generator would copy the binding and the error mapping, which is a rule
living in two places. So the interface is renamed `IImageArtGenerator` and its `GenerateAsync`
takes an `ImageShape` (`Portrait`, `Landscape`) and the operation name to record. The cover
service passes `Portrait, "CoverArt"`, exactly as today; key art passes `Landscape, "KeyArt"`.

### 2. The Prep pool pays

Key art is GM-only creative spend, like covers, and Prep's monthly pool is where the GM already
sees and caps that kind of spend. The daily world budget pays for keeping the record (extraction,
Ask, audits) and stays untouched. `AiOperationType.KeyArt` is new; Costs lists it beside Cover art.

### 3. Bounded storage and spend

- Spend: 8 images per world per calendar month (`KeyArtOptions.MaxImagesPerMonth`), two per
  Generate. The counter is `KeyArtImagesMonth` + `KeyArtImagesGenerated`, taken atomically
  before the call with the same conditional update covers use (`TryCountKeyArtImagesAsync`); a new
  month resets it in the same statement. Null month means none generated yet, not zero allowed.
- Storage: blobs under `worlds/{w}/key-art/`. A world holds at most the chosen art plus one
  generation's options (two). Generating again deletes the previous unchosen options; choosing
  points `KeyArt.BlobPath` at the option's blob without copying; replacing or removing an upload
  deletes its blob; deleting the world deletes the folder.
- Upload: the raw request body, bounded read, 10 MB, decoded to check it's an image (the cover
  upload's path, reused).

### 4. Who sees it

Key art is world-level and therefore party-visible by definition. The Settings card says so, and
says that a public world shows it too. Because the image could carry a secret, the brief is the
GM's own words and goes only to the image model; there is no "draft a brief" call in this feature,
so nothing GM-only can reach a party-visible picture through the Loremaster.

### 5. The fallback lives on the server

Which picture a hero shows (key art, else the caller's journey map, else none) is decided once,
in `WorldHeroService`, which calls `IJourneyMapService.GetJourneyAsync` with the caller's role so
the map's visibility rule is the Map page's own. The Web never picks. The public variant uses the
public journey read the public Locations page already uses.

### 6. Facts in the same read

`GET /api/worlds/{w}/hero` returns `{ name, gameSystem, sessionCount, lastSessionAt, entryCount,
picture: { kind: KeyArt|Map|None, url, focusY } }`. Counts are what the caller can see (sessions
are dated SessionNote sources; entries are artifacts), computed with the existing visibility
filters. One round trip, so the hero renders with Home's other cards rather than after them.

### 7. Type colours are theme tokens

| Type | Light | Dark |
| --- | --- | --- |
| Character | `#3F6079` slate | `#7FA3BF` |
| Location | `#557548` moss | `#8DB07E` |
| Item | `#9C7330` ochre | `#D2A75C` |
| Faction | `#6E4A6B` plum | `#B48AB0` |
| Event | `#3E7672` teal | `#79B5AF` |
| Storyline | `#A7843B` old gold | `#D6B672` |
| Concept | `#5E6080` slate violet | `#9EA0C2` |
| Document | secondary ink | secondary ink |

Constants in `NornisTheme` (`TypeColours.Light/Dark`), tokens `--nornis-type-{type}` in
`app.css` under `.nornis-dark` for the dark set, and `ArtifactTypeDisplay.TypeClass(type)`
returning `nornis-type-{type}` so components never name a colour. `ThemeTests` pins the table,
checks 3:1 on the surface in both palettes, and checks the CSS mirrors the constants (the same
pattern as the palette mirror test). The tile is `TypeTile.razor`: a 2.1rem rounded square, the
icon in the type colour on a 13% tint of it.

### 8. Components

- `HeroBand.razor` (Home and public world): picture as a CSS background with `background-position:
  center {focusY}`, two gradients to the page colour (left-to-right and top-to-bottom) so the
  title and Ask box sit on paper, never on the art. `loading` handled by setting the background
  only after the image's `onload` (a tiny JS helper), so a slow image never shifts the layout.
- The world switcher gets a 34px thumbnail from the same `hero` read, cached in `WorldState` per
  world so the sidebar doesn't refetch on every navigation.
- `KeyArtCard.razor` in Settings: preview at hero proportions, Upload, brief + Generate, the two
  options, Choose, the anchor (top / centre / bottom), Remove, and the visibility sentence. It
  follows `CoverCard`'s states (no key, cap reached, refused, rejected) and reuses `AiErrorAlert`.
- Prep list: a `BookShelf` grid of 2:3 covers; cover art via `/prep-covers/{w}/{a}`; typeset
  fallback in ink with the title in Newsreader.
- Map: the map takes the content width; the places panel moves below it (and floats over the map's
  right edge from 1280px, closable).

### 9. What stays

Paper, ink, the one terracotta accent, Newsreader and Plex, 6px/10px radii, and the rule that the
accent marks action. Imagery is the world's own content, not chrome texture, which is the steering
doc's line between "durable, archival" and "heavy fantasy UI".

## Correctness properties

1. A player never receives, through the hero or the switcher, a map the Map page would not show
   them (same service call, same role).
2. A non-public world's key art is never served anonymously (the public route resolves the world
   by slug with `PublicAccessEnabled`, as every `/api/public` read does).
3. A world never holds more than three key-art blobs (chosen + two options).
4. A month never generates more than `MaxImagesPerMonth` images for a world, under concurrency.
5. No component sets a type colour by value; all go through `TypeClass`.

## Testing strategy

- Domain/Application: `WorldKeyArtService` (upload validation, generate with and without key,
  cap and month rollover, give-back on failure, choose, remove deletes the right blobs, 403 for
  non-GMs); `WorldHeroService` (key art beats map, map respects role, none when neither).
- Infrastructure: the generator's shape → size mapping; `TryCountKeyArtImagesAsync` under two
  concurrent callers on SQL (relational-only, says so).
- API: the role matrix for `/key-art` and `/hero`; the public hero 404s for a private world.
- Web: bUnit for `HeroBand` (key art, map, none; focus applied), `KeyArtCard` states,
  `TypeTile`; `ThemeTests` for the type colour table, contrast and CSS mirror.
- Each new guard shown failing first.

# Requirements Document

## Introduction

Extraction is asynchronous, but nothing outside the Sources page shows that work is in
flight or has failed — and nothing anywhere shows how many proposals await review.

## Requirements

1. A world-scoped activity summary SHALL report, respecting the caller's visibility:
   counts of sources Ready/Queued/Processing (in flight), Failed, and pending review
   proposals (Observers see zero proposals, matching the review queue).
2. THE navigation SHALL show a count badge on **Sources** while sources are in flight,
   switching to an error-styled badge when any source is Failed, and a count badge on
   **Review** while proposals are pending.
3. Badges SHALL refresh on world change and on a light poll (~15s) without page
   navigation; no badge renders when counts are zero.

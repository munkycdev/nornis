# Requirements Document

## Introduction

This document specifies the Worlds & Campaigns restructuring. The entity previously named
`Campaign` — the root collaboration and authorization boundary — is renamed to `World`,
so that one long-living body of knowledge can host multiple runs of play. A new, thin
`Campaign` entity is introduced as a child of World to contextualize *when and with whom*
events happened. Characters become first-class: a world member may have any number of
characters, and a character may participate in any number of campaigns.

Knowledge (artifacts, facts, relationships, canon) remains world-level. Campaign
association of knowledge is derived through source provenance, never stamped onto
artifacts or facts.

## Glossary

- **World**: The root container and authorization boundary (formerly `Campaign`). Owns
  membership, sources, artifacts, facts, relationships, canon, and cost tracking.
- **World_Member**: A user with an active membership record for a world (role: GM,
  Player, or Observer). Formerly `CampaignMember`.
- **Campaign**: A play-context within a world: a named run of sessions with optional
  real-world start/end dates and a status. Carries no membership and no permissions.
- **Character**: A playable identity owned by a World_Member. Not the same thing as the
  AI-extracted `Artifact` of type Character.
- **Campaign_Character**: The join between a Campaign and a Character, expressing that
  the character is (or was) part of that campaign.
- **Source**: Raw user input. May optionally declare the campaign its events happened in.

## Requirements

### Requirement 1: World as Root Boundary (Rename)

**User Story:** As a GM with a long-living setting, I want the root container to be a
World rather than a Campaign, so my setting's knowledge outlives any single run of play.

#### Acceptance Criteria

1. THE domain entity formerly named `Campaign` SHALL be renamed `World`, including
   membership (`WorldMember`), roles (`WorldRole`), foreign keys (`WorldId`), API routes
   (`/api/worlds`), and UI language.
2. THE rename SHALL preserve all existing data via rename migrations (no drop/create of
   populated tables).
3. Authorization semantics SHALL be unchanged: world membership is validated server-side
   for every world-scoped endpoint, exactly as campaign membership was before.
4. THE per-world daily AI budget configuration SHALL be renamed accordingly
   (`DailyWorldBudgetUsd`) with unchanged behavior.

### Requirement 2: Thin Campaign Entity

**User Story:** As a GM, I want to define campaigns within my world, so that sources and
questions can be contextualized to a specific run of play.

#### Acceptance Criteria

1. THE Campaign entity SHALL belong to exactly one World and carry: Name (required),
   Description (optional), Status (Active | Completed | Archived), StartedAt/EndedAt
   (optional real-world dates), and audit fields.
2. World members with the GM role SHALL be able to create, update, and delete campaigns.
3. WHEN a campaign is deleted, THE system SHALL NOT delete any knowledge or sources;
   sources referencing the campaign SHALL revert to "no campaign" (SET NULL).
4. Campaigns SHALL NOT introduce any authorization or visibility rules.

### Requirement 3: Source Campaign Context

**User Story:** As a player writing a session note, I want to say which campaign it
happened in, so the record stays unambiguous as the world accumulates campaigns.

#### Acceptance Criteria

1. THE Source entity SHALL have an optional CampaignId referencing a campaign in the
   same world.
2. WHEN a source is created or updated with a CampaignId, THE system SHALL reject a
   campaign belonging to a different world.
3. Sources with no campaign SHALL remain valid (worldbuilding lore, GM prep, setting
   documents).
4. WHEN extraction runs for a source with a campaign, THE extraction prompt SHALL include
   the campaign name (and status/dates if available) as context.
5. Review batches SHALL derive campaign context from their source and SHALL NOT store a
   campaign ID of their own.
6. THE Sources list SHALL be filterable by campaign, including "no campaign".

### Requirement 4: Characters

**User Story:** As a player, I want to register any number of characters in a world and
attach them to the campaigns they played in — including several at once in one campaign,
and one character spanning several campaigns.

#### Acceptance Criteria

1. THE Character entity SHALL belong to one World and one World_Member, with Name
   (required) and Description (optional).
2. A World_Member SHALL be able to have any number of characters in a world.
3. A Character SHALL be assignable to any number of campaigns in the same world, and a
   campaign SHALL accept any number of characters, including several from one member.
4. THE (CampaignId, CharacterId) pair SHALL be unique.
5. WHEN a character is assigned to a campaign, THE system SHALL reject a campaign from a
   different world than the character's.
6. THE former WorldMember.CharacterName field SHALL be removed; existing values SHALL be
   migrated into Character records owned by that member.
7. Members SHALL manage their own characters; GMs SHALL be able to manage any character
   in the world.

### Requirement 5: Authorization

#### Acceptance Criteria

1. All campaign and character endpoints SHALL require authenticated world membership.
2. Observers SHALL NOT mutate campaigns or characters.
3. Non-members SHALL receive 403/404 without leaking resource existence, matching
   existing world-scoped endpoint behavior.
